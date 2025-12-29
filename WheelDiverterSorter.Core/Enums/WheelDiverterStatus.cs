using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Enums {

    /// <summary>
    /// 摆轮状态枚举
    /// </summary>
    public enum WheelDiverterStatus {

        /// <summary>
        /// 未连接
        /// </summary>
        [Description("未连接")]
        Disconnected = 0,

        /// <summary>
        /// 连接中
        /// </summary>
        [Description("连接中")]
        Connecting = 1,

        /// <summary>
        /// 已连接
        /// </summary>
        [Description("已连接")]
        Connected = 2,

        /// <summary>
        /// 运行中
        /// </summary>
        [Description("运行中")]
        Running = 3,

        /// <summary>
        /// 已停止
        /// </summary>
        [Description("已停止")]
        Stopped = 4,

        /// <summary>
        /// 故障
        /// </summary>
        [Description("故障")]
        Faulted = 5
    }
}
