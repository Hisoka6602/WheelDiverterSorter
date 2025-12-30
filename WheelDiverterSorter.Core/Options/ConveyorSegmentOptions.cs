using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Options {
    /// <summary>
    /// 输送线线段配置
    /// </summary>
    public sealed record class ConveyorSegmentOptions {
        /// <summary>
        /// 线段Id（建议全局唯一）
        /// </summary>
        public required long SegmentId { get; init; }

        /// <summary>
        /// 线段名称
        /// </summary>
        public required string SegmentName { get; init; } = string.Empty;

        /// <summary>
        /// 线段长度（毫米）
        /// </summary>
        public required long LengthMm { get; init; }

        /// <summary>
        /// 线段速度（毫米/秒）
        /// </summary>
        public required int SpeedMmps { get; init; }

        /// <summary>
        /// 时间容差（毫秒）
        /// </summary>
        public required int TimeToleranceMs { get; init; }

        /// <summary>
        /// 是否有效配置
        /// </summary>
        public bool IsValid => SegmentId > 0
                               && !string.IsNullOrWhiteSpace(SegmentName)
                               && LengthMm > 0
                               && SpeedMmps > 0
                               && TimeToleranceMs >= 0;
    }
}
