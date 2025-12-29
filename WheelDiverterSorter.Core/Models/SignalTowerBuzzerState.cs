using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Models {
    /// <summary>
    /// 蜂鸣器状态
    /// </summary>
    public sealed record class SignalTowerBuzzerState {
        /// <summary>
        /// 蜂鸣器模式
        /// </summary>
        public required SignalTowerBuzzerMode Mode { get; init; }

        /// <summary>
        /// 间歇周期（毫秒）。仅在 Mode=Beep 时生效；小于等于0表示使用默认值
        /// </summary>
        public int BeepPeriodMs { get; init; }
    }
}
