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
using System.Runtime.CompilerServices;
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

        private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss:fff";

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

            // NOTE: 仅日志：保持同步，避免 async-void 事件处理器带来的并发重入/异常不可控
            _parcelManager.ParcelCreated += (sender, args) => {
                _logger.LogDebug($"包裹Id:{args.ParcelId},创建成功,时间:{args.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss.fff}");
            };
            _parcelManager.ParcelTargetChuteUpdated += (sender, args) => {
                _logger.LogDebug(
                    $"包裹Id:{args.ParcelId},更新目标格口为:{args.NewTargetChuteId},时间:{args.AssignedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss.fff}");
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            BuildLookupCaches();

            // 订阅 PositionQueueManager 故障事件：用于定位 UpdateTaskAsync/CreateTaskAsync 等返回 false 的真实原因
            _positionQueueManager.Faulted += OnPositionQueueFaulted;

            foreach (var pos in _positionOptions.Value.OrderBy(o => o.PositionIndex)) {
                var ok = await _positionQueueManager.CreatePositionAsync(pos.PositionIndex, stoppingToken).ConfigureAwait(false);
                if (ok) {
                    _logger.LogInformation(new EventId(1001, "PositionCreated"),
                        "位置队列已创建：PositionIndex={PositionIndex}, FrontSensorId={FrontSensorId}, DiverterId={DiverterId}",
                        pos.PositionIndex, pos.FrontSensorId, (int)pos.DiverterId);
                }

                _ = GetOrCreateActor(pos.PositionIndex);
            }

            // Positions 强顺序 next 映射（基于当前 PositionQueueManager 的 Positions 快照）
            BuildNextPositionMap();

            _sensorManager.SensorStateChanged += OnSensorStateChanged;
            _logger.LogInformation(new EventId(1002, "Subscribed"), "已订阅传感器状态变更事件");

            try {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
            }
            finally {
                _sensorManager.SensorStateChanged -= OnSensorStateChanged;
                _positionQueueManager.Faulted -= OnPositionQueueFaulted;
                _logger.LogInformation(new EventId(1003, "Unsubscribed"), "已解除订阅传感器状态变更事件");

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

        private static string FormatTimestamp(DateTimeOffset value) => value.ToLocalTime().ToString(TimestampFormat);

        private static string FormatTimestampMs(long unixMs)
             => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime().ToString(TimestampFormat);

        private void OnPositionQueueFaulted(object? sender, PositionQueueManagerFaultedEventArgs e) {
            try {
                _logger.LogError(e.Exception,
                    "PositionQueueManager Faulted: Context={Context}, PositionIndex={PositionIndex}, ParcelId={ParcelId}, OccurredAt={OccurredAt:o}",
                    e.Context ?? string.Empty,
                    e.PositionIndex,
                    e.ParcelId,
                    e.OccurredAt);
            }
            catch {
            }
        }

        private void OnSensorStateChanged(object? sender, SensorStateChangedEventArgs args) {
            if (args.SensorType != IoPointType.DiverterPositionSensor) {
                return;
            }

            // 不创建 async 状态机；直接投递到 position Actor
            if (!_positionByFrontSensorId.TryGetValue(args.Point, out var pos)) {
                _logger.LogWarning(new EventId(2001, "SensorUnbound"),
                    "传感器触发但未绑定位置：Point={Point}, NewState={NewState}, OccurredAt={OccurredAt}",
                    args.Point, args.NewState, FormatTimestampMs(args.OccurredAtMs));
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
                    _owner._logger.LogError(ex, "PositionActor Pump 异常：PositionIndex={PositionIndex}", _positionIndex);
                }
            }
        }

        // ---------------- Core Handler per Position ----------------

        private async Task HandlePositionTriggerAsync(int positionIndex, SensorStateChangedEventArgs args) {
            try {
                // 触发追踪号：同一次传感器触发在本服务内的贯穿标识（便于按 TraceId 串联日志）
                // 选择 OccurredAtMs + Point：既稳定又无需引入全局计数器
                var traceId = $"T{args.OccurredAtMs:X}-{args.Point:X}";

                // 位置绑定（由 point -> pos 缓存，O(1)）
                if (!_positionByFrontSensorId.TryGetValue(args.Point, out var pos) || pos.PositionIndex != positionIndex) {
                    _logger.LogWarning(new EventId(2001, "SensorUnbound"),
                        "传感器触发但未绑定位置：TraceId={TraceId}, Point={Point}, NewState={NewState}, OccurredAt={OccurredAt}",
                        traceId, args.Point, args.NewState, FormatTimestampMs(args.OccurredAtMs));
                    return;
                }

                if (!_sensorByPoint.TryGetValue(args.Point, out var sensorOpt)) {
                    _logger.LogWarning(new EventId(2002, "SensorConfigMissing"),
                        "传感器配置缺失：TraceId={TraceId}, Point={Point}, PositionIndex={PositionIndex}, OccurredAt={OccurredAt}",
                        traceId, args.Point, pos.PositionIndex, FormatTimestampMs(args.OccurredAtMs));
                    return;
                }

                if (sensorOpt.TriggerState != args.NewState) {
                    //Log.SensorStateMismatch(_logger, args.Point, pos.PositionIndex, sensorOpt.TriggerState, args.NewState);
                    return;
                }

                if (_parcelManager.Parcels.Count <= 0) {
                    _logger.LogWarning(new EventId(2004, "NoParcels"),
                        "传感器触发但当前无包裹：TraceId={TraceId}, PositionIndex={PositionIndex}, Point={Point}, OccurredAt={OccurredAt}",
                        traceId, pos.PositionIndex, args.Point, FormatTimestampMs(args.OccurredAtMs));
                    return;
                }

                var occurredMs = args.OccurredAtMs;
                var occurredAt = DateTimeOffset.FromUnixTimeMilliseconds(occurredMs).ToLocalTime();
                var occurredAtText = FormatTimestamp(occurredAt);

                // 丢失成立后推进队头：最多 32 次保护
                for (var loop = 0; loop < 32; loop++) {
                    var peekResult = await _positionQueueManager.PeekFirstTaskAfterPruneAsync(
                        pos.PositionIndex,
                        maxPruneCount: 64,
                        reason: "SensorTriggered-PruneInvalidHead").ConfigureAwait(false);

                    if (!peekResult.HasTask) {
                        // NOTE: PositionQueuePeekResult 当前不提供 IsPruneLimitReached。
                        _logger.LogWarning(new EventId(3001, "PeekEmptyOrLimit"),
                            "传感器触发但队列为空或清理达到上限：TraceId={TraceId}, PositionIndex={PositionIndex}, Point={Point}, OccurredAt={OccurredAt}, PrunedCount={PrunedCount}, IsLimitReached={IsLimitReached}",
                            traceId, pos.PositionIndex, args.Point, occurredAtText, peekResult.PrunedCount, peekResult.PrunedCount >= 64);
                        return;
                    }

                    var head = peekResult.Task;
                    var earliestLocal = head.EarliestDequeueAt.ToLocalTime();
                    var latestLocal = head.LatestDequeueAt.ToLocalTime();

                    var earliestMs = earliestLocal.ToUnixTimeMilliseconds();
                    var latestMs = latestLocal.ToUnixTimeMilliseconds();

                    if (occurredAt < earliestLocal) {
                        // 提前触发：必须输出日志（带本地时间）
                        _logger.LogWarning(new EventId(3002, "EarlyTriggered"),
                            "包裹提前触发：TraceId={TraceId}, PositionIndex={PositionIndex}, ParcelId={ParcelId}, OccurredAt={OccurredAt}, Earliest={Earliest}, DeltaMs={DeltaMs}",
                            traceId, pos.PositionIndex, head.ParcelId, occurredAtText, FormatTimestamp(earliestLocal), earliestMs - occurredMs);
                        //直走
                        _diverterById.TryGetValue((int)pos.DiverterId, out var diverter);
                        if (diverter is not null) {
                            await diverter.StraightThroughAsync().ConfigureAwait(continueOnCapturedContext: false);
                        }

                        return;
                    }
                    if (occurredAt > latestLocal) {
                        // 超时触发：必须输出日志（带本地时间）
                        _logger.LogWarning(new EventId(3008, "TimeoutTriggered"),
                            "包裹超时触发：TraceId={TraceId}, PositionIndex={PositionIndex}, ParcelId={ParcelId}, ArrivedAt={ArrivedAt}, EarliestDequeueAt={EarliestDequeueAt}, LatestDequeueAt={LatestDequeueAt}, DeltaMs={DeltaMs}",
                            traceId, pos.PositionIndex, head.ParcelId, occurredAtText, FormatTimestamp(earliestLocal), FormatTimestamp(latestLocal), occurredMs - latestMs);
                    }

                    // 丢失判定：必须 LostDecisionAt
                    if (head.LostDecisionAt is not null) {
                        var lostDecisionLocal = head.LostDecisionAt.Value.ToLocalTime();
                        if (occurredAt > lostDecisionLocal) {
                            // 丢失成立：必须输出日志（带本地时间）
                            _logger.LogError(new EventId(3003, "LostConfirmed"),
                                "包裹丢失成立：TraceId={TraceId}, PositionIndex={PositionIndex}, ParcelId={ParcelId}, ArrivedAt={ArrivedAt}, EarliestDequeueAt={EarliestDequeueAt}, LatestDequeueAt={LatestDequeueAt}, LostDecisionAt={LostDecisionAt}",
                                traceId, pos.PositionIndex, head.ParcelId, occurredAtText, FormatTimestamp(earliestLocal), FormatTimestamp(latestLocal), FormatTimestamp(lostDecisionLocal));

                            var dqOk = await _positionQueueManager.DequeueAsync(pos.PositionIndex, "LostConfirmed-DequeueHead").ConfigureAwait(false);
                            if (!dqOk) {
                                _logger.LogError(new EventId(3004, "LostDequeueFailed"),
                                    "丢失分支出队失败：TraceId={TraceId}, PositionIndex={PositionIndex}, ParcelId={ParcelId}",
                                    traceId, pos.PositionIndex, head.ParcelId);
                                return;
                            }

                            var invalidated = await _positionQueueManager.InvalidateTasksAfterPositionAsync(
                                pos.PositionIndex, head.ParcelId, "LostConfirmed-InvalidateAfter").ConfigureAwait(false);

                            _logger.LogWarning(new EventId(3005, "InvalidateAfter"),
                                "已标记后续任务失效：TraceId={TraceId}, PositionIndex={PositionIndex}, ParcelId={ParcelId}, InvalidatedCount={InvalidatedCount}",
                                traceId, pos.PositionIndex, head.ParcelId, invalidated);

                            // 用同一次触发继续尝试处理新队头
                            continue;
                        }
                    }

                    // ---------- 防错位保护：传感器不携带 ParcelId，出队是不可逆的。
                    // 修正：改为在丢失判定后、出队前才检查（避免因传感器波动导致的误判与保护失效）
                    // 若下一站队列中尚不存在该 ParcelId 的任务，说明“当前队首推断”很可能错误（或编排滞后）。
                    // 为避免把队列越带越歪，这里不执行 Dequeue，直接输出诊断并等待下一次触发。
                    if (_nextPositionByIndex.TryGetValue(pos.PositionIndex, out var nextPositionIndex)) {
                        var nextHasThisParcel = _positionQueueManager.GetTasksSnapshot(nextPositionIndex).Any(t => t.ParcelId == head.ParcelId);
                        if (!nextHasThisParcel) {
                            var nextHeadInfo = _positionQueueManager.TryPeekFirstTask(nextPositionIndex, out var nHead)
                                ? $"NextHeadParcelId={nHead.ParcelId}"
                                : "NextQueueEmpty";

                            // 额外诊断：采样 next 队列前 N=5 个 ParcelId，帮助快速判断是否整体错位/是否缺任务
                            var nextTop5 = _positionQueueManager.GetTasksSnapshot(nextPositionIndex)
                                .Take(5)
                                .Select(t => t.ParcelId)
                                .ToArray();
                            var nextTop5Text = nextTop5.Length == 0 ? "[]" : $"[{string.Join(',', nextTop5)}]";

                            _logger.LogError(new EventId(7010, "DequeueGuardSkipped"),
                                "出队保护：下一站未找到该任务，跳过出队以避免队列错位。TraceId={TraceId}, CurrentPositionIndex={CurrentPositionIndex}, NextPositionIndex={NextPositionIndex}, ParcelId={ParcelId}, OccurredAt={OccurredAt}, NextInfo={NextInfo}",
                                traceId, pos.PositionIndex, nextPositionIndex, head.ParcelId, occurredAtText, $"{nextHeadInfo}, NextTop5={nextTop5Text}");
                            return;
                        }
                    }

                    // 出队依据（诊断）：当前站队首/队二窗口（Debug 级别，避免刷屏）
                    var hasSecond = _positionQueueManager.TryPeekSecondTask(pos.PositionIndex, out var second);
                    var secondText = hasSecond
                        ? $"SecondParcelId={second.ParcelId}, SecondEarliest={FormatTimestamp(second.EarliestDequeueAt)}, SecondLatest={FormatTimestamp(second.LatestDequeueAt)}, SecondInvalidated={second.IsInvalidated}"
                        : "Second=无";
                    _logger.LogDebug(new EventId(8001, "DequeueBasis"),
                        "准备出队（判定依据）：TraceId={TraceId}, PositionIndex={PositionIndex}, Point={Point}, OccurredAt={OccurredAt}, HeadParcelId={HeadParcelId}, HeadEarliest={HeadEarliest}, HeadLatest={HeadLatest}, HeadLostDecisionAt={HeadLostDecisionAt}, HeadInvalidated={HeadInvalidated}, {SecondText}",
                        traceId,
                        pos.PositionIndex,
                        args.Point,
                        occurredAtText,
                         head.ParcelId,
                         FormatTimestamp(earliestLocal),
                         FormatTimestamp(latestLocal),
                         head.LostDecisionAt is null ? "null" : FormatTimestamp(head.LostDecisionAt.Value),
                         head.IsInvalidated,
                         secondText);

                    // 命中当前队首：出队
                    var deqOk = await _positionQueueManager.DequeueAsync(pos.PositionIndex, "SensorMatched-DequeueHead").ConfigureAwait(false);
                    if (!deqOk) {
                        _logger.LogError(new EventId(3006, "DequeueFailed"),
                            "出队失败：TraceId={TraceId}, PositionIndex={PositionIndex}, ParcelId={ParcelId}",
                            traceId, pos.PositionIndex, head.ParcelId);
                        return;
                    }

                    // 出队结果确认：出队后新的队首（用于确认是否发生“出队后队首异常跳变”)
                    var afterHeadInfo = _positionQueueManager.TryPeekFirstTask(pos.PositionIndex, out var afterHead)
                        ? $"NewHeadParcelId={afterHead.ParcelId}, NewHeadEarliest={FormatTimestamp(afterHead.EarliestDequeueAt)}, NewHeadLatest={FormatTimestamp(afterHead.LatestDequeueAt)}, NewHeadInvalidated={afterHead.IsInvalidated}"
                        : "NewHead=队列空";
                    _logger.LogInformation(new EventId(8002, "DequeueConfirmed"),
                       "已完成出队：TraceId={TraceId}, PositionIndex={PositionIndex}, DequeuedParcelId={DequeuedParcelId}, OccurredAt={OccurredAt}, {AfterHeadInfo}",
                       traceId, pos.PositionIndex, head.ParcelId, occurredAtText, afterHeadInfo);

                    // 取包裹信息
                    _parcelManager.TryGet(head.ParcelId, out var info);
                    if (info is null) {
                        _logger.LogWarning(new EventId(3007, "ParcelInfoMissing"),
                            "包裹信息不存在：TraceId={TraceId}, PositionIndex={PositionIndex}, ParcelId={ParcelId}, OccurredAt={OccurredAt}",
                            traceId, pos.PositionIndex, head.ParcelId, occurredAtText);
                        return;
                    }

                    // ----------- 实际耗时：事件时间 - 上一站到达时间（调用 ArriveAtStation 前）-----------
                    var prevStationId = info.CurrentStationId;
                    var prevArrivedAt = info.CurrentStationArrivedTime;
                    var actualTransitMs = 0;
                    long? distanceMm = null;
                    if (prevStationId >= 0 && prevArrivedAt != default) {
                        var delta = (long)(occurredAt.DateTime - prevArrivedAt).TotalMilliseconds;
                        if (delta > 0) {
                            actualTransitMs = delta > int.MaxValue ? int.MaxValue : (int)delta;
                        }

                        distanceMm = actualTransitMs;
                    }

                    //两站统计
                    _logger.LogInformation(new EventId(4001, "TransitStats"),
                        "两站统计：TraceId={TraceId}, ParcelId={ParcelId}, FromStationId={FromStationId}, ToStationId={ToStationId}, ActualTransitTimeMs={ActualTransitTimeMs}, DistanceMm={DistanceMm}, OccurredAt={OccurredAt}",
                        traceId, info.ParcelId, prevStationId, pos.PositionIndex, actualTransitMs, distanceMm, occurredAtText);

                    // 再更新到站（内部会前移上一站信息并计算 TransitTimeMs）
                    info.ArriveAtStation(pos.PositionIndex, occurredAt.DateTime);

                    _logger.LogInformation(new EventId(4002, "Arrived"),
                        "包裹到达：TraceId={TraceId}, PositionIndex={PositionIndex}, ParcelId={ParcelId}, OccurredAt={OccurredAt}, Action={Action}, TargetChuteId={TargetChuteId}",
                        traceId, pos.PositionIndex, head.ParcelId, occurredAtText, head.Action, head.TargetChuteId);

                    // 更新下一站窗口（O(1) next）
                    var updated = await TryUpdateNextPositionWindowAsync(pos.PositionIndex, head.ParcelId, occurredAt, traceId).ConfigureAwait(false);
                    if (!updated) {
                        // 仅使用当前文件中已存在且确认可编译的日志写法（避免触发 LoggerMessage 委托签名冲突）
                        _logger.LogError(new EventId(9106, "NextWindow.UpdateFailed"),
                            "NextWindow 更新失败：TraceId={TraceId}, Current={CurrentPositionIndex}, ParcelId={ParcelId}",
                            traceId, pos.PositionIndex, head.ParcelId);
                    }

                    // 执行动作 + 落格（落格时间使用 occurredAt）
                    await ExecuteDiverterActionAsync(pos.PositionIndex, (int)pos.DiverterId, head, info, occurredAt).ConfigureAwait(false);
                    return;
                }

                _logger.LogError(new EventId(7001, "LoopGuard"),
                    "循环保护触发：TraceId={TraceId}, PositionIndex={PositionIndex}, Point={Point}, OccurredAt={OccurredAt}",
                    traceId, pos.PositionIndex, args.Point, FormatTimestampMs(args.OccurredAtMs));
            }
            catch (Exception ex) {
                _logger.LogError(ex, "处理传感器触发异常：Point={Point}, OccurredAt={OccurredAt}",
                    args.Point, FormatTimestampMs(args.OccurredAtMs));
            }
        }

        private async ValueTask<bool> TryUpdateNextPositionWindowAsync(int currentPositionIndex, long parcelId, DateTimeOffset currentOccurredAt, string traceId) {
            if (!_nextPositionByIndex.TryGetValue(currentPositionIndex, out var nextPositionIndex)) {
                _logger.LogDebug(new EventId(9200, "NextWindow.NoNext"),
                    "NextWindow 无下一站：TraceId={TraceId}, Current={CurrentPositionIndex}, ParcelId={ParcelId}",
                    traceId, currentPositionIndex, parcelId);
                return true; // 终点站：无需更新窗口，不视为失败
            }

            if (!_positionByIndex.TryGetValue(nextPositionIndex, out var nextPosOpt)) {
                _logger.LogWarning(new EventId(9201, "NextWindow.NextPositionMissing"),
                    "NextWindow 下一站配置缺失：TraceId={TraceId}, Current={CurrentPositionIndex}, Next={NextPositionIndex}, ParcelId={ParcelId}",
                    traceId, currentPositionIndex, nextPositionIndex, parcelId);
                return false;
            }

            var segmentId = nextPosOpt.SegmentId;
            if (!_segmentById.TryGetValue(segmentId, out var seg)) {
                _logger.LogWarning(new EventId(9202, "NextWindow.SegmentMissing"),
                    "NextWindow Segment 配置缺失：TraceId={TraceId}, Current={CurrentPositionIndex}, Next={NextPositionIndex}, SegmentId={SegmentId}, ParcelId={ParcelId}",
                    traceId, currentPositionIndex, nextPositionIndex, segmentId, parcelId);
                return false;
            }

            if (!_segmentTransitMsById.TryGetValue(segmentId, out var transitMs) || transitMs <= 0) {
                _logger.LogWarning(new EventId(9203, "NextWindow.TransitMsInvalid"),
                    "NextWindow TransitMs 无效：TraceId={TraceId}, Current={CurrentPositionIndex}, Next={NextPositionIndex}, SegmentId={SegmentId}, SpeedMmps={SpeedMmps}, LengthMm={LengthMm}, TransitMs={TransitMs}, ToleranceMs={ToleranceMs}, ParcelId={ParcelId}",
                    traceId, currentPositionIndex, nextPositionIndex, segmentId, seg.SpeedMmps, seg.LengthMm, transitMs, seg.TimeToleranceMs, parcelId);
                return false;
            }

            var earliest = currentOccurredAt.AddMilliseconds(transitMs - seg.TimeToleranceMs);
            var latest = currentOccurredAt.AddMilliseconds(transitMs + seg.TimeToleranceMs);

            // 重要：PositionQueueManager 对任务的有效性约束：LatestDequeueAt 必须 <= LostDecisionAt（若 LostDecisionAt 非空）。
            // 由于编排创建任务时会为“前一个任务”回写 LostDecisionAt=next.EarliestDequeueAt，
            // 若此处把 next 的窗口整体后移，可能出现 next.Latest > next.LostDecisionAt，从而 patch 被拒绝。
            // 解决：在重算 next 窗口时，同步把 next 的 LostDecisionAt 调整为 >= latest（最小取 latest 本身）。
            var updateMask = PositionQueueTaskUpdateMask.EarliestDequeueAt
                             | PositionQueueTaskUpdateMask.LatestDequeueAt
                             | PositionQueueTaskUpdateMask.LostDecisionAt;

            var patch = new PositionQueueTaskPatch {
                PositionIndex = nextPositionIndex,
                ParcelId = parcelId,
                UpdateMask = updateMask,
                EarliestDequeueAt = earliest,
                LatestDequeueAt = latest,
                LostDecisionAt = latest
            };

            var ok = await _positionQueueManager.UpdateTaskAsync(patch, "Arrived-UpdateNextWindow").ConfigureAwait(false);

            if (!ok) {
                return false;
            }

            _logger.LogDebug($"NextWindow 已更新: TraceId={traceId}, Current={currentPositionIndex}, Next={nextPositionIndex}, ParcelId={parcelId}, TransitMs={transitMs}, Earliest={FormatTimestamp(earliest)}, Latest={FormatTimestamp(latest)}");
            return true;
        }

        private async ValueTask ExecuteDiverterActionAsync(int positionIndex, int diverterId, PositionQueueTask head, ParcelInfo info, DateTimeOffset occurredAt) {
            if (!_diverterById.TryGetValue(diverterId, out var diverter)) {
                _logger.LogError(new EventId(9300, "DiverterMissing"),
                    "摆轮缺失：PositionIndex={PositionIndex}, DiverterId={DiverterId}, ParcelId={ParcelId}",
                    positionIndex, diverterId, head.ParcelId);
                return;
            }
            long chuteId = info.TargetChuteId;
            if (head.Action == Direction.Left) {
                _logger.LogInformation(new EventId(9301, "DiverterActionLeft"),
                    "摆轮左转：PositionIndex={PositionIndex}, DiverterId={DiverterId}, ParcelId={ParcelId}, OccurredAt={OccurredAt}",
                    positionIndex, diverterId, head.ParcelId, FormatTimestamp(occurredAt));
                await diverter.TurnLeftAsync().ConfigureAwait(continueOnCapturedContext: false);
                await _parcelManager.MarkDroppedAsync(head.ParcelId, chuteId, occurredAt).ConfigureAwait(continueOnCapturedContext: false);
                _logger.LogInformation(new EventId(9304, "Dropped"),
                    "包裹落格：ParcelId={ParcelId}, ChuteId={ChuteId}, DroppedAt={DroppedAt}",
                    head.ParcelId, chuteId, FormatTimestamp(occurredAt));
                return;
            }
            if (head.Action == Direction.Right) {
                _logger.LogInformation(new EventId(9302, "DiverterActionRight"),
                    "摆轮右转：PositionIndex={PositionIndex}, DiverterId={DiverterId}, ParcelId={ParcelId}, OccurredAt={OccurredAt}",
                    positionIndex, diverterId, head.ParcelId, FormatTimestamp(occurredAt));
                await diverter.TurnRightAsync().ConfigureAwait(continueOnCapturedContext: false);
                await _parcelManager.MarkDroppedAsync(head.ParcelId, chuteId, occurredAt).ConfigureAwait(continueOnCapturedContext: false);
                _logger.LogInformation(new EventId(9304, "Dropped"),
                    "包裹落格：ParcelId={ParcelId}, ChuteId={ChuteId}, DroppedAt={DroppedAt}",
                    head.ParcelId, chuteId, FormatTimestamp(occurredAt));
                return;
            }
            _logger.LogInformation(new EventId(9303, "DiverterActionStraight"),
                "摆轮直通：PositionIndex={PositionIndex}, DiverterId={DiverterId}, ParcelId={ParcelId}, OccurredAt={OccurredAt}",
                positionIndex, diverterId, head.ParcelId, FormatTimestamp(occurredAt));
            await diverter.StraightThroughAsync().ConfigureAwait(continueOnCapturedContext: false);
            if (diverterId == _lastDiverterId) {
                await _parcelManager.MarkDroppedAsync(head.ParcelId, 999L, occurredAt).ConfigureAwait(continueOnCapturedContext: false);
                _logger.LogInformation(new EventId(9305, "DroppedEnd"),
                    "包裹终点落格：ParcelId={ParcelId}, ChuteId={ChuteId}, DroppedAt={DroppedAt}",
                    head.ParcelId, 999L, FormatTimestamp(occurredAt));
            }
        }
    }
}
