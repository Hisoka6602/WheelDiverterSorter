using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Models;

namespace WheelDiverterSorter.Core.Manager {

    /// <summary>
    /// 位置队列管理器（给每个站点赋值应该到达的包裹和动作任务,先进先出队列管理[必须保证先进先出不能从中间删除],可窥探第二个包裹来判定是否丢失当前包裹,可让指定任务失效）
    /// </summary>
    public interface IPositionQueueManager : IDisposable {

        /// <summary>
        /// 队列异常事件（用于隔离异常，不影响上层调用链）
        /// </summary>
        event EventHandler<PositionQueueManagerFaultedEventArgs>? Faulted;

        // -------------------------
        // Position 管理（强顺序要求）
        // -------------------------

        /// <summary>
        /// 创建 Position（已存在返回 false）
        /// </summary>
        ValueTask<bool> CreatePositionAsync(int positionIndex, CancellationToken cancellationToken = default);

        /// <summary>
        /// 追加 Position 集合（必须从末尾按顺序追加；不满足强顺序返回 false）
        /// </summary>
        ValueTask<bool> AppendPositionsAsync(IReadOnlyList<int> positionIndexes, CancellationToken cancellationToken = default);

        /// <summary>
        /// 从末尾移除 Position（必须从末尾移除；不满足强顺序返回 false）
        /// </summary>
        ValueTask<bool> RemoveTailPositionsAsync(int count, string? reason = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取 Position 集合快照（用于监控/诊断；快照语义）
        /// </summary>
        IReadOnlyList<int> GetPositionsSnapshot();

        // -------------------------
        // 任务管理（FIFO + 失效跳过）
        // -------------------------

        /// <summary>
        /// 创建位置队列任务（通常由路径规划/上游分配产生）
        /// </summary>
        ValueTask<bool> CreateTaskAsync(PositionQueueTask task, CancellationToken cancellationToken = default);

        /// <summary>
        /// 更新指定位置任务（按位置与包裹定位；不存在返回 false；成功则覆盖原任务）
        /// </summary>
        ValueTask<bool> UpdateTaskAsync(PositionQueueTask task, string? reason = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 删除位置队列任务（按位置与包裹定位；不存在返回 false）
        /// </summary>
        ValueTask<bool> RemoveTaskAsync(int positionIndex, long parcelId, string? reason = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 清空位置队列任务（positionIndex 为 null 表示清空所有位置）
        /// </summary>
        ValueTask ClearAsync(int? positionIndex = null, string? reason = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 清空全部位置任务（等价于 ClearAsync(null)；提供显式语义便于调用方阅读）
        /// </summary>
        ValueTask ClearAllAsync(string? reason = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取指定位置的队列任务快照（用于监控/诊断；快照语义）
        /// </summary>
        IReadOnlyList<PositionQueueTask> GetTasksSnapshot(int positionIndex);

        /// <summary>
        /// 获取指定位置队列首个“有效任务”（只查看不出队；会跳过失效任务）
        /// </summary>
        bool TryPeekFirstValidTask(int positionIndex, out PositionQueueTask task);

        /// <summary>
        /// 获取指定位置队列第二个“有效任务”（只查看不出队；会跳过失效任务）
        /// </summary>
        bool TryPeekSecondValidTask(int positionIndex, out PositionQueueTask task);

        /// <summary>
        /// 标记指定位置指定包裹Id的任务为失效（只标记不出队；任务仍保留在队列中，Peek/Dequeue 时会被跳过）
        /// </summary>
        ValueTask<bool> InvalidateTaskAsync(int positionIndex, long parcelId, string? reason = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 出队：仅允许出队队首“有效任务”；若指定 parcelId 不是队首有效任务，则返回 false
        /// </summary>
        ValueTask<bool> DequeueAsync(int positionIndex, long parcelId, string? reason = null, CancellationToken cancellationToken = default);
    }
}
