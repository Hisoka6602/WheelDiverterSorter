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

        //包裹条码
        //包裹上一站Id
        //包裹本站Id
        //包裹从上一站到本站的耗时
    }
}
