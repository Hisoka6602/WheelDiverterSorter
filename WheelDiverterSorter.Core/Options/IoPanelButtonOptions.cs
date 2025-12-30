using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Options {
    public record class IoPanelButtonOptions {
        /// <summary>
        /// 按钮点位编号
        /// </summary>
        public required int Point { get; init; }

        /// <summary>
        /// 按钮类型
        /// </summary>
        public required IoPanelButtonType ButtonType { get; init; }
        /// <summary>
        /// 点位类型
        /// </summary>
        public required IoPointType Type { get; init; }
        /// <summary>
        /// 按钮名称
        /// </summary>
        public required string ButtonName { get; init; } = string.Empty;

        /// <summary>
        /// 触发电平（按下/有效电平）
        /// </summary>
        public required IoState TriggerState { get; init; }

        /// <summary>
        /// 防抖时间（毫秒）
        /// </summary>
        public int PollIntervalMs { get; init; } = 10;

        public int DebounceWindowMs { get; init; } = 30;
    }
}
