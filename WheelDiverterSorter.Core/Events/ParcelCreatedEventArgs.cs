using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Models;

namespace WheelDiverterSorter.Core.Events {
    /// <summary>
    /// 包裹创建事件载荷
    /// </summary>
    public readonly record struct ParcelCreatedEventArgs {
        /// <summary>
        /// 包裹信息快照
        /// </summary>
        public required ParcelInfo Parcel { get; init; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public required DateTimeOffset CreatedAt { get; init; }
    }
}
