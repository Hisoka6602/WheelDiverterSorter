using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WheelDiverterSorter.Core;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Manager;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Host.Servers {

    public class SpacingPauseHostedService : BackgroundService {
        private readonly ILogger<IoLinkageHostedService> _logger;
        private readonly ISystemStateManager _systemStateManager;
        private readonly IEmcController _emcController;
        private readonly ISensorManager _sensorManager;
        private readonly IOptions<SpacingPauseOptions> _spacingPauseOptions;
        private WeighingStationParcelState _weighingStationParcelState = new();

        public SpacingPauseHostedService(ILogger<IoLinkageHostedService> logger,
            ISystemStateManager systemStateManager,
            IEmcController emcController,
            ISensorManager sensorManager,
            IOptions<SpacingPauseOptions> spacingPauseOptions) {
            _logger = logger;
            _systemStateManager = systemStateManager;
            _emcController = emcController;
            _sensorManager = sensorManager;
            _spacingPauseOptions = spacingPauseOptions;

            _sensorManager.SensorStateChanged += async (sender, args) => {
                await Task.Yield();
                if (_spacingPauseOptions.Value.IsSpacingPauseEnabled
                    && _systemStateManager.CurrentState == SystemState.Running
                    && args.SensorType == IoPointType.SpacingPausePoint) {
                    //停止IO集合
                    if (_weighingStationParcelState.IsParcelPresentOnWeighingStation) {
                        foreach (var spacingIoTrigger in _spacingPauseOptions.Value.SpacingIoTriggers) {
                            await _emcController.WriteIoAsync(spacingIoTrigger.Point, spacingIoTrigger.TriggerState);

                            await Task.Delay(20);
                        }
                    }
                }
                if (_spacingPauseOptions.Value.IsSpacingPauseEnabled
                    && _systemStateManager.CurrentState == SystemState.Running
                    && args.SensorType == IoPointType.WeighingStationEntryPoint) {
                    //判断包裹(上升沿)
                    //包裹进入称重台
                    _weighingStationParcelState.ParcelEnteredWeighingStationAt = DateTime.Now;
                    _weighingStationParcelState.IsParcelPresentOnWeighingStation = true;
                }
                if (_spacingPauseOptions.Value.IsSpacingPauseEnabled
                    && _systemStateManager.CurrentState == SystemState.Running
                    && args.SensorType == IoPointType.WeighingStationExitPoint) {
                    //判断包裹(下降沿)
                    //包裹离开称重台
                    if (_weighingStationParcelState.IsParcelPresentOnWeighingStation) {
                        foreach (var spacingIoTrigger in _spacingPauseOptions.Value.SpacingIoTriggers) {
                            await _emcController.WriteIoAsync(spacingIoTrigger.Point, spacingIoTrigger.TriggerState == IoState.Low ? IoState.High : IoState.Low);

                            await Task.Delay(20);
                        }
                    }

                    _weighingStationParcelState.ParcelExitedWeighingStationAt = DateTime.Now;
                    _weighingStationParcelState.IsParcelPresentOnWeighingStation = false;
                }
            };
            _emcController.StatusChanged += (sender, args) => {
                if (args.Status == EmcControllerStatus.Disconnected) {
                    _weighingStationParcelState.IsParcelPresentOnWeighingStation = false;
                }
            };
            _systemStateManager.StateChanged += (sender, args) => {
                if (args.NewState != SystemState.Running) {
                    _weighingStationParcelState.IsParcelPresentOnWeighingStation = false;
                }
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            while (!stoppingToken.IsCancellationRequested) {
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    /// <summary>
    /// 称重台包裹状态（用于记录进入/离开时间与在位状态）
    /// </summary>
    public sealed record class WeighingStationParcelState {
        /// <summary>
        /// 包裹进入称重台时间（未进入时为 null）
        /// </summary>
        public DateTime? ParcelEnteredWeighingStationAt { get; set; }

        /// <summary>
        /// 称重台是否存在包裹
        /// </summary>
        public bool IsParcelPresentOnWeighingStation { get; set; }

        /// <summary>
        /// 包裹离开称重台时间（未离开时为 null）
        /// </summary>
        public DateTime? ParcelExitedWeighingStationAt { get; set; }
    }
}
