using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Options {

    public class SpacingPauseOptions {

        /// <summary>
        /// 是否启用拉距暂停
        /// </summary>
        public bool IsSpacingPauseEnabled { get; init; }

        /// <summary>
        /// 暂停感应点 IO 点位
        /// </summary>
        public int PauseSensorPoint { get; init; }

        /// <summary>
        /// 暂停感应点 IO 有效电平（触发电平）
        /// </summary>
        public IoState PauseSensorTriggerState { get; init; } = IoState.High;

        /// <summary>
        /// 称重台入口 IO 点位
        /// </summary>
        public int WeighingStationEntryPoint { get; init; }

        /// <summary>
        /// 称重台入口 IO 有效电平（触发电平）
        /// </summary>
        public IoState WeighingStationEntryTriggerState { get; init; } = IoState.High;

        /// <summary>
        /// 称重台出口 IO 点位
        /// </summary>
        public int WeighingStationExitPoint { get; init; }

        /// <summary>
        /// 称重台出口 IO 有效电平（触发电平）
        /// </summary>
        public IoState WeighingStationExitTriggerState { get; init; } = IoState.High;

        /// <summary>
        /// 拉距 IO 组集合（用于实施拉距暂停的相关 IO 触发点）
        /// </summary>
        public IReadOnlyList<SpacingIoTriggerOptions> SpacingIoTriggers { get; init; } = [];
    }
}
