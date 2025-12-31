using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using WheelDiverterSorter.Execution;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Models;
using WheelDiverterSorter.Core.Manager;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Host.Servers {

    /// <summary>
    /// 分拣编排服务
    /// </summary>
    public class SortingOrchestrationHostedService : BackgroundService {
        private readonly ILogger<SortingOrchestrationHostedService> _logger;
        private readonly IParcelManager _parcelManager;

        public SortingOrchestrationHostedService(ILogger<SortingOrchestrationHostedService> logger,
            IOptions<List<ConveyorSegmentOptions>> conveyorSegmentOptions,
            IOptions<List<PositionOptions>> positionOptions,
            IParcelManager parcelManager,
            IPositionQueueManager positionQueueManager) {
            _logger = logger;

            _parcelManager = parcelManager;

            _parcelManager.ParcelCreated += async (sender, args) => {
                await Task.Yield();
                //初次创建包裹时应该是全部直行
                var positionTimeOffset = DateTimeOffset.Now;
                foreach (var position in positionQueueManager.Positions) {
                    //获取最早出队时间

                    var segmentId = positionOptions.Value.FirstOrDefault(f => f.PositionIndex.Equals(position))
                        ?.SegmentId ?? 0;

                    var segmentOptions = conveyorSegmentOptions.Value.FirstOrDefault(f => f.IsValid &&
                        f.SegmentId == segmentId);
                    if (segmentOptions == null) {
                        _logger.LogWarning("未找到位置 {Position} 对应的输送线段配置，跳过该位置任务创建", position);
                        continue;
                    }

                    var lengthMm = segmentOptions.LengthMm / segmentOptions.SpeedMmps;
                    var mm = lengthMm * 1000;
                    await positionQueueManager.CreateTaskAsync(new PositionQueueTask {
                        PositionIndex = position,
                        ParcelId = args.ParcelId,
                        Action = Direction.Straight,
                        EarliestDequeueAt = positionTimeOffset.AddMilliseconds(mm)
                            .AddMilliseconds(0 - segmentOptions.TimeToleranceMs),
                        LatestDequeueAt = positionTimeOffset.AddMilliseconds(mm)
                            .AddMilliseconds(segmentOptions.TimeToleranceMs),
                        LostDecisionAt = null
                    });
                    positionTimeOffset = positionTimeOffset.AddMilliseconds(mm);
                }
            };
            _parcelManager.ParcelTargetChuteUpdated += async (sender, args) => {
                await Task.Yield();
                foreach (var position in positionQueueManager.Positions) {
                    var options = positionOptions.Value.FirstOrDefault(f => f.PositionIndex.Equals(position));
                    if (options == null) {
                        _logger.LogWarning("未找到位置 {Position} 对应的位置配置，跳过该位置任务创建", position);
                        continue;
                    }

                    //找格口
                    var any = options.LeftChuteIds?.Any(a => a == args.NewTargetChuteId);
                    if (any == true) {
                        //左侧找到
                        await positionQueueManager.UpdateTaskAsync(new PositionQueueTaskPatch {
                            PositionIndex = position,
                            ParcelId = args.ParcelId,
                            UpdateMask = PositionQueueTaskUpdateMask.Action,
                            Action = Direction.Left,
                        });
                        //更新剩余任务为失效
                        await positionQueueManager.InvalidateTasksAfterPositionAsync(position, args.ParcelId);
                    }

                    var b = options.RightChuteIds?.Any(a => a == args.NewTargetChuteId);
                    if (b == true) {
                        //右侧找到
                        await positionQueueManager.UpdateTaskAsync(new PositionQueueTaskPatch {
                            PositionIndex = position,
                            ParcelId = args.ParcelId,
                            UpdateMask = PositionQueueTaskUpdateMask.Action,
                            Action = Direction.Right,
                        });
                        //更新剩余任务为失效
                        await positionQueueManager.InvalidateTasksAfterPositionAsync(position, args.ParcelId);
                    }
                }
            };
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) {
            return Task.CompletedTask;
        }
    }
}
