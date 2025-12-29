using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Models;

namespace WheelDiverterSorter.Core.Manager {

    /// <summary>
    /// 包裹管理器（负责包裹生命周期：创建、分配目标格口、落格、移除）
    /// </summary>
    public interface IParcelManager : IDisposable {

        /// <summary>
        /// 当前包裹集合（建议实现为快照发布，支持高频读取）
        /// </summary>
        IReadOnlyCollection<ParcelInfo> Parcels { get; }

        /// <summary>
        /// 包裹创建事件
        /// </summary>
        event EventHandler<ParcelCreatedEventArgs>? ParcelCreated;

        /// <summary>
        /// 包裹目标格口更新事件
        /// </summary>
        event EventHandler<ParcelTargetChuteUpdatedEventArgs>? ParcelTargetChuteUpdated;

        /// <summary>
        /// 包裹落格事件
        /// </summary>
        event EventHandler<ParcelDroppedEventArgs>? ParcelDropped;

        /// <summary>
        /// 包裹移除事件
        /// </summary>
        event EventHandler<ParcelRemovedEventArgs>? ParcelRemoved;

        /// <summary>
        /// 异常事件（用于隔离异常，不影响上层调用链）
        /// </summary>
        event EventHandler<ParcelManagerFaultedEventArgs>? Faulted;

        /// <summary>
        /// 创建包裹（已存在返回 false）
        /// </summary>
        ValueTask<bool> CreateAsync(ParcelInfo parcel, CancellationToken cancellationToken = default);

        /// <summary>
        /// 更新包裹目标格口（包裹不存在返回 false）
        /// </summary>
        ValueTask<bool> AssignTargetChuteAsync(long parcelId, long targetChuteId, DateTimeOffset assignedAt, CancellationToken cancellationToken = default);

        /// <summary>
        /// 标记包裹已落格（包裹不存在返回 false）
        /// </summary>
        ValueTask<bool> MarkDroppedAsync(long parcelId, long actualChuteId, DateTimeOffset droppedAt, CancellationToken cancellationToken = default);

        /// <summary>
        /// 移除包裹（不存在返回 false）
        /// </summary>
        ValueTask<bool> RemoveAsync(long parcelId, string? reason = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 清空所有包裹
        /// </summary>
        ValueTask ClearAsync(string? reason = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 尝试获取包裹快照
        /// </summary>
        bool TryGet(long parcelId, out ParcelInfo parcel);
    }
}
