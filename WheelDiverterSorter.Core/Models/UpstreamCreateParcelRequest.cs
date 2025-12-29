using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Models {
    /// <summary>
    /// 创建包裹（检测通知）请求
    /// </summary>
    public sealed record class UpstreamCreateParcelRequest {
        public required long ParcelId { get; init; }           // 包裹ID（毫秒时间戳）
        public required DateTimeOffset DetectedAt { get; init; } // 检测时间
    }
}
