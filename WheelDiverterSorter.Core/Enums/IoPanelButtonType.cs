using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Enums {

    /// <summary>
    /// IO 面板按钮类型枚举
    /// </summary>
    public enum IoPanelButtonType {

        /// <summary>
        /// 启动按钮
        /// </summary>
        [Description("启动按钮")]
        Start = 0,

        /// <summary>
        /// 停止按钮
        /// </summary>
        [Description("停止按钮")]
        Stop = 1,

        /// <summary>
        /// 急停按钮
        /// </summary>
        [Description("急停按钮")]
        EmergencyStop = 2,

        /// <summary>
        /// 复位按钮
        /// </summary>
        [Description("复位按钮")]
        Reset = 3
    }
}
