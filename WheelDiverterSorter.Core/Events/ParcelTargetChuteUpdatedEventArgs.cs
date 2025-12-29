using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 包裹目标格口更新事件载荷
    /// </summary>
    public readonly record struct ParcelTargetChuteUpdatedEventArgs {
        /// <summary>
        /// 包裹Id
        /// </summary>
        public required long ParcelId { get; init; }

        /// <summary>
        /// 旧目标格口Id
        /// </summary>
        public long? OldTargetChuteId { get; init; }

        /// <summary>
        /// 新目标格口Id
        /// </summary>
        public required long NewTargetChuteId { get; init; }

        /// <summary>
        /// 赋值时间
        /// </summary>
        public required DateTimeOffset AssignedAt { get; init; }
    }
}
