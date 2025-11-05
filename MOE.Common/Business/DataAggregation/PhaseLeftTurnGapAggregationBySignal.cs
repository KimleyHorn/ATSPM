using System;
using System.Collections.Generic;
using System.Linq;
using MOE.Common.Business.Bins;
using MOE.Common.Business.WCFServiceLibrary;
using MOE.Common.Models;

namespace MOE.Common.Business.DataAggregation
{
    public class PhaseLeftTurnGapAggregationBySignal : AggregationBySignal
    {
        public List<PhaseLeftTurnGapAggregationByApproach> ApproachLeftTurnGaps { get; }

        public PhaseLeftTurnGapAggregationBySignal(PhaseLeftTurnGapAggregationOptions options, Models.ATSPM_Signals atspmSignals) : base(
            options, atspmSignals)
        {
            ApproachLeftTurnGaps = new List<PhaseLeftTurnGapAggregationByApproach>();
            GetApproachLeftTurnGapAggregationContainersForAllApporaches(options, atspmSignals);
            LoadBins(null, null);
        }


        public PhaseLeftTurnGapAggregationBySignal(PhaseLeftTurnGapAggregationOptions options, Models.ATSPM_Signals atspmSignals,
            int phaseNumber) : base(options, atspmSignals)
        {
            ApproachLeftTurnGaps = new List<PhaseLeftTurnGapAggregationByApproach>();
            foreach (var approach in atspmSignals.Approaches)
                if (approach.ProtectedPhaseNumber == phaseNumber)
                {
                    ApproachLeftTurnGaps.Add(
                        new PhaseLeftTurnGapAggregationByApproach(approach, options, options.StartDate,
                            options.EndDate,
                            true, options.SelectedAggregatedDataType));
                    if (approach.PermissivePhaseNumber != null && approach.PermissivePhaseNumber == phaseNumber)
                        ApproachLeftTurnGaps.Add(
                            new PhaseLeftTurnGapAggregationByApproach(approach, options, options.StartDate,
                                options.EndDate,
                                false, options.SelectedAggregatedDataType));
                }
            LoadBins(null, null);
        }

        public PhaseLeftTurnGapAggregationBySignal(PhaseLeftTurnGapAggregationOptions options, Models.ATSPM_Signals atspmSignals,
            DirectionType direction) : base(options, atspmSignals)
        {
            ApproachLeftTurnGaps = new List<PhaseLeftTurnGapAggregationByApproach>();
            foreach (var approach in atspmSignals.Approaches)
                if (approach.DirectionType.DirectionTypeID == direction.DirectionTypeID)
                {
                    ApproachLeftTurnGaps.Add(
                        new PhaseLeftTurnGapAggregationByApproach(approach, options, options.StartDate,
                            options.EndDate,
                            true, options.SelectedAggregatedDataType));
                    if (approach.PermissivePhaseNumber != null)
                        ApproachLeftTurnGaps.Add(
                            new PhaseLeftTurnGapAggregationByApproach(approach, options, options.StartDate,
                                options.EndDate,
                                false, options.SelectedAggregatedDataType));
                }
            LoadBins(null, null);
        }

        protected override void LoadBins(SignalAggregationMetricOptions options, Models.ATSPM_Signals atspmSignals)
        {
           
            for (var i = 0; i < BinsContainers.Count; i++)
            for (var binIndex = 0; binIndex < BinsContainers[i].Bins.Count; binIndex++)
            {
                var bin = BinsContainers[i].Bins[binIndex];
                foreach (var approachLeftTurnGapAggregationContainer in ApproachLeftTurnGaps)
                {
                    bin.Sum += approachLeftTurnGapAggregationContainer.BinsContainers[i].Bins[binIndex].Sum;
                    bin.Average = ApproachLeftTurnGaps.Count > 0 ? bin.Sum / ApproachLeftTurnGaps.Count : 0;
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
                    foreach (var approachLeftTurnGapAggregationContainer in ApproachLeftTurnGaps)
                        bin.Sum += approachLeftTurnGapAggregationContainer.BinsContainers[i].Bins[binIndex].Sum;
                    bin.Average = ApproachLeftTurnGaps.Count > 0 ? bin.Sum / ApproachLeftTurnGaps.Count : 0;
                }
            }
        }


        private void GetApproachLeftTurnGapAggregationContainersForAllApporaches(
            PhaseLeftTurnGapAggregationOptions options, Models.ATSPM_Signals atspmSignals)
        {
            foreach (var approach in atspmSignals.Approaches)
            {
                ApproachLeftTurnGaps.Add(
                    new PhaseLeftTurnGapAggregationByApproach(approach, options, options.StartDate,
                        options.EndDate,
                        true, options.SelectedAggregatedDataType));
                if (approach.PermissivePhaseNumber != null)
                    ApproachLeftTurnGaps.Add(
                        new PhaseLeftTurnGapAggregationByApproach(approach, options, options.StartDate,
                            options.EndDate,
                            false, options.SelectedAggregatedDataType));
            }
        }


        public double GetLeftTurnGapByDirection(DirectionType direction)
        {
            double splitFails = 0;
            if (ApproachLeftTurnGaps != null)
                splitFails = ApproachLeftTurnGaps
                    .Where(a => a.Approach.DirectionType.DirectionTypeID == direction.DirectionTypeID)
                    .Sum(a => a.BinsContainers.FirstOrDefault().SumValue);
            return splitFails;
        }

        public int GetAverageGapByDirection(DirectionType direction)
        {
            var approachLeftTurnGapByDirection = ApproachLeftTurnGaps
                .Where(a => a.Approach.DirectionType.DirectionTypeID == direction.DirectionTypeID);
            var splitFails = 0;
            if (approachLeftTurnGapByDirection.Any())
                splitFails = Convert.ToInt32(Math.Round(approachLeftTurnGapByDirection
                    .Average(a => a.BinsContainers.FirstOrDefault().SumValue)));
            return splitFails;
        }
    }
}