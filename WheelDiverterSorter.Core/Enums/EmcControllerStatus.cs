using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Enums {

    /// <summary>
    /// EMC 控制器状态枚举
    /// </summary>
    public enum EmcControllerStatus {

        /// <summary>
        /// 未初始化
        /// </summary>
        [Description("未初始化")]
        Uninitialized = 0,

        /// <summary>
        /// 初始化中
        /// </summary>
        [Description("初始化中")]
        Initializing = 1,

        /// <summary>
        /// 已就绪
        /// </summary>
        [Description("已就绪")]
        Ready = 2,

        /// <summary>
        /// 连接中
        /// </summary>
        [Description("连接中")]
        Connecting = 3,

        /// <summary>
        /// 已连接
        /// </summary>
        [Description("已连接")]
        Connected = 4,

        /// <summary>
        /// 已断开
        /// </summary>
        [Description("已断开")]
        Disconnected = 5,

        /// <summary>
        /// 故障
        /// </summary>
        [Description("故障")]
        Faulted = 6
    }
}
