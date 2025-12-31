using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 包裹提前触发事件载荷
    /// </summary>
    public readonly record struct ParcelEarlyTriggeredEventArgs {
        /// <summary>位置索引</summary>
        public required int PositionIndex { get; init; }

        /// <summary>包裹Id</summary>
        public required long ParcelId { get; init; }

        /// <summary>触发时间</summary>
        public required DateTimeOffset TriggeredAt { get; init; }

        /// <summary>理论到达时间</summary>
        public required DateTimeOffset ExpectedArriveAt { get; init; }

        /// <summary>提前毫秒数（>0 表示提前）</summary>
        public required int EarlyByMs { get; init; }
    }
}
