using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Models;

namespace WheelDiverterSorter.Core {

    public interface ISignalTower : IDisposable {

        /// <summary>
        /// 当前三色灯状态
        /// </summary>
        SignalTowerLightsState CurrentLights { get; }

        /// <summary>
        /// 当前蜂鸣器状态
        /// </summary>
        SignalTowerBuzzerState CurrentBuzzer { get; }

        /// <summary>
        /// 点位绑定信息
        /// </summary>
        SignalTowerPointBinding PointBinding { get; }

        /// <summary>
        /// 状态变更事件
        /// </summary>
        event EventHandler<SignalTowerStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// 设置三色灯和蜂鸣器点位
        /// </summary>
        ValueTask BindPointsAsync(SignalTowerPointBinding binding, CancellationToken cancellationToken = default);
    }
}
