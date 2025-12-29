using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Drivers.Vendors.ShuDiNiao {

    /// <summary>
    /// 数递鸟应答/完成状态码（信息三第5字节）
    /// </summary>
    public enum ShuDiNiaoResponseCode : byte {

        /// <summary>
        /// 运行应答
        /// </summary>
        [Description("运行应答")]
        RunAck = 0x51,

        /// <summary>
        /// 停止应答
        /// </summary>
        [Description("停止应答")]
        StopAck = 0x52,

        /// <summary>
        /// 左摆应答
        /// </summary>
        [Description("左摆应答")]
        TurnLeftAck = 0x53,

        /// <summary>
        /// 中摆应答
        /// </summary>
        [Description("中摆应答")]
        ReturnCenterAck = 0x54,

        /// <summary>
        /// 右摆应答
        /// </summary>
        [Description("右摆应答")]
        TurnRightAck = 0x55,

        /// <summary>
        /// 左摆完成
        /// </summary>
        [Description("左摆完成")]
        TurnLeftComplete = 0x56,

        /// <summary>
        /// 中摆完成
        /// </summary>
        [Description("中摆完成")]
        ReturnCenterComplete = 0x57,

        /// <summary>
        /// 右摆完成
        /// </summary>
        [Description("右摆完成")]
        TurnRightComplete = 0x58
    }
}
