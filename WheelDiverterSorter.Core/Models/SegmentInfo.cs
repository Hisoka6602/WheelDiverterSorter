using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Models {

    /// <summary>
    /// 线段
    /// </summary>
    public class SegmentInfo {

        /// <summary>
        /// 线段编号
        /// </summary>
        public int SegmentId { get; set; }

        /// <summary>
        /// 线段名称
        /// </summary>
        public string SegmentName { get; set; } = string.Empty;

        /// <summary>
        /// 线段长度（毫米）
        /// </summary>
        public double LengthMm { get; set; }

        /// <summary>
        /// 线段速度（毫米/秒）
        /// </summary>
        public required decimal SpeedMmps { get; init; }

        /// <summary>
        /// 时间容差（毫秒）
        /// </summary>
        public required int TimeToleranceMs { get; init; }
    }
}
