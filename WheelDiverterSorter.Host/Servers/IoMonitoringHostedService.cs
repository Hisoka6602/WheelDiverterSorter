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
using WheelDiverterSorter.Core.Utilities;

namespace WheelDiverterSorter.Host.Servers {

    public class IoMonitoringHostedService : BackgroundService {
        private readonly IEmcController _emcController;
        private readonly IIoPanel _ioPanel;
        private readonly ISensorManager _sensorManager;
        private readonly IOptions<List<IoPanelButtonOptions>> _ioPanelButtonOptions;
        private readonly IOptions<List<SensorOptions>> _sensorOptions;

        public IoMonitoringHostedService(ILogger<IoLinkageHostedService> logger,
            IEmcController emcController, IIoPanel ioPanel, ISensorManager sensorManager,
            IOptions<List<IoPanelButtonOptions>> ioPanelButtonOptions,
            IOptions<List<SensorOptions>> sensorOptions) {
            _emcController = emcController;
            _ioPanel = ioPanel;
            _sensorManager = sensorManager;
            _ioPanelButtonOptions = ioPanelButtonOptions;
            _sensorOptions = sensorOptions;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            //如果是电脑刚开机则等待15秒再初始化IO监控，避免IO控制器未就绪
            // 如果系统刚开机，补足 15 秒窗口，避免 IO 控制器未就绪
            await EnvironmentHelper.DelayAfterBootAsync(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false);
            await _emcController.InitializeAsync(stoppingToken);

            var ioPointInfos = _ioPanelButtonOptions.Value.Select(w => new IoPointInfo {
                Point = w.Point,
                Type = w.Type,
                Name = w.ButtonName,
                DebounceWindowMs = w.DebounceWindowMs,
                LastLevelChangedAtMs = null
            }).ToList();
            /*ioPointInfos.AddRange(_sensorOptions.Value.Select(s => new IoPointInfo {
                Point = s.Point,
                Type = s.Type,
                Name = s.SensorName,
                DebounceWindowMs = s.DebounceWindowMs,
                LastLevelChangedAtMs = null
            }));*/

            await _emcController.SetMonitoredIoPointsAsync(ioPointInfos, stoppingToken);
            await Task.Delay(500, stoppingToken);
            await _ioPanel.StartMonitoringAsync(stoppingToken);
            await _sensorManager.StartMonitoringAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested) {
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
