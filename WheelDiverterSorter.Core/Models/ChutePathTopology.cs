using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Models {
    /// <summary>
    /// 格口路径拓扑节点
    /// </summary>
    public sealed record class ChutePathTopology {
        /// <summary>
        /// 位置索引（PositionIndex）
        /// </summary>
        public required int PositionIndex { get; init; }

        /// <summary>
        /// 该位置的方向
        /// </summary>
        public required Direction Direction { get; init; }

        /// <summary>
        /// 是否最终节点
        /// </summary>
        public required bool IsFinalNode { get; init; }
    }
}
