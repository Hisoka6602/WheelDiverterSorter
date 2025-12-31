using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using WheelDiverterSorter.Core.Manager;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Host.Servers {

    /// <summary>
    /// 分拣编排服务
    /// </summary>
    public class SortingOrchestrationHostedService : BackgroundService {
        private readonly ILogger<SortingOrchestrationHostedService> _logger;
        private readonly IWheelDiverterManager _wheelDiverterManager;
        private readonly ISensorManager _sensorManager;
        private readonly IOptions<IReadOnlyList<WheelDiverterConnectionOptions>> _wheelDiverterConnectionOptions;
        private readonly IOptions<IReadOnlyList<ConveyorSegmentOptions>> _conveyorSegmentOptions;
        private readonly IOptions<IReadOnlyList<SensorOptions>> _sensorOptions;
        private readonly IOptions<IReadOnlyList<PositionOptions>> _positionOptions;
        private readonly IParcelManager _parcelManager;
        private readonly IPositionQueueManager _positionQueueManager;

        public SortingOrchestrationHostedService(ILogger<SortingOrchestrationHostedService> logger,
            IWheelDiverterManager wheelDiverterManager,
            ISensorManager sensorManager,
            IOptions<IReadOnlyList<WheelDiverterConnectionOptions>> wheelDiverterConnectionOptions,
            IOptions<IReadOnlyList<ConveyorSegmentOptions>> conveyorSegmentOptions,
            IOptions<IReadOnlyList<SensorOptions>> sensorOptions,
            IOptions<IReadOnlyList<PositionOptions>> positionOptions,
            IParcelManager parcelManager,
            IPositionQueueManager positionQueueManager) {
            _logger = logger;
            _wheelDiverterManager = wheelDiverterManager;
            _sensorManager = sensorManager;
            _wheelDiverterConnectionOptions = wheelDiverterConnectionOptions;
            _conveyorSegmentOptions = conveyorSegmentOptions;
            _sensorOptions = sensorOptions;
            _positionOptions = positionOptions;
            _parcelManager = parcelManager;
            _positionQueueManager = positionQueueManager;

            _parcelManager.ParcelCreated += (sender, args) => {
                //派发任务给对应的位置队列
            }
        }

        //分发站点任务
        //读取拓扑, 分配任务到各个分拣轮
        //更新包裹状态
        protected override Task ExecuteAsync(CancellationToken stoppingToken) {
            return Task.CompletedTask;
        }
    }
}
