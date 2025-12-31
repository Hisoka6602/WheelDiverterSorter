using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Manager;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Host.Servers {

    public class WheelDiverterHostedService : BackgroundService {
        private readonly ILogger<IoLinkageHostedService> _logger;
        private readonly IOptions<IReadOnlyList<WheelDiverterConnectionOptions>> _wheelDiverterConnectionOptions;
        private readonly IWheelDiverterManager _wheelDiverterManager;
        private readonly ISystemStateManager _systemStateManager;

        public WheelDiverterHostedService(ILogger<IoLinkageHostedService> logger,
            IOptions<IReadOnlyList<WheelDiverterConnectionOptions>> wheelDiverterConnectionOptions,
            IWheelDiverterManager wheelDiverterManager,
            ISystemStateManager systemStateManager) {
            _logger = logger;
            _wheelDiverterConnectionOptions = wheelDiverterConnectionOptions;
            _wheelDiverterManager = wheelDiverterManager;
            _systemStateManager = systemStateManager;

            _systemStateManager.StateChanged += async (sender, args) => {
                await Task.Yield();
                if (args.NewState == SystemState.Running) {
                    await _wheelDiverterManager.StraightThroughAllAsync();
                    await Task.Delay(100);
                    foreach (var wheelDiverter in _wheelDiverterManager.Diverters.OrderByDescending(o => o.DiverterId)) {
                        await wheelDiverter.RunAsync();
                        await Task.Delay(500);
                    }
                }
                else if (args.NewState is SystemState.EmergencyStop or SystemState.Paused) {
                    await _wheelDiverterManager.StopAllAsync();
                }
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            await _wheelDiverterManager.ConnectAllAsync(stoppingToken);
            await _wheelDiverterManager.StopAllAsync(stoppingToken);
        }
    }
}
