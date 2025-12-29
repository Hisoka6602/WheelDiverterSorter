using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Enums {

    /// <summary>
    /// IO 状态枚举
    /// </summary>
    public enum IoState {

        /// <summary>
        /// 高电平
        /// </summary>
        [Description("高电平")]
        High = 1,

        /// <summary>
        /// 低电平
        /// </summary>
        [Description("低电平")]
        Low = 0
    }
}
