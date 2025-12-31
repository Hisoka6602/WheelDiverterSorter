using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WheelDiverterSorter.Core;
using System.Threading.Channels;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using WheelDiverterSorter.Execution;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Models;
using WheelDiverterSorter.Core.Manager;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Host.Servers {
    /// <summary>
    /// 位置(队列服务)
    /// </summary>

    public sealed class PositionQueueHostedService : BackgroundService {
        private readonly ILogger<PositionQueueHostedService> _logger;
        private readonly IOptions<List<ConveyorSegmentOptions>> _conveyorSegmentOptions;
        private readonly IOptions<List<PositionOptions>> _positionOptions;
        private readonly IOptions<List<SensorOptions>> _sensorOptions;
        private readonly IWheelDiverterManager _wheelDiverterManager;
        private readonly IParcelManager _parcelManager;
        private readonly IPositionQueueManager _positionQueueManager;
        private readonly ISensorManager _sensorManager;

        // Lookup caches（只读快照）
        private Dictionary<long, PositionOptions> _positionByFrontSensorId = new();

        private Dictionary<int, PositionOptions> _positionByIndex = new();
        private Dictionary<int, SensorOptions> _sensorByPoint = new();
        private Dictionary<long, ConveyorSegmentOptions> _segmentById = new();
        private Dictionary<long, int> _segmentTransitMsById = new();
        private Dictionary<int, IWheelDiverter> _diverterById = new();
        private int _lastDiverterId;

        // 强顺序 Positions 的 O(1) next 映射
        private Dictionary<int, int> _nextPositionByIndex = new();

        // 每个 positionIndex 一个 Actor
        private readonly ConcurrentDictionary<int, PositionActor> _actors = new();

        public PositionQueueHostedService(
            ILogger<PositionQueueHostedService> logger,
            IOptions<List<ConveyorSegmentOptions>> conveyorSegmentOptions,
            IOptions<List<PositionOptions>> positionOptions,
            IOptions<List<SensorOptions>> sensorOptions,
            IWheelDiverterManager wheelDiverterManager,
            IParcelManager parcelManager,
            IPositionQueueManager positionQueueManager,
            ISensorManager sensorManager) {
            _logger = logger;
            _conveyorSegmentOptions = conveyorSegmentOptions;
            _positionOptions = positionOptions;
            _sensorOptions = sensorOptions;
            _wheelDiverterManager = wheelDiverterManager;
            _parcelManager = parcelManager;
            _positionQueueManager = positionQueueManager;
            _sensorManager = sensorManager;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            BuildLookupCaches();

            foreach (var pos in _positionOptions.Value.OrderBy(o => o.PositionIndex)) {
                var ok = await _positionQueueManager.CreatePositionAsync(pos.PositionIndex, stoppingToken).ConfigureAwait(false);
                if (ok) {
                    Log.PositionCreated(_logger, pos.PositionIndex, pos.FrontSensorId, (int)pos.DiverterId);
                }

                _ = GetOrCreateActor(pos.PositionIndex);
            }

            // Positions 强顺序 next 映射（基于当前 PositionQueueManager 的 Positions 快照）
            BuildNextPositionMap();

            _sensorManager.SensorStateChanged += OnSensorStateChanged;
            Log.Subscribed(_logger);

            try {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
            }
            finally {
                _sensorManager.SensorStateChanged -= OnSensorStateChanged;
                Log.Unsubscribed(_logger);

                foreach (var actor in _actors.Values) {
                    actor.Stop("Host stopping");
                }
            }
        }

        private void BuildLookupCaches() {
            _positionByFrontSensorId = _positionOptions.Value
                .GroupBy(x => x.FrontSensorId)
                .Select(x => x.First())
                .ToDictionary(x => x.FrontSensorId, x => x);

            _positionByIndex = _positionOptions.Value
                .GroupBy(x => x.PositionIndex)
                .Select(x => x.First())
                .ToDictionary(x => x.PositionIndex, x => x);

            _sensorByPoint = _sensorOptions.Value
                .GroupBy(x => x.Point)
                .Select(x => x.First())
                .ToDictionary(x => x.Point, x => x);

            _segmentById = _conveyorSegmentOptions.Value
                .Where(x => x.IsValid)
                .GroupBy(x => x.SegmentId)
                .Select(x => x.First())
                .ToDictionary(x => x.SegmentId, x => x);

            // 预计算 transitMs（整数四舍五入），避免热路径 decimal
            _segmentTransitMsById = _segmentById.Values.ToDictionary(
                x => x.SegmentId,
                x => {
                    if (x.SpeedMmps <= 0) return 0;

                    // transitMs = round((LengthMm / SpeedMmps) * 1000)
                    // 使用整数四舍五入：(len*1000 + speed/2)/speed
                    var len = (long)x.LengthMm;
                    var speed = (long)x.SpeedMmps;
                    var ms = ((len * 1000L) + (speed / 2L)) / speed;
                    if (ms <= 0) return 0;
                    return ms > int.MaxValue ? int.MaxValue : (int)ms;
                });

            _diverterById = _wheelDiverterManager.Diverters
                .GroupBy(x => x.DiverterId)
                .Select(x => x.First())
                .ToDictionary(x => x.DiverterId, x => x);

            _lastDiverterId = _wheelDiverterManager.Diverters.LastOrDefault()?.DiverterId ?? 0;
        }

        private void BuildNextPositionMap() {
            var positions = _positionQueueManager.Positions;
            var map = new Dictionary<int, int>(positions.Count);

            for (var i = 0; i < positions.Count - 1; i++) {
                map[positions[i]] = positions[i + 1];
            }

            _nextPositionByIndex = map;
        }

        private void OnSensorStateChanged(object? sender, SensorStateChangedEventArgs args) {
            if (args.SensorType != IoPointType.DiverterPositionSensor) {
                return;
            }

            // 不创建 async 状态机；直接投递到 position Actor
            if (!_positionByFrontSensorId.TryGetValue(args.Point, out var pos)) {
                Log.SensorUnbound(_logger, args.Point, args.NewState, args.OccurredAtMs);
                return;
            }

            var actor = GetOrCreateActor(pos.PositionIndex);
            actor.TryPost(args);
        }

        private PositionActor GetOrCreateActor(int positionIndex)
            => _actors.GetOrAdd(positionIndex, static (idx, owner) => new PositionActor(idx, owner), this);

        // ---------------- Actor ----------------

        private sealed class PositionActor {
            private readonly int _positionIndex;
            private readonly PositionQueueHostedService _owner;

            private readonly Channel<SensorStateChangedEventArgs> _channel;
            private readonly CancellationTokenSource _cts = new();

            public PositionActor(int positionIndex, PositionQueueHostedService owner) {
                _positionIndex = positionIndex;
                _owner = owner;

                _channel = Channel.CreateUnbounded<SensorStateChangedEventArgs>(new UnboundedChannelOptions {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = true
                });

                _ = Task.Run(PumpAsync);
            }

            public void Stop(string reason) {
                _channel.Writer.TryComplete();
                _cts.Cancel();
            }

            public void TryPost(SensorStateChangedEventArgs args) {
                // unbounded channel：不阻塞、不等待
                _channel.Writer.TryWrite(args);
            }

            private async Task PumpAsync() {
                var reader = _channel.Reader;

                try {
                    while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false)) {
                        while (reader.TryRead(out var args)) {
                            await _owner.HandlePositionTriggerAsync(_positionIndex, args).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException) {
                }
                catch (Exception ex) {
                    Log.ActorPumpFaulted(_owner._logger, _positionIndex, ex);
                }
            }
        }

        // ---------------- Core Handler per Position ----------------

        private async Task HandlePositionTriggerAsync(int positionIndex, SensorStateChangedEventArgs args) {
            try {
                // 位置绑定（由 point -> pos 缓存，O(1)）
                if (!_positionByFrontSensorId.TryGetValue(args.Point, out var pos) || pos.PositionIndex != positionIndex) {
                    Log.SensorUnbound(_logger, args.Point, args.NewState, args.OccurredAtMs);
                    return;
                }

                if (!_sensorByPoint.TryGetValue(args.Point, out var sensorOpt)) {
                    Log.SensorConfigMissing(_logger, args.Point, pos.PositionIndex, args.OccurredAtMs);
                    return;
                }

                if (sensorOpt.TriggerState != args.NewState) {
                    Log.SensorStateMismatch(_logger, args.Point, pos.PositionIndex, sensorOpt.TriggerState, args.NewState);
                    return;
                }

                if (_parcelManager.Parcels.Count <= 0) {
                    Log.NoParcels(_logger, pos.PositionIndex, args.Point, args.OccurredAtMs);
                    return;
                }

                var occurredMs = args.OccurredAtMs;
                var occurredAt = DateTimeOffset.FromUnixTimeMilliseconds(occurredMs);

                // 丢失成立后推进队头：最多 32 次保护
                for (var loop = 0; loop < 32; loop++) {
                    var peekResult = await _positionQueueManager.PeekFirstTaskAfterPruneAsync(
                        pos.PositionIndex,
                        maxPruneCount: 64,
                        reason: "SensorTriggered-PruneInvalidHead").ConfigureAwait(false);

                    if (!peekResult.HasTask) {
                        Log.PeekEmptyOrLimit(_logger, pos.PositionIndex, args.Point, occurredMs, peekResult.PrunedCount, peekResult.IsPruneLimitReached);
                        return;
                    }

                    var head = peekResult.Task;
                    var earliestMs = head.EarliestDequeueAt.ToUnixTimeMilliseconds();

                    if (occurredMs < earliestMs) {
                        Log.EarlyTriggered(_logger, pos.PositionIndex, head.ParcelId, occurredMs, earliestMs, earliestMs - occurredMs);
                        return;
                    }

                    // 丢失判定：必须 LostDecisionAt
                    if (head.LostDecisionAt is not null) {
                        var lostDecisionMs = head.LostDecisionAt.Value.ToUnixTimeMilliseconds();
                        if (occurredMs > lostDecisionMs) {
                            Log.LostConfirmed(_logger, pos.PositionIndex, head.ParcelId, occurredMs, lostDecisionMs);

                            var dqOk = await _positionQueueManager.DequeueAsync(pos.PositionIndex, "LostConfirmed-DequeueHead").ConfigureAwait(false);
                            if (!dqOk) {
                                Log.LostDequeueFailed(_logger, pos.PositionIndex, head.ParcelId);
                                return;
                            }

                            var invalidated = await _positionQueueManager.InvalidateTasksAfterPositionAsync(
                                pos.PositionIndex, head.ParcelId, "LostConfirmed-InvalidateAfter").ConfigureAwait(false);

                            Log.InvalidateAfter(_logger, pos.PositionIndex, head.ParcelId, invalidated);

                            // 用同一次触发继续尝试处理新队头
                            continue;
                        }
                    }

                    // 命中当前队首：出队
                    var deqOk = await _positionQueueManager.DequeueAsync(pos.PositionIndex, "SensorMatched-DequeueHead").ConfigureAwait(false);
                    if (!deqOk) {
                        Log.DequeueFailed(_logger, pos.PositionIndex, head.ParcelId);
                        return;
                    }

                    // 取包裹信息
                    _parcelManager.TryGet(head.ParcelId, out var info);
                    if (info is null) {
                        Log.ParcelInfoMissing(_logger, pos.PositionIndex, head.ParcelId, occurredMs);
                        return;
                    }

                    // ----------- 实际耗时：事件时间 - 上一站到达时间（调用 ArriveAtStation 前）-----------
                    var prevStationId = info.CurrentStationId;
                    var prevArrivedAt = info.CurrentStationArrivedTime;
                    var actualTransitMs = 0;

                    if (prevStationId != 0 && prevArrivedAt != default) {
                        // 同源时间：prevArrivedAt 也是由 FromUnixTimeMilliseconds(...).DateTime 写入
                        var delta = (long)(occurredAt.DateTime - prevArrivedAt).TotalMilliseconds;
                        if (delta > 0) {
                            actualTransitMs = delta > int.MaxValue ? int.MaxValue : (int)delta;
                        }
                    }

                    int? distanceMm = null;
                    if (prevStationId != 0 && prevStationId <= int.MaxValue) {
                        var prevIndex = (int)prevStationId;
                        if (_positionByIndex.TryGetValue(prevIndex, out var prevPos) &&
                            _segmentById.TryGetValue(prevPos.SegmentId, out var prevSeg)) {
                            distanceMm = (int?)prevSeg.LengthMm;
                        }
                    }

                    if (prevStationId != 0) {
                        Log.TransitStats(_logger, info.ParcelId, prevStationId, pos.PositionIndex, actualTransitMs, distanceMm, occurredMs);
                    }

                    // 再更新到站（内部会前移上一站信息并计算 TransitTimeMs）
                    info.ArriveAtStation(pos.PositionIndex, occurredAt.DateTime);

                    Log.Arrived(_logger, pos.PositionIndex, head.ParcelId, occurredMs, head.Action, head.TargetChuteId);

                    // 更新下一站窗口（O(1) next）
                    await TryUpdateNextPositionWindowAsync(pos.PositionIndex, pos.SegmentId, head.ParcelId, occurredAt).ConfigureAwait(false);

                    // 执行动作 + 落格（落格时间使用 occurredAt）
                    await ExecuteDiverterActionAsync(pos.PositionIndex, (int)pos.DiverterId, head, info, occurredAt).ConfigureAwait(false);
                    return;
                }

                Log.LoopGuard(_logger, pos.PositionIndex, args.Point, args.OccurredAtMs);
            }
            catch (Exception ex) {
                Log.TriggerFaulted(_logger, args.Point, args.OccurredAtMs, ex);
            }
        }

        private async ValueTask TryUpdateNextPositionWindowAsync(int currentPositionIndex, long segmentId, long parcelId, DateTimeOffset currentOccurredAt) {
            if (!_nextPositionByIndex.TryGetValue(currentPositionIndex, out var nextPositionIndex)) {
                Log.LastStationNoNext(_logger, currentPositionIndex, parcelId);
                return;
            }

            if (!_segmentById.TryGetValue(segmentId, out var seg)) {
                Log.SegmentMissing(_logger, currentPositionIndex, nextPositionIndex, segmentId, parcelId);
                return;
            }

            var transitMs = _segmentTransitMsById.TryGetValue(segmentId, out var ms) ? ms : 0;
            if (transitMs <= 0) {
                Log.SegmentSpeedInvalid(_logger, seg.SegmentId, seg.SpeedMmps);
                return;
            }

            var earliest = currentOccurredAt.AddMilliseconds(transitMs - seg.TimeToleranceMs);
            var latest = currentOccurredAt.AddMilliseconds(transitMs + seg.TimeToleranceMs);

            var ok = await _positionQueueManager.UpdateTaskAsync(new PositionQueueTaskPatch {
                PositionIndex = nextPositionIndex,
                ParcelId = parcelId,
                UpdateMask = PositionQueueTaskUpdateMask.EarliestDequeueAt | PositionQueueTaskUpdateMask.LatestDequeueAt,
                EarliestDequeueAt = earliest,
                LatestDequeueAt = latest
            }, reason: "Arrived-UpdateNextWindow").ConfigureAwait(false);

            if (ok) {
                Log.NextWindowUpdated(_logger, currentPositionIndex, nextPositionIndex, parcelId, transitMs, earliest, latest);
            }
            else {
                Log.NextWindowUpdateFailed(_logger, currentPositionIndex, nextPositionIndex, parcelId);
            }
        }

        private async ValueTask ExecuteDiverterActionAsync(int positionIndex, int diverterId, PositionQueueTask head, ParcelInfo info, DateTimeOffset occurredAt) {
            if (!_diverterById.TryGetValue(diverterId, out var diverter)) {
                Log.DiverterMissing(_logger, positionIndex, diverterId, head.ParcelId);
                return;
            }

            var chuteId = info.TargetChuteId;

            if (head.Action == Direction.Left) {
                Log.DiverterActionLeft(_logger, positionIndex, diverterId, head.ParcelId, occurredAt.ToUnixTimeMilliseconds());
                await diverter.TurnLeftAsync().ConfigureAwait(false);
                await _parcelManager.MarkDroppedAsync(head.ParcelId, chuteId, occurredAt).ConfigureAwait(false);
                Log.Dropped(_logger, head.ParcelId, chuteId, occurredAt);
                return;
            }

            if (head.Action == Direction.Right) {
                Log.DiverterActionRight(_logger, positionIndex, diverterId, head.ParcelId, occurredAt.ToUnixTimeMilliseconds());
                await diverter.TurnRightAsync().ConfigureAwait(false);
                await _parcelManager.MarkDroppedAsync(head.ParcelId, chuteId, occurredAt).ConfigureAwait(false);
                Log.Dropped(_logger, head.ParcelId, chuteId, occurredAt);
                return;
            }

            Log.DiverterActionStraight(_logger, positionIndex, diverterId, head.ParcelId, occurredAt.ToUnixTimeMilliseconds());
            await diverter.StraightThroughAsync().ConfigureAwait(false);

            if (diverterId == _lastDiverterId) {
                await _parcelManager.MarkDroppedAsync(head.ParcelId, 999, occurredAt).ConfigureAwait(false);
                Log.DroppedEnd(_logger, head.ParcelId, 999, occurredAt);
            }
        }

        // ---------------- LoggerMessage（高频优化） ----------------

        private static class Log {

            private static readonly Action<ILogger, int, long, int, Exception?> _positionCreated =
                LoggerMessage.Define<int, long, int>(LogLevel.Information, new EventId(1001, "PositionCreated"),
                    "位置队列已创建：PositionIndex={PositionIndex}, FrontSensorId={FrontSensorId}, DiverterId={DiverterId}");

            private static readonly Action<ILogger, Exception?> _subscribed =
                LoggerMessage.Define(LogLevel.Information, new EventId(1002, "Subscribed"),
                    "已订阅传感器状态变更事件");

            private static readonly Action<ILogger, Exception?> _unsubscribed =
                LoggerMessage.Define(LogLevel.Information, new EventId(1003, "Unsubscribed"),
                    "已解除订阅传感器状态变更事件");

            private static readonly Action<ILogger, int, IoState, long, Exception?> _sensorUnbound =
                LoggerMessage.Define<int, IoState, long>(LogLevel.Warning, new EventId(2001, "SensorUnbound"),
                    "传感器触发但未绑定位置：Point={Point}, NewState={NewState}, OccurredAtMs={OccurredAtMs}");

            private static readonly Action<ILogger, int, int, long, Exception?> _sensorConfigMissing =
                LoggerMessage.Define<int, int, long>(LogLevel.Warning, new EventId(2002, "SensorConfigMissing"),
                    "传感器配置缺失：Point={Point}, PositionIndex={PositionIndex}, OccurredAtMs={OccurredAtMs}");

            private static readonly Action<ILogger, int, int, IoState, IoState, Exception?> _sensorStateMismatch =
                LoggerMessage.Define<int, int, IoState, IoState>(LogLevel.Debug, new EventId(2003, "SensorStateMismatch"),
                    "传感器电平不满足触发条件：Point={Point}, PositionIndex={PositionIndex}, TriggerState={TriggerState}, NewState={NewState}");

            private static readonly Action<ILogger, int, int, long, Exception?> _noParcels =
                LoggerMessage.Define<int, int, long>(LogLevel.Warning, new EventId(2004, "NoParcels"),
                    "传感器触发但当前无包裹：PositionIndex={PositionIndex}, Point={Point}, OccurredAtMs={OccurredAtMs}");

            private static readonly Action<ILogger, int, int, long, int, bool, Exception?> _peekEmptyOrLimit =
                LoggerMessage.Define<int, int, long, int, bool>(LogLevel.Warning, new EventId(3001, "PeekEmptyOrLimit"),
                    "传感器触发但队列为空或清理达到上限：PositionIndex={PositionIndex}, Point={Point}, OccurredAtMs={OccurredAtMs}, PrunedCount={PrunedCount}, IsLimitReached={IsLimitReached}");

            private static readonly Action<ILogger, int, long, long, long, long, Exception?> _earlyTriggered =
                LoggerMessage.Define<int, long, long, long, long>(LogLevel.Warning, new EventId(3002, "EarlyTriggered"),
                    "包裹提前触发：PositionIndex={PositionIndex}, ParcelId={ParcelId}, OccurredAtMs={OccurredAtMs}, EarliestMs={EarliestMs}, DeltaMs={DeltaMs}");

            private static readonly Action<ILogger, int, long, long, long, Exception?> _lostConfirmed =
                LoggerMessage.Define<int, long, long, long>(LogLevel.Error, new EventId(3003, "LostConfirmed"),
                    "包裹丢失成立：PositionIndex={PositionIndex}, ParcelId={ParcelId}, OccurredAtMs={OccurredAtMs}, LostDecisionMs={LostDecisionMs}");

            private static readonly Action<ILogger, int, long, Exception?> _lostDequeueFailed =
                LoggerMessage.Define<int, long>(LogLevel.Error, new EventId(3004, "LostDequeueFailed"),
                    "丢失分支出队失败：PositionIndex={PositionIndex}, ParcelId={ParcelId}");

            private static readonly Action<ILogger, int, long, int, Exception?> _invalidateAfter =
                LoggerMessage.Define<int, long, int>(LogLevel.Warning, new EventId(3005, "InvalidateAfter"),
                    "已标记后续任务失效：PositionIndex={PositionIndex}, ParcelId={ParcelId}, InvalidatedCount={InvalidatedCount}");

            private static readonly Action<ILogger, int, long, Exception?> _dequeueFailed =
                LoggerMessage.Define<int, long>(LogLevel.Error, new EventId(3006, "DequeueFailed"),
                    "出队失败：PositionIndex={PositionIndex}, ParcelId={ParcelId}");

            private static readonly Action<ILogger, int, long, long, Exception?> _parcelInfoMissing =
                LoggerMessage.Define<int, long, long>(LogLevel.Warning, new EventId(3007, "ParcelInfoMissing"),
                    "包裹信息不存在：PositionIndex={PositionIndex}, ParcelId={ParcelId}, OccurredAtMs={OccurredAtMs}");

            private static readonly Action<ILogger, long, long, int, int, int?, long, Exception?> _transitStats =
                LoggerMessage.Define<long, long, int, int, int?, long>(LogLevel.Information, new EventId(4001, "TransitStats"),
                    "两站统计：ParcelId={ParcelId}, FromStationId={FromStationId}, ToStationId={ToStationId}, ActualTransitTimeMs={ActualTransitTimeMs}, DistanceMm={DistanceMm}, OccurredAtMs={OccurredAtMs}");

            private static readonly Action<ILogger, int, long, long, Direction, long?, Exception?> _arrived =
                LoggerMessage.Define<int, long, long, Direction, long?>(LogLevel.Information, new EventId(4002, "Arrived"),
                    "包裹到达：PositionIndex={PositionIndex}, ParcelId={ParcelId}, OccurredAtMs={OccurredAtMs}, Action={Action}, TargetChuteId={TargetChuteId}");

            private static readonly Action<ILogger, int, long, Exception?> _lastStationNoNext =
                LoggerMessage.Define<int, long>(LogLevel.Debug, new EventId(5001, "LastStationNoNext"),
                    "最后一站无需更新下一站窗口：PositionIndex={PositionIndex}, ParcelId={ParcelId}");

            private static readonly Action<ILogger, int, int, long, long, Exception?> _segmentMissing =
                LoggerMessage.Define<int, int, long, long>(LogLevel.Warning, new EventId(5002, "SegmentMissing"),
                    "线段配置缺失，无法更新下一站窗口：PositionIndex={PositionIndex}, NextPositionIndex={NextPositionIndex}, SegmentId={SegmentId}, ParcelId={ParcelId}");

            private static readonly Action<ILogger, long, int, Exception?> _segmentSpeedInvalid =
                LoggerMessage.Define<long, int>(LogLevel.Error, new EventId(5003, "SegmentSpeedInvalid"),
                    "线段速度无效：SegmentId={SegmentId}, SpeedMmps={SpeedMmps}");

            private static readonly Action<ILogger, int, int, long, int, DateTimeOffset, DateTimeOffset, Exception?> _nextWindowUpdated =
                LoggerMessage.Define<int, int, long, int, DateTimeOffset, DateTimeOffset>(LogLevel.Information, new EventId(5004, "NextWindowUpdated"),
                    "已更新下一站窗口：CurrentPositionIndex={CurrentPositionIndex}, NextPositionIndex={NextPositionIndex}, ParcelId={ParcelId}, TransitMs={TransitMs}, Earliest={Earliest}, Latest={Latest}");

            private static readonly Action<ILogger, int, int, long, Exception?> _nextWindowUpdateFailed =
                LoggerMessage.Define<int, int, long>(LogLevel.Warning, new EventId(5005, "NextWindowUpdateFailed"),
                    "更新下一站窗口失败：CurrentPositionIndex={CurrentPositionIndex}, NextPositionIndex={NextPositionIndex}, ParcelId={ParcelId}");

            private static readonly Action<ILogger, int, int, long, Exception?> _diverterMissing =
                LoggerMessage.Define<int, int, long>(LogLevel.Error, new EventId(6001, "DiverterMissing"),
                    "摆轮设备未找到：PositionIndex={PositionIndex}, DiverterId={DiverterId}, ParcelId={ParcelId}");

            private static readonly Action<ILogger, int, int, long, long, Exception?> _diverterActionLeft =
                LoggerMessage.Define<int, int, long, long>(LogLevel.Information, new EventId(6002, "DiverterActionLeft"),
                    "执行摆轮动作：TurnLeft，PositionIndex={PositionIndex}, DiverterId={DiverterId}, ParcelId={ParcelId}, OccurredAtMs={OccurredAtMs}");

            private static readonly Action<ILogger, int, int, long, long, Exception?> _diverterActionRight =
                LoggerMessage.Define<int, int, long, long>(LogLevel.Information, new EventId(6003, "DiverterActionRight"),
                    "执行摆轮动作：TurnRight，PositionIndex={PositionIndex}, DiverterId={DiverterId}, ParcelId={ParcelId}, OccurredAtMs={OccurredAtMs}");

            private static readonly Action<ILogger, int, int, long, long, Exception?> _diverterActionStraight =
                LoggerMessage.Define<int, int, long, long>(LogLevel.Debug, new EventId(6004, "DiverterActionStraight"),
                    "执行摆轮动作：StraightThrough，PositionIndex={PositionIndex}, DiverterId={DiverterId}, ParcelId={ParcelId}, OccurredAtMs={OccurredAtMs}");

            private static readonly Action<ILogger, long, long, DateTimeOffset, Exception?> _dropped =
                LoggerMessage.Define<long, long, DateTimeOffset>(LogLevel.Information, new EventId(6005, "Dropped"),
                    "落格完成：ParcelId={ParcelId}, ChuteId={ChuteId}, DroppedAt={DroppedAt}");

            private static readonly Action<ILogger, long, long, DateTimeOffset, Exception?> _droppedEnd =
                LoggerMessage.Define<long, long, DateTimeOffset>(LogLevel.Information, new EventId(6006, "DroppedEnd"),
                    "末端默认落格完成：ParcelId={ParcelId}, ChuteId={ChuteId}, DroppedAt={DroppedAt}");

            private static readonly Action<ILogger, int, int, long, Exception?> _loopGuard =
                LoggerMessage.Define<int, int, long>(LogLevel.Error, new EventId(7001, "LoopGuard"),
                    "循环保护触发：PositionIndex={PositionIndex}, Point={Point}, OccurredAtMs={OccurredAtMs}");

            private static readonly Action<ILogger, int, Exception?> _actorPumpFaulted =
                LoggerMessage.Define<int>(LogLevel.Error, new EventId(7002, "ActorPumpFaulted"),
                    "PositionActor Pump 异常：PositionIndex={PositionIndex}");

            private static readonly Action<ILogger, int, long, Exception?> _triggerFaulted =
                LoggerMessage.Define<int, long>(LogLevel.Error, new EventId(7003, "TriggerFaulted"),
                    "处理传感器触发异常：Point={Point}, OccurredAtMs={OccurredAtMs}");

            public static void PositionCreated(ILogger logger, int positionIndex, long frontSensorId, int diverterId) => _positionCreated(logger, positionIndex, frontSensorId, diverterId, null);

            public static void Subscribed(ILogger logger) => _subscribed(logger, null);

            public static void Unsubscribed(ILogger logger) => _unsubscribed(logger, null);

            public static void SensorUnbound(ILogger logger, int point, IoState newState, long occurredAtMs) => _sensorUnbound(logger, point, newState, occurredAtMs, null);

            public static void SensorConfigMissing(ILogger logger, int point, int positionIndex, long occurredAtMs) => _sensorConfigMissing(logger, point, positionIndex, occurredAtMs, null);

            public static void SensorStateMismatch(ILogger logger, int point, int positionIndex, IoState trigger, IoState newState) => _sensorStateMismatch(logger, point, positionIndex, trigger, newState, null);

            public static void NoParcels(ILogger logger, int positionIndex, int point, long occurredAtMs) => _noParcels(logger, positionIndex, point, occurredAtMs, null);

            public static void PeekEmptyOrLimit(ILogger logger, int positionIndex, int point, long occurredAtMs, int prunedCount, bool isLimitReached) => _peekEmptyOrLimit(logger, positionIndex, point, occurredAtMs, prunedCount, isLimitReached, null);

            public static void EarlyTriggered(ILogger logger, int positionIndex, long parcelId, long occurredAtMs, long earliestMs, long deltaMs) => _earlyTriggered(logger, positionIndex, parcelId, occurredAtMs, earliestMs, deltaMs, null);

            public static void LostConfirmed(ILogger logger, int positionIndex, long parcelId, long occurredAtMs, long lostDecisionMs) => _lostConfirmed(logger, positionIndex, parcelId, occurredAtMs, lostDecisionMs, null);

            public static void LostDequeueFailed(ILogger logger, int positionIndex, long parcelId) => _lostDequeueFailed(logger, positionIndex, parcelId, null);

            public static void InvalidateAfter(ILogger logger, int positionIndex, long parcelId, int invalidated) => _invalidateAfter(logger, positionIndex, parcelId, invalidated, null);

            public static void DequeueFailed(ILogger logger, int positionIndex, long parcelId) => _dequeueFailed(logger, positionIndex, parcelId, null);

            public static void ParcelInfoMissing(ILogger logger, int positionIndex, long parcelId, long occurredAtMs) => _parcelInfoMissing(logger, positionIndex, parcelId, occurredAtMs, null);

            public static void TransitStats(ILogger logger, long parcelId, long fromStationId, int toStationId, int actualTransitMs, int? distanceMm, long occurredAtMs)
                => _transitStats(logger, parcelId, fromStationId, toStationId, actualTransitMs, distanceMm, occurredAtMs, null);

            public static void Arrived(ILogger logger, int positionIndex, long parcelId, long occurredAtMs, Direction action, long? targetChuteId)
                => _arrived(logger, positionIndex, parcelId, occurredAtMs, action, targetChuteId, null);

            public static void LastStationNoNext(ILogger logger, int positionIndex, long parcelId) => _lastStationNoNext(logger, positionIndex, parcelId, null);

            public static void SegmentMissing(ILogger logger, int positionIndex, int nextPositionIndex, long segmentId, long parcelId) => _segmentMissing(logger, positionIndex, nextPositionIndex, segmentId, parcelId, null);

            public static void SegmentSpeedInvalid(ILogger logger, long segmentId, int speedMmps) => _segmentSpeedInvalid(logger, segmentId, speedMmps, null);

            public static void NextWindowUpdated(ILogger logger, int currentPositionIndex, int nextPositionIndex, long parcelId, int transitMs, DateTimeOffset earliest, DateTimeOffset latest)
                => _nextWindowUpdated(logger, currentPositionIndex, nextPositionIndex, parcelId, transitMs, earliest, latest, null);

            public static void NextWindowUpdateFailed(ILogger logger, int currentPositionIndex, int nextPositionIndex, long parcelId)
                => _nextWindowUpdateFailed(logger, currentPositionIndex, nextPositionIndex, parcelId, null);

            public static void DiverterMissing(ILogger logger, int positionIndex, int diverterId, long parcelId) => _diverterMissing(logger, positionIndex, diverterId, parcelId, null);

            public static void DiverterActionLeft(ILogger logger, int positionIndex, int diverterId, long parcelId, long occurredAtMs) => _diverterActionLeft(logger, positionIndex, diverterId, parcelId, occurredAtMs, null);

            public static void DiverterActionRight(ILogger logger, int positionIndex, int diverterId, long parcelId, long occurredAtMs) => _diverterActionRight(logger, positionIndex, diverterId, parcelId, occurredAtMs, null);

            public static void DiverterActionStraight(ILogger logger, int positionIndex, int diverterId, long parcelId, long occurredAtMs) => _diverterActionStraight(logger, positionIndex, diverterId, parcelId, occurredAtMs, null);

            public static void Dropped(ILogger logger, long parcelId, long chuteId, DateTimeOffset droppedAt) => _dropped(logger, parcelId, chuteId, droppedAt, null);

            public static void DroppedEnd(ILogger logger, long parcelId, long chuteId, DateTimeOffset droppedAt) => _droppedEnd(logger, parcelId, chuteId, droppedAt, null);

            public static void LoopGuard(ILogger logger, int positionIndex, int point, long occurredAtMs) => _loopGuard(logger, positionIndex, point, occurredAtMs, null);

            public static void ActorPumpFaulted(ILogger logger, int positionIndex, Exception ex) => _actorPumpFaulted(logger, positionIndex, ex);

            public static void TriggerFaulted(ILogger logger, int point, long occurredAtMs, Exception ex) => _triggerFaulted(logger, point, occurredAtMs, ex);
        }
    }
}
