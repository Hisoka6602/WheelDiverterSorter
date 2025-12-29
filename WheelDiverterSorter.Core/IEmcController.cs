using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Models;

namespace WheelDiverterSorter.Core {
    public interface IEmcController : IDisposable {

        /// <summary>
        /// 当前状态
        /// </summary>
        EmcControllerStatus Status { get; }

        /// <summary>
        /// 当前异常代码（无异常时为 null）
        /// </summary>
        int? FaultCode { get; }

        /// <summary>
        /// 当前监控的 IO 点集合
        /// </summary>
        IReadOnlyList<IoPointInfo> MonitoredIoPoints { get; }

        /// <summary>
        /// 状态变更事件
        /// </summary>
        event EventHandler<EmcStatusChangedEventArgs>? StatusChanged;

        /// <summary>
        /// 异常事件（用于隔离异常，不影响上层调用链）
        /// </summary>
        event EventHandler<EmcFaultedEventArgs>? Faulted;

        /// <summary>
        /// 初始化完成事件
        /// </summary>
        event EventHandler<EmcInitializedEventArgs>? Initialized;

        /// <summary>
        /// 初始化
        /// </summary>
        Task<bool> InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置监控的 IO 点集合
        /// </summary>
        ValueTask SetMonitoredIoPointsAsync(IReadOnlyList<IoPointInfo> ioPoints, CancellationToken cancellationToken = default);

        /// <summary>
        /// 重新连接
        /// </summary>
        Task<bool> ReconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 写 IO 电平
        /// </summary>
        Task<bool> WriteIoAsync(int point, IoState state, CancellationToken cancellationToken = default);
    }
}
