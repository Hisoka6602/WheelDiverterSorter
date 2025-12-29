using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// EMC 状态变更事件载荷
    /// </summary>
    public readonly record struct EmcStatusChangedEventArgs {
        /// <summary>
        /// 新状态
        /// </summary>
        public required EmcControllerStatus Status { get; init; }

        /// <summary>
        /// 异常代码（无异常时为 null）
        /// </summary>
        public int? FaultCode { get; init; }

        /// <summary>
        /// 发生时间戳（毫秒）
        /// </summary>
        public required long OccurredAtMs { get; init; }
    }
}
