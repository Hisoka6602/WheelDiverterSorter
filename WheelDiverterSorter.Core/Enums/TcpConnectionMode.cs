using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Enums {
    public enum TcpConnectionMode {

        /// <summary>
        /// 客户端模式（主动连接设备）
        /// </summary>
        [Description("客户端模式")]
        Client = 0,

        /// <summary>
        /// 服务端模式（监听端口等待设备连接）
        /// </summary>
        [Description("服务端模式")]
        Server = 1
    }
}
