﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace ATSPM.Application.Reports.Business.LeftTurnGapReport
{
    public class PedActuationResult
    {
        public int CyclesWithPedCallsNum { get; set; }
        public double CyclesWithPedCallsPercent { get; set; }
        public Dictionary<DateTime, double> PercentCyclesWithPedsList { get; set; }
        public string Direction { get; set; }
        public string OpposingDirection { get; set; }
    }
}