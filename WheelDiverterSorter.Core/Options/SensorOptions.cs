using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Options {
    public record class SensorOptions {
        /// <summary>
        /// 点位编号
        /// </summary>
        public required int Point { get; init; }

        /// <summary>
        /// 点位类型
        /// </summary>
        public required IoPointType Type { get; init; }

        /// <summary>
        /// 触发电平（有效电平）
        /// </summary>
        public required IoState TriggerState { get; init; }
        /// <summary>
        /// 传感器名称
        /// </summary>
        public required string SensorName { get; init; } = string.Empty;

        /// <summary>
        /// 采样间隔（毫秒）
        /// </summary>
        public int PollIntervalMs { get; init; } = 10;
        /// <summary>
        /// 防抖窗口（毫秒）
        /// </summary>
        public int DebounceWindowMs { get; init; } = 30;
    }
}
