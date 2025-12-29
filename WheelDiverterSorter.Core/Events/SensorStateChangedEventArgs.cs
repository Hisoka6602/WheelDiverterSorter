using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 传感器电平变更事件载荷
    /// </summary>
    public readonly record struct SensorStateChangedEventArgs(
        int Point,
        string SensorName,
        IoPointType SensorType,
        IoState OldState,
        IoState NewState,
        IoState TriggerState,
        long OccurredAtMs);
}
