using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Collections.Generic;
using System.Collections.Concurrent;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Models;
using WheelDiverterSorter.Core.Manager;

namespace WheelDiverterSorter.Execution {

    /// <summary>
    /// 高频/高并发/无竞态实现：每个 positionIndex 一个 Actor（单读 Channel）
    /// </summary>
    public sealed class PositionQueueManager : IPositionQueueManager {
        private readonly ConcurrentDictionary<int, PositionActor> _actors = new();

        // Position 集合强顺序（末尾追加/末尾移除）
        private readonly object _positionsGate = new();

        private int[] _positions = [];

        /// <summary>
        /// 位置索引集合（强顺序，只读快照，零拷贝）
        /// </summary>
        public IReadOnlyList<int> Positions => Volatile.Read(ref _positions);

        /// <summary>
        /// 异常隔离：事件直接触发，但捕获异常保证不影响调用链
        /// </summary>
        public event EventHandler<PositionQueueManagerFaultedEventArgs>? Faulted;

        public ValueTask<bool> CreatePositionAsync(int positionIndex, CancellationToken cancellationToken = default) {
            try {
                if (positionIndex < 0) {
                    return ValueTask.FromResult(false);
                }

                lock (_positionsGate) {
                    if (Array.IndexOf(_positions, positionIndex) >= 0) {
                        return ValueTask.FromResult(false);
                    }

                    var old = _positions;
                    var newArr = new int[old.Length + 1];
                    Array.Copy(old, newArr, old.Length);
                    newArr[^1] = positionIndex;

                    // 发布新快照（整体替换，读侧零拷贝）
                    Volatile.Write(ref _positions, newArr);
                }

                _ = GetOrCreateActor(positionIndex);
                return ValueTask.FromResult(true);
            }
            catch (Exception ex) {
                PublishFaulted(ex, "创建 Position 失败", positionIndex);
                return ValueTask.FromResult(false);
            }
        }

        public ValueTask<bool> AppendPositionsAsync(IReadOnlyList<int> positionIndexes, CancellationToken cancellationToken = default) {
            try {
                if (positionIndexes.Count == 0) {
                    return ValueTask.FromResult(true);
                }

                lock (_positionsGate) {
                    if (positionIndexes.Any(t => t < 0)) {
                        return ValueTask.FromResult(false);
                    }

                    var existing = new HashSet<int>(_positions);
                    if (positionIndexes.Any(t => !existing.Add(t))) {
                        return ValueTask.FromResult(false);
                    }

                    var old = _positions;
                    var newArr = new int[old.Length + positionIndexes.Count];
                    Array.Copy(old, newArr, old.Length);

                    for (var i = 0; i < positionIndexes.Count; i++) {
                        newArr[old.Length + i] = positionIndexes[i];
                    }

                    // 发布新快照（整体替换，读侧零拷贝）
                    Volatile.Write(ref _positions, newArr);
                }

                foreach (var t in positionIndexes) {
                    _ = GetOrCreateActor(t);
                }

                return ValueTask.FromResult(true);
            }
            catch (Exception ex) {
                PublishFaulted(ex, "追加 Position 集合失败");
                return ValueTask.FromResult(false);
            }
        }

        public ValueTask<bool> RemoveTailPositionsAsync(int count, string? reason = null, CancellationToken cancellationToken = default) {
            try {
                if (count <= 0) {
                    return ValueTask.FromResult(true);
                }

                int[] removed;
                lock (_positionsGate) {
                    var old = _positions;

                    if (count > old.Length) {
                        return ValueTask.FromResult(false);
                    }

                    removed = old[^count..];

                    var newArr = new int[old.Length - count];
                    Array.Copy(old, newArr, newArr.Length);

                    // 发布新快照（整体替换，读侧零拷贝）
                    Volatile.Write(ref _positions, newArr);
                }

                foreach (var idx in removed) {
                    if (_actors.TryRemove(idx, out var actor)) {
                        actor.Stop(reason ?? "从末尾移除 Position");
                    }
                }

                return ValueTask.FromResult(true);
            }
            catch (Exception ex) {
                PublishFaulted(ex, "从末尾移除 Position 失败");
                return ValueTask.FromResult(false);
            }
        }

        /// <summary>
        /// 获取 Position 快照（零拷贝）
        /// </summary>
        public IReadOnlyList<int> GetPositionsSnapshot()
            => Volatile.Read(ref _positions);

        public ValueTask<bool> CreateTaskAsync(PositionQueueTask task, CancellationToken cancellationToken = default) {
            try {
                if (!task.IsValid) {
                    return ValueTask.FromResult(false);
                }

                var actor = GetOrCreateActor(task.PositionIndex);
                return actor.EnqueueBoolAsync(PositionCommand.CreateCreate(task), cancellationToken);
            }
            catch (Exception ex) {
                PublishFaulted(ex, "创建任务失败", task.PositionIndex, task.ParcelId);
                return ValueTask.FromResult(false);
            }
        }

        public ValueTask<bool> UpdateTaskAsync(PositionQueueTaskPatch patch, string? reason = null, CancellationToken cancellationToken = default) {
            try {
                if (!patch.IsValid || !_actors.TryGetValue(patch.PositionIndex, out var actor)) {
                    return ValueTask.FromResult(false);
                }

                // PositionIndex 禁止修改：Actor 内双保险校验
                return actor.EnqueueBoolAsync(PositionCommand.CreatePatchUpdate(patch, reason), cancellationToken);
            }
            catch (Exception ex) {
                PublishFaulted(ex, "部分更新任务失败", patch.PositionIndex, patch.ParcelId);
                return ValueTask.FromResult(false);
            }
        }

        public ValueTask<bool> RemoveTaskAsync(int positionIndex, long parcelId, string? reason = null, CancellationToken cancellationToken = default) {
            try {
                if (positionIndex < 0 || parcelId <= 0 || !_actors.TryGetValue(positionIndex, out var actor)) {
                    return ValueTask.FromResult(false);
                }

                // 保持“按 parcelId 定位”但仍不允许中间删除：只允许队首匹配时删除
                return actor.EnqueueBoolAsync(PositionCommand.CreateRemoveHeadIfMatch(parcelId, reason), cancellationToken);
            }
            catch (Exception ex) {
                PublishFaulted(ex, "删除任务失败", positionIndex, parcelId);
                return ValueTask.FromResult(false);
            }
        }

        public async ValueTask ClearAsync(int? positionIndex = null, string? reason = null, CancellationToken cancellationToken = default) {
            try {
                if (positionIndex is { } idx) {
                    if (_actors.TryGetValue(idx, out var actor)) {
                        _ = await actor.EnqueueBoolAsync(PositionCommand.CreateClear(reason), cancellationToken).ConfigureAwait(false);
                    }
                    return;
                }

                var tasks = new List<Task>(_actors.Count);
                tasks.AddRange(_actors.Values.Select(a => a.EnqueueBoolAsync(PositionCommand.CreateClear(reason), cancellationToken).AsTask()).Cast<Task>());

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception ex) {
                PublishFaulted(ex, "清空任务失败", positionIndex: positionIndex);
            }
        }

        public ValueTask ClearAllAsync(string? reason = null, CancellationToken cancellationToken = default)
            => ClearAsync(null, reason, cancellationToken);

        public IReadOnlyList<PositionQueueTask> GetTasksSnapshot(int positionIndex) {
            try {
                if (positionIndex < 0 || !_actors.TryGetValue(positionIndex, out var actor)) {
                    return [];
                }

                // 高频读优化：直接返回内部快照数组（整体替换，读侧零拷贝）
                return actor.Snapshot;
            }
            catch (Exception ex) {
                PublishFaulted(ex, "获取任务快照失败", positionIndex);
                return [];
            }
        }

        public bool TryPeekFirstTask(int positionIndex, out PositionQueueTask task) {
            task = default;
            return _actors.TryGetValue(positionIndex, out var actor) && actor.TryPeekFirst(out task);
        }

        public bool TryPeekSecondTask(int positionIndex, out PositionQueueTask task) {
            task = default;
            return _actors.TryGetValue(positionIndex, out var actor) && actor.TryPeekSecond(out task);
        }

        public ValueTask<bool> InvalidateTaskAsync(int positionIndex, long parcelId, string? reason = null, CancellationToken cancellationToken = default) {
            try {
                if (positionIndex < 0 || parcelId <= 0 || !_actors.TryGetValue(positionIndex, out var actor)) {
                    return ValueTask.FromResult(false);
                }

                return actor.EnqueueBoolAsync(PositionCommand.CreateInvalidate(parcelId, reason), cancellationToken);
            }
            catch (Exception ex) {
                PublishFaulted(ex, "标记任务失效失败", positionIndex, parcelId);
                return ValueTask.FromResult(false);
            }
        }

        public ValueTask<bool> DequeueAsync(int positionIndex, string? reason = null, CancellationToken cancellationToken = default) {
            try {
                if (positionIndex < 0 || !_actors.TryGetValue(positionIndex, out var actor)) {
                    return ValueTask.FromResult(false);
                }

                // 新语义：只出队队首（不管是否失效，不做 parcelId 匹配）
                return actor.EnqueueBoolAsync(PositionCommand.CreateDequeueHead(reason), cancellationToken);
            }
            catch (Exception ex) {
                PublishFaulted(ex, "出队失败", positionIndex);
                return ValueTask.FromResult(false);
            }
        }

        public async ValueTask<int> InvalidateTasksAfterPositionAsync(int positionIndex, long parcelId, string? reason = null, CancellationToken cancellationToken = default) {
            try {
                if (positionIndex < 0 || parcelId <= 0) {
                    return 0;
                }

                var positionsSnapshot = Volatile.Read(ref _positions);
                if (positionsSnapshot.Length == 0) {
                    return 0;
                }

                var pivot = Array.IndexOf(positionsSnapshot, positionIndex);
                if (pivot < 0 || pivot >= positionsSnapshot.Length - 1) {
                    return 0;
                }

                var count = 0;
                for (var i = pivot + 1; i < positionsSnapshot.Length; i++) {
                    cancellationToken.ThrowIfCancellationRequested();

                    var pos = positionsSnapshot[i];
                    if (!_actors.TryGetValue(pos, out var actor)) {
                        continue;
                    }

                    var ok = await actor.EnqueueBoolAsync(PositionCommand.CreateInvalidate(parcelId, reason), cancellationToken).ConfigureAwait(false);
                    if (ok) {
                        count++;
                    }
                }

                return count;
            }
            catch (OperationCanceledException) {
                return 0;
            }
            catch (Exception ex) {
                PublishFaulted(ex, "标记后续 Position 任务失效失败", positionIndex, parcelId);
                return 0;
            }
        }

        public ValueTask<PositionQueuePeekResult> PeekFirstTaskAfterPruneAsync(
            int positionIndex,
            int maxPruneCount = 64,
            string? reason = null,
            CancellationToken cancellationToken = default) {
            try {
                if (positionIndex < 0 || !_actors.TryGetValue(positionIndex, out var actor)) {
                    return ValueTask.FromResult(new PositionQueuePeekResult {
                        HasTask = false,
                        Task = default,
                        PrunedCount = 0,
                        IsPruneLimitReached = false
                    });
                }

                var normalizedMax = maxPruneCount <= 0 ? 0 : maxPruneCount;
                return actor.EnqueuePeekAfterPruneAsync(normalizedMax, reason, cancellationToken);
            }
            catch (Exception ex) {
                PublishFaulted(ex, "PeekFirstTaskAfterPruneAsync 失败", positionIndex);
                return ValueTask.FromResult(new PositionQueuePeekResult {
                    HasTask = false,
                    Task = default,
                    PrunedCount = 0,
                    IsPruneLimitReached = false
                });
            }
        }

        public void Dispose() {
            foreach (var actor in _actors.Values) {
                actor.Stop("PositionQueueManager Dispose");
            }
        }

        private PositionActor GetOrCreateActor(int positionIndex)
            => _actors.GetOrAdd(positionIndex, static (idx, state) => new PositionActor(idx, state), this);

        private void PublishFaulted(Exception exception, string context, int? positionIndex = null, long? parcelId = null) {
            try {
                Faulted?.Invoke(this, new PositionQueueManagerFaultedEventArgs {
                    PositionIndex = positionIndex,
                    ParcelId = parcelId,
                    Context = context,
                    Exception = exception,
                    OccurredAt = DateTimeOffset.Now
                });
            }
            catch {
                // 终极隔离：禁止向外抛出
            }
        }

        private sealed class PositionActor {
            private readonly int _positionIndex;
            private readonly PositionQueueManager _owner;

            private readonly Channel<PositionCommand> _channel;
            private readonly CancellationTokenSource _cts = new();

            // FIFO：仅在 Actor 线程读写
            private readonly LinkedList<Entry> _queue = new();

            private readonly Dictionary<long, LinkedListNode<Entry>> _index = new();

            private volatile PositionQueueTask[] _snapshot = [];

            public PositionActor(int positionIndex, PositionQueueManager owner) {
                _positionIndex = positionIndex;
                _owner = owner;

                _channel = Channel.CreateUnbounded<PositionCommand>(new UnboundedChannelOptions {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });

                _ = Task.Run(PumpAsync);
            }

            public PositionQueueTask[] Snapshot => Volatile.Read(ref _snapshot);

            public void Stop(string reason) {
                _channel.Writer.TryComplete();
                _cts.Cancel();
            }

            public ValueTask<bool> EnqueueBoolAsync(in PositionCommand command, CancellationToken cancellationToken) {
                if (cancellationToken.IsCancellationRequested) {
                    return ValueTask.FromCanceled<bool>(cancellationToken);
                }

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var cmd = command with { BoolTcs = tcs };

                if (!_channel.Writer.TryWrite(cmd)) {
                    return ValueTask.FromResult(false);
                }

                if (!cancellationToken.CanBeCanceled) {
                    return new ValueTask<bool>(tcs.Task);
                }

                var reg = cancellationToken.Register(static s => ((TaskCompletionSource<bool>)s!).TrySetCanceled(), tcs);
                return AwaitWithDisposeAsync(tcs.Task, reg);

                static async ValueTask<bool> AwaitWithDisposeAsync(Task<bool> task, CancellationTokenRegistration reg) {
                    try { return await task.ConfigureAwait(false); }
                    finally { await reg.DisposeAsync().ConfigureAwait(false); }
                }
            }

            public bool TryPeekFirst(out PositionQueueTask task) {
                task = default;

                var snap = Volatile.Read(ref _snapshot);
                if (snap.Length == 0) {
                    return false;
                }

                task = snap[0];
                return true;
            }

            public bool TryPeekSecond(out PositionQueueTask task) {
                task = default;

                var snap = Volatile.Read(ref _snapshot);
                if (snap.Length < 2) {
                    return false;
                }

                task = snap[1];
                return true;
            }

            private async Task PumpAsync() {
                var reader = _channel.Reader;

                try {
                    while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false)) {
                        var dirty = false;

                        while (reader.TryRead(out var cmd)) {
                            try {
                                dirty |= Handle(cmd);
                            }
                            catch (Exception ex) {
                                _owner.PublishFaulted(ex, "PositionActor 处理命令异常", _positionIndex);
                                cmd.BoolTcs?.TrySetResult(false);
                                cmd.PeekTcs?.TrySetResult(new PositionQueuePeekResult {
                                    HasTask = false,
                                    Task = default,
                                    PrunedCount = 0,
                                    IsPruneLimitReached = false
                                });
                            }
                        }

                        if (dirty) {
                            RefreshSnapshot();
                        }
                    }
                }
                catch (OperationCanceledException) {
                    // 正常退出
                }
                catch (Exception ex) {
                    _owner.PublishFaulted(ex, "PositionActor Pump 异常", _positionIndex);
                }
                finally {
                    while (reader.TryRead(out var pending)) {
                        pending.BoolTcs?.TrySetResult(false);
                        pending.PeekTcs?.TrySetResult(new PositionQueuePeekResult {
                            HasTask = false,
                            Task = default,
                            PrunedCount = 0,
                            IsPruneLimitReached = false
                        });
                    }
                }
            }

            private bool Handle(in PositionCommand cmd) {
                switch (cmd.Kind) {
                    case CommandKind.Create:
                        return HandleCreate(cmd.Task, cmd.BoolTcs);

                    case CommandKind.PatchUpdate:
                        return HandlePatchUpdate(cmd.Patch, cmd.BoolTcs);

                    case CommandKind.Invalidate:
                        return HandleInvalidate(cmd.ParcelId, cmd.BoolTcs);

                    case CommandKind.RemoveHeadIfMatch:
                        return HandleRemoveHeadIfMatch(cmd.ParcelId, cmd.BoolTcs);

                    case CommandKind.DequeueHead:
                        return HandleDequeueHead(cmd.BoolTcs);

                    case CommandKind.Clear:
                        return HandleClear(cmd.BoolTcs);

                    case CommandKind.PeekFirstAfterPrune:
                        return HandlePeekFirstAfterPrune(cmd.MaxPruneCount, cmd.Reason, cmd.PeekTcs);

                    default:
                        cmd.BoolTcs?.TrySetResult(false);
                        return false;
                }
            }

            private bool HandleCreate(in PositionQueueTask task, TaskCompletionSource<bool>? tcs) {
                if (!task.IsValid || _index.ContainsKey(task.ParcelId)) {
                    tcs?.TrySetResult(false);
                    return false;
                }

                if (task.PositionIndex != _positionIndex) {
                    tcs?.TrySetResult(false);
                    return false;
                }

                var normalized = task.IsInvalidated ? task with { IsInvalidated = false } : task;

                var node = _queue.AddLast(new Entry { Task = normalized });
                _index.Add(normalized.ParcelId, node);

                // 关键：入队后，用本任务 EarliestDequeueAt 回写“前一个可用任务”的 LostDecisionAt
                var dirty = TryUpdatePrevLostDecisionAtByNext(node);

                tcs?.TrySetResult(true);
                return true | dirty;
            }

            private bool HandlePatchUpdate(in PositionQueueTaskPatch patch, TaskCompletionSource<bool>? tcs) {
                if (!patch.IsValid) {
                    tcs?.TrySetResult(false);
                    return false;
                }

                if (patch.PositionIndex != _positionIndex) {
                    tcs?.TrySetResult(false);
                    return false;
                }

                if (!_index.TryGetValue(patch.ParcelId, out var node)) {
                    tcs?.TrySetResult(false);
                    return false;
                }

                var oldTask = node.Value.Task;

                if (oldTask.PositionIndex != patch.PositionIndex) {
                    tcs?.TrySetResult(false);
                    return false;
                }

                var merged = oldTask;

                if (patch.HasAction) {
                    merged = merged with { Action = patch.Action };
                }

                if (patch.HasEarliestDequeueAt) {
                    merged = merged with { EarliestDequeueAt = patch.EarliestDequeueAt };
                }

                if (patch.HasLatestDequeueAt) {
                    merged = merged with { LatestDequeueAt = patch.LatestDequeueAt };
                }

                if (patch.HasLostDecisionAt) {
                    merged = merged with { LostDecisionAt = patch.LostDecisionAt };
                }

                merged = merged with { IsInvalidated = oldTask.IsInvalidated };

                if (merged.EarliestDequeueAt > merged.LatestDequeueAt) {
                    tcs?.TrySetResult(false);
                    return false;
                }

                if (merged.LostDecisionAt is not null && merged.LatestDequeueAt > merged.LostDecisionAt.Value) {
                    tcs?.TrySetResult(false);
                    return false;
                }

                node.Value = node.Value with { Task = merged };

                // 关键：当 EarliestDequeueAt 被修改时，用新 Earliest 回写“前一个可用任务”的 LostDecisionAt
                var dirty = true;
                if (patch.HasEarliestDequeueAt) {
                    dirty |= TryUpdatePrevLostDecisionAtByNext(node);
                }

                tcs?.TrySetResult(true);
                return dirty;
            }

            private bool HandleInvalidate(long parcelId, TaskCompletionSource<bool>? tcs) {
                if (!_index.TryGetValue(parcelId, out var node)) {
                    tcs?.TrySetResult(false);
                    return false;
                }

                var oldTask = node.Value.Task;
                if (oldTask.IsInvalidated) {
                    tcs?.TrySetResult(true);
                    return false;
                }

                node.Value = node.Value with { Task = oldTask with { IsInvalidated = true } };

                // 失效后，修正前后关联的 LostDecisionAt（避免误判）
                var dirty = FixLostDecisionAround(node);

                tcs?.TrySetResult(true);
                return true | dirty;
            }

            private bool HandleRemoveHeadIfMatch(long parcelId, TaskCompletionSource<bool>? tcs) {
                var head = _queue.First;
                if (head is null || head.Value.Task.ParcelId != parcelId) {
                    tcs?.TrySetResult(false);
                    return false;
                }

                RemoveNode(head);

                tcs?.TrySetResult(true);
                return true;
            }

            private bool HandleDequeueHead(TaskCompletionSource<bool>? tcs) {
                var head = _queue.First;
                if (head is null) {
                    tcs?.TrySetResult(false);
                    return false;
                }

                RemoveNode(head);

                tcs?.TrySetResult(true);
                return true;
            }

            private bool HandleClear(TaskCompletionSource<bool>? tcs) {
                if (_queue.Count == 0) {
                    tcs?.TrySetResult(true);
                    return false;
                }

                _queue.Clear();
                _index.Clear();

                tcs?.TrySetResult(true);
                return true;
            }

            private bool HandlePeekFirstAfterPrune(int maxPruneCount, string? reason, TaskCompletionSource<PositionQueuePeekResult>? tcs) {
                var pruned = 0;

                while (true) {
                    var head = _queue.First;
                    if (head is null) {
                        tcs?.TrySetResult(new PositionQueuePeekResult {
                            HasTask = false,
                            Task = default,
                            PrunedCount = pruned,
                            IsPruneLimitReached = false
                        });
                        return pruned > 0;
                    }

                    var task = head.Value.Task;

                    if (!task.IsValid || task.IsInvalidated) {
                        if (maxPruneCount > 0 && pruned >= maxPruneCount) {
                            _owner.PublishFaulted(
                                new InvalidOperationException($"清理队首无效任务达到上限：PositionIndex={_positionIndex}, MaxPruneCount={maxPruneCount}, Reason={reason}"),
                                "PeekFirstTaskAfterPruneAsync 达到清理上限",
                                _positionIndex);

                            tcs?.TrySetResult(new PositionQueuePeekResult {
                                HasTask = false,
                                Task = default,
                                PrunedCount = pruned,
                                IsPruneLimitReached = true
                            });

                            return pruned > 0;
                        }

                        RemoveNode(head);
                        pruned++;

                        // 失效任务被移除后，可能需要修正相邻 LostDecisionAt（避免误判）
                        FixLostDecisionAround(head.Next);
                        continue;
                    }

                    tcs?.TrySetResult(new PositionQueuePeekResult {
                        HasTask = true,
                        Task = task,
                        PrunedCount = pruned,
                        IsPruneLimitReached = false
                    });

                    return pruned > 0;
                }
            }

            private void RemoveNode(LinkedListNode<Entry> node) {
                var parcelId = node.Value.Task.ParcelId;
                _queue.Remove(node);
                _index.Remove(parcelId);
            }

            private void RefreshSnapshot() {
                if (_queue.Count == 0) {
                    Volatile.Write(ref _snapshot, []);
                    return;
                }

                var arr = new PositionQueueTask[_queue.Count];
                var i = 0;

                for (var n = _queue.First; n is not null; n = n.Next) {
                    arr[i++] = n.Value.Task;
                }

                Volatile.Write(ref _snapshot, arr);
            }

            public ValueTask<PositionQueuePeekResult> EnqueuePeekAfterPruneAsync(int maxPruneCount, string? reason, CancellationToken cancellationToken) {
                if (cancellationToken.IsCancellationRequested) {
                    return ValueTask.FromCanceled<PositionQueuePeekResult>(cancellationToken);
                }

                var tcs = new TaskCompletionSource<PositionQueuePeekResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                var cmd = PositionCommand.CreatePeekFirstAfterPrune(maxPruneCount, reason) with { PeekTcs = tcs };

                if (!_channel.Writer.TryWrite(cmd)) {
                    return ValueTask.FromResult(new PositionQueuePeekResult {
                        HasTask = false,
                        Task = default,
                        PrunedCount = 0,
                        IsPruneLimitReached = false
                    });
                }

                if (!cancellationToken.CanBeCanceled) {
                    return new ValueTask<PositionQueuePeekResult>(tcs.Task);
                }

                var reg = cancellationToken.Register(static s => ((TaskCompletionSource<PositionQueuePeekResult>)s!).TrySetCanceled(), tcs);
                return AwaitWithDisposeAsync(tcs.Task, reg);

                static async ValueTask<PositionQueuePeekResult> AwaitWithDisposeAsync(Task<PositionQueuePeekResult> task, CancellationTokenRegistration reg) {
                    try { return await task.ConfigureAwait(false); }
                    finally { await reg.DisposeAsync().ConfigureAwait(false); }
                }
            }

            private static bool IsProcessable(in PositionQueueTask task)
    => task.IsValid && !task.IsInvalidated;

            private LinkedListNode<Entry>? FindPrevProcessableNode(LinkedListNode<Entry>? start) {
                for (var n = start; n is not null; n = n.Previous) {
                    if (IsProcessable(n.Value.Task)) {
                        return n;
                    }
                }
                return null;
            }

            private LinkedListNode<Entry>? FindNextProcessableNode(LinkedListNode<Entry>? start) {
                for (var n = start; n is not null; n = n.Next) {
                    if (IsProcessable(n.Value.Task)) {
                        return n;
                    }
                }
                return null;
            }

            /// <summary>
            /// 用“next 的 EarliestDequeueAt”回写“prev 的 LostDecisionAt”，并确保不早于 prev.LatestDequeueAt
            /// </summary>
            private bool TryUpdatePrevLostDecisionAtByNext(LinkedListNode<Entry>? nextNode) {
                if (nextNode is null) {
                    return false;
                }

                if (!IsProcessable(nextNode.Value.Task)) {
                    return false;
                }

                var prevNode = FindPrevProcessableNode(nextNode.Previous);
                if (prevNode is null) {
                    return false;
                }

                var prevTask = prevNode.Value.Task;
                var nextTask = nextNode.Value.Task;

                var candidate = nextTask.EarliestDequeueAt > prevTask.LatestDequeueAt
                    ? nextTask.EarliestDequeueAt
                    : prevTask.LatestDequeueAt;

                if (prevTask.LostDecisionAt == candidate) {
                    return false;
                }

                prevNode.Value = prevNode.Value with { Task = prevTask with { LostDecisionAt = candidate } };
                return true;
            }

            /// <summary>
            /// 当中间任务被失效/移除后，修正“前一个可用任务”的 LostDecisionAt 指向“后一个可用任务”的 EarliestDequeueAt；
            /// 若后继不存在，则清空 LostDecisionAt（丢失不允许凭空判断，仅能超时）
            /// </summary>
            private bool FixLostDecisionAround(LinkedListNode<Entry>? pivot) {
                var prev = FindPrevProcessableNode(pivot?.Previous ?? _queue.Last);
                if (prev is null) {
                    return false;
                }

                var next = FindNextProcessableNode(prev.Next);
                var prevTask = prev.Value.Task;

                if (next is null) {
                    if (prevTask.LostDecisionAt is null) {
                        return false;
                    }

                    prev.Value = prev.Value with { Task = prevTask with { LostDecisionAt = null } };
                    return true;
                }

                var nextTask = next.Value.Task;

                var candidate = nextTask.EarliestDequeueAt > prevTask.LatestDequeueAt
                    ? nextTask.EarliestDequeueAt
                    : prevTask.LatestDequeueAt;

                if (prevTask.LostDecisionAt == candidate) {
                    return false;
                }

                prev.Value = prev.Value with { Task = prevTask with { LostDecisionAt = candidate } };
                return true;
            }

            private readonly record struct Entry {
                public required PositionQueueTask Task { get; init; }
            }
        }

        private enum CommandKind {

            /// <summary>创建任务</summary>
            [Description("创建任务")]
            Create = 1,

            /// <summary>部分更新任务</summary>
            [Description("部分更新任务")]
            PatchUpdate = 2,

            /// <summary>标记任务失效</summary>
            [Description("标记任务失效")]
            Invalidate = 3,

            /// <summary>仅当队首匹配时移除</summary>
            [Description("仅当队首匹配时移除")]
            RemoveHeadIfMatch = 4,

            /// <summary>出队队首</summary>
            [Description("出队队首")]
            DequeueHead = 5,

            /// <summary>清空队列</summary>
            [Description("清空队列")]
            Clear = 6,

            /// <summary>清理队首无效任务后窥探队首</summary>
            [Description("清理队首无效任务后窥探队首")]
            PeekFirstAfterPrune = 7
        }

        private readonly record struct PositionCommand {
            public required CommandKind Kind { get; init; }

            public PositionQueueTask Task { get; init; }

            public PositionQueueTaskPatch Patch { get; init; }

            public long ParcelId { get; init; }

            public int MaxPruneCount { get; init; }

            public string? Reason { get; init; }

            public TaskCompletionSource<bool>? BoolTcs { get; init; }

            public TaskCompletionSource<PositionQueuePeekResult>? PeekTcs { get; init; }

            public static PositionCommand CreateCreate(PositionQueueTask task)
                => new() { Kind = CommandKind.Create, Task = task };

            public static PositionCommand CreatePatchUpdate(PositionQueueTaskPatch patch, string? reason)
                => new() { Kind = CommandKind.PatchUpdate, Patch = patch, Reason = reason };

            public static PositionCommand CreateInvalidate(long parcelId, string? reason)
                => new() { Kind = CommandKind.Invalidate, ParcelId = parcelId, Reason = reason };

            public static PositionCommand CreateRemoveHeadIfMatch(long parcelId, string? reason)
                => new() { Kind = CommandKind.RemoveHeadIfMatch, ParcelId = parcelId, Reason = reason };

            public static PositionCommand CreateDequeueHead(string? reason)
                => new() { Kind = CommandKind.DequeueHead, Reason = reason };

            public static PositionCommand CreateClear(string? reason)
                => new() { Kind = CommandKind.Clear, Reason = reason };

            public static PositionCommand CreatePeekFirstAfterPrune(int maxPruneCount, string? reason)
                => new() { Kind = CommandKind.PeekFirstAfterPrune, MaxPruneCount = maxPruneCount, Reason = reason };
        }
    }
}
