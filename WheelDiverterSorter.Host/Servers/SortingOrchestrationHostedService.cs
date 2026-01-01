using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using WheelDiverterSorter.Execution;
using System.Collections.Concurrent;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Models;
using WheelDiverterSorter.Core.Manager;
using WheelDiverterSorter.Core.Options;
using System.Threading.Channels;
using WheelDiverterSorter.Core.Events;

namespace WheelDiverterSorter.Host.Servers {

    /// <summary>
    /// 分拣编排服务
    /// </summary>
    public class SortingOrchestrationHostedService : BackgroundService {
        private readonly ILogger<SortingOrchestrationHostedService> _logger;
        private readonly IParcelManager _parcelManager;

        private readonly IOptions<List<ConveyorSegmentOptions>> _conveyorSegmentOptions;
        private readonly IOptions<List<PositionOptions>> _positionOptions;
        private readonly IPositionQueueManager _positionQueueManager;

        // 按 ParcelId 串行化编排事件处理（避免 Create/Update/Invalidate 并发破坏 FIFO/LostDecisionAt 等强顺序逻辑）
        private static readonly ConcurrentDictionary<long, SemaphoreSlim> _parcelGates = new();

        // 将事件处理从事件触发线程剥离：避免阻塞其他订阅者/调用链
        private readonly Channel<OrchestrationWorkItem> _work = Channel.CreateUnbounded<OrchestrationWorkItem>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = true
        });

        public SortingOrchestrationHostedService(ILogger<SortingOrchestrationHostedService> logger,
            IOptions<List<ConveyorSegmentOptions>> conveyorSegmentOptions,
            IOptions<List<PositionOptions>> positionOptions,
            IParcelManager parcelManager,
            IPositionQueueManager positionQueueManager) {
            _logger = logger;

            _conveyorSegmentOptions = conveyorSegmentOptions;
            _positionOptions = positionOptions;
            _positionQueueManager = positionQueueManager;

            _parcelManager = parcelManager;

            // 事件回调只做入队（立即返回，不阻塞触发线程）
            _parcelManager.ParcelCreated += (sender, args) => {
                _work.Writer.TryWrite(OrchestrationWorkItem.ForParcelCreated(args));
            };

            _parcelManager.ParcelTargetChuteUpdated += (sender, args) => {
                _work.Writer.TryWrite(OrchestrationWorkItem.ForTargetUpdated(args));
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            var reader = _work.Reader;

            while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false)) {
                while (reader.TryRead(out var item)) {
                    try {
                        switch (item.Kind) {
                            case WorkKind.ParcelCreated:
                                await HandleParcelCreatedAsync(item.ParcelCreatedArgs!.Value, stoppingToken).ConfigureAwait(false);
                                break;

                            case WorkKind.ParcelTargetUpdated:
                                await HandleParcelTargetUpdatedAsync(item.TargetUpdatedArgs!.Value, stoppingToken).ConfigureAwait(false);
                                break;
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                        return;
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, "编排处理异常：Kind={Kind}", item.Kind);
                    }
                }
            }
        }

        private async Task HandleParcelCreatedAsync(ParcelCreatedEventArgs args, CancellationToken cancellationToken) {
            var gate = _parcelGates.GetOrAdd(args.ParcelId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                var positionTimeOffset = args.CreatedAt;

                foreach (var position in _positionQueueManager.Positions) {
                    cancellationToken.ThrowIfCancellationRequested();

                    var posOpt = _positionOptions.Value.FirstOrDefault(f => f.PositionIndex == position);
                    if (posOpt is null) {
                        _logger.LogWarning(
                            "编排创建任务跳过：未找到位置配置。ParcelId={ParcelId}, PositionIndex={PositionIndex}",
                            args.ParcelId, position);
                        continue;
                    }

                    var segmentId = posOpt.SegmentId;
                    if (segmentId <= 0) {
                        _logger.LogWarning(
                            "编排创建任务跳过：PositionOptions.SegmentId 无效。ParcelId={ParcelId}, PositionIndex={PositionIndex}, SegmentId={SegmentId}",
                            args.ParcelId, position, segmentId);
                        continue;
                    }

                    var segmentOptions = _conveyorSegmentOptions.Value.FirstOrDefault(f => f.IsValid && f.SegmentId == segmentId);
                    if (segmentOptions == null) {
                        _logger.LogWarning(
                            "编排创建任务跳过：未找到线段配置。ParcelId={ParcelId}, PositionIndex={PositionIndex}, SegmentId={SegmentId}",
                            args.ParcelId, position, segmentId);
                        continue;
                    }

                    if (segmentOptions.SpeedMmps <= 0) {
                        _logger.LogWarning(
                            "编排创建任务跳过：线段速度无效。ParcelId={ParcelId}, PositionIndex={PositionIndex}, SegmentId={SegmentId}, SpeedMmps={SpeedMmps}",
                            args.ParcelId, position, segmentOptions.SegmentId, segmentOptions.SpeedMmps);
                        continue;
                    }

                    var len = segmentOptions.LengthMm;
                    var speed = (long)segmentOptions.SpeedMmps;
                    var transitMsLong = ((len * 1000L) + (speed / 2L)) / speed;
                    var transitMs = transitMsLong > int.MaxValue ? int.MaxValue : (int)transitMsLong;

                    var earliest = positionTimeOffset.AddMilliseconds(transitMs - segmentOptions.TimeToleranceMs);
                    var latest = positionTimeOffset.AddMilliseconds(transitMs + segmentOptions.TimeToleranceMs);

                    var positionQueueTask = new PositionQueueTask {
                        PositionIndex = position,
                        ParcelId = args.ParcelId,
                        Action = Direction.Straight,
                        EarliestDequeueAt = earliest,
                        LatestDequeueAt = latest,
                        LostDecisionAt = null
                    };

                    var ok = await _positionQueueManager.CreateTaskAsync(positionQueueTask, cancellationToken).ConfigureAwait(false);
                    if (!ok) {
                        _logger.LogWarning(
                            "编排创建任务失败：CreateTaskAsync 返回 false。ParcelId={ParcelId}, PositionIndex={PositionIndex}, SegmentId={SegmentId}, Earliest={Earliest:o}, Latest={Latest:o}",
                            args.ParcelId, position, segmentOptions.SegmentId, earliest, latest);
                    }
                    else {
                        _logger.LogDebug(
                            "编排创建任务成功：ParcelId={ParcelId}, PositionIndex={PositionIndex}, SegmentId={SegmentId}, Earliest={Earliest:o}, Latest={Latest:o}",
                            args.ParcelId, position, segmentOptions.SegmentId, earliest, latest);
                    }

                    positionTimeOffset = positionTimeOffset.AddMilliseconds(transitMs);
                }
            }
            finally {
                gate.Release();
            }
        }

        private async Task HandleParcelTargetUpdatedAsync(ParcelTargetChuteUpdatedEventArgs args, CancellationToken cancellationToken) {
            var gate = _parcelGates.GetOrAdd(args.ParcelId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                foreach (var position in _positionQueueManager.Positions) {
                    cancellationToken.ThrowIfCancellationRequested();

                    var options = _positionOptions.Value.FirstOrDefault(f => f.PositionIndex.Equals(position));
                    if (options == null) {
                        _logger.LogWarning("未找到位置 {Position} 对应的位置配置，跳过该位置任务创建", position);
                        continue;
                    }

                    // 找格口
                    var any = options.LeftChuteIds?.Any(a => a == args.NewTargetChuteId);
                    if (any == true) {
                        var ok = await _positionQueueManager.UpdateTaskAsync(new PositionQueueTaskPatch {
                            PositionIndex = position,
                            ParcelId = args.ParcelId,
                            UpdateMask = PositionQueueTaskUpdateMask.Action,
                            Action = Direction.Left,
                        }, cancellationToken: cancellationToken).ConfigureAwait(false);

                        if (!ok) {
                            _logger.LogWarning(
                                "编排更新动作失败：UpdateTaskAsync 返回 false。ParcelId={ParcelId}, PositionIndex={PositionIndex}, NewTargetChuteId={NewTargetChuteId}, Action=Left",
                                args.ParcelId, position, args.NewTargetChuteId);
                        }

                        await _positionQueueManager.InvalidateTasksAfterPositionAsync(position, args.ParcelId, cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                    }

                    var b = options.RightChuteIds?.Any(a => a == args.NewTargetChuteId);
                    if (b == true) {
                        var ok = await _positionQueueManager.UpdateTaskAsync(new PositionQueueTaskPatch {
                            PositionIndex = position,
                            ParcelId = args.ParcelId,
                            UpdateMask = PositionQueueTaskUpdateMask.Action,
                            Action = Direction.Right,
                        }, cancellationToken: cancellationToken).ConfigureAwait(false);

                        if (!ok) {
                            _logger.LogWarning(
                                "编排更新动作失败：UpdateTaskAsync 返回 false。ParcelId={ParcelId}, PositionIndex={PositionIndex}, NewTargetChuteId={NewTargetChuteId}, Action=Right",
                                args.ParcelId, position, args.NewTargetChuteId);
                        }

                        await _positionQueueManager.InvalidateTasksAfterPositionAsync(position, args.ParcelId, cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            finally {
                gate.Release();
            }
        }

        private enum WorkKind {
            ParcelCreated = 1,
            ParcelTargetUpdated = 2
        }

        private readonly record struct OrchestrationWorkItem {
            public required WorkKind Kind { get; init; }
            public ParcelCreatedEventArgs? ParcelCreatedArgs { get; init; }
            public ParcelTargetChuteUpdatedEventArgs? TargetUpdatedArgs { get; init; }

            public static OrchestrationWorkItem ForParcelCreated(ParcelCreatedEventArgs args)
                => new() { Kind = WorkKind.ParcelCreated, ParcelCreatedArgs = args };

            public static OrchestrationWorkItem ForTargetUpdated(ParcelTargetChuteUpdatedEventArgs args)
                => new() { Kind = WorkKind.ParcelTargetUpdated, TargetUpdatedArgs = args };
        }
    }
}
