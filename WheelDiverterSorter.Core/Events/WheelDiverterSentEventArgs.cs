using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 摆轮发送内容事件载荷
    /// </summary>
    public readonly record struct WheelDiverterSentEventArgs {
        /// <summary>
        /// 原始内容（例如报文/帧）
        /// </summary>
        public required ReadOnlyMemory<byte> Payload { get; init; }

        /// <summary>
        /// 发送时间戳（毫秒）
        /// </summary>
        public required long OccurredAtMs { get; init; }
    }
}
