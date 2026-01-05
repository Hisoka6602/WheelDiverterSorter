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
using WheelDiverterSorter.Core.Utilities;

namespace WheelDiverterSorter.Host.Servers {

    public class UpstreamRoutingHostedService : BackgroundService {
        private readonly ILogger<UpstreamRoutingHostedService> _logger;
        private readonly IUpstreamRouting _upstreamRouting;
        private readonly IParcelManager _parcelManager;
        private readonly IOptions<UpstreamRoutingConnectionOptions> _upstreamRoutingConnectionOptions;
        private readonly string _instanceId = Guid.NewGuid().ToString("N");
        private int _connectCalled;

        public UpstreamRoutingHostedService(ILogger<UpstreamRoutingHostedService> logger,
            IUpstreamRouting upstreamRouting, IParcelManager parcelManager,
            IOptions<UpstreamRoutingConnectionOptions> upstreamRoutingConnectionOptions) {
            _logger = logger;
            _upstreamRouting = upstreamRouting;
            _parcelManager = parcelManager;
            _upstreamRoutingConnectionOptions = upstreamRoutingConnectionOptions;

            _upstreamRouting.Connected += (sender, args) => {
                var opt = _upstreamRouting.ConnectionOptions;
                _logger.LogInformation("[UpstreamRoutingHostedService] Connected event. Mode={Mode}, Endpoint={Endpoint}, Port={Port}, OccurredAtMs={OccurredAtMs}",
                    opt?.Mode, opt?.Endpoint, opt?.Port, args.OccurredAtMs);
            };

            _upstreamRouting.Disconnected += (sender, args) => {
                var opt = _upstreamRouting.ConnectionOptions;
                _logger.LogWarning("[UpstreamRoutingHostedService] Disconnected event. Mode={Mode}, Endpoint={Endpoint}, Port={Port}, OccurredAtMs={OccurredAtMs}",
                    opt?.Mode, opt?.Endpoint, opt?.Port, args.OccurredAtMs);
            };

            _upstreamRouting.Faulted += (sender, args) => {
                var opt = _upstreamRouting.ConnectionOptions;
                _logger.LogError(args.Exception, "[UpstreamRoutingHostedService] Faulted event. Mode={Mode}, Endpoint={Endpoint}, Port={Port}, OccurredAtMs={OccurredAtMs}",
                    opt?.Mode, opt?.Endpoint, opt?.Port, args.OccurredAtMs);
            };

            _parcelManager.ParcelCreated += async (sender, args) => {
                var sendCreateParcelAsync = await _upstreamRouting.SendCreateParcelAsync(new UpstreamCreateParcelRequest {
                    ParcelId = args.ParcelId,
                    DetectedAt = DateTimeOffset.Now
                });
                if (!sendCreateParcelAsync) {
                    var opt = _upstreamRouting.ConnectionOptions;
                    _logger.LogError("包裹Id:{Parcel},发送上游失败 | Status={Status} | Mode={Mode} | Endpoint={Endpoint}:{Port}",
                        args.Parcel,
                        _upstreamRouting.Status,
                        opt?.Mode,
                        opt?.Endpoint,
                        opt?.Port);
                }
            };
            _parcelManager.ParcelDropped += async (sender, args) => {
                var ok = await _upstreamRouting.SendDropToChuteAsync(new SortingCompletedMessage {
                    ParcelId = args.ParcelId,
                    ActualChuteId = args.ActualChuteId,
                    CompletedAt = DateTimeOffset.Now
                });
                if (!ok) {
                    var opt = _upstreamRouting.ConnectionOptions;
                    _logger.LogError("包裹Id:{ParcelId},落格上报失败 | Status={Status} | Mode={Mode} | Endpoint={Endpoint}:{Port}",
                        args.ParcelId,
                        _upstreamRouting.Status,
                        opt?.Mode,
                        opt?.Endpoint,
                        opt?.Port);
                }
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            _logger.LogInformation("[UpstreamRoutingHostedService] ExecuteAsync started. InstanceId={InstanceId}", _instanceId);

            await EnvironmentHelper.DelayAfterBootAsync(TimeSpan.FromSeconds(15), stoppingToken);

            var called = Interlocked.Increment(ref _connectCalled);
            _logger.LogInformation("[UpstreamRoutingHostedService] ConnectAsync invoking. InstanceId={InstanceId}, CallNo={CallNo}", _instanceId, called);

            var ok = await _upstreamRouting.ConnectAsync(_upstreamRoutingConnectionOptions.Value, stoppingToken);

            if (!ok) {
                _logger.LogError("上游路由连接失败! InstanceId={InstanceId}", _instanceId);
            }
            else {
                _logger.LogInformation("上游路由连接成功 InstanceId={InstanceId}", _instanceId);
            }

            while (!stoppingToken.IsCancellationRequested) {
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
