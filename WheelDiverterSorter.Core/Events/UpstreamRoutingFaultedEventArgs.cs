using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 上游连接异常事件（用于隔离异常，不影响上层调用链）
    /// </summary>
    public readonly record struct UpstreamRoutingFaultedEventArgs {
        /// <summary>
        /// 事件时间戳（Unix ms）
        /// </summary>
        public required long OccurredAtMs { get; init; }

        /// <summary>
        /// 异常信息
        /// </summary>
        public required Exception? Exception { get; init; }

        /// <summary>
        /// 操作上下文（无则为空）
        /// </summary>
        public string? Operation { get; init; }
    }
}
