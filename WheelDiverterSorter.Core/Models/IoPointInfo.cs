using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;
using System.Runtime.CompilerServices;

namespace WheelDiverterSorter.Core.Models {
    /// <summary>
    /// IO 点位信息
    /// </summary>
    public sealed record class IoPointInfo {
        // 说明：
        // 1) State 使用 int 存储，配合 Volatile 实现跨线程可见与原子读写
        // 2) LastChangedTicks 用 long 存储，记录变化时刻（Stopwatch ticks 或 DateTime ticks，按需求统一）
        private int _stateValue;
        private long _lastChangedTicks;
        /// <summary>
        /// 点位编号
        /// </summary>
        public required int Point { get; init; }

        /// <summary>
        /// 点位类型
        /// </summary>
        public required IoPointType Type { get; init; }
        /// <summary>
        /// 当前IO电平
        /// </summary>
        public IoState State {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (IoState)Volatile.Read(ref _stateValue);
        }

        /// <summary>
        /// 状态变化时刻（ticks）
        /// </summary>
        public long LastChangedTicks {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref _lastChangedTicks);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateState(IoState newState, long changedTicks) {
            Volatile.Write(ref _stateValue, (int)newState);
            Volatile.Write(ref _lastChangedTicks, changedTicks);
        }
        /// <summary>
        /// 点位名称
        /// </summary>
        public required string Name { get; init; } = string.Empty;

        /// <summary>
        /// 防抖时间（毫秒），0 表示不启用防抖
        /// </summary>
        public required int DebounceWindowMs { get; init; }

        /// <summary>
        /// 上次电平变化时间戳（毫秒）；未记录时为 null
        /// </summary>
        public long? LastLevelChangedAtMs { get; init; }

        /// <summary>
        /// 是否启用防抖
        /// </summary>
        public bool IsDebounceEnabled => DebounceWindowMs > 0;
    }
}
