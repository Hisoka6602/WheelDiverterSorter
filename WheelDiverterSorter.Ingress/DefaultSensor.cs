using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WheelDiverterSorter.Core;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Models;
using WheelDiverterSorter.Core.Manager;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Ingress {

    /// <summary>
    /// 默认传感器监控器（从 EMC 快照读取点位电平）
    /// </summary>
    public sealed class DefaultSensor : ISensorManager, IDisposable {
        private readonly IReadOnlyList<SensorOptions> _optionsInfos;
        private readonly ILogger<DefaultSensor> _logger;
        private readonly IEmcController _emcController;

        private readonly object _gate = new();

        private CancellationTokenSource? _monitoringCts;
        private Task? _monitoringTask;

        // point -> option / sensor / runtime（避免热路径反复扫描）
        private readonly Dictionary<int, SensorOptions> _optionByPoint;

        private readonly Dictionary<int, SensorInfo> _sensorByPoint;
        private readonly Dictionary<int, SensorRuntimeState> _runtimeByPoint;

        // 空快照节流日志（避免刷屏）
        private long _lastEmptySnapshotLogTicks;

        public SensorMonitoringStatus Status { get; private set; } = SensorMonitoringStatus.Stopped;

        public bool IsMonitoring { get; private set; }

        public IReadOnlyList<SensorInfo> Sensors { get; }

        public event EventHandler<SensorStateChangedEventArgs>? SensorStateChanged;

        public event EventHandler<SensorMonitoringStatusChangedEventArgs>? MonitoringStatusChanged;

        public event EventHandler<SensorFaultedEventArgs>? Faulted;

        public DefaultSensor(
            IOptions<List<SensorOptions>> optionsInfos,
            ILogger<DefaultSensor> logger,
            IEmcController emcController) {
            _optionsInfos = optionsInfos.Value ?? [];
            _logger = logger;
            _emcController = emcController;

            _optionByPoint = new Dictionary<int, SensorOptions>(_optionsInfos.Count);
            _sensorByPoint = new Dictionary<int, SensorInfo>(_optionsInfos.Count);
            _runtimeByPoint = new Dictionary<int, SensorRuntimeState>(_optionsInfos.Count);

            var sensors = new List<SensorInfo>(_optionsInfos.Count);

            foreach (var opt in _optionsInfos) {
                // 同点位重复配置直接忽略，避免运行期语义不一致
                if (!_optionByPoint.TryAdd(opt.Point, opt)) {
                    continue;
                }

                var info = new SensorInfo {
                    Point = opt.Point,
                    Type = opt.Type,
                    State = default
                };

                sensors.Add(info);
                _sensorByPoint.Add(opt.Point, info);

                _runtimeByPoint.Add(opt.Point, new SensorRuntimeState {
                    LastState = null,
                    LastChangedTicks = 0
                });
            }

            Sensors = sensors;
        }

        public void Dispose() {
            _ = StopMonitoringAsync();
        }

        public async ValueTask StartMonitoringAsync(CancellationToken cancellationToken = default) {
            StopMonitoringInternal("StartMonitoringAsync 触发重启");

            if (_optionByPoint.Count == 0) {
                SetStatus(SensorMonitoringStatus.Stopped);
                IsMonitoring = false;
                return;
            }

            ResetRuntimeStates();

            // 尝试同步监控点位到 EMC（不要求 EMC 必须接受，失败隔离）
            await TrySyncMonitoredIoPointsToEmcAsync(cancellationToken).ConfigureAwait(false);

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _monitoringCts = linkedCts;

            SetStatus(SensorMonitoringStatus.Monitoring);
            IsMonitoring = true;

            _monitoringTask = Task.Run(() => MonitoringLoopAsync(linkedCts.Token), CancellationToken.None);
        }

        public async ValueTask StopMonitoringAsync(CancellationToken cancellationToken = default) {
            Task? task;
            lock (_gate) {
                if (_monitoringCts is null) {
                    SetStatus(SensorMonitoringStatus.Stopped);
                    IsMonitoring = false;
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
                    _logger.LogWarning(ex, "停止传感器监控线程异常。");
                }
            }

            lock (_gate) {
                try { _monitoringCts?.Dispose(); } catch { /* ignore */ }
                _monitoringCts = null;
                _monitoringTask = null;
            }

            SetStatus(SensorMonitoringStatus.Stopped);
            IsMonitoring = false;
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

                _logger.LogInformation("传感器监控已停止：{Reason}", reason);
            }

            SetStatus(SensorMonitoringStatus.Stopped);
            IsMonitoring = false;
        }

        private async ValueTask TrySyncMonitoredIoPointsToEmcAsync(CancellationToken token) {
            try {
                var ioPoints = new List<IoPointInfo>(_optionByPoint.Count);

                foreach (var opt in _optionByPoint.Values) {
                    ioPoints.Add(new IoPointInfo {
                        Point = opt.Point,
                        Type = opt.Type,
                        Name = opt.SensorName,
                        DebounceWindowMs = opt.DebounceWindowMs
                    });
                }

                await _emcController.SetMonitoredIoPointsAsync(ioPoints, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // 外部取消属于正常路径
            }
            catch (Exception ex) {
                // 异常隔离：不得影响调用链
                _logger.LogWarning(ex, "同步监控点位到 EMC 失败。");
                RaiseFaulted("同步监控点位到 EMC 失败。", ex);
            }
        }

        private async Task MonitoringLoopAsync(CancellationToken token) {
            var pollIntervalMs = GetMinPollIntervalMs(_optionByPoint.Values);

            try {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(pollIntervalMs));

                while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token).ConfigureAwait(false)) {
                    var points = _emcController.MonitoredIoPoints;
                    if (points.Count == 0) {
                        ThrottledLogEmptySnapshot();
                        continue;
                    }

                    // 遍历 EMC 快照：只处理被关注的点位
                    foreach (var p in points) {
                        if (!_optionByPoint.TryGetValue(p.Point, out var opt)) {
                            continue;
                        }

                        ProcessSample(opt, p.State);
                    }
                }
            }
            catch (OperationCanceledException) {
                // 正常停止
            }
            catch (Exception ex) {
                _logger.LogError(ex, "传感器监控循环异常。");
                RaiseFaulted("传感器监控循环异常。", ex);
                SetStatus(SensorMonitoringStatus.Faulted);
            }
        }

        private void ProcessSample(SensorOptions opt, IoState current) {
            if (!_runtimeByPoint.TryGetValue(opt.Point, out var rt)) {
                rt = new SensorRuntimeState();
            }

            // 首次采样：仅记录，不上报
            if (rt.LastState is null) {
                rt.LastState = current;
                rt.LastChangedTicks = 0;
                _runtimeByPoint[opt.Point] = rt;

                SetSensorStateFast(opt.Point, current);
                return;
            }

            var last = rt.LastState.Value;

            // 无变化：热路径最常见分支
            if (last.Equals(current)) {
                return;
            }

            // 防抖：窗口内不重复上报
            if (!PassDebounce(ref rt.LastChangedTicks, opt.DebounceWindowMs)) {
                return;
            }

            rt.LastState = current;
            _runtimeByPoint[opt.Point] = rt;

            SetSensorStateFast(opt.Point, current);

            var args = new SensorStateChangedEventArgs(
                opt.Point,
                opt.SensorName,
                opt.Type,
                last,
                current,
                opt.TriggerState,
                DateTimeOffset.Now.ToUnixTimeMilliseconds());

            RaiseSensorStateChanged(args);
        }

        private void SetSensorStateFast(int point, IoState state) {
            if (_sensorByPoint.TryGetValue(point, out var sensor)) {
                sensor.State = state;
            }
        }

        private void ResetRuntimeStates() {
            // 仅重置值，避免重建字典
            foreach (var key in _runtimeByPoint.Keys.ToArray()) {
                _runtimeByPoint[key] = new SensorRuntimeState {
                    LastState = null,
                    LastChangedTicks = 0
                };
            }
        }

        private void ThrottledLogEmptySnapshot() {
            var now = DateTimeOffset.Now.Ticks;
            var last = Interlocked.Read(ref _lastEmptySnapshotLogTicks);

            // 1 秒节流
            if (last != 0 && (now - last) < TimeSpan.TicksPerSecond) {
                return;
            }

            Interlocked.Exchange(ref _lastEmptySnapshotLogTicks, now);
            _logger.LogDebug("EMC 监控点位快照为空，传感器状态暂不可读取。");
        }

        private static int GetMinPollIntervalMs(IEnumerable<SensorOptions> options) {
            var min = int.MaxValue;

            foreach (var t in options) {
                var v = t.PollIntervalMs <= 0 ? 10 : t.PollIntervalMs;
                if (v < min) {
                    min = v;
                }
            }

            return min == int.MaxValue ? 10 : min;
        }

        private static bool PassDebounce(ref long lastTicksField, int debounceWindowMs) {
            var windowMs = debounceWindowMs <= 0 ? 30 : debounceWindowMs;

            var nowTicks = DateTimeOffset.Now.Ticks;
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

        private void RaiseSensorStateChanged(SensorStateChangedEventArgs args) {
            try {
                SensorStateChanged?.Invoke(this, args);
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "SensorStateChanged 回调异常。");
                RaiseFaulted("SensorStateChanged 回调异常。", ex);
            }
        }

        private void SetStatus(SensorMonitoringStatus newStatus) {
            var old = Status;
            if (old == newStatus) {
                return;
            }

            Status = newStatus;

            try {
                MonitoringStatusChanged?.Invoke(
                    this,
                    new SensorMonitoringStatusChangedEventArgs(old, newStatus, DateTimeOffset.Now));
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "MonitoringStatusChanged 回调异常。");
                RaiseFaulted("MonitoringStatusChanged 回调异常。", ex);
            }
        }

        private void RaiseFaulted(string message, Exception? exception) {
            try {
                Faulted?.Invoke(this, new SensorFaultedEventArgs(message, exception, DateTimeOffset.Now));
            }
            catch {
                // 事件回调异常隔离
            }
        }

        private struct SensorRuntimeState {
            public IoState? LastState;
            public long LastChangedTicks;
        }
    }
}
