using System;
using System.Linq;
using System.Text;
using TouchSocket.Core;
using TouchSocket.Sockets;
using System.Threading.Tasks;
using WheelDiverterSorter.Core;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Drivers.Vendors.ShuDiNiao {
    public class ShuDiNiaoWheelDiverter : IWheelDiverter {
        private readonly ILogger<ShuDiNiaoWheelDiverter> _logger;

        private readonly SemaphoreSlim _connectGate = new(1, 1);

        private TcpClient? _tcpClient;
        private TcpService? _tcpService;
        private volatile string? _activeSessionId;
        private int _isDisposed;
        private CancellationTokenSource? _autoReconnectCts;
        private Task? _autoReconnectTask;
        private int _autoReconnectRunning;
        private int _isClosing;

        public void Dispose() {
            //断开
            _ = DisconnectAsync();
        }

        public int DiverterId { get; private set; }
        public WheelDiverterStatus Status { get; private set; }
        public WheelDiverterConnectionOptions ConnectionOptions { get; private set; }

        public event EventHandler<WheelDiverterReceivedEventArgs>? Received;

        public event EventHandler<WheelDiverterSentEventArgs>? Sent;

        public event EventHandler<WheelDiverterConnectedEventArgs>? Connected;

        public event EventHandler<WheelDiverterDisconnectedEventArgs>? Disconnected;

        public event EventHandler<WheelDiverterFaultedEventArgs>? Faulted;

        public ShuDiNiaoWheelDiverter(
            ILogger<ShuDiNiaoWheelDiverter> logger,
            WheelDiverterConnectionOptions connectionOptions) {
            _logger = logger;
            ConnectionOptions = connectionOptions;
            DiverterId = connectionOptions.DiverterId;
        }

        public async ValueTask<bool> ConnectAsync(WheelDiverterConnectionOptions connectionOptions, CancellationToken cancellationToken = default) {
            ThrowIfDisposed();

            await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                ConnectionOptions = connectionOptions;

                // 重要：门闩持有期间只能调用不重入的断开实现，避免自锁死
                await DisconnectCoreAsync("重新建立连接", cancellationToken, true).ConfigureAwait(false);

                return connectionOptions.Mode switch {
                    TcpConnectionMode.Client => await ConnectAsClientAsync(connectionOptions, cancellationToken).ConfigureAwait(false),
                    TcpConnectionMode.Server => await StartAsServerAsync(connectionOptions, cancellationToken).ConfigureAwait(false),
                    _ => false
                };
            }
            catch (Exception ex) {
                OnFaulted(ex, "ConnectAsync");
                return false;
            }
            finally {
                _connectGate.Release();
            }
        }

        public async ValueTask DisconnectAsync(string? reason = null, CancellationToken cancellationToken = default) {
            if (Volatile.Read(ref _isDisposed) != 0) {
                return;
            }

            await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                await DisconnectCoreAsync(reason, cancellationToken, true).ConfigureAwait(false);
            }
            finally {
                _connectGate.Release();
            }
        }

        public async ValueTask<bool> ReconnectAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();

            var options = ConnectionOptions;
            if (options is null) {
                _logger.LogWarning("摆轮未配置 ConnectionOptions，重连被忽略");
                return false;
            }

            await DisconnectAsync("重连前断开", cancellationToken).ConfigureAwait(false);
            return await ConnectAsync(options, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask RunAsync(CancellationToken cancellationToken = default) {
            // 构造命令帧
            var frame = ShuDiNiaoProtocol.BuildCommandFrame(0x51, ShuDiNiaoControlCommand.Run);

            _logger.LogInformation("摆轮 {DiverterId} 运行命令发送成功", DiverterId);
            switch (ConnectionOptions.Mode) {
                case TcpConnectionMode.Client:
                    await SendViaClientAsync(frame, cancellationToken);
                    break;

                case TcpConnectionMode.Server:
                    await SendViaServerAsync(frame, cancellationToken);
                    break;
            }
        }

        public async ValueTask StopAsync(CancellationToken cancellationToken = default) {
            var frame = ShuDiNiaoProtocol.BuildCommandFrame(0x51, ShuDiNiaoControlCommand.Stop);

            _logger.LogInformation("摆轮 {DiverterId} 停止命令发送成功", DiverterId);
            switch (ConnectionOptions.Mode) {
                case TcpConnectionMode.Client:
                    await SendViaClientAsync(frame, cancellationToken);
                    break;

                case TcpConnectionMode.Server:
                    await SendViaServerAsync(frame, cancellationToken);
                    break;
            }
        }

        public async ValueTask TurnLeftAsync(CancellationToken cancellationToken = default) {
            var frame = ShuDiNiaoProtocol.BuildCommandFrame(0x51, ShuDiNiaoControlCommand.TurnLeft);

            _logger.LogInformation("摆轮 {DiverterId} 左转命令发送成功", DiverterId);
            switch (ConnectionOptions.Mode) {
                case TcpConnectionMode.Client:
                    await SendViaClientAsync(frame, cancellationToken);
                    break;

                case TcpConnectionMode.Server:
                    await SendViaServerAsync(frame, cancellationToken);
                    break;
            }
        }

        public async ValueTask TurnRightAsync(CancellationToken cancellationToken = default) {
            var frame = ShuDiNiaoProtocol.BuildCommandFrame(0x51, ShuDiNiaoControlCommand.TurnRight);

            _logger.LogInformation("摆轮 {DiverterId} 右转命令发送成功", DiverterId);
            switch (ConnectionOptions.Mode) {
                case TcpConnectionMode.Client:
                    await SendViaClientAsync(frame, cancellationToken);
                    break;

                case TcpConnectionMode.Server:
                    await SendViaServerAsync(frame, cancellationToken);
                    break;
            }
        }

        public async ValueTask StraightThroughAsync(CancellationToken cancellationToken = default) {
            var frame = ShuDiNiaoProtocol.BuildCommandFrame(0x51, ShuDiNiaoControlCommand.ReturnCenter);

            _logger.LogInformation("摆轮 {DiverterId} 直通命令发送成功", DiverterId);
            switch (ConnectionOptions.Mode) {
                case TcpConnectionMode.Client:
                    await SendViaClientAsync(frame, cancellationToken);
                    break;

                case TcpConnectionMode.Server:
                    await SendViaServerAsync(frame, cancellationToken);
                    break;
            }
        }

        public async ValueTask SetSpeedMmpsAsync(decimal speedMmps, CancellationToken cancellationToken = default) {
            var speedMPerMin = ShuDiNiaoSpeedConverter.ConvertMmPerSecondToMPerMin((double)speedMmps);
            var speedAfterSwingMPerMin = ShuDiNiaoSpeedConverter.ConvertMmPerSecondToMPerMin((double)speedMmps);
            var frame = ShuDiNiaoProtocol.BuildSpeedSettingFrame(0x51, speedMPerMin, speedAfterSwingMPerMin);

            // 保存最后发送的命令（用于诊断）
            var lastCommandSent = ShuDiNiaoProtocol.FormatBytes(frame);
            _logger.LogInformation(
                "[摆轮通信-发送] 摆轮 {DiverterId} 发送速度设置 | 节点:{DeviceAddress:X2} | 速度={Speed}m/min | 摆动后速度={SpeedAfterSwing}m/min | 速度帧={Frame}",
                ConnectionOptions.Endpoint,
                DiverterId,
                speedMPerMin,
                speedAfterSwingMPerMin,
                lastCommandSent);

            switch (ConnectionOptions.Mode) {
                case TcpConnectionMode.Client:
                    await SendViaClientAsync(frame, cancellationToken);
                    break;

                case TcpConnectionMode.Server:
                    await SendViaServerAsync(frame, cancellationToken);
                    break;
            }
        }

        private async Task<bool> ConnectAsClientAsync(WheelDiverterConnectionOptions options, CancellationToken cancellationToken) {
            var remote = BuildTcpEndpoint(options.Endpoint, options.Port);

            var client = new TcpClient {
                Connected = (c, e) => {
                    OnConnected(TcpConnectionMode.Client, remote);
                    return EasyTask.CompletedTask;
                },
                Closed = (c, e) => {
                    OnDisconnected("连接已断开");
                    if (Interlocked.Exchange(ref _isClosing, 0) != 0) {
                        // 本地主动关闭导致的 Closed，不触发自动重连，不重复通知断开
                        return EasyTask.CompletedTask;
                    }

                    StartAutoReconnectLoopIfNeeded("ClientClosed");
                    return EasyTask.CompletedTask;
                },
                Received = (c, e) => {
                    OnReceived(e.Memory);
                    return EasyTask.CompletedTask;
                }
            };

            // 配置：低延迟 + 可扩展插件点

            var config = new TouchSocketConfig()
                .SetRemoteIPHost(remote)
                .ConfigureContainer(a => { })
                .ConfigurePlugins(_ => { });
            await client.SetupAsync(config).ConfigureAwait(false);
            _tcpClient = client;

            try {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (options.ConnectTimeoutMs > 0) {
                    cts.CancelAfter(options.ConnectTimeoutMs);
                }

                await client.ConnectAsync().WaitAsync(cts.Token).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) {
                OnFaulted(ex, $"ConnectAsClientAsync remote={remote}");
                client.SafeDispose();
                _tcpClient = null;
                return false;
            }
        }

        private async Task<bool> StartAsServerAsync(WheelDiverterConnectionOptions options, CancellationToken cancellationToken) {
            var listenHost = BuildListenHost(options.Endpoint, options.Port);

            var service = new TcpService {
                Connected = (c, e) => {
                    // 服务端避免持有 SessionClient 引用，仅记录 Id
                    _activeSessionId = c.Id;
                    OnConnected(TcpConnectionMode.Server, c.IP);
                    return EasyTask.CompletedTask;
                },
                Closed = (c, e) => {
                    if (string.Equals(_activeSessionId, c.Id, StringComparison.Ordinal)) {
                        _activeSessionId = null;
                    }

                    OnDisconnected("连接已断开");
                    return EasyTask.CompletedTask;
                },
                Received = (c, e) => {
                    // 仅接受当前活跃连接的数据（避免多连接造成上层混乱）
                    if (string.Equals(_activeSessionId, c.Id, StringComparison.Ordinal)) {
                        OnReceived(e.Memory);
                    }
                    return EasyTask.CompletedTask;
                }
            };

            var config = new TouchSocketConfig()
                .SetListenIPHosts(listenHost)
                .ConfigureContainer(a => { })
                .ConfigurePlugins(_ => { });
            await service.SetupAsync(config).ConfigureAwait(false);
            _tcpService = service;

            try {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (options.ConnectTimeoutMs > 0) {
                    cts.CancelAfter(options.ConnectTimeoutMs);
                }

                await service.StartAsync().WaitAsync(cts.Token).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) {
                OnFaulted(ex, $"StartAsServerAsync listen={listenHost}");
                service.SafeDispose();
                _tcpService = null;
                return false;
            }
        }

        private async ValueTask SendViaClientAsync(ReadOnlyMemory<byte> command, CancellationToken cancellationToken) {
            var client = _tcpClient;
            if (client is null) {
                return;
            }

            try {
                await client.SendAsync(command, cancellationToken).ConfigureAwait(false);
                Sent?.Invoke(this, new WheelDiverterSentEventArgs {
                    Payload = command,
                    OccurredAtMs = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                });
            }
            catch (Exception ex) {
                OnFaulted(ex, "TcpClient.SendAsync");
            }
        }

        private async ValueTask SendViaServerAsync(ReadOnlyMemory<byte> command, CancellationToken cancellationToken) {
            var service = _tcpService;
            var id = _activeSessionId;

            if (service is null || string.IsNullOrWhiteSpace(id)) {
                return;
            }

            try {
                // 通过 Id 查找会话再发送，避免引用生命周期问题
                if (service.TryGetClient(id, out var session)) {
                    await session.SendAsync(command, cancellationToken).ConfigureAwait(false);
                    Sent?.Invoke(this, new WheelDiverterSentEventArgs {
                        Payload = command,
                        OccurredAtMs = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                    });
                }
            }
            catch (Exception ex) {
                OnFaulted(ex, "TcpService.SendAsync");
            }
        }

        private void OnReceived(ReadOnlyMemory<byte> payload) {
            try {
                Received?.Invoke(this, new WheelDiverterReceivedEventArgs {
                    Payload = payload,
                    OccurredAtMs = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                });
                _logger.LogDebug(
                    "[摆轮通信-接收] 摆轮 {DiverterId} 收到未识别的帧 | 帧={Frame}",
                    DiverterId,
                    ShuDiNiaoProtocol.FormatBytes(payload.Span));
            }
            catch (Exception ex) {
                // 事件回调异常不可向外冒泡
                OnFaulted(ex, "Received event handler");
            }
        }

        private void OnConnected(TcpConnectionMode mode, string? remoteEndpoint) {
            try {
                Connected?.Invoke(this, new WheelDiverterConnectedEventArgs {
                    OccurredAtMs = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                });
            }
            catch (Exception ex) {
                OnFaulted(ex, "Connected event handler");
            }
        }

        private void OnDisconnected(string? reason) {
            try {
                Disconnected?.Invoke(this, new WheelDiverterDisconnectedEventArgs {
                    OccurredAtMs = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                    Reason = reason
                });
            }
            catch (Exception ex) {
                OnFaulted(ex, "Disconnected event handler");
            }
        }

        private void OnFaulted(Exception ex, string context) {
            try {
                _logger.LogError(ex, "树递鸟摆轮发生异常: {Context}", context);

                Faulted?.Invoke(this, new WheelDiverterFaultedEventArgs {
                    Exception = ex,
                    OccurredAtMs = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                    Operation = null,
                });
            }
            catch {
                // Faulted 通知链路异常同样不可外溢
            }
        }

        private void ThrowIfDisposed() {
            if (Volatile.Read(ref _isDisposed) != 0) {
                throw new ObjectDisposedException(nameof(ShuDiNiaoWheelDiverter), "对象已释放");
            }
        }

        private static string BuildTcpEndpoint(string endpoint, int port) {
            if (string.IsNullOrWhiteSpace(endpoint)) {
                return $"127.0.0.1:{port}";
            }

            if (endpoint.Contains("://", StringComparison.Ordinal)) {
                return endpoint;
            }

            return $"{endpoint}:{port}";
        }

        private static string BuildListenHost(string endpoint, int port) {
            // TouchSocket 支持 "tcp://ip:port" 或直接 port 等方式；此处统一输出 tcp://
            var ip = string.IsNullOrWhiteSpace(endpoint) ? "0.0.0.0" : endpoint;
            return $"tcp://{ip}:{port}";
        }

        private void StartAutoReconnectLoopIfNeeded(string reason) {
            var options = ConnectionOptions;
            if (options is null) {
                return;
            }

            // 仅 Client 模式才需要“自动重连”
            if (options.Mode != TcpConnectionMode.Client) {
                return;
            }

            // 建议增加开关；若暂不改 Options，可先默认开启或默认关闭
            // if (!options.IsAutoReconnectEnabled) return;

            if (Interlocked.CompareExchange(ref _autoReconnectRunning, 1, 0) != 0) {
                return;
            }

            _autoReconnectCts?.Cancel();
            _autoReconnectCts = new CancellationTokenSource();

            var token = _autoReconnectCts.Token;
            _autoReconnectTask = Task.Run(() => AutoReconnectLoopAsync(reason, token), token);
        }

        private void StopAutoReconnectLoop() {
            _autoReconnectCts?.Cancel();
            _autoReconnectCts = null;
            Interlocked.Exchange(ref _autoReconnectRunning, 0);
        }

        private async Task AutoReconnectLoopAsync(string reason, CancellationToken cancellationToken) {
            try {
                var delayMs = 300;
                var maxDelayMs = 3_000;

                while (!cancellationToken.IsCancellationRequested && Volatile.Read(ref _isDisposed) == 0) {
                    try {
                        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);

                        var ok = await TryReconnectOnceAsync(cancellationToken).ConfigureAwait(false);
                        if (ok) {
                            return;
                        }

                        delayMs = Math.Min(maxDelayMs, delayMs * 2);
                    }
                    catch (OperationCanceledException) {
                        return;
                    }
                    catch (Exception ex) {
                        OnFaulted(ex, $"AutoReconnectLoop reason={reason}");
                        delayMs = Math.Min(maxDelayMs, delayMs * 2);
                    }
                }
            }
            finally {
                // 确保自动重连状态可再次启动
                Interlocked.Exchange(ref _autoReconnectRunning, 0);
            }
        }

        private async Task<bool> TryReconnectOnceAsync(CancellationToken cancellationToken) {
            var options = ConnectionOptions;
            if (options is null) {
                return false;
            }

            await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                // 已连接则直接结束
                if (_tcpClient is not null && _tcpClient.Online) {
                    return true;
                }

                // 清理旧连接：此处必须调用“核心断开”，且不能停止自动重连循环
                await DisconnectCoreAsync("自动重连清理", cancellationToken, stopAutoReconnectLoop: false).ConfigureAwait(false);

                // 仅 Client 模式
                return await ConnectAsClientAsync(options, cancellationToken).ConfigureAwait(false);
            }
            finally {
                _connectGate.Release();
            }
        }

        private async ValueTask DisconnectCoreAsync(string? reason, CancellationToken cancellationToken, bool stopAutoReconnectLoop) {
            if (stopAutoReconnectLoop) {
                StopAutoReconnectLoop();
            }

            _activeSessionId = null;

            if (_tcpClient is not null) {
                try {
                    await _tcpClient.CloseAsync(reason ?? "断开连接", cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) {
                    OnFaulted(ex, "TcpClient.CloseAsync");
                }
                finally {
                    _tcpClient.SafeDispose();
                    _tcpClient = null;
                }
            }

            if (_tcpService is not null) {
                try {
                    await _tcpService.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) {
                    OnFaulted(ex, "TcpService.StopAsync");
                }
                finally {
                    _tcpService.SafeDispose();
                    _tcpService = null;
                }
            }

            OnDisconnected(reason);
        }
    }
}
