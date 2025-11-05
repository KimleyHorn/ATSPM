using System;
using System.Collections.Generic;
using System.Linq;
using MOE.Common.Business.WCFServiceLibrary;
using MOE.Common.Models;

namespace MOE.Common.Business.DataAggregation
{
    public class PhasePedAggregationBySignal : AggregationBySignal
    {

        public List<PhasePedAggregationByPhase> PedAggregations { get; }
        public PhasePedAggregationBySignal(PhasePedAggregationOptions options, Models.ATSPM_Signals atspmSignals) : base(
            options, atspmSignals)
        {
            PedAggregations = new List<PhasePedAggregationByPhase>();
            GetPhasePedAggregationContainersForAllPhases(options, atspmSignals);
            LoadBins(null, null);
        }

        public PhasePedAggregationBySignal(PhasePedAggregationOptions options, Models.ATSPM_Signals atspmSignals,
            int phaseNumber) : base(options, atspmSignals)
        {
            PedAggregations = new List<PhasePedAggregationByPhase>();
            PedAggregations.Add(new PhasePedAggregationByPhase(atspmSignals, phaseNumber, options, options.SelectedAggregatedDataType));
            LoadBins(null, null);
        }

        protected override void LoadBins(SignalAggregationMetricOptions options, Models.ATSPM_Signals atspmSignals)
        {
            for (var i = 0; i < BinsContainers.Count; i++)
            for (var binIndex = 0; binIndex < BinsContainers[i].Bins.Count; binIndex++)
            {
                var bin = BinsContainers[i].Bins[binIndex];
                foreach (var approachSplitFailAggregationContainer in PedAggregations)
                {
                    bin.Sum += approachSplitFailAggregationContainer.BinsContainers[i].Bins[binIndex].Sum;
                    bin.Average = PedAggregations.Count > 0 ? bin.Sum / PedAggregations.Count : 0;
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
                    foreach (var approachSplitFailAggregationContainer in PedAggregations)
                        bin.Sum += approachSplitFailAggregationContainer.BinsContainers[i].Bins[binIndex].Sum;
                    bin.Average = PedAggregations.Count > 0 ? bin.Sum / PedAggregations.Count : 0;
                }
            }
        }

        private void GetPhasePedAggregationContainersForAllPhases(
            PhasePedAggregationOptions options, Models.ATSPM_Signals atspmSignals)
        {
            List<int> availablePhases = GetAvailablePhasesForSignal(options, atspmSignals);
            foreach (var phaseNumber in availablePhases)
            {
                PedAggregations.Add(
                    new PhasePedAggregationByPhase(atspmSignals, phaseNumber, options, options.SelectedAggregatedDataType));
            }
        }

        private static List<int> GetAvailablePhasesForSignal(PhasePedAggregationOptions options, Models.ATSPM_Signals atspmSignals)
        {
            var phaseTerminationAggregationRepository =
                Models.Repositories.PhasePedAggregationRepositoryFactory.Create();
            var availablePhases =
                phaseTerminationAggregationRepository.GetAvailablePhaseNumbers(atspmSignals, options.StartDate,
                    options.EndDate);
            return availablePhases;
        }
        
    }
}