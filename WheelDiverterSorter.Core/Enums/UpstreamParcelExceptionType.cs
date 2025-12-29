using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Enums {

    /// <summary>
    /// 包裹异常类型
    /// </summary>
    public enum UpstreamParcelExceptionType {

        /// <summary>
        /// 超时
        /// </summary>
        [Description("超时")]
        Timeout = 1,

        /// <summary>
        /// 丢失
        /// </summary>
        [Description("丢失")]
        Lost = 2,

        /// <summary>
        /// 去异常口
        /// </summary>
        [Description("去异常口")]
        SentToExceptionChute = 3,

        /// <summary>
        /// 上游数据无效
        /// </summary>
        [Description("上游数据无效")]
        InvalidUpstreamPayload = 4
    }
}
