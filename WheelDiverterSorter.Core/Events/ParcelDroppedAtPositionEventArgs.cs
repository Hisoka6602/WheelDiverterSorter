using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 包裹在位置落格事件载荷
    /// </summary>
    public readonly record struct ParcelDroppedAtPositionEventArgs {
        /// <summary>位置索引</summary>
        public required int PositionIndex { get; init; }

        /// <summary>包裹Id</summary>
        public required long ParcelId { get; init; }

        /// <summary>实际落格格口Id</summary>
        public required long ActualChuteId { get; init; }

        /// <summary>落格时间</summary>
        public required DateTimeOffset DroppedAt { get; init; }
    }
}
