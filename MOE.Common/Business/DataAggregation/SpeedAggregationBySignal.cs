using System;
using System.Collections.Generic;
using System.Linq;
using MOE.Common.Business.WCFServiceLibrary;
using MOE.Common.Models;

namespace MOE.Common.Business.DataAggregation
{
    public class SpeedAggregationBySignal : AggregationBySignal
    {
        public SpeedAggregationBySignal(ApproachSpeedAggregationOptions options, Models.ATSPM_Signals atspmSignals) : base(
            options, atspmSignals)
        {
            ApproachSpeedEvents = new List<SpeedAggregationByApproach>();
            GetApproachSpeedEventsAggregationContainersForAllApporaches(options, atspmSignals);
            LoadBins(null, null);
        }

        public SpeedAggregationBySignal(ApproachSpeedAggregationOptions options, Models.ATSPM_Signals atspmSignals,
            int phaseNumber) : base(options, atspmSignals)
        {
            ApproachSpeedEvents = new List<SpeedAggregationByApproach>();
            foreach (var approach in atspmSignals.Approaches)
                if (approach.ProtectedPhaseNumber == phaseNumber)
                {
                    ApproachSpeedEvents.Add(
                        new SpeedAggregationByApproach(approach, options, options.StartDate,
                            options.EndDate,
                            true, options.SelectedAggregatedDataType));
                    if (approach.PermissivePhaseNumber != null && approach.PermissivePhaseNumber == phaseNumber)
                        ApproachSpeedEvents.Add(
                            new SpeedAggregationByApproach(approach, options, options.StartDate,
                                options.EndDate,
                                false, options.SelectedAggregatedDataType));
                }
            LoadBins(null, null);
        }

        public SpeedAggregationBySignal(ApproachSpeedAggregationOptions options, Models.ATSPM_Signals atspmSignals,
            DirectionType direction) : base(options, atspmSignals)
        {
            ApproachSpeedEvents = new List<SpeedAggregationByApproach>();
            foreach (var approach in atspmSignals.Approaches)
                if (approach.DirectionType.DirectionTypeID == direction.DirectionTypeID)
                {
                    ApproachSpeedEvents.Add(
                        new SpeedAggregationByApproach(approach, options, options.StartDate,
                            options.EndDate,
                            true, options.SelectedAggregatedDataType));
                    if (approach.PermissivePhaseNumber != null)
                        ApproachSpeedEvents.Add(
                            new SpeedAggregationByApproach(approach, options, options.StartDate,
                                options.EndDate,
                                false, options.SelectedAggregatedDataType));
                }
            LoadBins(null, null);
        }

        public List<SpeedAggregationByApproach> ApproachSpeedEvents { get; }

        protected override void LoadBins(SignalAggregationMetricOptions options, Models.ATSPM_Signals atspmSignals)
        {
            for (var i = 0; i < BinsContainers.Count; i++)
            for (var binIndex = 0; binIndex < BinsContainers[i].Bins.Count; binIndex++)
            {
                var bin = BinsContainers[i].Bins[binIndex];
                foreach (var speedAggregationByApproach in ApproachSpeedEvents)
                    bin.Sum += speedAggregationByApproach.BinsContainers[i].Bins[binIndex].Sum;
                bin.Average = ApproachSpeedEvents.Count > 0 ? bin.Sum / ApproachSpeedEvents.Count : 0;
            }
        }

        protected override void LoadBins(ApproachAggregationMetricOptions options, Models.ATSPM_Signals atspmSignals)
        {
            for (var i = 0; i < BinsContainers.Count; i++)
            for (var binIndex = 0; binIndex < BinsContainers[i].Bins.Count; binIndex++)
            {
                var bin = BinsContainers[i].Bins[binIndex];
                foreach (var speedAggregationByApproach in ApproachSpeedEvents)
                    bin.Sum += speedAggregationByApproach.BinsContainers[i].Bins[binIndex].Sum;
                bin.Average = ApproachSpeedEvents.Count > 0 ? bin.Sum / ApproachSpeedEvents.Count : 0;
            }
        }

        private void GetApproachSpeedEventsAggregationContainersForAllApporaches(
            ApproachSpeedAggregationOptions options, Models.ATSPM_Signals atspmSignals)
        {
            foreach (var approach in atspmSignals.Approaches)
            {
                ApproachSpeedEvents.Add(
                    new SpeedAggregationByApproach(approach, options, options.StartDate,
                        options.EndDate,
                        true, options.SelectedAggregatedDataType));
                if (approach.PermissivePhaseNumber != null)
                    ApproachSpeedEvents.Add(
                        new SpeedAggregationByApproach(approach, options, options.StartDate,
                            options.EndDate,
                            false, options.SelectedAggregatedDataType));
            }
        }


        public double GetSpeedEventssByDirection(DirectionType direction)
        {
            double speedEvents = 0;
            if (ApproachSpeedEvents != null)
                speedEvents = ApproachSpeedEvents
                    .Where(a => a.Approach.DirectionType.DirectionTypeID == direction.DirectionTypeID)
                    .Sum(a => a.BinsContainers.FirstOrDefault().SumValue);
            return speedEvents;
        }

        public int GetAverageSpeedEventssByDirection(DirectionType direction)
        {
            var approachSpeedsbyDirection = ApproachSpeedEvents
                .Where(a => a.Approach.DirectionType.DirectionTypeID == direction.DirectionTypeID);
            var speedEvents = 0;
            if (approachSpeedsbyDirection.Any())
                speedEvents = Convert.ToInt32(Math.Round(approachSpeedsbyDirection
                    .Average(a => a.BinsContainers.FirstOrDefault().SumValue)));
            return speedEvents;
        }
    }
}