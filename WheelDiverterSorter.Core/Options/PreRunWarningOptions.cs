using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WheelDiverterSorter.Core.Options {
    /// <summary>
    /// 运行前预警配置
    /// </summary>
    public sealed record class PreRunWarningOptions {
        /// <summary>
        /// 预警时长（毫秒）
        /// </summary>

        public int PreWarningDurationMs { get; init; } = 3000;

        /// <summary>
        /// 预警 IO 组
        /// </summary>
        public List<PreRunWarningIoOptions> IoGroup { get; init; } = [];
    }
}
