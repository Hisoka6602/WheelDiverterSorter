using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Models {
    public class SensorInfo {

        /// <summary>
        /// 点位编号
        /// </summary>
        public required int Point { get; init; }

        /// <summary>
        /// 点位类型
        /// </summary>
        public required IoPointType Type { get; init; }

        /// <summary>
        /// 状态
        /// </summary>
        public IoState State { get; set; }
    }
}
