using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Models;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 位置队列窥探结果（用于“清理队首无效任务后再窥探队首”的返回值）
    /// </summary>
    public readonly record struct PositionQueuePeekResult {
        /// <summary>
        /// 是否存在可窥探的队首任务
        /// </summary>
        public required bool HasTask { get; init; }

        /// <summary>
        /// 队首任务（HasTask=false 时为默认值）
        /// </summary>
        public PositionQueueTask Task { get; init; }

        /// <summary>
        /// 本次调用清理的队首无效任务数量
        /// </summary>
        public required int PrunedCount { get; init; }

        /// <summary>
        /// 是否因达到清理上限而提前终止
        /// </summary>
        public required bool IsPruneLimitReached { get; init; }
    }
}
