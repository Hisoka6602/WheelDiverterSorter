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
    public class LeadshineEmcController : IEmcController {

        /// <summary>
        /// 软复位后重新初始化前的等待时间（毫秒）
        /// </summary>
        private const int SoftResetDelayMs = 500;

        private readonly ILogger<LeadshineEmcController> _logger;
        private readonly ushort _cardNo = 8;
        private readonly ushort _portNo;
        private readonly string? _controllerIp = "192.168.5.11";
        private bool _isAvailable;
        private bool _isInitialized;

        //暂定32个输入口
        private readonly ConcurrentDictionary<ushort, uint> _inputPortCache = new(Enumerable.Range(0, 32)
            .Select(i => new KeyValuePair<ushort, uint>((ushort)i, 0u)));

        // 关键：外部高频读，直接读这个 volatile 快照数组
        private volatile IoPointInfo[] _monitoredIoPoints = [];

        private readonly object _ioMonitorSync = new();
        private CancellationTokenSource? _ioMonitorCts;
        private Task? _ioMonitorTask;

        /// <summary>
        /// IO轮询间隔（毫秒），按现场负载与IO数量调参
        /// </summary>
        private const int IoPollIntervalMs = 10;

        public LeadshineEmcController(ILogger<LeadshineEmcController> logger) {
            _logger = logger;
        }

        public void Dispose() {
            StopIoMonitorLoop();

            try {
                LTDMC.dmc_board_close();
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "关闭EMC连接时发生异常");
            }
        }

        public EmcControllerStatus Status { get; private set; }
        public int? FaultCode { get; private set; }

        // 删掉原自动属性，改为返回快照字段（数组实现 IReadOnlyList<T>）
        public IReadOnlyList<IoPointInfo> MonitoredIoPoints {
            get {
                var monitoredIoPoints = _monitoredIoPoints;
                return Volatile.Read(ref monitoredIoPoints);
            }
        }

        public event EventHandler<EmcStatusChangedEventArgs>? StatusChanged;

        public event EventHandler<EmcFaultedEventArgs>? Faulted;

        public event EventHandler<EmcInitializedEventArgs>? Initialized;

        public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default) {
            // 重试策略：0ms → 300ms → 1s → 2s（参考 ZakYip.Singulation 项目）
            Status = EmcControllerStatus.Initializing;

            var delays = new[]
            {
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(300),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
        };

            var policy = Policy
                .HandleResult<bool>(r => r == false)
                .Or<Exception>()
                .WaitAndRetryAsync(
                    delays,
                    onRetryAsync: (outcome, delay, retryAttempt, _) => {
                        var reason = outcome.Exception?.Message ?? "初始化失败";
                        _logger.LogWarning(
                            "EMC初始化重试 #{RetryAttempt}，等待 {Delay}ms，原因: {Reason}",
                            retryAttempt, delay.TotalMilliseconds, reason);
                        return Task.CompletedTask;
                    });

            return await policy.ExecuteAsync(async () => {
                try {
                    Status = EmcControllerStatus.Connecting;
                    var isEthernet = _controllerIp != null;
                    var methodName = isEthernet ? "dmc_board_init_eth" : "dmc_board_init";

                    _logger.LogInformation(
                        "正在初始化EMC，卡号: {CardNo}, 端口: {PortNo}, 模式: {Mode}, IP: {IP}",
                        _cardNo,
                        _portNo,
                        isEthernet ? "以太网" : "PCI",
                        _controllerIp ?? "N/A");

                    // 根据是否配置IP选择初始化方式
                    short result;
                    if (isEthernet) {
                        // 以太网模式：使用 dmc_board_init_eth
                        result = LTDMC.dmc_board_init_eth(_cardNo, _controllerIp!);
                        if (result != 0) {
                            _logger.LogError(
                                "【EMC初始化失败】方法: {Method}, 返回值: {ErrorCode}（预期: 0），卡号: {CardNo}, IP: {IP}",
                                methodName, result, _cardNo, _controllerIp);
                            return false;
                        }
                    }
                    else {
                        // PCI 模式：使用 dmc_board_init
                        result = LTDMC.dmc_board_init();
                        if (result != 0) {
                            _logger.LogError(
                                "【EMC初始化失败】方法: {Method}, 返回值: {ErrorCode}（预期: 0），卡号: {CardNo}",
                                methodName, result, _cardNo);
                            return false;
                        }
                    }

                    _logger.LogInformation(
                        "【EMC初始化成功】方法: {Method}, 返回值: {Result}, 卡号: {CardNo}",
                        methodName, result, _cardNo);

                    // 检查总线状态
                    ushort errcode = 0;
                    LTDMC.nmc_get_errcode(_cardNo, _portNo, ref errcode);
                    FaultCode = errcode;
                    if (errcode != 0 && errcode != 45) {
                        _logger.LogWarning(
                            "【EMC总线异常检测】方法: nmc_get_errcode, 错误码: {ErrorCode}（预期: 0），卡号: {CardNo}, 端口: {PortNo}，尝试软复位并重新连接",
                            errcode, _cardNo, _portNo);

                        // 执行软复位
                        LTDMC.dmc_soft_reset(_cardNo);

                        // 关闭连接
                        LTDMC.dmc_board_close();

                        // 等待复位完成
                        await Task.Delay(SoftResetDelayMs, cancellationToken);

                        // 重新初始化连接（软复位后必须重新调用 dmc_board_init_eth/dmc_board_init）
                        if (isEthernet) {
                            result = LTDMC.dmc_board_init_eth(_cardNo, _controllerIp!);
                            if (result != 0) {
                                _logger.LogError(
                                    "【EMC软复位后重新初始化失败】方法: dmc_board_init_eth, 返回值: {ErrorCode}（预期: 0），卡号: {CardNo}, IP: {IP}",
                                    result, _cardNo, _controllerIp);
                                return false;
                            }
                        }
                        else {
                            result = LTDMC.dmc_board_init();
                            if (result != 0) {
                                _logger.LogError(
                                    "【EMC软复位后重新初始化失败】方法: dmc_board_init, 返回值: {ErrorCode}（预期: 0），卡号: {CardNo}",
                                    result, _cardNo);
                                return false;
                            }
                        }

                        _logger.LogInformation(
                            "【EMC软复位后重新初始化成功】方法: {Method}, 卡号: {CardNo}",
                            methodName, _cardNo);

                        // 再次检查总线状态
                        LTDMC.nmc_get_errcode(_cardNo, _portNo, ref errcode);
                        if (errcode != 0) {
                            _logger.LogError(
                                "【EMC总线异常未恢复】方法: nmc_get_errcode, 错误码: {ErrorCode}（预期: 0），卡号: {CardNo}, 端口: {PortNo}",
                                errcode, _cardNo, _portNo);
                            return false;
                        }
                        _logger.LogInformation(
                            "【EMC总线异常已恢复】错误码: {ErrorCode}, 卡号: {CardNo}, 端口: {PortNo}",
                            errcode, _cardNo, _portNo);
                    }

                    _isInitialized = true;
                    _isAvailable = true;
                    //初始化完成事件
                    Status = EmcControllerStatus.Connected;
                    OnInitialized(new EmcInitializedEventArgs {
                        IsSuccess = true
                    });
                    StartIoMonitorLoop();
                    _logger.LogInformation("EMC初始化完成，卡号: {CardNo}, 端口: {PortNo}", _cardNo, _portNo);
                    return true;
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "初始化EMC时发生异常");
                    return false;
                }
            });
        }

        public ValueTask SetMonitoredIoPointsAsync(IReadOnlyList<IoPointInfo> ioPoints, CancellationToken cancellationToken = default) {
            if (ioPoints is null) {
                throw new ArgumentNullException(nameof(ioPoints), "监控IO点集合不能为空");
            }

            // 低频操作（配置更新），允许使用 LINQ 做一次性规整
            var snapshot = ioPoints
                .Where(static p => true)
                .DistinctBy(static p => p.Point)
                .OrderBy(static p => p.Point)
                .ToArray();

            // 发布新快照：外部读永远拿到一致数组
            _monitoredIoPoints = snapshot;
            //var monitoredIoPoints = _monitoredIoPoints;
            Volatile.Write(ref _monitoredIoPoints, snapshot);

            _logger.LogInformation("已更新监控IO点集合，数量: {Count}", snapshot.Length);
            return ValueTask.CompletedTask;
        }

        public Task<bool> ReconnectAsync(CancellationToken cancellationToken = default) {
            return Task.FromResult(false);
        }

        public async Task<bool> WriteIoAsync(int point, IoState state, CancellationToken cancellationToken = default) {
            await Task.Yield();
            try {
                short result = LTDMC.dmc_write_outbit(
                    _cardNo,
                    (ushort)point,
                    state == IoState.High ? (ushort)1 : (ushort)0);
                return result == 0;
            }
            catch (Exception e) {
                _logger.LogError($"{e}");
                return false;
            }
        }

        protected virtual void OnInitialized(EmcInitializedEventArgs e) {
            Initialized?.Invoke(this, e);
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
                    TaskCreationOptions.LongRunning,
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
                    _logger.LogWarning(ex, "停止IO监控线程时发生异常");
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
                // 按工程内 EmcFaultedEventArgs 的结构补齐
                // Faulted?.Invoke(this, new EmcFaultedEventArgs(...));

                _logger.LogError(ex, "【EMC异常】{Message}", message);
            }
            catch (Exception invokeEx) {
                _logger.LogError(invokeEx, "触发Faulted事件时发生异常");
            }
        }

        private void RunIoMonitorLoop(CancellationToken token) {
            try {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(IoPollIntervalMs));

                // 采用 Stopwatch ticks，避免 DateTime.Now 高频调用
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

                    var nowTicks = Stopwatch.GetTimestamp();
                    TryReadArrayIoState();
                    // for 循环避免 foreach 捕获与边界额外开销
                    foreach (var p in points) {
                        if (p.Point is >= 0 and <= 32) {
                            var key = (ushort)p.Point;

                            // 避免 KeyNotFoundException；缺失时按 0 处理
                            var raw = _inputPortCache.GetValueOrDefault(key, 0u);

                            // IoPointInfo.State 是枚举时，建议按底层 int 取值（保持原语义）
                            var current = unchecked((uint)(int)p.State);

                            if (raw == current) {
                                continue;
                            }

                            // 明确用采样值决定新状态，避免“翻转”带来的漂移风险
                            var newState = raw == 0u ? IoState.Low : IoState.High;
                            p.UpdateState(newState, nowTicks);
                        }
                    }
                }
            }
            catch (OperationCanceledException) {
                // 正常退出
            }
            catch (Exception ex) {
                _logger.LogError(ex, "IO监控线程发生异常并退出");
                PublishFault(ex, "IO监控线程异常退出");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TryReadArrayIoState() {
            try {
                var dmcReadInport = LTDMC.dmc_read_inport(_cardNo, 0);

                if (dmcReadInport == 0) {
                    _logger.LogWarning(
                        "批量读取输入端口失败，错误码={ErrorCode}",
                        dmcReadInport);
                }

                var ints = ToBits32_LsbFirst(dmcReadInport);

                // 更新缓存
                for (ushort i = 0; i < ints.Length; i++) {
                    _inputPortCache[i] = ints[i];
                }
            }
            catch (Exception ex) {
                // 异常隔离：不得影响调用链
                _logger.LogWarning(ex, "读取IO失败");
            }
        }

        /// <summary>
        /// 以“从低位到高位（LSB->MSB）”输出 32 位 bit 数组（0/1）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint[] ToBits32_LsbFirst(uint value) {
            var v = value;

            // 0 表示最低位（bit0），31 表示最高位（bit31）
            return Enumerable.Range(0, 32)
                .Select(i => (v >> i) & 1u)
                .ToArray();
        }
    }
}
