using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Manager {

    /// <summary>
    /// 摆轮管理器接口
    /// </summary>
    public interface IWheelDiverterManager : IDisposable {

        /// <summary>
        /// 摆轮集合
        /// </summary>
        IReadOnlyList<IWheelDiverter> Diverters { get; }

        /// <summary>
        /// 全部连接
        /// </summary>
        ValueTask ConnectAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 全部运行
        /// </summary>
        ValueTask RunAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 全部停止
        /// </summary>
        ValueTask StopAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 全部直通
        /// </summary>
        ValueTask StraightThroughAllAsync(CancellationToken cancellationToken = default);
    }
}
