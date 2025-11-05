using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using MOE.Common.Models.Repositories;

namespace MOE.Common.Models
{
    public partial class ATSPM_Signals
    {
        [NotMapped]
        public string SignalDescription => SignalID + " - " + PrimaryName + " " + SecondaryName;

        [NotMapped]
        public List<Controller_Event_Log> PlanEvents { get; set; }

        [NotMapped]
        public List<ATSPM_Signals> VersionList { get; set; }

        [NotMapped]
        public DateTime FirstDate => Convert.ToDateTime("1/1/2011");

        [NotMapped]
        public string SelectListName
        {
            get
            {
                return Start.ToShortDateString() + " - " + Note;
            }
        }

        public void SetPlanEvents(DateTime startTime, DateTime endTime)
        {
            var repository =
                ControllerEventLogRepositoryFactory.Create();
            PlanEvents = repository.GetSignalEventsByEventCode(SignalID, startTime, endTime, 131);
        }


        //public List<Models.Lane> GetLaneGroupsForSignal()
        //{
        //    List<Models.Lane> laneGroups = new List<Lane>();
        //    foreach (Models.Approach a in this.RouteSignals)
        //    {
        //        foreach (Models.Lane lg in a.Lanes)
        //        {
        //            laneGroups.Add(lg);
        //        }
        //    }
        //    return laneGroups;
        //    }

        public string GetMetricTypesString()
        {
            var metricTypesString = string.Empty;
            foreach (var metric in GetAvailableMetrics())
                metricTypesString += metric.MetricID + ",";

            if (!string.IsNullOrEmpty(metricTypesString))
                metricTypesString = metricTypesString.TrimEnd(',');
            return metricTypesString;
        }

        public string GetAreasString()
        {
            var areasString = ",";
            foreach (var area in GetAreas())
                areasString += area.Id + ",";

            return areasString;
        }

        public List<int> GetPhasesForSignal()
        {
            if (this.Approaches == null)
            {
                var approachRepository = ApproachRepositoryFactory.Create();
                this.Approaches = approachRepository.GetApproachesForSignal(this.VersionID);
            }
            var phases = new List<int>();
            foreach (var a in Approaches)
            {
                if (a.PermissivePhaseNumber != null)
                    phases.Add(a.PermissivePhaseNumber.Value);
                phases.Add(a.ProtectedPhaseNumber);
            }
            return phases.Select(p => p).Distinct().ToList();
        }

        public string GetSignalLocation()
        {
            return PrimaryName + " @ " + SecondaryName;
        }


        public List<Detector> GetDetectorsForSignal()
        {
            var detectors = new List<Detector>();
            if (Approaches != null)
            {
                foreach (var a in Approaches.OrderBy(a => a.ProtectedPhaseNumber))
                foreach (var d in a.Detectors)
                    detectors.Add(d);
            }

            return detectors.OrderBy(d => d.DetectorID).ToList();
        }


        public List<Detector> GetDetectorsForSignalThatSupportAMetric(int MetricTypeID)
        {
            var gdr =
                DetectorRepositoryFactory.Create();
            var detectors = new List<Detector>();
            foreach (var d in GetDetectorsForSignal())
                if (gdr.CheckReportAvialbility(d.DetectorID, MetricTypeID))
                    detectors.Add(d);
            return detectors;
        }

        public Detector GetDetectorForSignalByChannel(int detectorChannel)
        {
            Detector returnDet = null;


            foreach (var a in Approaches)
                if (a.Detectors.Count > 0)
                    foreach (var det in a.Detectors)
                        if (det.DetChannel == detectorChannel)
                            returnDet = det;

            return returnDet;
        }

        public bool CheckReportAvailabilityForSignal(int MetricTypeID)
        {
            var gdr =
                DetectorRepositoryFactory.Create();
            var detectors = new List<Detector>();
            foreach (var d in GetDetectorsForSignal())
                if (gdr.CheckReportAvialbility(d.DetectorID, MetricTypeID))
                    detectors.Add(d);
            if (detectors.Count > 0)
                return true;
            return false;
        }

        public List<Detector> GetDetectorsForSignalThatSupportAMetricByApproachDirection(int MetricTypeID,
            string Direction)
        {
            var gdr =
                DetectorRepositoryFactory.Create();
            var detectors = new List<Detector>();
            foreach (var d in GetDetectorsForSignal())
                if (gdr.CheckReportAvialbility(d.DetectorID, MetricTypeID) &&
                    d.Approach.DirectionType.Description == Direction)
                    detectors.Add(d);
            return detectors;
        }

        public List<Detector> GetDetectorsForSignalThatSupportAMetricByPhaseNumber(int metricTypeId, int phaseNumber)
        {
            var gdr = DetectorRepositoryFactory.Create();
            var detectors = new List<Detector>();
            foreach (var d in GetDetectorsForSignal())
                if (gdr.CheckReportAvialbilityByDetector(d, metricTypeId) &&
                    (d.Approach.ProtectedPhaseNumber == phaseNumber || d.Approach.PermissivePhaseNumber == phaseNumber))
                    detectors.Add(d);
            return detectors;
        }

        public List<Detector> GetDetectorsForSignalByPhaseNumber(int phaseNumber)
        {
            var dets = new List<Detector>();
            foreach (var d in GetDetectorsForSignal())
                if (d.Approach.ProtectedPhaseNumber == phaseNumber || d.Approach.PermissivePhaseNumber == phaseNumber)
                    dets.Add(d);
            return dets;
        }

        public List<MetricType> GetAvailableMetricsVisibleToWebsite()
        {
//TODO: The list really should be filtered by active timestamp.  We Will do it if we have time. 
            var metRep =
                MetricTypeRepositoryFactory.Create();

            var sigRep = SignalsRepositoryFactory.Create();

            var versions = sigRep.GetAllVersionsOfSignalBySignalID(signalID);

            var availableMetrics = metRep.GetBasicMetrics();
            foreach (var version in versions)
                if (version.VersionActionId != 3)
                    foreach (var d in GetDetectorsForSignal())
                    foreach (var dt in d.DetectionTypes)
                        if (dt.DetectionTypeID != 1)
                            foreach (var m in dt.MetricTypes)
                                if (m.ShowOnWebsite&& !availableMetrics.Contains(m))
                                    availableMetrics.Add(m);
            //availableMetrics = availableMetrics.Distinct().OrderBy(m => m.DisplayOrder).ToList();
            //return availableMetrics.OrderBy(a => a.MetricID).ToList();
            return availableMetrics;
        }

        public List<MetricType> GetAvailableMetrics()
        {
            var repository =
                MetricTypeRepositoryFactory.Create();

            var availableMetrics = repository.GetBasicMetrics();
            foreach (var d in GetDetectorsForSignal())
            foreach (var dt in d.DetectionTypes)
                if (dt.DetectionTypeID != 1)
                    foreach (var m in dt.MetricTypes)
                        availableMetrics.Add(m);
            return availableMetrics.Distinct().ToList();
        }

        public List<Area> GetAreas()
        {
            var repository =
                AreaRepositoryFactory.Create();

            var areas = repository.GetListOfAreasForSignal(SignalID);
            return areas.ToList();
        }

        private List<MetricType> GetBasicMetrics()
        {
            var repository =
                MetricTypeRepositoryFactory.Create();
            return repository.GetBasicMetrics();
        }

        public bool Equals(ATSPM_Signals atspmSignalsToCompare)
        {
            return CompareSignalProperties(atspmSignalsToCompare);
        }

        private bool CompareSignalProperties(ATSPM_Signals atspmSignalsToCompare)
        {
            if (atspmSignalsToCompare != null
                && SignalID == atspmSignalsToCompare.SignalID
                && PrimaryName == atspmSignalsToCompare.PrimaryName
                && SecondaryName == atspmSignalsToCompare.SecondaryName
                && IPAddress == atspmSignalsToCompare.IPAddress
                && Latitude == atspmSignalsToCompare.Latitude
                && Longitude == atspmSignalsToCompare.Longitude
                && RegionID == atspmSignalsToCompare.RegionID
                && ControllerTypeID == atspmSignalsToCompare.ControllerTypeID
                && Enabled == atspmSignalsToCompare.Enabled
                && Pedsare1to1 == atspmSignalsToCompare.Pedsare1to1
                && Approaches.Count() == atspmSignalsToCompare.Approaches.Count()
            )
                return true;
            return false;
        }

        public static ATSPM_Signals CopyVersion(ATSPM_Signals origVersion)
        {
            var signalRepository = Repositories.SignalsRepositoryFactory.Create();
            var newVersion = new ATSPM_Signals();
            CopyCommonSignalSettings(origVersion, newVersion);
            newVersion.SignalID = origVersion.SignalID;
            newVersion.IPAddress = newVersion.IPAddress;
            newVersion.Start = DateTime.Now;
            newVersion.Note = "Copy of " + origVersion.Note;
            newVersion.Comments = new List<MetricComment>();
            newVersion.VersionList = signalRepository.GetAllVersionsOfSignalBySignalID(newVersion.SignalID);
            return newVersion;
        }

        private static void CopyCommonSignalSettings(ATSPM_Signals origAtspmSignals, ATSPM_Signals newAtspmSignals)
        {
            newAtspmSignals.IPAddress = "10.10.10.10";
            newAtspmSignals.PrimaryName = origAtspmSignals.PrimaryName;
            newAtspmSignals.SecondaryName = origAtspmSignals.SecondaryName;
            newAtspmSignals.Longitude = origAtspmSignals.Longitude;
            newAtspmSignals.Latitude = origAtspmSignals.Latitude;
            newAtspmSignals.RegionID = origAtspmSignals.RegionID;
            newAtspmSignals.ControllerTypeID = origAtspmSignals.ControllerTypeID;
            newAtspmSignals.Enabled = origAtspmSignals.Enabled;
            newAtspmSignals.Pedsare1to1 = origAtspmSignals.Pedsare1to1;
            newAtspmSignals.Approaches = new List<Approach>();
            newAtspmSignals.JurisdictionId = origAtspmSignals.JurisdictionId;

            if (origAtspmSignals.Approaches != null)
                foreach (var a in origAtspmSignals.Approaches)
                {
                    var aForNewSignal =
                        Approach.CopyApproachForSignal(a); //this does the db.Save inside.
                    newAtspmSignals.Approaches.Add(aForNewSignal);
                }
        }

        public static ATSPM_Signals CopySignal(ATSPM_Signals origAtspmSignals, string newSignalID)
        {
            var newSignal = new ATSPM_Signals();

            CopyCommonSignalSettings(origAtspmSignals, newSignal);

            newSignal.SignalID = newSignalID;

            return newSignal;
        }

        public List<Approach> GetApproachesForSignalThatSupportMetric(int metricTypeID)
        {
            var approachesForMeticType = new List<Approach>();
            foreach (var a in Approaches)
            foreach (var d in a.Detectors)
                if (d.DetectorSupportsThisMetric(metricTypeID))
                {
                    approachesForMeticType.Add(a);
                    break;
                }
            //return approachesForMeticType;
            return approachesForMeticType.OrderBy(a => a.PermissivePhaseNumber).ThenBy(a => a.ProtectedPhaseNumber).ThenBy(a => a.DirectionType.Description)
                .ToList();
        }

        public List<DirectionType> GetAvailableDirections()
        {
            var directions = Approaches.Select(a => a.DirectionType).Distinct().ToList();
            return directions;
        }

        internal List<Approach> GetApproachesForAggregation()
        {
            List<Approach> approachesToReturn = new List<Approach>();
            if (Approaches != null)
            {
                var approaches = Approaches.Where(a => a.IsPedestrianPhaseOverlap == false && a.IsPermissivePhaseOverlap == false && a.IsProtectedPhaseOverlap == false);
                foreach(var approach in approaches)
                {
                    if ((!approachesToReturn.Select(a => a.ProtectedPhaseNumber).Contains(approach.ProtectedPhaseNumber) && approach.ProtectedPhaseNumber != 0) 
                        || (approach.PermissivePhaseNumber != null && !approachesToReturn.Select(a => a.PermissivePhaseNumber).Contains(approach.PermissivePhaseNumber)))
                    {
                        approachesToReturn.Add(approach);
                    }
                }
            }
            return approachesToReturn;
        }
    }
}