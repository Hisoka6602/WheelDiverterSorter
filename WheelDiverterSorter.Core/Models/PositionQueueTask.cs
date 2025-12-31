using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Models {
    /// <summary>
    /// 位置队列任务（高频：用于驱动位置队列出队、提前触发判定、超时/丢失判定）
    /// </summary>
    public readonly record struct PositionQueueTask {
        /// <summary>
        /// 位置索引（拓扑中的 PositionIndex）
        /// </summary>
        public required int PositionIndex { get; init; }

        /// <summary>
        /// 包裹Id
        /// </summary>
        public required long ParcelId { get; init; }

        /// <summary>
        /// 目标格口Id（任务与落格相关时必填；否则为 null）
        /// </summary>
        public long? TargetChuteId { get; init; }

        /// <summary>
        /// 执行动作方向
        /// </summary>
        public required Direction Action { get; init; }

        /// <summary>
        /// 最早出队时间
        /// </summary>
        public required DateTimeOffset EarliestDequeueAt { get; init; }

        /// <summary>
        /// 最晚出队时间
        /// </summary>
        public required DateTimeOffset LatestDequeueAt { get; init; }

        /// <summary>
        /// 判定为包裹丢失时间
        /// </summary>
        public required DateTimeOffset LostDecisionAt { get; init; }

        /// <summary>是否已被标记为失效（由上层决定是否出队）</summary>
        public bool IsInvalidated { get; init; }
        /// <summary>
        /// 任务有效性校验（避免在热路径里反复计算）
        /// </summary>
        public bool IsValid =>
            PositionIndex >= 0
            && ParcelId > 0
            && EarliestDequeueAt <= LatestDequeueAt
            && LatestDequeueAt <= LostDecisionAt;
    }
}
