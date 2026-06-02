using System;

namespace MOE.Common.Models.Repositories
{
    public class ControllerEventLogArchiveDiagnostics
    {
        public int TotalRecordsInRange { get; set; }
        public int DistinctSignalsInRange { get; set; }
        public DateTime? EarliestRecordInDatabase { get; set; }
        public DateTime? LatestRecordInDatabase { get; set; }
        public DateTime? ClosestRecordBeforeRange { get; set; }
        public DateTime? ClosestRecordAfterRange { get; set; }
    }
}
