using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 干扰信号事件载荷
    /// </summary>
    public record struct PositionInterferenceDetectedEventArgs {
        /// <summary>
        /// 位置索引
        /// </summary>
        public required int PositionIndex { get; init; }

        /// <summary>
        /// 发生时间
        /// </summary>
        public required DateTimeOffset OccurredAt { get; init; }

        /// <summary>
        /// 详情信息
        /// </summary>
        public string? Details { get; init; }
    }
}
