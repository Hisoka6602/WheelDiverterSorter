using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 包裹丢失事件载荷
    /// </summary>
    public readonly record struct ParcelLostEventArgs {
        /// <summary>位置索引</summary>
        public required int PositionIndex { get; init; }

        /// <summary>包裹Id</summary>
        public required long ParcelId { get; init; }

        /// <summary>判定丢失时间</summary>
        public required DateTimeOffset LostAt { get; init; }

        /// <summary>判定原因</summary>
        public string? Reason { get; init; }
    }
}
