using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Models;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Core {

    /// <summary>
    /// 上游路由通信契约
    /// 负责与上游系统建立连接、发送包裹生命周期消息、接收格口分配（路由）结果。
    /// </summary>
    public interface IUpstreamRouting : IDisposable {

        /// <summary>
        /// 当前连接状态
        /// </summary>
        UpstreamRoutingStatus Status { get; }

        /// <summary>
        /// 当前连接参数（未连接时可能为 null）
        /// </summary>
        UpstreamRoutingConnectionOptions? ConnectionOptions { get; }

        /// <summary>
        /// 接收到上游下发的格口分配（路由结果）时触发
        /// </summary>
        event EventHandler<ChuteAssignmentInfo>? ChuteAssignedReceived;

        /// <summary>
        /// 连接成功事件
        /// </summary>
        event EventHandler<UpstreamRoutingConnectedEventArgs>? Connected;

        /// <summary>
        /// 连接断开事件
        /// </summary>
        event EventHandler<UpstreamRoutingDisconnectedEventArgs>? Disconnected;

        /// <summary>
        /// 连接异常事件（用于隔离异常，不影响上层调用链）
        /// </summary>
        event EventHandler<UpstreamRoutingFaultedEventArgs>? Faulted;

        /// <summary>
        /// 建立连接
        /// </summary>
        ValueTask<bool> ConnectAsync(UpstreamRoutingConnectionOptions connectionOptions, CancellationToken cancellationToken = default);

        /// <summary>
        /// 重连（基于已保存的连接参数）
        /// </summary>
        ValueTask<bool> ReconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 断开连接
        /// </summary>
        ValueTask DisconnectAsync(string? reason = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 发送创建包裹信息（检测通知）
        /// </summary>
        ValueTask<bool> SendCreateParcelAsync(UpstreamCreateParcelRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 发送落格信息（完成通知）
        /// </summary>
        ValueTask<bool> SendDropToChuteAsync(SortingCompletedMessage request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 发送包裹异常信息（超时/丢失/异常口等）
        /// </summary>
        ValueTask<bool> SendParcelExceptionAsync(ParcelExceptionMessage request, CancellationToken cancellationToken = default);
    }
}
