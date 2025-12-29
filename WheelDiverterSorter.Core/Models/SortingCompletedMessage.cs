using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Models {
    public class SortingCompletedMessage {
        public required long ParcelId { get; init; }
        public required long ActualChuteId { get; init; }
        public required DateTimeOffset CompletedAt { get; init; }
        public bool IsSuccess { get; init; } = true;
        public string? FailureReason { get; init; }
    }
}
