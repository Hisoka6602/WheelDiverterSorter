using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 摆轮连接事件载荷
    /// </summary>
    public readonly record struct WheelDiverterConnectedEventArgs {
        /// <summary>
        /// 发生时间戳（毫秒）
        /// </summary>
        public required long OccurredAtMs { get; init; }
    }
}
