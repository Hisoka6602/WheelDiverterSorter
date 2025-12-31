using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using WheelDiverterSorter.Core.Events;

namespace WheelDiverterSorter.Core.Models {
    /// <summary>
    /// 包裹信息（集合中长期驻留对象，支持高频原地更新）
    /// </summary>
    public sealed record class ParcelInfo {
        /// <summary>
        /// 包裹Id
        /// </summary>
        public required long ParcelId { get; init; }

        private long _targetChuteId = 999;

        /// <summary>
        /// 目标格口Id
        /// </summary>
        public long TargetChuteId {
            get => _targetChuteId;
            set {
                if (_targetChuteId == value) {
                    return;
                }

                _targetChuteId = value;
                TargetChuteUpdatedTime = DateTime.Now;
            }
        }

        /// <summary>
        /// 更新目标格口时间（默认值表示未更新/未设置）
        /// </summary>
        public DateTime? TargetChuteUpdatedTime { get; internal set; }

        /// <summary>
        /// 实际落格格口Id（未落格时为 null）
        /// </summary>
        public long? ActualChuteId { get; private set; }

        /// <summary>
        /// 包裹条码
        /// </summary>
        public string BarCode { get; init; } = string.Empty;

        /// <summary>
        /// 包裹上一站Id（0 表示未知/未设置）
        /// </summary>
        public long PrevStationId { get; private set; }

        /// <summary>
        /// 包裹本站Id（0 表示未知/未设置）
        /// </summary>
        public long CurrentStationId { get; private set; }

        /// <summary>
        /// 包裹从上一站到本站的耗时（毫秒，0 表示未知/未设置）
        /// </summary>
        public int TransitTimeMs { get; private set; }

        /// <summary>
        /// 上一站到达时间（默认值表示未知/未设置）
        /// </summary>
        public DateTime PrevStationArrivedTime { get; private set; }

        /// <summary>
        /// 本站到达时间（默认值表示未知/未设置）
        /// </summary>
        public DateTime CurrentStationArrivedTime { get; private set; }

        /// <summary>
        /// 落格时间（默认值表示未落格/未设置）
        /// </summary>
        public DateTime DroppedTime { get; private set; }

        private int _remainingStationCount;

        /// <summary>
        /// 剩余站点数量（不包含当前站点；0 表示当前站点为终点/无后续站点）
        /// </summary>
        public int RemainingStationCount {
            get => _remainingStationCount;
            init {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(RemainingStationCount), "参数无效：RemainingStationCount 不能为负数。");
                _remainingStationCount = value;
            }
        }

        /// <summary>
        /// 下一站是否终点站（由 RemainingStationCount 自动推导）
        /// </summary>
        public bool IsNextStationTerminal => _remainingStationCount == 1;

        /// <summary>
        /// 高频更新：到达站点并自动前移上一站信息
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ArriveAtStation(long stationId, DateTime arrivedTime) {
            if (stationId < 0) throw new ArgumentOutOfRangeException(nameof(stationId), "参数无效：stationId 不能为负数。");

            // 旧本站前移为上一站
            PrevStationId = CurrentStationId;
            PrevStationArrivedTime = CurrentStationArrivedTime;

            // 写入新本站
            CurrentStationId = stationId;
            CurrentStationArrivedTime = arrivedTime;

            // 剩余站点数量递减（最小为 0）
            if (_remainingStationCount > 0) {
                _remainingStationCount--;
            }

            // 可计算时自动刷新耗时
            if (PrevStationId != 0 && PrevStationArrivedTime != default && CurrentStationArrivedTime != default) {
                var ms = (long)(CurrentStationArrivedTime - PrevStationArrivedTime).TotalMilliseconds;
                TransitTimeMs = ms <= 0 ? 0 : ms > int.MaxValue ? int.MaxValue : (int)ms;
            }
            else {
                TransitTimeMs = 0;
            }
        }

        /// <summary>
        /// 标记落格（通常只调用一次）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkDropped(long actualChuteId, DateTime droppedTime) {
            if (actualChuteId <= 0) throw new ArgumentOutOfRangeException(nameof(actualChuteId), "参数无效：ActualChuteId 必须为正数。");

            ActualChuteId = actualChuteId;
            DroppedTime = droppedTime;
        }
    }
}
