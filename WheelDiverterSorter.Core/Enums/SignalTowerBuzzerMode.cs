using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Enums {

    /// <summary>
    /// 信号塔蜂鸣器模式
    /// </summary>
    public enum SignalTowerBuzzerMode {

        /// <summary>
        /// 关闭
        /// </summary>
        [Description("关闭")]
        Off = 0,

        /// <summary>
        /// 常鸣
        /// </summary>
        [Description("常鸣")]
        On = 1,
    }
}
