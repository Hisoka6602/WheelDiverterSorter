using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Options {
    public record class LogCleanupSettings {
        public bool Enabled { get; set; } = true;
        public int RetentionDays { get; set; } = 2;
        public int CheckIntervalHours { get; set; } = 1;
        public string LogDirectory { get; set; } = "logs";
    }
}
