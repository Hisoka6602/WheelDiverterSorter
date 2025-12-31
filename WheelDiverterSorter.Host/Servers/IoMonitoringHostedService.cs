using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WheelDiverterSorter.Core;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Models;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Host.Servers {

    public class IoMonitoringHostedService : BackgroundService {
        private readonly IEmcController _emcController;
        private readonly IIoPanel _ioPanel;
        private readonly IOptions<IReadOnlyList<IoPanelButtonOptions>> _ioPanelButtonOptions;
        private readonly IOptions<IReadOnlyList<SensorOptions>> _sensorOptions;

        public IoMonitoringHostedService(ILogger<IoLinkageHostedService> logger,
            IEmcController emcController, IIoPanel ioPanel,
            IOptions<IReadOnlyList<IoPanelButtonOptions>> ioPanelButtonOptions,
            IOptions<IReadOnlyList<SensorOptions>> sensorOptions) {
            _emcController = emcController;
            _ioPanel = ioPanel;
            _ioPanelButtonOptions = ioPanelButtonOptions;
            _sensorOptions = sensorOptions;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            //如果是电脑刚开机则等待15秒再初始化IO监控，避免IO控制器未就绪
            // 如果系统刚开机，补足 15 秒窗口，避免 IO 控制器未就绪
            await DelayAfterBootAsync(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false);
            await _emcController.InitializeAsync(stoppingToken);

            var ioPointInfos = _ioPanelButtonOptions.Value.Select(w => new IoPointInfo {
                Point = w.Point,
                Type = w.Type,
                Name = w.ButtonName,
                DebounceWindowMs = w.DebounceWindowMs,
                LastLevelChangedAtMs = null
            }).ToList();
            ioPointInfos.AddRange(_sensorOptions.Value.Select(s => new IoPointInfo {
                Point = s.Point,
                Type = s.Type,
                Name = s.SensorName,
                DebounceWindowMs = s.DebounceWindowMs,
                LastLevelChangedAtMs = null
            }));

            await _emcController.SetMonitoredIoPointsAsync(ioPointInfos, stoppingToken);

            await _ioPanel.StartMonitoringAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested) {
                await Task.Delay(1000, stoppingToken);
            }
        }

        private static ValueTask DelayAfterBootAsync(TimeSpan minUptime, CancellationToken cancellationToken) {
            // TickCount64：系统启动以来的毫秒数（Windows 上可直接用于开机时间窗口判断）
            var uptimeMs = Environment.TickCount64;
            var minUptimeMs = (long)minUptime.TotalMilliseconds;

            if (uptimeMs >= minUptimeMs) {
                return ValueTask.CompletedTask;
            }

            var remainingMs = (int)(minUptimeMs - uptimeMs);
            return new ValueTask(Task.Delay(remainingMs, cancellationToken));
        }
    }
}
