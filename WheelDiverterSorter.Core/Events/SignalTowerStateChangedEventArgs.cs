using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Models;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 信号塔状态变更事件载荷
    /// </summary>
    public readonly record struct SignalTowerStateChangedEventArgs {
        /// <summary>
        /// 三色灯状态
        /// </summary>
        public required SignalTowerLightsState Lights { get; init; }

        /// <summary>
        /// 蜂鸣器状态
        /// </summary>
        public required SignalTowerBuzzerState Buzzer { get; init; }

        /// <summary>
        /// 发生时间戳（毫秒）
        /// </summary>
        public required long OccurredAtMs { get; init; }
    }
}
