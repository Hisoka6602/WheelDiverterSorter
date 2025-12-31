using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WheelDiverterSorter.Core;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Models;
using WheelDiverterSorter.Core.Manager;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Host.Servers {

    public class ParcelHostedService : BackgroundService {
        private readonly ILogger<IoLinkageHostedService> _logger;
        private readonly IOptions<List<SensorOptions>> _sensorOptions;
        private readonly ISystemStateManager _systemStateManager;
        private readonly ISensorManager _sensorManager;
        private readonly IUpstreamRouting _upstreamRouting;
        private IoState _triggerState = IoState.Low;

        public ParcelHostedService(ILogger<IoLinkageHostedService> logger,
            IOptions<List<SensorOptions>> sensorOptions,
            ISystemStateManager systemStateManager, IParcelManager parcelManager,
            ISensorManager sensorManager, IUpstreamRouting upstreamRouting) {
            _logger = logger;
            _sensorOptions = sensorOptions;
            _systemStateManager = systemStateManager;
            _sensorManager = sensorManager;
            _upstreamRouting = upstreamRouting;
            _sensorManager.SensorStateChanged += async (sender, args) => {
                await Task.Yield();
                if (args.SensorType == IoPointType.ParcelCreateSensor && args.NewState == _triggerState &&
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
            _triggerState = _sensorOptions.Value.FirstOrDefault(f => f.Type == IoPointType.ParcelCreateSensor)?.TriggerState ?? IoState.Low;
            return Task.CompletedTask;
        }
    }
}
