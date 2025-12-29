using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Enums {
    public enum SensorMonitoringStatus {

        /// <summary>
        /// 已停止
        /// </summary>
        [Description("已停止")]
        Stopped = 0,

        /// <summary>
        /// 监控中
        /// </summary>
        [Description("监控中")]
        Monitoring = 1,

        /// <summary>
        /// 异常
        /// </summary>
        [Description("异常")]
        Faulted = 2
    }
}
