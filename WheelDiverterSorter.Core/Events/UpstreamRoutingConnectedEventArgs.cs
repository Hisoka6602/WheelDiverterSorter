using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 上游连接成功事件
    /// </summary>
    public readonly record struct UpstreamRoutingConnectedEventArgs {
        /// <summary>
        /// 事件时间戳（Unix ms）
        /// </summary>
        public required long OccurredAtMs { get; init; }

        /// <summary>
        /// 远端端点描述（无则为空）
        /// </summary>
        public string? RemoteEndpoint { get; init; }
    }
}
