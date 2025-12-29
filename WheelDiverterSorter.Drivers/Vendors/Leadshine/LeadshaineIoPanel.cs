using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WheelDiverterSorter.Core;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Models;
using WheelDiverterSorter.Core.Manager;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Drivers.Vendors.Leadshine {
    public sealed class LeadshaineIoPanel : IIoPanel {
        private readonly IEmcController _emcController;
        private readonly ISystemStateManager _systemStateManager;
        private readonly ILogger<LeadshaineIoPanel> _logger;

        private readonly object _gate = new();

        private CancellationTokenSource? _monitoringCts;
        private Task? _monitoringTask;

        private IoPanelButtonOptions[] _options = Array.Empty<IoPanelButtonOptions>();

        // point -> option（避免每次 tick 反复遍历 options）
        private readonly Dictionary<int, IoPanelButtonOptions> _optionByPoint = new();

        // point -> runtime state（每个按钮独立状态）
        private readonly Dictionary<int, ButtonRuntimeState> _runtimeByPoint = new();

        // 复用快照容器，避免每 tick 分配
        private readonly Dictionary<int, IoState> _snapshotStates = new();

        // 急停是否处于“已激活（任一急停按下）”的锁存状态
        private int _isEmergencyStopLatched; // 0=false, 1=true

        public LeadshaineIoPanel(
            IEmcController emcController,
            ISystemStateManager systemStateManager,
            List<IoPanelButtonOptions> ioPanelButtonOptionsInfos,
            ILogger<LeadshaineIoPanel> logger) {
            _emcController = emcController;
            _systemStateManager = systemStateManager;
            _logger = logger;

            _options = ioPanelButtonOptionsInfos is { Count: > 0 }
                ? CopyOptions(ioPanelButtonOptionsInfos)
                : Array.Empty<IoPanelButtonOptions>();

            RebuildCaches(_options);
        }

        public void Dispose() {
            _ = StopMonitoringAsync();
        }

        public event EventHandler<IoPanelButtonPressedEventArgs>? StartButtonPressed;

        public event EventHandler<IoPanelButtonPressedEventArgs>? StopButtonPressed;

        public event EventHandler<IoPanelButtonPressedEventArgs>? EmergencyStopButtonPressed;

        public event EventHandler<IoPanelButtonPressedEventArgs>? ResetButtonPressed;

        public event EventHandler<IoPanelButtonReleasedEventArgs>? EmergencyStopButtonReleased;

        public event EventHandler<IoPanelFaultedEventArgs>? Faulted;

        public ValueTask StartMonitoringAsync(
            CancellationToken cancellationToken = default) {
            StopMonitoringInternal("StartMonitoringAsync 触发重启");

            RebuildCaches(_options);

            Interlocked.Exchange(ref _isEmergencyStopLatched, 0);

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _monitoringCts = linkedCts;

            _monitoringTask = Task.Run(() => MonitoringLoopAsync(linkedCts.Token), CancellationToken.None);
            return ValueTask.CompletedTask;
        }

        public async ValueTask StopMonitoringAsync(CancellationToken cancellationToken = default) {
            Task? task;
            lock (_gate) {
                if (_monitoringCts is null) {
                    return;
                }

                try { _monitoringCts.Cancel(); } catch { /* ignore */ }
                task = _monitoringTask;
            }

            if (task is not null) {
                try {
                    await task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    // 停止阶段允许取消
                }
                catch (Exception ex) {
                    _logger.LogWarning(ex, "停止监控线程异常。");
                }
            }

            lock (_gate) {
                try { _monitoringCts?.Dispose(); } catch { /* ignore */ }
                _monitoringCts = null;
                _monitoringTask = null;
            }
        }

        private void StopMonitoringInternal(string reason) {
            lock (_gate) {
                if (_monitoringCts is null) {
                    return;
                }

                try { _monitoringCts.Cancel(); } catch { /* ignore */ }
                try { _monitoringCts.Dispose(); } catch { /* ignore */ }

                _monitoringCts = null;
                _monitoringTask = null;

                _logger.LogInformation("IO 面板监控已停止：{Reason}", reason);
            }
        }

        private async Task MonitoringLoopAsync(CancellationToken token) {
            if (_options.Length == 0) {
                return;
            }

            var pollIntervalMs = GetMinPollIntervalMs(_options);

            try {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(pollIntervalMs));

                while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token).ConfigureAwait(false)) {
                    BuildSnapshotStates();

                    // 处理每个按钮的边沿
                    for (var i = 0; i < _options.Length; i++) {
                        var option = _options[i];

                        if (!_snapshotStates.TryGetValue(option.Point, out var current)) {
                            continue;
                        }

                        if (!_runtimeByPoint.TryGetValue(option.Point, out var runtime)) {
                            runtime = new ButtonRuntimeState();
                        }

                        // 首次采样仅记录
                        if (runtime.LastState is null) {
                            runtime.LastState = current;
                            _runtimeByPoint[option.Point] = runtime;
                            continue;
                        }

                        var last = runtime.LastState.Value;
                        runtime.LastState = current;
                        _runtimeByPoint[option.Point] = runtime;

                        var isPressedEdge = !last.Equals(option.TriggerState) && current.Equals(option.TriggerState);
                        var isReleasedEdge = last.Equals(option.TriggerState) && !current.Equals(option.TriggerState);

                        if (isPressedEdge) {
                            if (!PassDebounce(ref runtime.LastPressedTicks, option.DebounceWindowMs)) {
                                _runtimeByPoint[option.Point] = runtime;
                                continue;
                            }

                            _runtimeByPoint[option.Point] = runtime;

                            var pressedArgs = BuildPressedArgs(option);

                            if (option.ButtonType == IoPanelButtonType.EmergencyStop) {
                                HandleEmergencyStopPressed(pressedArgs);
                            }
                            else {
                                RaiseButtonPressed(pressedArgs);
                            }

                            continue;
                        }

                        // 多急停释放：只有全部急停都已释放时才回调
                        if (isReleasedEdge && option.ButtonType == IoPanelButtonType.EmergencyStop) {
                            if (!PassDebounce(ref runtime.LastReleasedTicks, option.DebounceWindowMs)) {
                                _runtimeByPoint[option.Point] = runtime;
                                continue;
                            }

                            _runtimeByPoint[option.Point] = runtime;

                            var releasedArgs = BuildReleasedArgs(option);
                            HandleEmergencyStopReleased(releasedArgs);
                        }
                    }

                    // 兜底：若系统启动时急停已经处于按下状态（没有按下边沿），需要锁存并切状态
                    if (IsAnyEmergencyStopActive() && Interlocked.CompareExchange(ref _isEmergencyStopLatched, 1, 0) == 0) {
                        _ = ChangeStateSafeAsync(SystemState.EmergencyStop);
                    }
                }
            }
            catch (OperationCanceledException) {
                // 正常停止
            }
            catch (Exception ex) {
                _logger.LogError(ex, "IO 面板监控循环异常。");
                RaiseFaulted("IO 面板监控循环异常。", ex);
            }
        }

        private void BuildSnapshotStates() {
            _snapshotStates.Clear();

            try {
                var list = _emcController.MonitoredIoPoints;

                for (var i = 0; i < list.Count; i++) {
                    var p = list[i];
                    if (_optionByPoint.ContainsKey(p.Point)) {
                        _snapshotStates[p.Point] = p.State;
                    }
                }
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "读取 MonitoredIoPoints 异常。");
                RaiseFaulted("读取 MonitoredIoPoints 异常。", ex);
            }
        }

        private bool IsAnyEmergencyStopActive() {
            // 任一急停电平等于各自 TriggerState，则认为急停激活
            for (var i = 0; i < _options.Length; i++) {
                var opt = _options[i];
                if (opt.ButtonType != IoPanelButtonType.EmergencyStop) {
                    continue;
                }

                // 快照缺失时按安全策略：认为仍未释放（避免误判“全部已释放”）
                if (!_snapshotStates.TryGetValue(opt.Point, out var state)) {
                    return true;
                }

                if (state.Equals(opt.TriggerState)) {
                    return true;
                }
            }

            return false;
        }

        private bool AreAllEmergencyStopsReleased() {
            // 全部急停都满足 current != TriggerState，才算“全部已释放”
            var hasAnyEStop = false;

            for (var i = 0; i < _options.Length; i++) {
                var opt = _options[i];
                if (opt.ButtonType != IoPanelButtonType.EmergencyStop) {
                    continue;
                }

                hasAnyEStop = true;

                if (!_snapshotStates.TryGetValue(opt.Point, out var state)) {
                    return false;
                }

                if (state.Equals(opt.TriggerState)) {
                    return false;
                }
            }

            // 没有配置急停时，不触发释放逻辑
            return hasAnyEStop;
        }

        private void HandleEmergencyStopPressed(IoPanelButtonPressedEventArgs args) {
            try {
                // 仅首次进入急停锁存时切状态
                if (Interlocked.CompareExchange(ref _isEmergencyStopLatched, 1, 0) == 0) {
                    _ = ChangeStateSafeAsync(SystemState.EmergencyStop);
                }

                EmergencyStopButtonPressed?.Invoke(this, args);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "触发急停按下事件异常，Point={Point}", args.Point);
                RaiseFaulted("触发急停按下事件异常。", ex);
            }
        }

        private void HandleEmergencyStopReleased(IoPanelButtonReleasedEventArgs args) {
            try {
                // 释放前必须确认全部急停都已释放
                if (!AreAllEmergencyStopsReleased()) {
                    return;
                }

                // 仅从“锁存=1”切回 0 的那个瞬间触发一次释放事件
                if (Interlocked.CompareExchange(ref _isEmergencyStopLatched, 0, 1) != 1) {
                    return;
                }

                // 急停释放：按状态机规则 EmergencyStop 只能切到 Ready
                _ = ChangeStateSafeAsync(SystemState.Ready);
                EmergencyStopButtonReleased?.Invoke(this, args);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "触发急停释放事件异常，Point={Point}", args.Point);
                RaiseFaulted("触发急停释放事件异常。", ex);
            }
        }

        private void RaiseButtonPressed(IoPanelButtonPressedEventArgs args) {
            try {
                switch (args.ButtonType) {
                    case IoPanelButtonType.Start:
                        _ = ChangeStateSafeAsync(SystemState.Running);
                        StartButtonPressed?.Invoke(this, args);
                        break;

                    case IoPanelButtonType.Stop:
                        _ = ChangeStateSafeAsync(SystemState.Paused);
                        StopButtonPressed?.Invoke(this, args);
                        break;

                    case IoPanelButtonType.Reset:
                        _ = ChangeStateSafeAsync(SystemState.Booting);
                        ResetButtonPressed?.Invoke(this, args);
                        break;

                    case IoPanelButtonType.EmergencyStop:
                        // EmergencyStop 由 HandleEmergencyStopPressed 处理
                        EmergencyStopButtonPressed?.Invoke(this, args);
                        break;

                    default:
                        _logger.LogInformation("收到未处理的按钮类型：{ButtonType}，Point={Point}", args.ButtonType, args.Point);
                        break;
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "触发按钮按下事件异常，ButtonType={ButtonType}, Point={Point}", args.ButtonType, args.Point);
                RaiseFaulted("触发按钮按下事件异常。", ex);
            }
        }

        private async Task ChangeStateSafeAsync(SystemState targetState) {
            try {
                await _systemStateManager.ChangeStateAsync(targetState).ConfigureAwait(false);
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "系统状态切换异常，TargetState={TargetState}", targetState);
                RaiseFaulted("系统状态切换异常。", ex);
            }
        }

        private void RaiseFaulted(string message, Exception? exception) {
            try {
                Faulted?.Invoke(this, new IoPanelFaultedEventArgs(message, exception, DateTimeOffset.UtcNow));
            }
            catch {
                // 事件回调异常隔离
            }
        }

        private void RebuildCaches(IoPanelButtonOptions[] options) {
            _optionByPoint.Clear();
            _runtimeByPoint.Clear();
            _snapshotStates.Clear();

            for (var i = 0; i < options.Length; i++) {
                var opt = options[i];
                _optionByPoint[opt.Point] = opt;
                _runtimeByPoint[opt.Point] = new ButtonRuntimeState();
            }
        }

        private static bool PassDebounce(ref long lastTicksField, int debounceWindowMs) {
            var windowMs = debounceWindowMs <= 0 ? 30 : debounceWindowMs;

            var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
            var lastTicks = Interlocked.Read(ref lastTicksField);

            if (lastTicks != 0) {
                var elapsedMs = (nowTicks - lastTicks) / TimeSpan.TicksPerMillisecond;
                if (elapsedMs < windowMs) {
                    return false;
                }
            }

            Interlocked.Exchange(ref lastTicksField, nowTicks);
            return true;
        }

        private static int GetMinPollIntervalMs(IoPanelButtonOptions[] options) {
            var min = int.MaxValue;

            for (var i = 0; i < options.Length; i++) {
                var v = options[i].PollIntervalMs <= 0 ? 5 : options[i].PollIntervalMs;
                if (v < min) {
                    min = v;
                }
            }

            return min == int.MaxValue ? 5 : min;
        }

        private IoPanelButtonPressedEventArgs BuildPressedArgs(IoPanelButtonOptions option) {
            return new IoPanelButtonPressedEventArgs {
                ButtonType = option.ButtonType,
                Point = new IoPointInfo {
                    Point = option.Point,
                    Type = IoPointType.PanelButton,
                    Name = option.ButtonName,
                    DebounceWindowMs = option.DebounceWindowMs
                },
                OccurredAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        private IoPanelButtonReleasedEventArgs BuildReleasedArgs(IoPanelButtonOptions option) {
            // 字段按当前项目现有结构构造；若类型字段不同，按真实定义对齐即可

            return new IoPanelButtonReleasedEventArgs(option.Point, option.ButtonType,
                option.ButtonName, DateTimeOffset.Now);
        }

        private static IoPanelButtonOptions[] CopyOptions(IReadOnlyList<IoPanelButtonOptions> list) {
            var arr = new IoPanelButtonOptions[list.Count];
            for (var i = 0; i < list.Count; i++) {
                arr[i] = list[i];
            }

            return arr;
        }

        private struct ButtonRuntimeState {
            public IoState? LastState;
            public long LastPressedTicks;
            public long LastReleasedTicks;
        }
    }
}
