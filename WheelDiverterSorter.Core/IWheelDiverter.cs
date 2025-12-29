using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Core {

    /// <summary>
    /// 摆轮接口
    /// </summary>
    public interface IWheelDiverter : IDisposable {

        /// <summary>
        /// 位置（例如拓扑中的 PositionIndex 或设备位置编号）
        /// </summary>
        int PositionIndex { get; }

        /// <summary>
        /// 当前状态
        /// </summary>
        WheelDiverterStatus Status { get; }

        /// <summary>
        /// 连接参数
        /// </summary>
        WheelDiverterConnectionOptions ConnectionOptions { get; }

        /// <summary>
        /// 接收内容事件
        /// </summary>
        event EventHandler<WheelDiverterReceivedEventArgs>? Received;

        /// <summary>
        /// 发送内容事件
        /// </summary>
        event EventHandler<WheelDiverterSentEventArgs>? Sent;

        /// <summary>
        /// 连接成功事件
        /// </summary>
        event EventHandler<WheelDiverterConnectedEventArgs>? Connected;

        /// <summary>
        /// 断开事件
        /// </summary>
        event EventHandler<WheelDiverterDisconnectedEventArgs>? Disconnected;

        /// <summary>
        /// 异常事件
        /// </summary>
        event EventHandler<WheelDiverterFaultedEventArgs>? Faulted;

        /// <summary>
        /// 连接
        /// </summary>
        ValueTask<bool> ConnectAsync(WheelDiverterConnectionOptions connectionOptions, CancellationToken cancellationToken = default);

        /// <summary>
        /// 断开
        /// </summary>
        ValueTask DisconnectAsync(string? reason = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 重连
        /// </summary>
        ValueTask<bool> ReconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 运行
        /// </summary>
        ValueTask RunAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止
        /// </summary>
        ValueTask StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 左转
        /// </summary>
        ValueTask TurnLeftAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 右转
        /// </summary>
        ValueTask TurnRightAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 直通
        /// </summary>
        ValueTask StraightThroughAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置速度（毫米/秒）
        /// </summary>
        ValueTask SetSpeedMmpsAsync(decimal speedMmps, CancellationToken cancellationToken = default);
    }
}
