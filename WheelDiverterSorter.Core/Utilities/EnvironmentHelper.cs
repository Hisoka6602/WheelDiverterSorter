using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Utilities {

    public static class EnvironmentHelper {

        public static ValueTask DelayAfterBootAsync(TimeSpan minUptime, CancellationToken cancellationToken) {
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
