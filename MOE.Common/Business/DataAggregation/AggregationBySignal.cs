using System;
using System.Collections.Generic;
using System.Linq;
using MOE.Common.Business.Bins;
using MOE.Common.Business.WCFServiceLibrary;
using MOE.Common.Models;

namespace MOE.Common.Business.DataAggregation
{
    public abstract class AggregationBySignal
    {

        public Models.ATSPM_Signals AtspmSignals { get; }

        public double Total
        {
            get { return BinsContainers.Sum(c => c.SumValue); }
        }

        public List<BinsContainer> BinsContainers { get; protected set; }
        public List<SignalEventCountAggregation> SignalEventCountAggregations { get; set; }

        public int Average
        {
            get
            {
                if (BinsContainers.Count > 1)
                    return Convert.ToInt32(Math.Round(BinsContainers.Average(b => b.SumValue)));
                double numberOfBins = 0;
                foreach (var binsContainer in BinsContainers)
                    numberOfBins += binsContainer.Bins.Count;
                return numberOfBins > 0 ? Convert.ToInt32(Math.Round(Total / numberOfBins)) : 0;
            }
        }        

        public AggregationBySignal(SignalAggregationMetricOptions options, Models.ATSPM_Signals atspmSignals)
        {
            BinsContainers = BinFactory.GetBins(options.TimeOptions);
            AtspmSignals = atspmSignals;
        }

        protected abstract void LoadBins(SignalAggregationMetricOptions options, Models.ATSPM_Signals atspmSignals);

        protected abstract void LoadBins(ApproachAggregationMetricOptions options, Models.ATSPM_Signals atspmSignals);
    }
}