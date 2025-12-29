using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Enums {

    /// <summary>
    /// 上游路由连接状态
    /// </summary>
    public enum UpstreamRoutingStatus {

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
        /// 断开中
        /// </summary>
        [Description("断开中")]
        Disconnecting = 3,

        /// <summary>
        /// 异常
        /// </summary>
        [Description("异常")]
        Faulted = 9
    }
}
