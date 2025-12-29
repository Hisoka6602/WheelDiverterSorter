using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Models {

    public class PositionCachedParcelInfo {

        /// <summary>
        /// 包裹编号
        /// </summary>
        public required long ParcelId { get; init; }

        /// <summary>
        /// 预估出站时间戳（毫秒）
        /// </summary>
        public required long EstimatedExitAtMs { get; init; }

        /// <summary>
        /// 最早出站时间戳（毫秒）
        /// </summary>
        public required long EarliestExitAtMs { get; init; }

        /// <summary>
        /// 最晚出站时间戳（毫秒）
        /// </summary>
        public required long LatestExitAtMs { get; init; }

        /// <summary>
        /// 包裹创建时间戳（毫秒）
        /// </summary>
        public required DateTime CreatedAtMs { get; init; }

        /// <summary>
        /// 格口赋值时间戳（毫秒，未赋值时为 null）
        /// </summary>
        public DateTime? ChuteAssignedAtMs { get; init; }
    }
}
