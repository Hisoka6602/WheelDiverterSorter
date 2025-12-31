using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Models {
    /// <summary>
    /// 位置队列任务部分更新载荷
    /// </summary>
    /// <remarks>
    /// 仅对 UpdateMask 指定的字段执行更新；未包含的字段保持不变。
    /// </remarks>
    public readonly record struct PositionQueueTaskPatch {
        /// <summary>位置索引（定位键）</summary>
        public required int PositionIndex { get; init; }

        /// <summary>包裹Id（定位键）</summary>
        public required long ParcelId { get; init; }

        /// <summary>更新掩码（决定哪些字段生效）</summary>
        public required PositionQueueTaskUpdateMask UpdateMask { get; init; }

        /// <summary>动作方向（UpdateMask 包含 Action 时生效）</summary>
        public Direction Action { get; init; }

        /// <summary>最早出队时间（UpdateMask 包含 EarliestDequeueAt 时生效）</summary>
        public DateTimeOffset EarliestDequeueAt { get; init; }

        /// <summary>最晚出队时间（UpdateMask 包含 LatestDequeueAt 时生效）</summary>
        public DateTimeOffset LatestDequeueAt { get; init; }

        /// <summary>丢失判定时间（UpdateMask 包含 LostDecisionAt 时生效）</summary>
        public DateTimeOffset? LostDecisionAt { get; init; }

        /// <summary>基础有效性校验</summary>
        public bool IsValid => PositionIndex >= 0 && ParcelId > 0 && UpdateMask != PositionQueueTaskUpdateMask.None;

        public bool HasAction => (UpdateMask & PositionQueueTaskUpdateMask.Action) != 0;
        public bool HasEarliestDequeueAt => (UpdateMask & PositionQueueTaskUpdateMask.EarliestDequeueAt) != 0;
        public bool HasLatestDequeueAt => (UpdateMask & PositionQueueTaskUpdateMask.LatestDequeueAt) != 0;
        public bool HasLostDecisionAt => (UpdateMask & PositionQueueTaskUpdateMask.LostDecisionAt) != 0;
    }
}
