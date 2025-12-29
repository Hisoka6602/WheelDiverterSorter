using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WheelDiverterSorter.Core;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using WheelDiverterSorter.Core.Manager;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Execution {
    public class WheelDiverterManager : IWheelDiverterManager {
        private readonly ILogger<WheelDiverterManager> _logger;

        public WheelDiverterManager(List<IWheelDiverter> wheelDiverters,
            ILogger<WheelDiverterManager> logger) {
            Diverters = wheelDiverters;
            _logger = logger;
        }

        public void Dispose() {
            foreach (var diverter in Diverters) {
                diverter.Dispose();
            }
        }

        public IReadOnlyList<IWheelDiverter> Diverters { get; private set; }

        public async ValueTask ConnectAllAsync(CancellationToken cancellationToken = default) {
            foreach (var wheelDiverter in Diverters) {
                await wheelDiverter.ConnectAsync(wheelDiverter.ConnectionOptions, cancellationToken);
            }
        }

        public async ValueTask RunAllAsync(CancellationToken cancellationToken = default) {
            var diverters = Diverters.OrderByDescending(o => o.DiverterId);
            foreach (var wheelDiverter in diverters) {
                await wheelDiverter.RunAsync(cancellationToken);
                await Task.Delay(500, cancellationToken);
            }
        }

        public async ValueTask StopAllAsync(CancellationToken cancellationToken = default) {
            foreach (var wheelDiverter in Diverters) {
                await wheelDiverter.StopAsync(cancellationToken);
            }
        }

        public async ValueTask StraightThroughAllAsync(CancellationToken cancellationToken = default) {
            foreach (var wheelDiverter in Diverters) {
                await wheelDiverter.StraightThroughAsync(cancellationToken);
            }
        }
    }
}
