using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Models {

    public class ChuteInfo {

        /// <summary>
        /// 格口编号
        /// </summary>
        public required long ChuteId { get; init; }

        /// <summary>
        /// 格口名称
        /// </summary>
        public required string ChuteName { get; init; } = string.Empty;

        /// <summary>
        /// 位置
        /// </summary>
        public required int PositionId { get; init; }

        /// <summary>
        /// 格口方向
        /// </summary>
        public required Direction Direction { get; init; }
    }
}
