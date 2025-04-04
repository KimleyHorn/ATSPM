﻿using System;
using System.Collections.Generic;

#nullable disable

namespace ATSPM.Infrastructure.Repositories.EntityFramework.Repositories
{
    public partial class DetectorEventCountAggregation
    {
        public DateTime BinStartTime { get; set; }
        public string SignalId { get; set; }
        public int ApproachId { get; set; }
        public int DetectorPrimaryId { get; set; }
        public int EventCount { get; set; }
    }
}
