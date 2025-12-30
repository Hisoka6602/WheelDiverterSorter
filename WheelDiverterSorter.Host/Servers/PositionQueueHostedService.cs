using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Host.Servers {

    /// <summary>
    /// 位置(队列服务)
    /// </summary>
    public class PositionQueueHostedService : BackgroundService {

        protected override Task ExecuteAsync(CancellationToken stoppingToken) {
            return Task.CompletedTask;
        }
    }
}
