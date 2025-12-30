using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Enums {

    /// <summary>
    /// IO 执行计划状态
    /// </summary>
    public enum IoExecutionPlanStatus {

        /// <summary>
        /// 待执行
        /// </summary>
        [Description("待执行")]
        Pending = 0,

        /// <summary>
        /// 正在执行
        /// </summary>
        [Description("正在执行")]
        Executing = 1,

        /// <summary>
        /// 已完成
        /// </summary>
        [Description("已完成")]
        Completed = 2
    }
}
