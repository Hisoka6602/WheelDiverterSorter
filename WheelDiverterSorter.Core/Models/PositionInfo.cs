using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Models {
    /// <summary>
    /// 位置点信息（用于描述某个 PositionIndex 上的路径/时序信息）
    /// </summary>
    public sealed record class PositionInfo {
        /// <summary>
        /// 位置索引（PositionIndex）
        /// </summary>
        public required int Index { get; init; }

        /// <summary>
        /// 线段编号
        /// </summary>
        public required int SegmentId { get; init; }

        /// <summary>
        /// 摆轮编号（该位置无摆轮时为 null）
        /// </summary>
        public long? DiverterId { get; init; }

        /// <summary>
        /// 传感器编号（该位置无传感器时为 null）
        /// </summary>
        public long? SensorId { get; init; }

        /// <summary>
        /// 传感器触发电平（该位置无传感器时为 null）
        /// </summary>
        public IoState? SensorTriggerState { get; init; }

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
    }
}
