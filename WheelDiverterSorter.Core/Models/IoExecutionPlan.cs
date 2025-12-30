using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Models {
    public record class IoExecutionPlan {
        /// <summary>
        /// 点位编号
        /// </summary>
        public required int Point { get; init; }

        /// <summary>
        /// 触发电平（计划开始执行时写入的电平）
        /// </summary>
        public required IoState TriggerState { get; init; }

        /// <summary>
        /// 反向电平（计划停止/复位时写入的电平）
        /// </summary>
        public required IoState ReverseState { get; init; }

        /// <summary>
        /// 计划执行时间（Unix 毫秒时间戳）
        /// </summary>
        public required DateTime ExecuteTime { get; init; }

        /// <summary>
        /// 计划停止时间（Unix 毫秒时间戳）
        /// </summary>
        public required DateTime? StopTime { get; init; }

        /// <summary>
        /// 计划状态
        /// </summary>
        public IoExecutionPlanStatus Status { get; set; } = IoExecutionPlanStatus.Pending;
    }
}
