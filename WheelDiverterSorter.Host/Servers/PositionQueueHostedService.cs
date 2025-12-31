using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WheelDiverterSorter.Core;
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
        private readonly IOptions<IReadOnlyList<ConveyorSegmentOptions>> _conveyorSegmentOptions;
        private readonly IOptions<IReadOnlyList<PositionOptions>> _positionOptions;
        private readonly IOptions<IReadOnlyList<SensorOptions>> _sensorOptions;
        private readonly IWheelDiverterManager _wheelDiverterManager;
        private readonly IParcelManager _parcelManager;
        private readonly IPositionQueueManager _positionQueueManager;
        private readonly ISensorManager _sensorManager;

        private readonly ConcurrentDictionary<int, SemaphoreSlim> _positionGates = new();

        private Dictionary<long, PositionOptions> _positionByFrontSensorId = new();
        private Dictionary<int, SensorOptions> _sensorByPoint = new();
        private Dictionary<long, ConveyorSegmentOptions> _segmentById = new();
        private Dictionary<int, IWheelDiverter> _diverterById = new();

        public PositionQueueHostedService(
            ILogger<PositionQueueHostedService> logger,
            IOptions<IReadOnlyList<ConveyorSegmentOptions>> conveyorSegmentOptions,
            IOptions<IReadOnlyList<PositionOptions>> positionOptions,
            IOptions<IReadOnlyList<SensorOptions>> sensorOptions,
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
                    _logger.LogInformation("位置队列已创建：PositionIndex={PositionIndex}, FrontSensorId={FrontSensorId}, DiverterId={DiverterId}",
                        pos.PositionIndex, pos.FrontSensorId, pos.DiverterId);
                }
            }

            _sensorManager.SensorStateChanged += OnSensorStateChanged;
            _logger.LogInformation("已订阅传感器状态变更事件");

            try {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
            }
            finally {
                _sensorManager.SensorStateChanged -= OnSensorStateChanged;
                _logger.LogInformation("已解除订阅传感器状态变更事件");
            }
        }

        private void BuildLookupCaches() {
            _positionByFrontSensorId = _positionOptions.Value
                .GroupBy(x => x.FrontSensorId)
                .Select(x => x.First())
                .ToDictionary(x => x.FrontSensorId, x => x);

            _sensorByPoint = _sensorOptions.Value
                .GroupBy(x => x.Point)
                .Select(x => x.First())
                .ToDictionary(x => x.Point, x => x);

            _segmentById = _conveyorSegmentOptions.Value
                .Where(x => x.IsValid)
                .GroupBy(x => x.SegmentId)
                .Select(x => x.First())
                .ToDictionary(x => x.SegmentId, x => x);

            _diverterById = _wheelDiverterManager.Diverters
                .GroupBy(x => x.DiverterId)
                .Select(x => x.First())
                .ToDictionary(x => x.DiverterId, x => x);
        }

        private void OnSensorStateChanged(object? sender, SensorStateChangedEventArgs args) {
            if (args.SensorType != IoPointType.DiverterPositionSensor) {
                return;
            }

            // 事件回调不做 await，避免高频调度开销；内部捕获异常
            _ = HandleDiverterPositionSensorAsync(args);
        }

        private async Task HandleDiverterPositionSensorAsync(SensorStateChangedEventArgs args) {
            try {
                if (!_positionByFrontSensorId.TryGetValue(args.Point, out var pos)) {
                    _logger.LogWarning("传感器触发但未绑定位置：Point={Point}, NewState={NewState}, OccurredAtMs={OccurredAtMs}",
                        args.Point, args.NewState, args.OccurredAtMs);
                    return;
                }

                if (!_sensorByPoint.TryGetValue(args.Point, out var sensorOpt)) {
                    _logger.LogWarning("传感器配置缺失：Point={Point}, PositionIndex={PositionIndex}, OccurredAtMs={OccurredAtMs}",
                        args.Point, pos.PositionIndex, args.OccurredAtMs);
                    return;
                }

                if (sensorOpt.TriggerState != args.NewState) {
                    // 高频噪声路径，使用 Debug 避免打爆日志
                    _logger.LogDebug("传感器电平不满足触发条件：Point={Point}, PositionIndex={PositionIndex}, TriggerState={TriggerState}, NewState={NewState}",
                        args.Point, pos.PositionIndex, sensorOpt.TriggerState, args.NewState);
                    return;
                }

                if (_parcelManager.Parcels.Count <= 0) {
                    _logger.LogWarning("传感器触发但当前无包裹：PositionIndex={PositionIndex}, Point={Point}, OccurredAtMs={OccurredAtMs}",
                        pos.PositionIndex, args.Point, args.OccurredAtMs);
                    return;
                }

                var gate = _positionGates.GetOrAdd(pos.PositionIndex, _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync().ConfigureAwait(false);

                try {
                    var occurredMs = args.OccurredAtMs;
                    var pruneReason = "SensorTriggered-PruneInvalidHead";

                    // 循环用于“丢失成立后”推进队头，继续用同一次触发匹配新的队头（严格 FIFO）
                    for (var loop = 0; loop < 32; loop++) {
                        var peekResult = await _positionQueueManager.PeekFirstTaskAfterPruneAsync(
                            pos.PositionIndex,
                            maxPruneCount: 64,
                            reason: pruneReason).ConfigureAwait(false);

                        if (!peekResult.HasTask) {
                            _logger.LogWarning("传感器触发但队列为空或清理达到上限：PositionIndex={PositionIndex}, Point={Point}, OccurredAtMs={OccurredAtMs}, PrunedCount={PrunedCount}, IsLimitReached={IsLimitReached}",
                                pos.PositionIndex, args.Point, occurredMs, peekResult.PrunedCount, peekResult.IsPruneLimitReached);
                            return;
                        }

                        var head = peekResult.Task;
                        var earliestMs = head.EarliestDequeueAt.ToUnixTimeMilliseconds();

                        if (occurredMs < earliestMs) {
                            _logger.LogWarning("包裹提前触发：PositionIndex={PositionIndex}, ParcelId={ParcelId}, OccurredAtMs={OccurredAtMs}, EarliestMs={EarliestMs}, DeltaMs={DeltaMs}",
                                pos.PositionIndex, head.ParcelId, occurredMs, earliestMs, earliestMs - occurredMs);
                            return;
                        }

                        // 丢失：必须由 LostDecisionAt 给出“后继证据时间点”
                        if (head.LostDecisionAt is not null) {
                            var lostDecisionMs = head.LostDecisionAt.Value.ToUnixTimeMilliseconds();
                            if (occurredMs > lostDecisionMs) {
                                _logger.LogError("包裹丢失成立：PositionIndex={PositionIndex}, ParcelId={ParcelId}, OccurredAtMs={OccurredAtMs}, LostDecisionMs={LostDecisionMs}",
                                    pos.PositionIndex, head.ParcelId, occurredMs, lostDecisionMs);

                                // 关键：推进队列（严格 FIFO 仅出队队首）
                                var dqOk = await _positionQueueManager.DequeueAsync(pos.PositionIndex, "LostConfirmed-DequeueHead").ConfigureAwait(false);
                                if (!dqOk) {
                                    _logger.LogError("丢失分支出队失败：PositionIndex={PositionIndex}, ParcelId={ParcelId}", pos.PositionIndex, head.ParcelId);
                                    return;
                                }

                                // 关键：失效后续站点任务
                                var invalidated = await _positionQueueManager.InvalidateTasksAfterPositionAsync(pos.PositionIndex, head.ParcelId, "LostConfirmed-InvalidateAfter").ConfigureAwait(false);
                                _logger.LogWarning("已标记后续任务失效：PositionIndex={PositionIndex}, ParcelId={ParcelId}, InvalidatedCount={InvalidatedCount}",
                                    pos.PositionIndex, head.ParcelId, invalidated);

                                // 同一次触发继续尝试处理新队头
                                continue;
                            }
                        }

                        // 命中当前队首：出队
                        var deqOk = await _positionQueueManager.DequeueAsync(pos.PositionIndex, "SensorMatched-DequeueHead").ConfigureAwait(false);
                        if (!deqOk) {
                            _logger.LogError("出队失败：PositionIndex={PositionIndex}, ParcelId={ParcelId}", pos.PositionIndex, head.ParcelId);
                            return;
                        }

                        // 到达记录
                        _parcelManager.TryGet(head.ParcelId, out var info);
                        var occurredAt = DateTimeOffset.FromUnixTimeMilliseconds(occurredMs);

                        info?.ArriveAtStation(pos.PositionIndex, occurredAt.DateTime);

                        _logger.LogInformation("包裹到达：PositionIndex={PositionIndex}, ParcelId={ParcelId}, OccurredAtMs={OccurredAtMs}, Action={Action}, TargetChuteId={TargetChuteId}",
                            pos.PositionIndex, head.ParcelId, occurredMs, head.Action, head.TargetChuteId);

                        // 更新下一站任务的窗口（严格按 Positions 强顺序找 next）
                        await TryUpdateNextPositionWindowAsync(pos, head.ParcelId, occurredAt).ConfigureAwait(false);

                        // 执行动作 + 落格
                        await ExecuteDiverterActionAsync(pos, head, info, occurredAt).ConfigureAwait(false);
                        return;
                    }

                    _logger.LogError("循环保护触发：PositionIndex={PositionIndex}, Point={Point}, OccurredAtMs={OccurredAtMs}", pos.PositionIndex, args.Point, args.OccurredAtMs);
                }
                finally {
                    gate.Release();
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "处理传感器触发异常：Point={Point}, OccurredAtMs={OccurredAtMs}", args.Point, args.OccurredAtMs);
            }
        }

        private async ValueTask TryUpdateNextPositionWindowAsync(PositionOptions currentPos, long parcelId, DateTimeOffset currentOccurredAt) {
            // 使用强顺序 Positions，不假设 PositionIndex 连续递增
            var positions = _positionQueueManager.Positions;
            if (positions.Count <= 0) {
                return;
            }

            var pivot = -1;
            for (var i = 0; i < positions.Count; i++) {
                if (positions[i] == currentPos.PositionIndex) {
                    pivot = i;
                    break;
                }
            }

            if (pivot < 0 || pivot >= positions.Count - 1) {
                _logger.LogDebug("最后一站无需更新下一站窗口：PositionIndex={PositionIndex}, ParcelId={ParcelId}", currentPos.PositionIndex, parcelId);
                return;
            }

            var nextPositionIndex = positions[pivot + 1];

            // 当前线段配置：优先用 currentPos.SegmentId
            var segmentId = currentPos.SegmentId;
            if (!_segmentById.TryGetValue(segmentId, out var seg)) {
                _logger.LogWarning("线段配置缺失，无法更新下一站窗口：PositionIndex={PositionIndex}, NextPositionIndex={NextPositionIndex}, SegmentId={SegmentId}, ParcelId={ParcelId}",
                    currentPos.PositionIndex, nextPositionIndex, segmentId, parcelId);
                return;
            }

            if (seg.SpeedMmps <= 0) {
                _logger.LogError("线段速度无效：SegmentId={SegmentId}, SpeedMmps={SpeedMmps}", seg.SegmentId, seg.SpeedMmps);
                return;
            }

            // transitMs = (LengthMm / SpeedMmps) * 1000
            var transitMs = (long)decimal.Round(((decimal)seg.LengthMm / (decimal)seg.SpeedMmps) * 1000m, 0, MidpointRounding.AwayFromZero);

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
                _logger.LogInformation("已更新下一站窗口：CurrentPositionIndex={CurrentPositionIndex}, NextPositionIndex={NextPositionIndex}, ParcelId={ParcelId}, TransitMs={TransitMs}, Earliest={Earliest}, Latest={Latest}",
                    currentPos.PositionIndex, nextPositionIndex, parcelId, transitMs, earliest, latest);
            }
            else {
                _logger.LogWarning("更新下一站窗口失败：CurrentPositionIndex={CurrentPositionIndex}, NextPositionIndex={NextPositionIndex}, ParcelId={ParcelId}",
                    currentPos.PositionIndex, nextPositionIndex, parcelId);
            }
        }

        private async ValueTask ExecuteDiverterActionAsync(PositionOptions pos, PositionQueueTask head, ParcelInfo? info, DateTimeOffset occurredAt) {
            if (!_diverterById.TryGetValue((int)pos.DiverterId, out var diverter)) {
                _logger.LogError("摆轮设备未找到：PositionIndex={PositionIndex}, DiverterId={DiverterId}, ParcelId={ParcelId}",
                    pos.PositionIndex, pos.DiverterId, head.ParcelId);
                return;
            }

            var chuteId = info?.TargetChuteId ?? 0L;

            if (head.Action == Direction.Left) {
                _logger.LogInformation("执行摆轮动作：TurnLeft，PositionIndex={PositionIndex}, DiverterId={DiverterId}, ParcelId={ParcelId}", pos.PositionIndex, pos.DiverterId, head.ParcelId);
                await diverter.TurnLeftAsync().ConfigureAwait(false);
                await _parcelManager.MarkDroppedAsync(head.ParcelId, chuteId, occurredAt).ConfigureAwait(false);
                _logger.LogInformation("落格完成：ParcelId={ParcelId}, ChuteId={ChuteId}, DroppedAt={DroppedAt}", head.ParcelId, chuteId, occurredAt);
                return;
            }

            if (head.Action == Direction.Right) {
                _logger.LogInformation("执行摆轮动作：TurnRight，PositionIndex={PositionIndex}, DiverterId={DiverterId}, ParcelId={ParcelId}", pos.PositionIndex, pos.DiverterId, head.ParcelId);
                await diverter.TurnRightAsync().ConfigureAwait(false);
                await _parcelManager.MarkDroppedAsync(head.ParcelId, chuteId, occurredAt).ConfigureAwait(false);
                _logger.LogInformation("落格完成：ParcelId={ParcelId}, ChuteId={ChuteId}, DroppedAt={DroppedAt}", head.ParcelId, chuteId, occurredAt);
                return;
            }

            _logger.LogDebug("执行摆轮动作：StraightThrough，PositionIndex={PositionIndex}, DiverterId={DiverterId}, ParcelId={ParcelId}", pos.PositionIndex, pos.DiverterId, head.ParcelId);
            await diverter.StraightThroughAsync().ConfigureAwait(false);

            // 最后一台摆轮的“默认落格”逻辑保留
            var last = _wheelDiverterManager.Diverters.LastOrDefault();
            if (last?.DiverterId == diverter.DiverterId) {
                await _parcelManager.MarkDroppedAsync(head.ParcelId, 999, occurredAt).ConfigureAwait(false);
                _logger.LogInformation("末端默认落格完成：ParcelId={ParcelId}, ChuteId={ChuteId}, DroppedAt={DroppedAt}", head.ParcelId, 999, occurredAt);
            }
        }
    }
}
