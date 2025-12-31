using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 包裹位置超时事件载荷
    /// </summary>
    public readonly record struct ParcelPositionTimedOutEventArgs {
        /// <summary>位置索引</summary>
        public required int PositionIndex { get; init; }

        /// <summary>包裹Id</summary>
        public required long ParcelId { get; init; }

        /// <summary>理论到达时间</summary>
        public required DateTimeOffset ExpectedArriveAt { get; init; }

        /// <summary>超时触发时间</summary>
        public required DateTimeOffset TimedOutAt { get; init; }

        /// <summary>超时毫秒数（>=0）</summary>
        public required int TimeoutByMs { get; init; }
    }
}
