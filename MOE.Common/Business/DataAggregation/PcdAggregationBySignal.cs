using System;
using System.Collections.Generic;
using System.Linq;
using MOE.Common.Business.WCFServiceLibrary;
using MOE.Common.Models;

namespace MOE.Common.Business.DataAggregation
{
    public class PcdAggregationBySignal : AggregationBySignal
    {
        public PcdAggregationBySignal(ApproachPcdAggregationOptions options, Models.ATSPM_Signals atspmSignals) : base(
            options, atspmSignals)
        {
            ApproachPcds = new List<PcdAggregationByApproach>();
            GetApproachPcdAggregationContainersForAllApporaches(options, atspmSignals);
            LoadBins(null, null);
        }

        public PcdAggregationBySignal(ApproachPcdAggregationOptions options, Models.ATSPM_Signals atspmSignals,
            int phaseNumber) : base(options, atspmSignals)
        {
            ApproachPcds = new List<PcdAggregationByApproach>();
            foreach (var approach in atspmSignals.Approaches)
                if (approach.ProtectedPhaseNumber == phaseNumber)
                {
                    ApproachPcds.Add(
                        new PcdAggregationByApproach(approach, options, options.StartDate,
                            options.EndDate,
                            true, options.SelectedAggregatedDataType));
                    if (approach.PermissivePhaseNumber != null && approach.PermissivePhaseNumber == phaseNumber)
                        ApproachPcds.Add(
                            new PcdAggregationByApproach(approach, options, options.StartDate,
                                options.EndDate,
                                false, options.SelectedAggregatedDataType));
                }
            LoadBins(null, null);
        }

        public PcdAggregationBySignal(ApproachPcdAggregationOptions options, Models.ATSPM_Signals atspmSignals,
            DirectionType direction) : base(options, atspmSignals)
        {
            ApproachPcds = new List<PcdAggregationByApproach>();
            foreach (var approach in atspmSignals.Approaches)
                if (approach.DirectionType.DirectionTypeID == direction.DirectionTypeID)
                {
                    ApproachPcds.Add(
                        new PcdAggregationByApproach(approach, options, options.StartDate,
                            options.EndDate,
                            true, options.SelectedAggregatedDataType));
                    if (approach.PermissivePhaseNumber != null)
                        ApproachPcds.Add(
                            new PcdAggregationByApproach(approach, options, options.StartDate,
                                options.EndDate,
                                false, options.SelectedAggregatedDataType));
                }
            LoadBins(null, null);
        }

        public List<PcdAggregationByApproach> ApproachPcds { get; }

        protected override void LoadBins(SignalAggregationMetricOptions options, Models.ATSPM_Signals atspmSignals)
        {
            for (var i = 0; i < BinsContainers.Count; i++)
            {
                for (var binIndex = 0; binIndex < BinsContainers[i].Bins.Count; binIndex++)
                {
                    var bin = BinsContainers[i].Bins[binIndex];
                    foreach (var approachPcdAggregationContainer in ApproachPcds)
                        bin.Sum += approachPcdAggregationContainer.BinsContainers[i].Bins[binIndex].Sum;
                    bin.Average = ApproachPcds.Count > 0 ? bin.Sum / ApproachPcds.Count : 0;
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
                    foreach (var approachPcdAggregationContainer in ApproachPcds)
                        bin.Sum += approachPcdAggregationContainer.BinsContainers[i].Bins[binIndex].Sum;
                    bin.Average = ApproachPcds.Count > 0 ? bin.Sum / ApproachPcds.Count : 0;
                }
            }
        }


        private void GetApproachPcdAggregationContainersForAllApporaches(
            ApproachPcdAggregationOptions options, Models.ATSPM_Signals atspmSignals)
        {
            foreach (var approach in atspmSignals.Approaches)
            {
                ApproachPcds.Add(
                    new PcdAggregationByApproach(approach, options, options.StartDate,
                        options.EndDate,
                        true, options.SelectedAggregatedDataType));
                if (approach.PermissivePhaseNumber != null)
                    ApproachPcds.Add(
                        new PcdAggregationByApproach(approach, options, options.StartDate,
                            options.EndDate,
                            false, options.SelectedAggregatedDataType));
            }
        }


        public double GetPcdsByDirection(DirectionType direction)
        {
            double splitFails = 0;
            if (ApproachPcds != null)
                splitFails = ApproachPcds
                    .Where(a => a.Approach.DirectionType.DirectionTypeID == direction.DirectionTypeID)
                    .Sum(a => a.BinsContainers.FirstOrDefault().SumValue);
            return splitFails;
        }

        public int GetAveragePcdsByDirection(DirectionType direction)
        {
            var approachPcduresByDirection = ApproachPcds
                .Where(a => a.Approach.DirectionType.DirectionTypeID == direction.DirectionTypeID);
            var splitFails = 0;
            if (approachPcduresByDirection.Any())
                splitFails = Convert.ToInt32(Math.Round(approachPcduresByDirection
                    .Average(a => a.BinsContainers.FirstOrDefault().SumValue)));
            return splitFails;
        }
    }
}