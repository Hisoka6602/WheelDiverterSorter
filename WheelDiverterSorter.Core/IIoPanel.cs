using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Models;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Core {

    /// <summary>
    /// IO面板接口
    /// </summary>
    public interface IIoPanel : IDisposable {

        /// <summary>
        /// 按下启动按钮事件
        /// </summary>
        event EventHandler<IoPanelButtonPressedEventArgs>? StartButtonPressed;

        /// <summary>
        /// 按下停止按钮事件
        /// </summary>
        event EventHandler<IoPanelButtonPressedEventArgs>? StopButtonPressed;

        /// <summary>
        /// 按下急停按钮事件
        /// </summary>
        event EventHandler<IoPanelButtonPressedEventArgs>? EmergencyStopButtonPressed;

        /// <summary>
        /// 按下复位按钮事件
        /// </summary>
        event EventHandler<IoPanelButtonPressedEventArgs>? ResetButtonPressed;

        /// <summary>
        /// 解除急停按钮事件
        /// </summary>
        event EventHandler<IoPanelButtonReleasedEventArgs>? EmergencyStopButtonReleased;

        /// <summary>
        /// 异常事件（用于隔离异常，不影响上层调用链）
        /// </summary>
        event EventHandler<IoPanelFaultedEventArgs>? Faulted;

        /// <summary>
        /// 启动监控
        /// </summary>
        ValueTask StartMonitoringAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止监控
        /// </summary>
        ValueTask StopMonitoringAsync(CancellationToken cancellationToken = default);
    }
}
