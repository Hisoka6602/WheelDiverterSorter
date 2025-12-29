using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Models;

namespace WheelDiverterSorter.Core.Events {
    public record IoPanelButtonPressedEventArgs {
        /// <summary>
        /// 按钮类型
        /// </summary>
        public required IoPanelButtonType ButtonType { get; init; }

        /// <summary>
        /// 点位信息
        /// </summary>
        public required IoPointInfo Point { get; init; }

        /// <summary>
        /// 发生时间戳（毫秒）
        /// </summary>
        public required long OccurredAtMs { get; init; }
    }
}
