using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// IO 面板按钮释放事件载荷
    /// </summary>
    public readonly record struct IoPanelButtonReleasedEventArgs(
        int Point,
        IoPanelButtonType ButtonType,
        string ButtonName,
        DateTimeOffset Timestamp);
}
