using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace WheelDiverterSorter.Core.Options {
    /// <summary>
    /// 运行前预警 IO 点配置
    /// </summary>
    public sealed record class PreRunWarningIoOptions {
        /// <summary>
        /// IO 点位编号
        /// </summary>
        public required int Point { get; init; }

        /// <summary>
        /// 触发电平（有效电平）
        /// </summary>
        public required IoState TriggerState { get; init; }

        /// <summary>
        /// 说明（用于日志与运维定位）
        /// </summary>
        public string Description { get; init; } = string.Empty;
    }
}
