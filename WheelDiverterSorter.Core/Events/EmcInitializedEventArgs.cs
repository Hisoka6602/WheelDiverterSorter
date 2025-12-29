using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// EMC 初始化完成事件载荷
    /// </summary>
    public readonly record struct EmcInitializedEventArgs {
        /// <summary>
        /// 初始化是否成功
        /// </summary>
        public required bool IsSuccess { get; init; }

        /// <summary>
        /// 初始化耗时（可选）
        /// </summary>
        public TimeSpan? Elapsed { get; init; }

        /// <summary>
        /// 附加信息（用于日志与诊断）
        /// </summary>
        public string? Message { get; init; }
    }
}
