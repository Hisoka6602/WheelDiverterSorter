using Polly;
using System;
using csLTDMC;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using WheelDiverterSorter.Core;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using WheelDiverterSorter.Core.Enums;
using System.Runtime.CompilerServices;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Models;

namespace WheelDiverterSorter.Drivers.Vendors.Leadshine {

    /// <summary>
    /// 雷赛 EMC 控制器
    /// </summary>
    public sealed class LeadshineEmcController : IEmcController {

        /// <summary>
        /// 软复位后重新初始化前的等待时间（毫秒）
        /// </summary>
        private const int SoftResetDelayMs = 500;

        /// <summary>
        /// IO 轮询间隔（毫秒），按现场负载与 IO 数量调参
        /// </summary>
        private const int IoPollIntervalMs = 10;

        /// <summary>
        /// 输入点位最大数量（bit0 ~ bit31）
        /// </summary>
        private const int MaxInputBits = 32;

        private readonly ILogger<LeadshineEmcController> _logger;

        /// <summary>
        /// 卡号（示例固定值，按现场卡号调整/配置化）
        /// </summary>
        private readonly ushort _cardNo = 8;

        /// <summary>
        /// 端口号（示例默认 0）
        /// </summary>
        private readonly ushort _portNo = 0;

        /// <summary>
        /// 控制器 IP（示例固定值，按现场配置化）
        /// </summary>
        private readonly string? _controllerIp = "192.168.5.11";

        private volatile bool _isAvailable;
        private volatile bool _isInitialized;

        /// <summary>
        /// 关键：外部高频读，直接读这个 volatile 快照数组
        /// </summary>
        private volatile IoPointInfo[] _monitoredIoPoints = Array.Empty<IoPointInfo>();

        /// <summary>
        /// 关键：输入口 32bit 快照（bit0~bit31）
        /// </summary>
        private volatile uint _inputPortSnapshot;

        private readonly object _ioMonitorSync = new();
        private CancellationTokenSource? _ioMonitorCts;
        private Task? _ioMonitorTask;

        public LeadshineEmcController(ILogger<LeadshineEmcController> logger) {
            _logger = logger;
        }

        public void Dispose() {
            StopIoMonitorLoop();

            try {
                LTDMC.dmc_board_close();
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "关闭 EMC 连接时发生异常");
            }
        }

        public EmcControllerStatus Status { get; private set; }

        public int? FaultCode { get; private set; }

        /// <summary>
        /// 当前监控的 IO 点集合（数组实现 IReadOnlyList）
        /// </summary>
        public IReadOnlyList<IoPointInfo> MonitoredIoPoints => Volatile.Read(ref _monitoredIoPoints);

        public event EventHandler<EmcStatusChangedEventArgs>? StatusChanged;

        public event EventHandler<EmcFaultedEventArgs>? Faulted;

        public event EventHandler<EmcInitializedEventArgs>? Initialized;

        public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default) {
            Status = EmcControllerStatus.Initializing;

            // 重试策略：0ms → 300ms → 1s → 2s
            var delays = new[] {
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(300),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
            };

            var policy = Policy
                .HandleResult<bool>(static r => r == false)
                .Or<Exception>()
                .WaitAndRetryAsync(
                    delays,
                    onRetryAsync: (outcome, delay, retryAttempt, _) => {
                        var reason = outcome.Exception?.Message ?? "初始化失败";
                        _logger.LogWarning(
                            "EMC 初始化重试 #{RetryAttempt}，等待 {Delay}ms，原因: {Reason}",
                            retryAttempt, delay.TotalMilliseconds, reason);
                        return Task.CompletedTask;
                    });

            return await policy.ExecuteAsync(async () => {
                try {
                    Status = EmcControllerStatus.Connecting;

                    var isEthernet = !string.IsNullOrWhiteSpace(_controllerIp);
                    var methodName = isEthernet ? "dmc_board_init_eth" : "dmc_board_init";

                    _logger.LogInformation(
                        "正在初始化 EMC，卡号: {CardNo}, 端口: {PortNo}, 模式: {Mode}, IP: {IP}",
                        _cardNo,
                        _portNo,
                        isEthernet ? "以太网" : "PCI",
                        _controllerIp ?? "N/A");

                    // 根据是否配置 IP 选择初始化方式
                    short result;
                    if (isEthernet) {
                        result = LTDMC.dmc_board_init_eth(_cardNo, _controllerIp!);
                        if (result != 0) {
                            _logger.LogError(
                                "【EMC 初始化失败】方法: {Method}, 返回值: {ErrorCode}（预期: 0），卡号: {CardNo}, IP: {IP}",
                                methodName, result, _cardNo, _controllerIp);
                            return false;
                        }
                    }
                    else {
                        result = LTDMC.dmc_board_init();
                        if (result != 0) {
                            _logger.LogError(
                                "【EMC 初始化失败】方法: {Method}, 返回值: {ErrorCode}（预期: 0），卡号: {CardNo}",
                                methodName, result, _cardNo);
                            return false;
                        }
                    }

                    _logger.LogInformation(
                        "【EMC 初始化成功】方法: {Method}, 返回值: {Result}, 卡号: {CardNo}",
                        methodName, result, _cardNo);

                    // 检查总线状态
                    ushort errcode = 0;
                    LTDMC.nmc_get_errcode(_cardNo, _portNo, ref errcode);
                    FaultCode = errcode;

                    // errcode == 45 视为可忽略（保持原逻辑）
                    if (errcode != 0 && errcode != 45) {
                        _logger.LogWarning(
                            "【EMC 总线异常检测】方法: nmc_get_errcode, 错误码: {ErrorCode}（预期: 0），卡号: {CardNo}, 端口: {PortNo}，尝试软复位并重新连接",
                            errcode, _cardNo, _portNo);

                        // 执行软复位
                        LTDMC.dmc_soft_reset(_cardNo);

                        // 关闭连接
                        LTDMC.dmc_board_close();

                        // 等待复位完成
                        await Task.Delay(SoftResetDelayMs, cancellationToken);

                        // 重新初始化连接
                        if (isEthernet) {
                            result = LTDMC.dmc_board_init_eth(_cardNo, _controllerIp!);
                            if (result != 0) {
                                _logger.LogError(
                                    "【EMC 软复位后重新初始化失败】方法: dmc_board_init_eth, 返回值: {ErrorCode}（预期: 0），卡号: {CardNo}, IP: {IP}",
                                    result, _cardNo, _controllerIp);
                                return false;
                            }
                        }
                        else {
                            result = LTDMC.dmc_board_init();
                            if (result != 0) {
                                _logger.LogError(
                                    "【EMC 软复位后重新初始化失败】方法: dmc_board_init, 返回值: {ErrorCode}（预期: 0），卡号: {CardNo}",
                                    result, _cardNo);
                                return false;
                            }
                        }

                        _logger.LogInformation(
                            "【EMC 软复位后重新初始化成功】方法: {Method}, 卡号: {CardNo}",
                            methodName, _cardNo);

                        // 再次检查总线状态
                        LTDMC.nmc_get_errcode(_cardNo, _portNo, ref errcode);
                        FaultCode = errcode;

                        if (errcode != 0) {
                            _logger.LogError(
                                "【EMC 总线异常未恢复】方法: nmc_get_errcode, 错误码: {ErrorCode}（预期: 0），卡号: {CardNo}, 端口: {PortNo}",
                                errcode, _cardNo, _portNo);
                            return false;
                        }

                        _logger.LogInformation(
                            "【EMC 总线异常已恢复】错误码: {ErrorCode}, 卡号: {CardNo}, 端口: {PortNo}",
                            errcode, _cardNo, _portNo);
                    }

                    _isInitialized = true;
                    _isAvailable = true;

                    Status = EmcControllerStatus.Connected;

                    OnInitialized(new EmcInitializedEventArgs {
                        IsSuccess = true
                    });

                    StartIoMonitorLoop();

                    _logger.LogInformation("EMC 初始化完成，卡号: {CardNo}, 端口: {PortNo}", _cardNo, _portNo);
                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    _logger.LogInformation("EMC 初始化已取消");
                    return false;
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "初始化 EMC 时发生异常");
                    PublishFault(ex, "初始化 EMC 异常");
                    return false;
                }
            });
        }

        /// <summary>
        /// 增量设置监控 IO 点：点位不存在则追加，已存在则保留（不做全量替换）
        /// </summary>
        public ValueTask SetMonitoredIoPointsAsync(IReadOnlyList<IoPointInfo> ioPoints, CancellationToken cancellationToken = default) {
            if (ioPoints is null) {
                throw new ArgumentNullException(nameof(ioPoints), "监控 IO 点集合不能为空");
            }

            if (ioPoints.Count == 0) {
                _logger.LogInformation("监控 IO 点集合为空，本次不做更新");
                return ValueTask.CompletedTask;
            }

            lock (_ioMonitorSync) {
                var current = Volatile.Read(ref _monitoredIoPoints);

                // 初次设置：去重+排序后发布
                if (current.Length == 0) {
                    var first = BuildDistinctSortedSnapshot(ioPoints);
                    Volatile.Write(ref _monitoredIoPoints, first);
                    _logger.LogInformation("已初始化监控 IO 点集合，数量: {Count}", first.Length);
                    return ValueTask.CompletedTask;
                }

                // 当前点位集合
                var existingPoints = new HashSet<int>(current.Length);
                for (var i = 0; i < current.Length; i++) {
                    existingPoints.Add(current[i].Point);
                }

                // 传入集合按 Point 去重（最后一个为准）
                var incomingByPoint = new Dictionary<int, IoPointInfo>(ioPoints.Count);
                for (var i = 0; i < ioPoints.Count; i++) {
                    var p = ioPoints[i];
                    incomingByPoint[p.Point] = p;
                }

                // 收集新增点位
                var additions = new List<IoPointInfo>(incomingByPoint.Count);
                foreach (var kv in incomingByPoint) {
                    if (!existingPoints.Contains(kv.Key)) {
                        additions.Add(kv.Value);
                    }
                }

                if (additions.Count == 0) {
                    _logger.LogInformation("监控 IO 点集合未变化（无新增点位），数量: {Count}", current.Length);
                    return ValueTask.CompletedTask;
                }

                // 合并并保持排序
                var merged = new IoPointInfo[current.Length + additions.Count];
                Array.Copy(current, merged, current.Length);
                additions.CopyTo(merged, current.Length);

                Array.Sort(merged, static (a, b) => a.Point.CompareTo(b.Point));

                Volatile.Write(ref _monitoredIoPoints, merged);

                _logger.LogInformation(
                    "监控 IO 点集合已增量追加，原数量: {OldCount}, 新增: {Added}, 现数量: {NewCount}",
                    current.Length, additions.Count, merged.Length);

                return ValueTask.CompletedTask;
            }
        }

        public async Task<bool> ReconnectAsync(CancellationToken cancellationToken = default) {
            try {
                StopIoMonitorLoop();

                try {
                    LTDMC.dmc_board_close();
                }
                catch (Exception ex) {
                    _logger.LogWarning(ex, "重连前关闭 EMC 连接时发生异常");
                }

                _isInitialized = false;
                _isAvailable = false;
                FaultCode = null;
                Status = EmcControllerStatus.Disconnected;

                return await InitializeAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                _logger.LogInformation("EMC 重连已取消");
                return false;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "EMC 重连时发生异常");
                PublishFault(ex, "EMC 重连异常");
                return false;
            }
        }

        public async Task<bool> WriteIoAsync(int point, IoState state, CancellationToken cancellationToken = default) {
            await Task.Yield();

            if (point < 0) {
                _logger.LogWarning("写入 IO 失败：点位编号无效，Point={Point}", point);
                return false;
            }

            try {
                short result = LTDMC.dmc_write_outbit(
                    _cardNo,
                    (ushort)point,
                    state == IoState.High ? (ushort)1 : (ushort)0);

                if (result != 0) {
                    _logger.LogWarning("写入 IO 失败：返回值={ErrorCode}，Point={Point}，State={State}", result, point, state);
                }

                return result == 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                _logger.LogInformation("写入 IO 已取消，Point={Point}", point);
                return false;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "写入 IO 时发生异常，Point={Point}，State={State}", point, state);
                PublishFault(ex, "写入 IO 异常");
                return false;
            }
        }

        private static IoPointInfo[] BuildDistinctSortedSnapshot(IReadOnlyList<IoPointInfo> ioPoints) {
            var dict = new Dictionary<int, IoPointInfo>(ioPoints.Count);
            foreach (var p in ioPoints) {
                dict[p.Point] = p;
            }

            var arr = dict.Values.ToArray();
            Array.Sort(arr, static (a, b) => a.Point.CompareTo(b.Point));
            return arr;
        }

        private void OnInitialized(EmcInitializedEventArgs e) {
            try {
                Initialized?.Invoke(this, e);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "触发 Initialized 事件时发生异常");
            }
        }

        private void StartIoMonitorLoop() {
            lock (_ioMonitorSync) {
                if (_ioMonitorTask is not null && !_ioMonitorTask.IsCompleted) {
                    return;
                }

                _ioMonitorCts?.Cancel();
                _ioMonitorCts?.Dispose();
                _ioMonitorCts = new CancellationTokenSource();

                var token = _ioMonitorCts.Token;

                // LongRunning：独占线程，避免线程池抖动影响高频采样
                _ioMonitorTask = Task.Factory.StartNew(
                    () => RunIoMonitorLoop(token),
                    token,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
            }
        }

        private void StopIoMonitorLoop() {
            lock (_ioMonitorSync) {
                if (_ioMonitorCts is null) {
                    return;
                }

                try {
                    _ioMonitorCts.Cancel();
                    _ioMonitorTask?.Wait(TimeSpan.FromSeconds(2));
                }
                catch (Exception ex) {
                    _logger.LogWarning(ex, "停止 IO 监控线程时发生异常");
                }
                finally {
                    _ioMonitorCts.Dispose();
                    _ioMonitorCts = null;
                    _ioMonitorTask = null;
                }
            }
        }

        private void PublishFault(Exception ex, string message) {
            try {
                Faulted?.Invoke(this, new EmcFaultedEventArgs {
                    Operation = message,
                    Exception = ex,
                    OccurredAtMs = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                });
            }
            catch (Exception invokeEx) {
                _logger.LogError(invokeEx, "触发 Faulted 事件时发生异常");
            }
        }

        private void RunIoMonitorLoop(CancellationToken token) {
            try {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(IoPollIntervalMs));

                while (!token.IsCancellationRequested) {
                    if (!timer.WaitForNextTickAsync(token).AsTask().GetAwaiter().GetResult()) {
                        break;
                    }

                    if (!_isInitialized || !_isAvailable) {
                        continue;
                    }

                    var points = Volatile.Read(ref _monitoredIoPoints);
                    if (points.Length == 0) {
                        continue;
                    }

                    // 批量读取输入口快照
                    TryReadInputPortSnapshot();

                    var snapshot = Volatile.Read(ref _inputPortSnapshot);
                    var nowTicks = Stopwatch.GetTimestamp();

                    // 遍历监控点位
                    for (var i = 0; i < points.Length; i++) {
                        var p = points[i];

                        // 仅处理 0~31
                        if ((uint)p.Point >= MaxInputBits) {
                            continue;
                        }

                        var raw = (snapshot >> p.Point) & 1u;
                        var newState = raw == 0u ? IoState.Low : IoState.High;

                        if (p.State == newState) {
                            continue;
                        }

                        p.UpdateState(newState, nowTicks);
                    }
                }
            }
            catch (OperationCanceledException) {
                // 正常退出
            }
            catch (Exception ex) {
                _logger.LogError(ex, "IO 监控线程发生异常并退出");
                PublishFault(ex, "IO 监控线程异常退出");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TryReadInputPortSnapshot() {
            try {
                // 说明：dmc_read_inport 返回输入口 bitmask，0 可能表示全部为低电平，不能当成失败
                var value = LTDMC.dmc_read_inport(_cardNo, 0);
                Volatile.Write(ref _inputPortSnapshot, value);
            }
            catch (Exception ex) {
                // 异常隔离：不得影响调用链
                _logger.LogWarning(ex, "读取 IO 失败");
            }
        }
    }
}
