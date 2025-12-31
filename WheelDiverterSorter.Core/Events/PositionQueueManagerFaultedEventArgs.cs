using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 位置队列管理器异常事件载荷（用于隔离异常，不影响上层调用链）
    /// </summary>
    public readonly record struct PositionQueueManagerFaultedEventArgs {
        /// <summary>异常对象</summary>
        public required Exception Exception { get; init; }

        /// <summary>发生时间</summary>
        public required DateTimeOffset OccurredAt { get; init; }
        /// <summary>
        /// 位置索引（无则为 null）
        /// </summary>
        public int? PositionIndex { get; init; }

        /// <summary>
        /// 包裹Id（无则为 null）
        /// </summary>
        public long? ParcelId { get; init; }

        /// <summary>
        /// 异常上下文（无则为 null）
        /// </summary>
        public string? Context { get; init; }
    }
}
