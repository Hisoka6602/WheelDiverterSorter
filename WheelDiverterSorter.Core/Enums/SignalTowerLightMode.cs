using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Enums {

    public enum SignalTowerLightMode {

        /// <summary>
        /// 熄灭
        /// </summary>
        [Description("熄灭")]
        Off = 0,

        /// <summary>
        /// 常亮
        /// </summary>
        [Description("常亮")]
        On = 1,
    }
}
