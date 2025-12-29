using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// IO 面板异常事件载荷
    /// </summary>
    public readonly record struct IoPanelFaultedEventArgs(
        string Message,
        Exception? Exception,
        DateTimeOffset Timestamp);
}
