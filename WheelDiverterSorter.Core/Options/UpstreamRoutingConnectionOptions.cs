using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Options {
    /// <summary>
    /// 上游路由连接参数
    /// </summary>
    public sealed record class UpstreamRoutingConnectionOptions {
        /// <summary>
        /// 主机地址（TCP/串口名等）
        /// </summary>
        public required string Endpoint { get; init; } = string.Empty;

        /// <summary>
        /// 端口（非 TCP 场景可为 0）
        /// </summary>
        public required int Port { get; init; }

        /// <summary>
        /// 连接模式（客户端/服务端）
        /// </summary>
        public required TcpConnectionMode Mode { get; init; }

        /// <summary>
        /// 连接超时（毫秒）
        /// </summary>
        public required int ConnectTimeoutMs { get; init; }

        /// <summary>
        /// 接收超时（毫秒）
        /// </summary>
        public required int ReceiveTimeoutMs { get; init; }

        /// <summary>
        /// 发送超时（毫秒）
        /// </summary>
        public required int SendTimeoutMs { get; init; }

        /// <summary>
        /// 是否启用自动重连
        /// </summary>
        public bool IsAutoReconnectEnabled { get; init; } = true;

        /// <summary>
        /// 重连最小延迟（毫秒）
        /// </summary>
        public int ReconnectMinDelayMs { get; init; } = 100;

        /// <summary>
        /// 重连最大延迟（毫秒）
        /// </summary>
        public int ReconnectMaxDelayMs { get; init; } = 2000;

        /// <summary>
        /// 重连退避倍数（建议 1.2~2.0）
        /// </summary>
        public decimal ReconnectBackoffFactor { get; init; } = 1.6m;
    }
}
