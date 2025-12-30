using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Options {
    /// <summary>
    /// IO 联动点位信息
    /// </summary>
    public sealed record class IoLinkagePointOptions {
        /// <summary>
        /// 点位编号
        /// </summary>
        public required int Point { get; init; }

        /// <summary>
        /// 关联的系统状态
        /// </summary>
        public required SystemState RelatedSystemState { get; init; }

        /// <summary>
        /// 名称
        /// </summary>
        public required string Name { get; init; } = string.Empty;

        /// <summary>
        /// 触发电平
        /// </summary>
        public required IoState TriggerState { get; init; }

        /// <summary>
        /// 持续时间（毫秒），0 表示永久持续
        /// </summary>
        public required int DurationMs { get; init; }

        /// <summary>
        /// 延迟触发时间（毫秒），0 表示不延迟
        /// </summary>
        public required int DelayMs { get; init; }

        /// <summary>
        /// 说明
        /// </summary>
        public string? Note { get; init; }

        /// <summary>
        /// 是否永久持续
        /// </summary>
        public bool IsPermanent => DurationMs <= 0;

        /// <summary>
        /// 是否启用延迟触发
        /// </summary>
        public bool IsDelayEnabled => DelayMs > 0;
    }
}
