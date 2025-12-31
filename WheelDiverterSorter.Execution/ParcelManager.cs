using System;
using System.Linq;
using System.Text;
using System.Collections;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Models;
using WheelDiverterSorter.Core.Manager;

namespace WheelDiverterSorter.Execution {

    /// <summary>
    /// 包裹管理器（高频/高并发查询、更新、删除）
    /// </summary>
    public sealed class ParcelManager : IParcelManager {
        private readonly ILogger<ParcelManager> _logger;
        private readonly ConcurrentDictionary<long, ParcelInfo> _parcels;

        // 锁分段：避免每个包裹一个锁对象的内存压力
        private readonly object[] _gates;

        private readonly int _gateMask;
        private int _isClearing; // 0=否, 1=是

        public ParcelManager(ILogger<ParcelManager> logger,
            int initialCapacity = 4096,
            int gateCountPowerOfTwo = 1024) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // gateCountPowerOfTwo 建议为 2^N，便于位运算取模
            if (gateCountPowerOfTwo <= 0 || (gateCountPowerOfTwo & (gateCountPowerOfTwo - 1)) != 0) {
                throw new ArgumentOutOfRangeException(nameof(gateCountPowerOfTwo), "参数无效：gateCountPowerOfTwo 必须为 2 的幂。");
            }

            _parcels = new ConcurrentDictionary<long, ParcelInfo>(
                concurrencyLevel: Environment.ProcessorCount,
                capacity: initialCapacity);

            _gates = new object[gateCountPowerOfTwo];
            for (var i = 0; i < _gates.Length; i++) {
                _gates[i] = new object();
            }

            _gateMask = gateCountPowerOfTwo - 1;
            Parcels = new ParcelInfoReadOnlyView(_parcels);
        }

        public void Dispose() {
            // 释放时清空集合，避免长生命周期引用导致资源滞留
            _parcels.Clear();
            GC.SuppressFinalize(this);
        }

        public IReadOnlyCollection<ParcelInfo> Parcels { get; }

        public event EventHandler<ParcelCreatedEventArgs>? ParcelCreated;

        public event EventHandler<ParcelTargetChuteUpdatedEventArgs>? ParcelTargetChuteUpdated;

        public event EventHandler<ParcelDroppedEventArgs>? ParcelDropped;

        public event EventHandler<ParcelRemovedEventArgs>? ParcelRemoved;

        public event EventHandler<ParcelManagerFaultedEventArgs>? Faulted;

        public ValueTask<bool> CreateAsync(ParcelInfo parcel, CancellationToken cancellationToken = default) {
            if (Volatile.Read(ref _isClearing) == 1 || cancellationToken.IsCancellationRequested) {
                ParcelManagerLog.RejectedByClearing(_logger, "CreateAsync");
                return new ValueTask<bool>(false);
            }

            try {
                if (parcel.ParcelId <= 0) {
                    throw new ArgumentOutOfRangeException(nameof(parcel.ParcelId), "参数无效：ParcelId 必须为正数。");
                }

                var gate = GetGate(parcel.ParcelId);
                var added = false;

                lock (gate) {
                    added = _parcels.TryAdd(parcel.ParcelId, parcel);
                }

                if (!added) {
                    ParcelManagerLog.CreateDuplicate(_logger, parcel.ParcelId);
                    return new ValueTask<bool>(false);
                }
                var createdAt = DateTimeOffset.Now;
                ParcelManagerLog.Created(_logger, parcel.ParcelId, parcel.BarCode, createdAt);
                RaiseSafe(ParcelCreated, new ParcelCreatedEventArgs {
                    ParcelId = parcel.ParcelId,
                    Parcel = parcel,
                    CreatedAt = createdAt
                });

                return new ValueTask<bool>(true);
            }
            catch (Exception ex) {
                RaiseFaulted("CreateAsync", parcel.ParcelId, ex);
                return new ValueTask<bool>(false);
            }
        }

        public ValueTask<bool> AssignTargetChuteAsync(long parcelId, long targetChuteId, DateTimeOffset assignedAt, CancellationToken cancellationToken = default) {
            if (Volatile.Read(ref _isClearing) == 1 || cancellationToken.IsCancellationRequested || parcelId <= 0 || targetChuteId <= 0) {
                ParcelManagerLog.RejectedByClearing(_logger, "AssignTargetChuteAsync");
                return new ValueTask<bool>(false);
            }

            try {
                var gate = GetGate(parcelId);
                ParcelTargetChuteUpdatedEventArgs args;
                long oldTargetChuteIdForLog;

                lock (gate) {
                    if (!_parcels.TryGetValue(parcelId, out var parcel)) {
                        ParcelManagerLog.TargetUpdateNotFound(_logger, parcelId);
                        return new ValueTask<bool>(false);
                    }

                    var oldTargetChuteId = parcel.TargetChuteId;
                    oldTargetChuteIdForLog = oldTargetChuteId <= 0 ? 0 : oldTargetChuteId;

                    parcel.TargetChuteId = targetChuteId;

                    args = new ParcelTargetChuteUpdatedEventArgs {
                        ParcelId = parcelId,
                        OldTargetChuteId = oldTargetChuteId <= 0 ? null : oldTargetChuteId,
                        NewTargetChuteId = targetChuteId,
                        AssignedAt = assignedAt
                    };
                }

                ParcelManagerLog.TargetUpdated(_logger, parcelId, oldTargetChuteIdForLog, targetChuteId, assignedAt);

                RaiseSafe(ParcelTargetChuteUpdated, args);
                return new ValueTask<bool>(true);
            }
            catch (Exception ex) {
                RaiseFaulted("AssignTargetChuteAsync", parcelId, ex);
                return new ValueTask<bool>(false);
            }
        }

        public ValueTask<bool> MarkDroppedAsync(long parcelId, long actualChuteId, DateTimeOffset droppedAt, CancellationToken cancellationToken = default) {
            if (Volatile.Read(ref _isClearing) == 1 || cancellationToken.IsCancellationRequested || parcelId <= 0 || actualChuteId <= 0) {
                ParcelManagerLog.RejectedByClearing(_logger, "MarkDroppedAsync");
                return new ValueTask<bool>(false);
            }

            try {
                var gate = GetGate(parcelId);
                ParcelDroppedEventArgs args;

                lock (gate) {
                    if (!_parcels.TryGetValue(parcelId, out var parcel)) {
                        ParcelManagerLog.DropNotFound(_logger, parcelId);
                        return new ValueTask<bool>(false);
                    }

                    parcel.MarkDropped(actualChuteId, droppedAt.UtcDateTime);

                    args = new ParcelDroppedEventArgs {
                        ParcelId = parcelId,
                        ActualChuteId = actualChuteId,
                        DroppedAt = droppedAt
                    };
                }

                ParcelManagerLog.Dropped(_logger, parcelId, actualChuteId, droppedAt);

                RaiseSafe(ParcelDropped, args);
                return new ValueTask<bool>(true);
            }
            catch (Exception ex) {
                RaiseFaulted("MarkDroppedAsync", parcelId, ex);
                return new ValueTask<bool>(false);
            }
        }

        public ValueTask<bool> RemoveAsync(long parcelId, string? reason = null, CancellationToken cancellationToken = default) {
            if (Volatile.Read(ref _isClearing) == 1 || cancellationToken.IsCancellationRequested || parcelId <= 0) {
                ParcelManagerLog.RejectedByClearing(_logger, "RemoveAsync");
                return new ValueTask<bool>(false);
            }

            try {
                var gate = GetGate(parcelId);
                var removed = false;

                lock (gate) {
                    removed = _parcels.TryRemove(parcelId, out _);
                }

                if (!removed) {
                    ParcelManagerLog.RemoveNotFound(_logger, parcelId);
                    return new ValueTask<bool>(false);
                }

                var removedAt = DateTimeOffset.Now;
                ParcelManagerLog.Removed(_logger, parcelId, reason, removedAt);

                RaiseSafe(ParcelRemoved, new ParcelRemovedEventArgs {
                    ParcelId = parcelId,
                    Reason = reason,
                    RemovedAt = removedAt
                });

                return new ValueTask<bool>(true);
            }
            catch (Exception ex) {
                RaiseFaulted("RemoveAsync", parcelId, ex);
                return new ValueTask<bool>(false);
            }
        }

        public ValueTask ClearAsync(string? reason = null, CancellationToken cancellationToken = default) {
            if (Interlocked.Exchange(ref _isClearing, 1) == 1) {
                return ValueTask.CompletedTask;
            }

            try {
                var countBefore = _parcels.Count;
                _parcels.Clear();

                ParcelManagerLog.Cleared(_logger, reason, countBefore, DateTimeOffset.Now);
            }
            finally {
                Volatile.Write(ref _isClearing, 0);
            }

            return ValueTask.CompletedTask;
        }

        public bool TryGet(long parcelId, out ParcelInfo parcel)
            => _parcels.TryGetValue(parcelId, out parcel!);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private object GetGate(long parcelId) {
            var h = (uint)parcelId ^ (uint)(parcelId >> 32);
            var idx = (int)(h & (uint)_gateMask);
            return _gates[idx];
        }

        private void RaiseFaulted(string operation, long? parcelId, Exception exception) {
            try {
                var message = parcelId.HasValue
                    ? $"包裹管理器发生异常：Operation={operation} ParcelId={parcelId.Value}"
                    : $"包裹管理器发生异常：Operation={operation}";

                _logger.LogError(exception, "{Message}", message);

                RaiseSafe(Faulted, new ParcelManagerFaultedEventArgs {
                    Message = message,
                    Exception = exception,
                    OccurredAt = DateTimeOffset.Now
                });
            }
            catch {
                // 异常彻底隔离，防止事件链导致宿主崩溃
            }
        }

        private void RaiseSafe<TArgs>(EventHandler<TArgs>? handler, TArgs args) {
            if (handler is null) {
                return;
            }

            try {
                handler.Invoke(this, args);
            }
            catch (Exception ex) {
                RaiseFaulted("EventDispatch", null, ex);
            }
        }
    }

    /// <summary>
    /// 并发包裹集合只读视图（零拷贝）
    /// </summary>
    internal sealed class ParcelInfoReadOnlyView(ConcurrentDictionary<long, ParcelInfo> source)
        : IReadOnlyCollection<ParcelInfo> {
        private readonly ConcurrentDictionary<long, ParcelInfo> _source = source ?? throw new ArgumentNullException(nameof(source));

        public int Count => _source.Count;

        public IEnumerator<ParcelInfo> GetEnumerator() => _source.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal static partial class ParcelManagerLog {

        [LoggerMessage(EventId = 2100, Level = LogLevel.Information,
            Message = "包裹创建：ParcelId={ParcelId} BarCode={BarCode} CreatedAt={CreatedAt:o}")]
        public static partial void Created(ILogger logger, long parcelId, string barCode, DateTimeOffset createdAt);

        [LoggerMessage(EventId = 2101, Level = LogLevel.Trace,
            Message = "包裹创建失败：ParcelId={ParcelId} 原因=已存在")]
        public static partial void CreateDuplicate(ILogger logger, long parcelId);

        [LoggerMessage(EventId = 2110, Level = LogLevel.Information,
            Message = "目标格口更新：ParcelId={ParcelId} OldTargetChuteId={OldTargetChuteId} NewTargetChuteId={NewTargetChuteId} AssignedAt={AssignedAt:o}")]
        public static partial void TargetUpdated(ILogger logger, long parcelId, long oldTargetChuteId, long newTargetChuteId, DateTimeOffset assignedAt);

        [LoggerMessage(EventId = 2111, Level = LogLevel.Trace,
            Message = "目标格口更新失败：ParcelId={ParcelId} 原因=不存在")]
        public static partial void TargetUpdateNotFound(ILogger logger, long parcelId);

        [LoggerMessage(EventId = 2120, Level = LogLevel.Information,
            Message = "包裹落格：ParcelId={ParcelId} ActualChuteId={ActualChuteId} DroppedAt={DroppedAt:o}")]
        public static partial void Dropped(ILogger logger, long parcelId, long actualChuteId, DateTimeOffset droppedAt);

        [LoggerMessage(EventId = 2121, Level = LogLevel.Trace,
            Message = "包裹落格失败：ParcelId={ParcelId} 原因=不存在")]
        public static partial void DropNotFound(ILogger logger, long parcelId);

        [LoggerMessage(EventId = 2130, Level = LogLevel.Information,
            Message = "包裹移除：ParcelId={ParcelId} Reason={Reason} RemovedAt={RemovedAt:o}")]
        public static partial void Removed(ILogger logger, long parcelId, string? reason, DateTimeOffset removedAt);

        [LoggerMessage(EventId = 2131, Level = LogLevel.Trace,
            Message = "包裹移除失败：ParcelId={ParcelId} 原因=不存在")]
        public static partial void RemoveNotFound(ILogger logger, long parcelId);

        [LoggerMessage(EventId = 2140, Level = LogLevel.Information,
            Message = "包裹清空：Reason={Reason} CountBefore={CountBefore} ClearedAt={ClearedAt:o}")]
        public static partial void Cleared(ILogger logger, string? reason, int countBefore, DateTimeOffset clearedAt);

        [LoggerMessage(EventId = 2141, Level = LogLevel.Trace,
            Message = "操作被拒绝：Operation={Operation} 原因=清空中")]
        public static partial void RejectedByClearing(ILogger logger, string operation);

        [LoggerMessage(EventId = 2999, Level = LogLevel.Error,
            Message = "包裹管理器异常：Message={Message}")]
        public static partial void Faulted(ILogger logger, string message, Exception exception);
    }
}
