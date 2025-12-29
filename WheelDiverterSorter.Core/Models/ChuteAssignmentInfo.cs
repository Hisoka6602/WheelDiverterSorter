using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Models {
    public record ChuteAssignmentInfo {
        public required long ParcelId { get; init; }
        public required long ChuteId { get; init; }
        /// <summary>
        /// DWS（尺寸重量扫描）数据（可选）
        /// </summary>
        /// <remarks>
        /// PR-UPSTREAM02: 新增字段，由上游在推送格口分配时一并提供。
        /// </remarks>
        public DwsMeasurement? DwsPayload { get; init; }
        public required DateTimeOffset AssignedAt { get; init; }
    }
}
