using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Models {
    public class ParcelExceptionMessage {

        /// <summary>
        /// 包裹Id
        /// </summary>
        public required long ParcelId { get; init; }

        /// <summary>
        /// 异常发生时间戳（Unix ms）
        /// </summary>
        public required long OccurredAtMs { get; init; }

        /// <summary>
        /// 异常描述（无则为空）
        /// </summary>
        public string? Message { get; init; }

        /// <summary>
        /// 原始载荷（无则为空）
        /// </summary>
        public ReadOnlyMemory<byte> RawPayload { get; init; }
    }
}
