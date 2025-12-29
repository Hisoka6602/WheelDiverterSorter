using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Models {
    /// <summary>
    /// 三色灯状态
    /// </summary>
    public sealed record class SignalTowerLightsState {
        /// <summary>
        /// 红灯模式
        /// </summary>
        public required SignalTowerLightMode Red { get; init; }

        /// <summary>
        /// 黄灯模式
        /// </summary>
        public required SignalTowerLightMode Yellow { get; init; }

        /// <summary>
        /// 绿灯模式
        /// </summary>
        public required SignalTowerLightMode Green { get; init; }
    }
}
