using System;
using System.Net;
using System.Linq;
using System.Text;
using System.Buffers;
using System.Text.Json;
using TouchSocket.Core;
using TouchSocket.Sockets;
using System.Threading.Tasks;
using WheelDiverterSorter.Core;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Models;
using WheelDiverterSorter.Core.Options;
using System.Runtime.InteropServices.JavaScript;

namespace WheelDiverterSorter.Ingress {

    public sealed class UpstreamRouting : IUpstreamRouting, IDisposable {
        private readonly ILogger<UpstreamRouting> _logger;

        private readonly ConcurrentDictionary<string, object> _clients = new();
        private TcpClient? _client;
        private TcpService? _server;

        /// <summary>
        /// 服务端模式下的最近一次连接会话（用于优先发送）
        /// </summary>
        private object? _activeSession;

        private int _isConnected; // 0=false, 1=true

        private CancellationTokenSource? _lifetimeCts;
        private Task? _clientReconnectLoopTask;

        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) {
            PropertyNameCaseInsensitive = true
        };

        public UpstreamRouting(ILogger<UpstreamRouting> logger) {
            _logger = logger;
            Status = UpstreamRoutingStatus.Disconnected;
        }

        public UpstreamRoutingStatus Status { get; private set; }

        public UpstreamRoutingConnectionOptions? ConnectionOptions { get; private set; }

        public bool IsConnected => Volatile.Read(ref _isConnected) == 1;

        public event EventHandler<ChuteAssignmentInfo>? ChuteAssignedReceived;

        public event EventHandler<UpstreamRoutingConnectedEventArgs>? Connected;

        public event EventHandler<UpstreamRoutingDisconnectedEventArgs>? Disconnected;

        public event EventHandler<UpstreamRoutingFaultedEventArgs>? Faulted;

        public void Dispose() {
            // 同步 Dispose 中不执行阻塞关闭，转为异步 best-effort
            _ = SafeDisposeAsync();
        }

        public async ValueTask<bool> ConnectAsync(
            UpstreamRoutingConnectionOptions connectionOptions,
            CancellationToken cancellationToken = default) {
            ConnectionOptions = connectionOptions;

            // 统一生命周期取消源：DisconnectAsync / Dispose 可以主动取消后台循环
            _lifetimeCts?.Cancel();
            _lifetimeCts?.Dispose();
            _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try {
                return connectionOptions.Mode switch {
                    TcpConnectionMode.Client => await ConnectAsClientAsync(_lifetimeCts.Token).ConfigureAwait(false),
                    TcpConnectionMode.Server => await StartServerAsync(_lifetimeCts.Token).ConfigureAwait(false),
                    _ => throw new InvalidOperationException($"不支持的连接模式：{connectionOptions.Mode}")
                };
            }
            catch (OperationCanceledException) {
                RaiseDisconnected("连接已取消");
                return false;
            }
            catch (Exception ex) {
                RaiseFaulted("连接发生异常。", ex);
                RaiseDisconnected("连接失败");
                return false;
            }
        }

        public async ValueTask<bool> ReconnectAsync(CancellationToken cancellationToken = default) {
            var options = ConnectionOptions;
            if (options is null) {
                RaiseFaulted("缺少连接参数，无法重连。", null);
                return false;
            }

            try {
                await DisconnectAsync("重连触发：先断开再连接", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) {
                RaiseFaulted("重连断开阶段异常。", ex);
            }

            return await ConnectAsync(options, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisconnectAsync(string? reason = null, CancellationToken cancellationToken = default) {
            try {
                _lifetimeCts?.Cancel();
            }
            catch {
                // 忽略取消异常
            }

            await SafeDisposeAsync().ConfigureAwait(false);

            var msg = string.IsNullOrWhiteSpace(reason) ? "主动断开" : reason;
            RaiseDisconnected(msg);
        }

        public ValueTask<bool> SendCreateParcelAsync(UpstreamCreateParcelRequest request, CancellationToken cancellationToken = default)
            => SendTypedJsonLineAsync("ParcelDetected", request, cancellationToken);

        public ValueTask<bool> SendDropToChuteAsync(SortingCompletedMessage request, CancellationToken cancellationToken = default)
            => SendTypedJsonLineAsync("SortingCompleted", request, cancellationToken);

        // 该 Type 字符串必须与上游 switch 完全一致；若上游未实现该 case，则需要改为其实际约定值
        public ValueTask<bool> SendParcelExceptionAsync(ParcelExceptionMessage request, CancellationToken cancellationToken = default)
            => SendTypedJsonLineAsync("ParcelException", request, cancellationToken);

        private async Task<bool> ConnectAsClientAsync(CancellationToken cancellationToken) {
            // 客户端模式：参考 DefaultDws 的思路，后台循环负责断线重连
            if (_clientReconnectLoopTask is not null && !_clientReconnectLoopTask.IsCompleted) {
                return IsConnected;
            }

            // 首次先尝试连接一次，返回给调用方明确结果；之后再进入断线重连循环
            var firstOk = await StartClientOnceAsync(cancellationToken).ConfigureAwait(false);

            if (ConnectionOptions?.IsAutoReconnectEnabled == true) {
                _clientReconnectLoopTask = RunClientReconnectLoopAsync(cancellationToken);
            }

            return firstOk;
        }

        private async Task SafeDisposeAsync() {
            try {
                Interlocked.Exchange(ref _isConnected, 0);
                Status = UpstreamRoutingStatus.Disconnected;

                var client = Interlocked.Exchange(ref _client, null);
                if (client is not null) {
                    try { client.Received -= OnClientReceived; } catch { /* ignore */ }
                    try { await client.CloseAsync().ConfigureAwait(false); } catch { /* ignore */ }
                    try { client.Dispose(); } catch { /* ignore */ }
                }

                var server = Interlocked.Exchange(ref _server, null);
                if (server is not null) {
                    try {
                        server.Connected -= OnServerClientConnected;
                        server.Closed -= OnServerClientDisconnected;
                        server.Received -= OnServerReceived;
                    }
                    catch {
                        // ignore
                    }

                    try { await server.StopAsync().ConfigureAwait(false); } catch { /* ignore */ }
                    try { server.Dispose(); } catch { /* ignore */ }
                }

                _clients.Clear();
                _activeSession = null;
            }
            catch (Exception ex) {
                RaiseFaulted("释放连接资源异常。", ex);
            }
        }

        private async Task<bool> StartServerAsync(CancellationToken cancellationToken) {
            if (_server != null) {
                return true;
            }

            var options = ConnectionOptions;
            if (options is null) {
                RaiseFaulted("缺少连接参数，无法启动服务端。", null);
                return false;
            }

            var service = new TcpService();

            var config = new TouchSocketConfig()
                .SetListenIPHosts([new IPHost($"{options.Endpoint}:{options.Port}")])
                // 参照 DefaultDws 的 TryGetMessage：如果需要稳定分包，建议启用终止符适配器
                .SetTcpDataHandlingAdapter(() => new TerminatorPackageAdapter("\n"))
                .ConfigureContainer(a => { })
                .ConfigurePlugins(_ => { });

            service.Connected += OnServerClientConnected;
            service.Closed += OnServerClientDisconnected;
            service.Received += OnServerReceived;

            await service.SetupAsync(config).ConfigureAwait(false);
            await service.StartAsync(cancellationToken).ConfigureAwait(false);

            _server = service;

            Status = UpstreamRoutingStatus.Connected;
            Interlocked.Exchange(ref _isConnected, 1);

            RaiseConnected($"[Upstream] Server started at {options.Endpoint}:{options.Port}");
            _logger.LogInformation("[Upstream] Server started at {Endpoint}:{Port}", options.Endpoint, options.Port);

            return true;
        }

        private async Task RunClientReconnectLoopAsync(CancellationToken cancellationToken) {
            var options = ConnectionOptions;
            if (options is null) {
                return;
            }

            var backoff = TimeSpan.FromMilliseconds(options.ReconnectMinDelayMs);
            var maxBackoff = TimeSpan.FromMilliseconds(options.ReconnectMaxDelayMs);

            while (!cancellationToken.IsCancellationRequested) {
                try {
                    // 等待断开/取消
                    while (!cancellationToken.IsCancellationRequested && _client is not null && _client.Online) {
                        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                    }

                    if (cancellationToken.IsCancellationRequested) {
                        break;
                    }

                    Interlocked.Exchange(ref _isConnected, 0);
                    Status = UpstreamRoutingStatus.Disconnected;
                    RaiseDisconnected($"[Upstream] Client disconnected from {options.Endpoint}:{options.Port}");

                    // 断线后开始重连
                    var ok = await StartClientOnceAsync(cancellationToken).ConfigureAwait(false);
                    if (ok) {
                        backoff = TimeSpan.FromMilliseconds(options.ReconnectMinDelayMs);
                        continue;
                    }
                }
                catch (OperationCanceledException) {
                    break;
                }
                catch (Exception ex) {
                    RaiseFaulted($"[Upstream] Client reconnect failed: {options.Endpoint}:{options.Port}", ex);
                }

                // 退避等待
                try {
                    var delay = backoff <= maxBackoff ? backoff : maxBackoff;
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    break;
                }

                // 指数退避（decimal -> double 仅用于计算毫秒）
                var factor = (double)options.ReconnectBackoffFactor;
                var nextMs = backoff.TotalMilliseconds * factor;
                var next = TimeSpan.FromMilliseconds(nextMs);

                backoff = next <= maxBackoff ? next : maxBackoff;
            }
        }

        private async Task<bool> StartClientOnceAsync(CancellationToken cancellationToken) {
            var options = ConnectionOptions;
            if (options is null) {
                RaiseFaulted("缺少连接参数，无法启动客户端。", null);
                return false;
            }

            // 清理旧连接
            if (_client != null) {
                try { _client.Received -= OnClientReceived; } catch { }
                try { await _client.CloseAsync().ConfigureAwait(false); } catch { }
                try { _client.Dispose(); } catch { }
                _client = null;
            }

            var client = new TcpClient();

            var config = new TouchSocketConfig()
                .SetRemoteIPHost(new IPHost($"{options.Endpoint}:{options.Port}"))
                .SetTcpDataHandlingAdapter(() => new TerminatorPackageAdapter("\n"))
                .ConfigureContainer(a => { })
                .ConfigurePlugins(_ => { });

            client.Received += OnClientReceived;

            await client.SetupAsync(config).ConfigureAwait(false);

            // 连接超时控制
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(options.ConnectTimeoutMs);

            try {
                await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException oce) {
                RaiseFaulted("[Upstream] Client connect timeout/canceled.", oce);
                return false;
            }
            catch (Exception ex) {
                RaiseFaulted("[Upstream] Client connect failed.", ex);
                return false;
            }

            _client = client;

            Status = UpstreamRoutingStatus.Connected;
            Interlocked.Exchange(ref _isConnected, 1);

            RaiseConnected($"[Upstream] Client connected to {options.Endpoint}:{options.Port}");
            _logger.LogInformation("[Upstream] Client connected to {Endpoint}:{Port}", options.Endpoint, options.Port);

            return true;
        }

        private Task OnServerReceived(object client, ReceivedDataEventArgs e) {
            var msg = TryGetMessage(e);
            if (!string.IsNullOrWhiteSpace(msg)) {
                _logger.LogInformation("[Upstream][RECV][SERVER] {Payload}", Truncate(msg));
                TryHandleChuteAssigned(msg);
            }

            return Task.CompletedTask;
        }

        private Task OnClientReceived(object client, ReceivedDataEventArgs e) {
            var msg = TryGetMessage(e);
            if (!string.IsNullOrWhiteSpace(msg)) {
                _logger.LogInformation("[Upstream][RECV][CLIENT] {Payload}", Truncate(msg));
                TryHandleChuteAssigned(msg);
            }

            return Task.CompletedTask;
        }

        private void TryHandleChuteAssigned(string msg) {
            try {
                // 按 JSON 解析为 ChuteAssignmentInfo
                var info = JsonSerializer.Deserialize<ChuteAssignmentInfo>(msg, _jsonOptions);
                if (info is null) {
                    return;
                }

                ChuteAssignedReceived?.Invoke(this, info);
            }
            catch (JsonException) {
                // 非该类型消息时忽略
            }
            catch (Exception ex) {
                RaiseFaulted("接收消息解析异常。", ex);
            }
        }

        private Task OnServerClientConnected(object client, ConnectedEventArgs e) {
            var id = TryGetClientId(client) ?? Guid.NewGuid().ToString("N");
            _clients[id] = client;
            _activeSession = client;

            _logger.LogInformation("[Upstream][SERVER] Client connected: {ClientId}, total={Count}", id, _clients.Count);
            return Task.CompletedTask;
        }

        private Task OnServerClientDisconnected(object client, ClosedEventArgs e) {
            var id = TryGetClientId(client);
            if (!string.IsNullOrWhiteSpace(id)) {
                _clients.TryRemove(id, out _);
            }

            if (ReferenceEquals(_activeSession, client)) {
                _activeSession = null;
            }

            _logger.LogWarning("[Upstream][SERVER] Client disconnected: {ClientId}, total={Count}", id ?? "unknown", _clients.Count);
            return Task.CompletedTask;
        }

        private ValueTask<bool> SendTypedJsonLineAsync<T>(string type, T data, CancellationToken cancellationToken) {
            try {
                if (!IsConnected) {
                    return ValueTask.FromResult(false);
                }

                // 先把 data 按现有 _jsonOptions 序列化为 JsonElement，再把属性展开到根节点
                var element = JsonSerializer.SerializeToElement(data, _jsonOptions);

                var buffer = new ArrayBufferWriter<byte>(256);
                using (var writer = new Utf8JsonWriter(buffer)) {
                    writer.WriteStartObject();

                    // 根节点 Type：上游 EnvelopeHead 依赖该字段
                    writer.WriteString("Type", type);

                    if (element.ValueKind == JsonValueKind.Object) {
                        foreach (var prop in element.EnumerateObject()) {
                            writer.WritePropertyName(prop.Name);
                            prop.Value.WriteTo(writer);
                        }
                    }

                    writer.WriteEndObject();
                    writer.Flush();
                }

                // 追加 '\n'，与 TerminatorPackageAdapter("\n") 对齐
                var payload = new byte[buffer.WrittenCount + 1];
                buffer.WrittenSpan.CopyTo(payload);
                payload[^1] = (byte)'\n';

                return SendRawAsync(payload, cancellationToken);
            }
            catch (Exception ex) {
                RaiseFaulted("发送消息异常。", ex);
                return ValueTask.FromResult(false);
            }
        }

        private async ValueTask<bool> SendRawAsync(byte[] payload, CancellationToken cancellationToken) {
            try {
                // 客户端模式：直接发送
                if (_client is not null) {
                    if (!_client.Online) {
                        return false;
                    }

                    await _client.SendAsync(payload, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                // 服务端模式：优先发送活跃会话；活跃会话不存在时广播
                if (_activeSession is not null) {
                    dynamic d = _activeSession;
                    if ((bool)d.Online) {
                        await d.SendAsync(payload, cancellationToken).ConfigureAwait(false);
                        return true;
                    }
                }

                var sent = false;
                foreach (var kv in _clients) {
                    try {
                        dynamic d = kv.Value;
                        if (!(bool)d.Online) {
                            continue;
                        }

                        await d.SendAsync(payload, cancellationToken).ConfigureAwait(false);
                        sent = true;
                    }
                    catch (Exception ex) {
                        RaiseFaulted("服务端广播发送异常。", ex);
                    }
                }

                return sent;
            }
            catch (Exception ex) {
                RaiseFaulted("底层发送异常。", ex);
                return false;
            }
        }

        private void RaiseConnected(string message) {
            try {
                var options = ConnectionOptions;
                Connected?.Invoke(this, new UpstreamRoutingConnectedEventArgs {
                    OccurredAtMs = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                });
            }
            catch (Exception ex) {
                _logger.LogError(ex, "触发 Connected 事件失败。");
            }
        }

        private void RaiseDisconnected(string reason) {
            try {
                Disconnected?.Invoke(this, new UpstreamRoutingDisconnectedEventArgs {
                    OccurredAtMs = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                });
            }
            catch (Exception ex) {
                _logger.LogError(ex, "触发 Disconnected 事件失败。");
            }
        }

        private void RaiseFaulted(string message, Exception? exception) {
            try {
                Faulted?.Invoke(this, new UpstreamRoutingFaultedEventArgs {
                    OccurredAtMs = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                    Exception = exception
                });
            }
            catch (Exception ex) {
                _logger.LogError(ex, "触发 Faulted 事件失败。");
            }
        }

        private static string? TryGetMessage(ReceivedDataEventArgs e) {
            // 1) 启用 TerminatorPackageAdapter("\n") 时，通常优先从 RequestInfo 获取
            if (e.RequestInfo is not null) {
                var text = e.RequestInfo.ToString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }

            // 2) TouchSocket 4.x 优先从 Memory 获取
            var memory = e.Memory;
            if (memory.IsEmpty) {
                return null;
            }

            return Encoding.UTF8.GetString(memory.Span);
        }

        private static string Truncate(string s, int max = 500) {
            if (string.IsNullOrEmpty(s)) {
                return string.Empty;
            }

            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= max ? s : s[..max] + "...";
        }

        private static string? TryGetClientId(object client) {
            try {
                dynamic d = client;
                return (string?)d.Id;
            }
            catch {
                return null;
            }
        }
    }
}
