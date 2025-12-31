using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Enums {

    [Flags]
    public enum PositionQueueTaskUpdateMask {

        /// <summary>不更新任何字段</summary>
        [Description("不更新任何字段")]
        None = 0,

        /// <summary>更新动作方向</summary>
        [Description("更新动作方向")]
        Action = 1 << 0,

        /// <summary>更新最早出队时间</summary>
        [Description("更新最早出队时间")]
        EarliestDequeueAt = 1 << 1,

        /// <summary>更新最晚出队时间</summary>
        [Description("更新最晚出队时间")]
        LatestDequeueAt = 1 << 2,

        /// <summary>更新丢失判定时间</summary>
        [Description("更新丢失判定时间")]
        LostDecisionAt = 1 << 3
    }
}
