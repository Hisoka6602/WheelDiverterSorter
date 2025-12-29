using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 上游连接断开事件
    /// </summary>
    public readonly record struct UpstreamRoutingDisconnectedEventArgs {
        /// <summary>
        /// 事件时间戳（Unix ms）
        /// </summary>
        public required long OccurredAtMs { get; init; }

        /// <summary>
        /// 断开原因（无则为空）
        /// </summary>
        public string? Reason { get; init; }
    }
}
