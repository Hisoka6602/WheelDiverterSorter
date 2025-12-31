using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WheelDiverterSorter.Core;
using System.Threading.Channels;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Models;
using WheelDiverterSorter.Core.Manager;
using WheelDiverterSorter.Core.Options;
using Microsoft.Extensions.Configuration;

namespace WheelDiverterSorter.Host.Servers {

    /// <summary>
    /// 联动IO服务
    /// </summary>
    public sealed class IoLinkageHostedService : BackgroundService {
        private readonly ILogger<IoLinkageHostedService> _logger;
        private readonly ISystemStateManager _systemStateManager;
        private readonly IEmcController _emcController;

        private readonly IReadOnlyList<IoLinkagePointOptions> _ioLinkagePointOptionsInfos;

        // 仅用于投递系统状态变化，单读多写
        private readonly Channel<SystemState> _stateChannel =
            Channel.CreateUnbounded<SystemState>(new UnboundedChannelOptions {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = true
            });

        // 仅在 ExecuteAsync 单线程访问，使用 Dictionary 性能更好
        private readonly Dictionary<int, IoExecutionPlan> _plans = new();

        // 用于尽量取消急停/暂停时正在进行的 IO 写入
        private CancellationTokenSource _runCts = new();

        private EventHandler<StateChangeEventArgs>? _stateChangedHandler;

        public IoLinkageHostedService(
            ILogger<IoLinkageHostedService> logger,
            ISystemStateManager systemStateManager,
            IEmcController emcController,
            IOptions<List<IoLinkagePointOptions>> ioLinkagePointOptionsInfos) {
            _logger = logger;
            _systemStateManager = systemStateManager;
            _emcController = emcController;

            _ioLinkagePointOptionsInfos =
                ioLinkagePointOptionsInfos.Value;
            _stateChangedHandler = (o, args) => {
                try {
                    _ = _stateChannel.Writer.TryWrite(args.NewState);
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "系统状态变更投递失败");
                }
            };

            _systemStateManager.StateChanged += _stateChangedHandler;
        }

        public override Task StopAsync(CancellationToken cancellationToken) {
            try {
                if (_stateChangedHandler != null) {
                    _systemStateManager.StateChanged -= _stateChangedHandler;
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "卸载系统状态事件订阅失败");
            }

            try {
                _runCts.Cancel();
                _runCts.Dispose();
            }
            catch (Exception ex) {
                _logger.LogError(ex, "停止运行令牌失败");
            }

            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));

            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken)) {
                // 1) 串行处理所有状态变化，避免多线程直接改共享数据
                while (_stateChannel.Reader.TryRead(out var newState)) {
                    HandleSystemStateChanged(newState);
                }

                if (_plans.Count == 0) {
                    continue;
                }

                var now = DateTime.Now;

                // 2) 推进计划状态（复制键列表以支持删除）
                //    IO 点数量通常很小，此处 ToArray 分配可接受；如需极致，可改为复用 List<int>
                foreach (var kv in _plans.ToArray()) {
                    var point = kv.Key;
                    var plan = kv.Value;

                    if (plan.Status == IoExecutionPlanStatus.Pending && plan.ExecuteTime <= now) {
                        if (_runCts.IsCancellationRequested) {
                            continue;
                        }

                        try {
                            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _runCts.Token);
                            await _emcController.WriteIoAsync(point, plan.TriggerState, linkedCts.Token);

                            plan.Status = plan.StopTime == null
                                ? IoExecutionPlanStatus.Completed
                                : IoExecutionPlanStatus.Executing;
                        }
                        catch (OperationCanceledException) {
                            // 取消属于正常控制流
                        }
                        catch (Exception ex) {
                            _logger.LogError(ex, "写入IO失败：Point={Point}，State={State}", point, plan.TriggerState);
                            // 隔离异常，避免后台服务退出
                            plan.Status = IoExecutionPlanStatus.Completed;
                        }
                    }

                    if (plan is { Status: IoExecutionPlanStatus.Executing, StopTime: not null }
                        && plan.StopTime <= now) {
                        if (_runCts.IsCancellationRequested) {
                            plan.Status = IoExecutionPlanStatus.Completed;
                        }
                        else {
                            try {
                                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _runCts.Token);
                                await _emcController.WriteIoAsync(point, plan.ReverseState, linkedCts.Token);

                                plan.Status = IoExecutionPlanStatus.Completed;
                            }
                            catch (OperationCanceledException) {
                                plan.Status = IoExecutionPlanStatus.Completed;
                            }
                            catch (Exception ex) {
                                _logger.LogError(ex, "回写IO失败：Point={Point}，State={State}", point, plan.ReverseState);
                                plan.Status = IoExecutionPlanStatus.Completed;
                            }
                        }
                    }

                    if (plan.Status == IoExecutionPlanStatus.Completed) {
                        _plans.Remove(point);
                    }
                }
            }
        }

        private void HandleSystemStateChanged(SystemState newState) {
            if (newState is SystemState.EmergencyStop or SystemState.Paused) {
                // 急停/暂停：取消正在进行的 IO 操作，并清空待执行计划
                try {
                    _runCts.Cancel();
                    _runCts.Dispose();
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "取消运行令牌失败");
                }

                _runCts = new CancellationTokenSource();
                _plans.Clear();
            }

            var now = DateTime.Now;

            foreach (var options in _ioLinkagePointOptionsInfos) {
                if (options.RelatedSystemState != newState) {
                    continue;
                }

                if (_plans.TryGetValue(options.Point, out var existing)
                    && existing.Status is IoExecutionPlanStatus.Pending or IoExecutionPlanStatus.Executing) {
                    continue;
                }

                _plans[options.Point] = new IoExecutionPlan {
                    Point = options.Point,
                    TriggerState = options.TriggerState,
                    ReverseState = options.TriggerState == IoState.Low ? IoState.High : IoState.Low,
                    ExecuteTime = now.AddMilliseconds(options.DelayMs),
                    StopTime = options.DurationMs == 0
                        ? null
                        : now.AddMilliseconds(options.DelayMs + options.DurationMs),
                    Status = IoExecutionPlanStatus.Pending
                };
            }
        }
    }
}
