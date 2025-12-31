using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WheelDiverterSorter.Core;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Models;
using WheelDiverterSorter.Core.Manager;

namespace WheelDiverterSorter.Host.Servers {

    public class ParcelHostedService : BackgroundService {
        private readonly ILogger<IoLinkageHostedService> _logger;
        private readonly ISystemStateManager _systemStateManager;
        private readonly ISensorManager _sensorManager;
        private readonly IUpstreamRouting _upstreamRouting;

        public ParcelHostedService(ILogger<IoLinkageHostedService> logger,
            ISystemStateManager systemStateManager, IParcelManager parcelManager,
            ISensorManager sensorManager, IUpstreamRouting upstreamRouting) {
            _logger = logger;
            _systemStateManager = systemStateManager;
            _sensorManager = sensorManager;
            _upstreamRouting = upstreamRouting;
            _sensorManager.SensorStateChanged += async (sender, args) => {
                await Task.Yield();
                if (args.SensorType == IoPointType.ParcelCreateSensor &&
                    _systemStateManager.CurrentState == SystemState.Running) {
                    //创建包裹
                    await parcelManager.CreateAsync(new ParcelInfo {
                        ParcelId = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                    });
                }
            };
            _systemStateManager.StateChanged += async (sender, args) => {
                await Task.Yield();

                if (args.NewState != SystemState.Running) {
                    await parcelManager.ClearAsync();
                }
            };
            _upstreamRouting.ChuteAssignedReceived += async (sender, info) => {
                //更新包裹目标格口
                await Task.Yield();
                await parcelManager.AssignTargetChuteAsync(info.ParcelId, info.ChuteId, info.AssignedAt);
            };
            parcelManager.ParcelDropped += async (sender, args) => {
                await Task.Yield();
                await _upstreamRouting.SendDropToChuteAsync(new SortingCompletedMessage {
                    ParcelId = args.ParcelId,
                    ActualChuteId = args.ActualChuteId,
                    CompletedAt = args.DroppedAt
                });
            };
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) {
            return Task.CompletedTask;
        }
    }
}
