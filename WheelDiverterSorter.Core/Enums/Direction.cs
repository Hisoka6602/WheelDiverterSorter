using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Enums {

    /// <summary>
    /// 方向枚举
    /// </summary>
    public enum Direction {

        /// <summary>
        /// 左
        /// </summary>
        [Description("左")]
        Left = 0,

        /// <summary>
        /// 直通
        /// </summary>
        [Description("直通")]
        Straight = 1,

        /// <summary>
        /// 右
        /// </summary>
        [Description("右")]
        Right = 2
    }
}
