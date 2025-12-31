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
        IReadOnlyList<int> Positions { get; }

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
        /// 创建位置队列任务（通常由路径规划/上游分配产生,如果队列前面有包裹则需要根据EarliestDequeueAt更新上一个包裹的LostDecisionAt）
        /// </summary>
        ValueTask<bool> CreateTaskAsync(PositionQueueTask task, CancellationToken cancellationToken = default);

        /// <summary>
        /// 位置队列任务更新(如果队列前面有包裹则需要根据EarliestDequeueAt更新上一个包裹的LostDecisionAt)
        /// </summary>
        /// <param name="patch"></param>
        /// <param name="reason"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        ValueTask<bool> UpdateTaskAsync(PositionQueueTaskPatch patch, string? reason = null, CancellationToken cancellationToken = default);

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
        /// 获取指定位置队列首个任务（只查看不出队；不限制有效性）
        /// </summary>
        bool TryPeekFirstTask(int positionIndex, out PositionQueueTask task);

        /// <summary>
        /// 获取指定位置队列第二个任务（只查看不出队；不限制有效性）
        /// </summary>
        bool TryPeekSecondTask(int positionIndex, out PositionQueueTask task);

        /// <summary>
        /// 标记指定位置指定包裹Id的任务为失效（只标记不出队；任务仍保留在队列中，Peek/Dequeue 时会被跳过）
        /// </summary>
        ValueTask<bool> InvalidateTaskAsync(int positionIndex, long parcelId, string? reason = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 出队：仅允许出队队首任务（不做 parcelId 匹配）
        /// </summary>
        ValueTask<bool> DequeueAsync(int positionIndex, string? reason = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 标记“指定 positionIndex 之后”的剩余 position 任务为失效（通常用于包裹丢失场景）
        /// </summary>
        /// <remarks>
        /// 语义示例：传入 positionIndex=2，则该 parcelId 在“强顺序集合”中位于 positionIndex=2 后面的所有 Position 的任务将被标记为失效。
        /// 该方法不做出队，仅做失效标记。
        /// </remarks>
        /// <returns>
        /// 返回成功标记为失效的任务数量（未找到对应任务的 position 将被跳过）。
        /// </returns>
        ValueTask<int> InvalidateTasksAfterPositionAsync(int positionIndex, long parcelId, string? reason = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 自动清理指定位置队列“队首连续无效任务”后，窥探清理后的队首任务（不出队）
        /// </summary>
        /// <remarks>
        /// 设计目的：保证先进先出语义下的“清理 + 窥探”一体化，避免调用方出现 do/while 或 goto。
        ///
        /// 规则：
        /// 1) 仅清理队首任务：当队首满足 (IsInvalidated == true) 或 (IsValid == false) 时，自动从队首依次出队。
        /// 2) 清理过程严格 FIFO：不会跳过有效队首去窥探中间任务。
        /// 3) 清理后若队列仍有任务，则返回清理后的队首任务（仅窥探，不出队）。
        /// 4) 为避免异常堆积导致单次调用耗时不可控，maxPruneCount 用于限制单次最多清理数量；超过限制时应触发 Faulted 事件，并返回 HasTask=false。
        /// </remarks>
        /// <param name="positionIndex">位置索引</param>
        /// <param name="maxPruneCount">单次最多清理的队首无效任务数量</param>
        /// <param name="reason">清理原因（用于日志/诊断）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>窥探结果（包含是否存在队首任务与本次清理数量）</returns>
        ValueTask<PositionQueuePeekResult> PeekFirstTaskAfterPruneAsync(
            int positionIndex,
            int maxPruneCount = 64,
            string? reason = null,
            CancellationToken cancellationToken = default);
    }
}
