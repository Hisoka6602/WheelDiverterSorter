using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 监控状态变更事件载荷
    /// </summary>
    public readonly record struct SensorMonitoringStatusChangedEventArgs(
        SensorMonitoringStatus OldStatus,
        SensorMonitoringStatus NewStatus,
        DateTimeOffset Timestamp);
}
