using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WheelDiverterSorter.Core;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WheelDiverterSorter.Core.Models;
using WheelDiverterSorter.Core.Manager;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Host.Servers {

    public class UpstreamRoutingHostedService : BackgroundService {
        private readonly ILogger<UpstreamRoutingHostedService> _logger;
        private readonly IUpstreamRouting _upstreamRouting;
        private readonly IParcelManager _parcelManager;
        private readonly IOptions<UpstreamRoutingConnectionOptions> _upstreamRoutingConnectionOptions;

        public UpstreamRoutingHostedService(ILogger<UpstreamRoutingHostedService> logger,
            IUpstreamRouting upstreamRouting, IParcelManager parcelManager,
            IOptions<UpstreamRoutingConnectionOptions> upstreamRoutingConnectionOptions) {
            _logger = logger;
            _upstreamRouting = upstreamRouting;
            _parcelManager = parcelManager;
            _upstreamRoutingConnectionOptions = upstreamRoutingConnectionOptions;
            _upstreamRouting.Connected += async (sender, args) => {
                Console.WriteLine("客户端连接成功");
            };
            _parcelManager.ParcelCreated += async (sender, args) => {
                var sendCreateParcelAsync = await _upstreamRouting.SendCreateParcelAsync(new UpstreamCreateParcelRequest {
                    ParcelId = args.ParcelId,
                    DetectedAt = DateTimeOffset.Now
                });
                if (!sendCreateParcelAsync) {
                    _logger.LogError($"包裹Id:{args.Parcel},发送上游失败");
                }
            };
            _parcelManager.ParcelDropped += async (sender, args) => {
                await _upstreamRouting.SendDropToChuteAsync(new SortingCompletedMessage {
                    ParcelId = args.ParcelId,
                    ActualChuteId = args.ActualChuteId,
                    CompletedAt = DateTimeOffset.Now
                });
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
