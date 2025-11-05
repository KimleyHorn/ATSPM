using System;
using System.Collections.Generic;
using System.Linq;
using MOE.Common.Business.WCFServiceLibrary;
using MOE.Common.Models;

namespace MOE.Common.Business.DataAggregation
{
    public class PhaseSplitMonitorAggregationBySignal : AggregationBySignal
    {

        public List<PhaseSplitMonitorAggregationByPhase> SplitMonitorAggregations { get; }
        public PhaseSplitMonitorAggregationBySignal(PhaseSplitMonitorAggregationOptions options, Models.ATSPM_Signals atspmSignals) : base(
            options, atspmSignals)
        {
            SplitMonitorAggregations = new List<PhaseSplitMonitorAggregationByPhase>();
            GetSplitMonitorAggregationContainersForAllPhases(options, atspmSignals);
            LoadBins(null, null);
        }

        public PhaseSplitMonitorAggregationBySignal(PhaseSplitMonitorAggregationOptions options, Models.ATSPM_Signals atspmSignals,
            int phaseNumber) : base(options, atspmSignals)
        {
            SplitMonitorAggregations = new List<PhaseSplitMonitorAggregationByPhase>();
            SplitMonitorAggregations.Add(new PhaseSplitMonitorAggregationByPhase(atspmSignals, phaseNumber, options, options.SelectedAggregatedDataType));
            LoadBins(null, null);
        }

        protected override void LoadBins(SignalAggregationMetricOptions options, Models.ATSPM_Signals atspmSignals)
        {
            for (var i = 0; i < BinsContainers.Count; i++)
            for (var binIndex = 0; binIndex < BinsContainers[i].Bins.Count; binIndex++)
            {
                var bin = BinsContainers[i].Bins[binIndex];
                foreach (var approachSplitFailAggregationContainer in SplitMonitorAggregations)
                {
                    bin.Sum += approachSplitFailAggregationContainer.BinsContainers[i].Bins[binIndex].Sum;
                    bin.Average = SplitMonitorAggregations.Count > 0 ? bin.Sum / SplitMonitorAggregations.Count : 0;
                    }
            }
        }

        protected override void LoadBins(ApproachAggregationMetricOptions options, Models.ATSPM_Signals atspmSignals)
        {
            for (var i = 0; i < BinsContainers.Count; i++)
            {
                for (var binIndex = 0; binIndex < BinsContainers[i].Bins.Count; binIndex++)
                {
                    var bin = BinsContainers[i].Bins[binIndex];
                    foreach (var approachSplitFailAggregationContainer in SplitMonitorAggregations)
                        bin.Sum += approachSplitFailAggregationContainer.BinsContainers[i].Bins[binIndex].Sum;
                    bin.Average = SplitMonitorAggregations.Count > 0 ? bin.Sum / SplitMonitorAggregations.Count : 0;
                }
            }
        }

        private void GetSplitMonitorAggregationContainersForAllPhases(
            PhaseSplitMonitorAggregationOptions options, Models.ATSPM_Signals atspmSignals)
        {
            List<int> availablePhases = GetAvailablePhasesForSignal(options, atspmSignals);
            foreach (var phaseNumber in availablePhases)
            {
                SplitMonitorAggregations.Add(
                    new PhaseSplitMonitorAggregationByPhase(atspmSignals, phaseNumber, options,
                        options.SelectedAggregatedDataType));
            }
        }

        private static List<int> GetAvailablePhasesForSignal(PhaseSplitMonitorAggregationOptions options, Models.ATSPM_Signals atspmSignals)
        {
            var phaseTerminationAggregationRepository =
                Models.Repositories.PhaseSplitMonitorAggregationRepositoryFactory.Create();
            var availablePhases =
                phaseTerminationAggregationRepository.GetAvailablePhaseNumbers(atspmSignals, options.StartDate,
                    options.EndDate);
            return availablePhases;
        }
        
    }
}