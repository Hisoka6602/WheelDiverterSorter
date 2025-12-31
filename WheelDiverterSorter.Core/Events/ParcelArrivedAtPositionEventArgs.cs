using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 包裹到达位置事件载荷
    /// </summary>
    public readonly record struct ParcelArrivedAtPositionEventArgs {
        /// <summary>位置索引</summary>
        public required int PositionIndex { get; init; }

        /// <summary>包裹Id</summary>
        public required long ParcelId { get; init; }

        /// <summary>到达时间</summary>
        public required DateTimeOffset ArrivedAt { get; init; }
    }
}
