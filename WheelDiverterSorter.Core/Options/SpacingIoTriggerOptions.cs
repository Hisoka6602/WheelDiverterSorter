using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Options {
    /// <summary>
    /// 拉距 IO 触发配置项
    /// </summary>
    public readonly record struct SpacingIoTriggerOptions {
        /// <summary>
        /// 拉距 IO 点位
        /// </summary>
        public int Point { get; init; }

        /// <summary>
        /// 拉距 IO 有效电平（触发电平）
        /// </summary>
        public IoState TriggerState { get; init; }
    }
}
