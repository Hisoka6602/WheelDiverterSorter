using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WheelDiverterSorter.Core.Options {
    public sealed record class PositionOptions {
        /// <summary>
        /// 位置索引(需要保证强顺序)
        /// </summary>
        public required int PositionIndex { get; init; }

        /// <summary>
        /// 摆轮前感应IO的ID（引用感应IO配置中的SensorId，必须配置）
        /// </summary>
        public required long FrontSensorId { get; init; }

        /// <summary>
        /// 摆轮ID
        /// </summary>
        public required long DiverterId { get; init; }

        /// <summary>
        /// 前置线体段ID（引用线体段配置中的SegmentId）
        /// </summary>
        public required long SegmentId { get; init; }

        /// <summary>
        /// 左侧格口ID列表
        /// </summary>
        /// <remarks>
        /// 摆轮左转时可分拣到的格口ID列表
        /// </remarks>
        /// <example>[2, 3]</example>
        public List<long>? LeftChuteIds { get; init; }

        /// <summary>
        /// 右侧格口ID列表
        /// </summary>
        /// <remarks>
        /// 摆轮右转时可分拣到的格口ID列表
        /// </remarks>
        /// <example>[1, 4]</example>
        public List<long>? RightChuteIds { get; init; }

        /// <summary>
        /// 备注信息
        /// </summary>
        [StringLength(500)]
        public string? Remarks { get; init; }
    }
}
