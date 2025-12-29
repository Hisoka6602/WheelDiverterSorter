using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 摆轮断开事件载荷
    /// </summary>
    public readonly record struct WheelDiverterDisconnectedEventArgs {
        /// <summary>
        /// 断开原因（可为空）
        /// </summary>
        public string? Reason { get; init; }

        /// <summary>
        /// 发生时间戳（毫秒）
        /// </summary>
        public required long OccurredAtMs { get; init; }
    }
}
