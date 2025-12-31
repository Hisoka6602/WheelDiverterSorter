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

                    var newArr = new int[_positions.Length + 1];
                    Array.Copy(_positions, newArr, _positions.Length);
                    newArr[^1] = positionIndex;
                    _positions = newArr;
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

                    var newArr = new int[_positions.Length + positionIndexes.Count];
                    Array.Copy(_positions, newArr, _positions.Length);

                    for (var i = 0; i < positionIndexes.Count; i++) {
                        newArr[_positions.Length + i] = positionIndexes[i];
                    }

                    _positions = newArr;
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
                    if (count > _positions.Length) {
                        return ValueTask.FromResult(false);
                    }

                    removed = _positions[^count..];
                    var newArr = new int[_positions.Length - count];
                    Array.Copy(_positions, newArr, newArr.Length);
                    _positions = newArr;
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

        public IReadOnlyList<int> GetPositionsSnapshot() {
            lock (_positionsGate) {
                return _positions.Length == 0 ? [] : _positions.ToArray();
            }
        }

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

        public ValueTask<bool> UpdateTaskAsync(PositionQueueTask task, string? reason = null, CancellationToken cancellationToken = default) {
            try {
                if (!task.IsValid || !_actors.TryGetValue(task.PositionIndex, out var actor)) {
                    return ValueTask.FromResult(false);
                }

                return actor.EnqueueBoolAsync(PositionCommand.CreateUpdate(task, reason), cancellationToken);
            }
            catch (Exception ex) {
                PublishFaulted(ex, "更新任务失败", task.PositionIndex, task.ParcelId);
                return ValueTask.FromResult(false);
            }
        }

        public ValueTask<bool> RemoveTaskAsync(int positionIndex, long parcelId, string? reason = null, CancellationToken cancellationToken = default) {
            try {
                if (positionIndex < 0 || parcelId <= 0 || !_actors.TryGetValue(positionIndex, out var actor)) {
                    return ValueTask.FromResult(false);
                }

                // 重要：禁止中间删除。仅允许删除队首任务（包含失效任务）
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
                tasks.AddRange(_actors.Values.Select(actor => actor.EnqueueBoolAsync(PositionCommand.CreateClear(reason), cancellationToken).AsTask()).Cast<Task>());

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

                // 高频读优化：直接返回内部快照数组（快照整体替换，读侧零拷贝）
                return actor.Snapshot;
            }
            catch (Exception ex) {
                PublishFaulted(ex, "获取任务快照失败", positionIndex);
                return [];
            }
        }

        public bool TryPeekFirstValidTask(int positionIndex, out PositionQueueTask task) {
            // 语义调整：返回队首任务（可能为失效），不做跳过决策
            task = default;
            return _actors.TryGetValue(positionIndex, out var actor) && actor.TryPeekFirst(out task);
        }

        public bool TryPeekSecondValidTask(int positionIndex, out PositionQueueTask task) {
            // 语义调整：返回第二个任务（可能为失效），不做跳过决策
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

        public ValueTask<bool> DequeueAsync(int positionIndex, long parcelId, string? reason = null, CancellationToken cancellationToken = default) {
            try {
                if (positionIndex < 0 || parcelId <= 0 || !_actors.TryGetValue(positionIndex, out var actor)) {
                    return ValueTask.FromResult(false);
                }

                // 只允许队首任务出队（包含失效任务），避免跳过导致中间出队
                return actor.EnqueueBoolAsync(PositionCommand.CreateDequeueHeadIfMatch(parcelId, reason), cancellationToken);
            }
            catch (Exception ex) {
                PublishFaulted(ex, "出队失败", positionIndex, parcelId);
                return ValueTask.FromResult(false);
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
                // 先完成写端，再取消用于加速退出 WaitToRead
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

                        // 批处理：同一批次 drain 完后只刷新一次快照
                        while (reader.TryRead(out var cmd)) {
                            try {
                                dirty |= Handle(cmd);
                            }
                            catch (Exception ex) {
                                _owner.PublishFaulted(ex, "PositionActor 处理命令异常", _positionIndex);
                                cmd.BoolTcs?.TrySetResult(false);
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
                    // 兜底：释放残留等待方，避免挂起
                    while (reader.TryRead(out var pending)) {
                        pending.BoolTcs?.TrySetResult(false);
                    }
                }
            }

            private bool Handle(in PositionCommand cmd) {
                switch (cmd.Kind) {
                    case CommandKind.Create:
                        return HandleCreate(cmd.Task, cmd.BoolTcs);

                    case CommandKind.Update:
                        return HandleUpdate(cmd.Task, cmd.BoolTcs);

                    case CommandKind.Invalidate:
                        return HandleInvalidate(cmd.ParcelId, cmd.Reason, cmd.BoolTcs);

                    case CommandKind.RemoveHeadIfMatch:
                        return HandleRemoveHeadIfMatch(cmd.ParcelId, cmd.BoolTcs);

                    case CommandKind.DequeueHeadIfMatch:
                        return HandleDequeueHeadIfMatch(cmd.ParcelId, cmd.BoolTcs);

                    case CommandKind.Clear:
                        return HandleClear(cmd.BoolTcs);

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

                // 入队默认视为未失效
                var normalized = task.IsInvalidated ? task with { IsInvalidated = false } : task;

                var node = _queue.AddLast(new Entry { Task = normalized });
                _index.Add(normalized.ParcelId, node);

                tcs?.TrySetResult(true);
                return true;
            }

            private bool HandleUpdate(in PositionQueueTask task, TaskCompletionSource<bool>? tcs) {
                if (!task.IsValid || !_index.TryGetValue(task.ParcelId, out var node)) {
                    tcs?.TrySetResult(false);
                    return false;
                }

                // Update 不覆盖失效标记，避免将已失效任务“复活”
                var oldTask = node.Value.Task;
                var merged = task with { IsInvalidated = oldTask.IsInvalidated };

                node.Value = node.Value with { Task = merged };

                tcs?.TrySetResult(true);
                return true;
            }

            private bool HandleInvalidate(long parcelId, string? reason, TaskCompletionSource<bool>? tcs) {
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

                tcs?.TrySetResult(true);
                return true;
            }

            private bool HandleRemoveHeadIfMatch(long parcelId, TaskCompletionSource<bool>? tcs) {
                // 禁止中间删除：仅允许删除队首任务（包含失效任务）
                var head = _queue.First;
                if (head is null || head.Value.Task.ParcelId != parcelId) {
                    tcs?.TrySetResult(false);
                    return false;
                }

                RemoveNode(head);

                tcs?.TrySetResult(true);
                return true;
            }

            private bool HandleDequeueHeadIfMatch(long parcelId, TaskCompletionSource<bool>? tcs) {
                // 禁止中间出队：仅允许队首任务出队（包含失效任务）
                var head = _queue.First;
                if (head is null || head.Value.Task.ParcelId != parcelId) {
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

            private readonly record struct Entry {
                public required PositionQueueTask Task { get; init; }
            }
        }

        private enum CommandKind {

            /// <summary>创建任务</summary>
            [Description("创建任务")]
            Create = 1,

            /// <summary>更新任务</summary>
            [Description("更新任务")]
            Update = 2,

            /// <summary>标记任务失效</summary>
            [Description("标记任务失效")]
            Invalidate = 3,

            /// <summary>仅当队首匹配时移除</summary>
            [Description("仅当队首匹配时移除")]
            RemoveHeadIfMatch = 4,

            /// <summary>仅当队首匹配时出队</summary>
            [Description("仅当队首匹配时出队")]
            DequeueHeadIfMatch = 5,

            /// <summary>清空队列</summary>
            [Description("清空队列")]
            Clear = 6
        }

        private readonly record struct PositionCommand {
            public required CommandKind Kind { get; init; }
            public PositionQueueTask Task { get; init; }
            public long ParcelId { get; init; }
            public string? Reason { get; init; }
            public TaskCompletionSource<bool>? BoolTcs { get; init; }

            public static PositionCommand CreateCreate(PositionQueueTask task)
                => new() { Kind = CommandKind.Create, Task = task };

            public static PositionCommand CreateUpdate(PositionQueueTask task, string? reason)
                => new() { Kind = CommandKind.Update, Task = task, Reason = reason };

            public static PositionCommand CreateInvalidate(long parcelId, string? reason)
                => new() { Kind = CommandKind.Invalidate, ParcelId = parcelId, Reason = reason };

            public static PositionCommand CreateRemoveHeadIfMatch(long parcelId, string? reason)
                => new() { Kind = CommandKind.RemoveHeadIfMatch, ParcelId = parcelId, Reason = reason };

            public static PositionCommand CreateDequeueHeadIfMatch(long parcelId, string? reason)
                => new() { Kind = CommandKind.DequeueHeadIfMatch, ParcelId = parcelId, Reason = reason };

            public static PositionCommand CreateClear(string? reason)
                => new() { Kind = CommandKind.Clear, Reason = reason };
        }
    }
}
