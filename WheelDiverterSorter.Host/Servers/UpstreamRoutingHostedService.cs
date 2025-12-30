using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WheelDiverterSorter.Core;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Host.Servers {

    public class UpstreamRoutingHostedService : BackgroundService {
        private readonly ILogger<UpstreamRoutingHostedService> _logger;
        private readonly IUpstreamRouting _upstreamRouting;
        private readonly IOptions<UpstreamRoutingConnectionOptions> _upstreamRoutingConnectionOptions;

        public UpstreamRoutingHostedService(ILogger<UpstreamRoutingHostedService> logger,
            IUpstreamRouting upstreamRouting,
            IOptions<UpstreamRoutingConnectionOptions> upstreamRoutingConnectionOptions) {
            _logger = logger;
            _upstreamRouting = upstreamRouting;
            _upstreamRoutingConnectionOptions = upstreamRoutingConnectionOptions;
            _upstreamRouting.Connected += async (sender, args) => {
                Console.WriteLine("客户端连接成功");
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            var connectAsync = await _upstreamRouting.ConnectAsync(_upstreamRoutingConnectionOptions.Value, stoppingToken);
            if (!connectAsync) {
                _logger.LogError("上游路由连接失败!");
                Console.WriteLine("上游路由连接失败!");
            }
            else {
                _logger.LogError("上游路由连接成功");
                Console.WriteLine("上游路由连接成功");
            }

            while (!stoppingToken.IsCancellationRequested) {
                await Task.Delay(1000, stoppingToken);
            }
            //测试发消息
        }
    }
}
