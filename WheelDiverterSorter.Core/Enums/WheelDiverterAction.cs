using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Enums {

    /// <summary>
    /// 摆轮动作枚举
    /// </summary>
    public enum WheelDiverterAction {

        /// <summary>
        /// 左转
        /// </summary>
        [Description("左转")]
        Left = 0,

        /// <summary>
        /// 直通
        /// </summary>
        [Description("直通")]
        Straight = 1,

        /// <summary>
        /// 右转
        /// </summary>
        [Description("右转")]
        Right = 2,
    }
}
