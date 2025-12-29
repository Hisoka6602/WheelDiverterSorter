using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;
using WheelDiverterSorter.Core.Events;
using WheelDiverterSorter.Core.Manager;

namespace WheelDiverterSorter.Execution {
    public class SystemStateManager : ISystemStateManager {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private int _isDisposed;

        public SystemState CurrentState { get; private set; } = SystemState.Ready;

        public event EventHandler<StateChangeEventArgs>? StateChanged;

        public void Dispose() {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 1) {
                return;
            }

            try {
                _gate.Dispose();
            }
            catch {
                // 释放阶段忽略异常
            }
        }

        public async Task<bool> ChangeStateAsync(SystemState targetState, CancellationToken cancellationToken = default) {
            if (Volatile.Read(ref _isDisposed) == 1) {
                return false;
            }

            if (CurrentState == targetState) {
                return true;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                if (Volatile.Read(ref _isDisposed) == 1) {
                    return false;
                }

                var current = CurrentState;

                if (current == targetState) {
                    return true;
                }

                if (!IsTransitionAllowed(current, targetState)) {
                    return false;
                }

                CurrentState = targetState;

                RaiseStateChanged(current, targetState);
                return true;
            }
            catch (OperationCanceledException) {
                return false;
            }
            catch {
                // 状态切换属于控制面逻辑，异常需要隔离，避免影响调用链
                return false;
            }
            finally {
                _gate.Release();
            }
        }

        private static bool IsTransitionAllowed(SystemState current, SystemState target) {
            // 规则：
            // 1) 当前状态是急停，只能改成 Ready
            // 2) 当前状态是故障，只能改成 Booting
            // 3) 其他状态可以任意转换

            if (current == SystemState.EmergencyStop) {
                return target == SystemState.Ready;
            }

            if (current == SystemState.Faulted) {
                return target == SystemState.Booting;
            }

            return true;
        }

        private void RaiseStateChanged(SystemState oldState, SystemState newState) {
            try {
                StateChanged?.Invoke(this, new StateChangeEventArgs {
                    OldState = oldState,
                    NewState = newState,
                    ChangedAt = DateTimeOffset.Now
                });
            }
            catch {
                // 事件回调异常必须隔离
            }
        }
    }
}
