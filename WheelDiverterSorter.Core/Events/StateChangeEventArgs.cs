using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Events {

    /// <summary>
    /// 系统状态变更事件参数
    /// </summary>
    public class StateChangeEventArgs : EventArgs {

        /// <summary>转移前的状态</summary>
        public required SystemState OldState { get; init; }

        /// <summary>转移后的状态</summary>
        public required SystemState NewState { get; init; }

        /// <summary>状态转换时间</summary>
        public required DateTimeOffset ChangedAt { get; init; }
    }
}
