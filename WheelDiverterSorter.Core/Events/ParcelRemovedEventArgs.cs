using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 包裹移除事件载荷
    /// </summary>
    public readonly record struct ParcelRemovedEventArgs {
        /// <summary>
        /// 包裹Id
        /// </summary>
        public required long ParcelId { get; init; }

        /// <summary>
        /// 移除原因（可选：超时、完成、异常口、手工清理等）
        /// </summary>
        public string? Reason { get; init; }

        /// <summary>
        /// 移除时间
        /// </summary>
        public required DateTimeOffset RemovedAt { get; init; }
    }
}
