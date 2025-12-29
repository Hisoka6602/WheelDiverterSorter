using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Enums {

    /// <summary>
    /// 包裹状态枚举
    /// </summary>
    public enum ParcelStatus {

        /// <summary>
        /// 已创建等待格口赋值
        /// </summary>
        [Description("已创建等待格口赋值")]
        CreatedWaitingChuteAssignment = 0,

        /// <summary>
        /// 已赋值等待落格
        /// </summary>
        [Description("已赋值等待落格")]
        AssignedWaitingDrop = 1,

        /// <summary>
        /// 已落格
        /// </summary>
        [Description("已落格")]
        Dropped = 2,

        /// <summary>
        /// 已丢失
        /// </summary>
        [Description("已丢失")]
        Lost = 3
    }
}
