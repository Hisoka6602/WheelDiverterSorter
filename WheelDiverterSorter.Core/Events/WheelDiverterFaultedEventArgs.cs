using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 摆轮异常事件载荷
    /// </summary>
    public readonly record struct WheelDiverterFaultedEventArgs {
        /// <summary>
        /// 操作名称
        /// </summary>
        public required string? Operation { get; init; }

        /// <summary>
        /// 异常对象
        /// </summary>
        public required Exception Exception { get; init; }

        /// <summary>
        /// 发生时间戳（毫秒）
        /// </summary>
        public required long OccurredAtMs { get; init; }
    }
}
