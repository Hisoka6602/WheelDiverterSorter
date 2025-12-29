using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Events;

namespace WheelDiverterSorter.Core.Models {
    /// <summary>
    /// 包裹信息快照
    /// </summary>
    public sealed record ParcelInfo {
        /// <summary>
        /// 包裹Id
        /// </summary>
        public required long ParcelId { get; init; }

        /// <summary>
        /// 目标格口Id（未分配时为 null）
        /// </summary>
        public long? TargetChuteId { get; private set; }

        /// <summary>
        /// 实际落格格口Id（未落格时为 null）
        /// </summary>
        public long? ActualChuteId { get; private set; }
    }
}
