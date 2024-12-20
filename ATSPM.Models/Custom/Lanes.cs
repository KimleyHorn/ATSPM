﻿using ATSPM.Application.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ATSPM.Models
{
    public partial class Lane
    {
        [NotMapped]
        public List<ControllerEventLog> RedYellowGreenEvents { get; set; }
        [NotMapped]
        public string Index { get; set; }

        public void SetRedYellowGreenEvents(DateTime startTime, DateTime endTime)
        {
            //MOE.Common.Models.Repositories.IControllerEventLogRepository repository =
            //    MOE.Common.Models.Repositories.ControllerEventLogRepositoryFactory.Create();
            //TODO:ConfigChange
            //if (IsProtectedPhaseOverlap)
            //{
            //    RedYellowGreenEvents = repository.GetEventsByEventCodesParam(Approach.SignalID, startTime,
            //        endTime, new List<int>() { 62, 63, 64 }, ProtectedPhaseNumber);                
            //}
            //else
            //{
            //    RedYellowGreenEvents = repository.GetEventsByEventCodesParam(Approach.SignalID, startTime,
            //        endTime, new List<int>() { 1, 8, 10 }, ProtectedPhaseNumber);
            //}
        }

        //public bool Equals(Lane laneToCompare)
        //{
        //    return CompareLaneProperties(laneToCompare);
        //}

    //    private bool CompareLaneProperties(Lane laneToCompare)
    //    {
    //        if (laneToCompare != null
    //            && this.ApproachID == laneToCompare.ApproachID
    //            && this.LaneID == laneToCompare.LaneID
    //            && this.Description == laneToCompare.Description
    //            && this.MovementTypeID == laneToCompare.MovementTypeID
    //            && this.LaneGroupTypeID == laneToCompare.LaneGroupTypeID
    //            && this.Detectors == laneToCompare.Detectors
    //            )
    //        {
    //            return true;
    //        }
    //        else
    //        {
    //            return false;
    //        }

    //    }

    //    public static Lane CopyLane(int incommingLaneGroupID)
    //    {
    //        MOE.Common.Models.Repositories.ILaneGroupRepository repository =
    //            MOE.Common.Models.Repositories.LaneGroupRepositoryFactory.Create();
    //        Lane laneToCopy = repository.GetLaneGroupByLaneGroupID(incommingLaneGroupID);
    //        Lane newLG = new Models.Lane();
    //        newLG.ApproachID = laneToCopy.ApproachID;
    //        newLG.Description = laneToCopy.Approach.Description + laneToCopy.MovementType.Abbreviation;
    //        newLG.LaneGroupTypeID = laneToCopy.LaneGroupTypeID;
    //        newLG.MovementTypeID = laneToCopy.MovementTypeID;
    //        newLG.Detectors = new List<Detector>();

    //        return newLG;
    //    }

    //    public static Lane CopyLane(Lane laneToCopy)
    //    {
    //        Lane newLG = new Models.Lane();
    //        newLG.ApproachID = laneToCopy.ApproachID;
    //        newLG.Description = laneToCopy.Approach.Description + laneToCopy.MovementType.Abbreviation;
    //        newLG.LaneGroupTypeID = laneToCopy.LaneGroupTypeID;
    //        newLG.MovementTypeID = laneToCopy.MovementTypeID;
    //        newLG.Detectors = new List<Detector>();

    //        return newLG;
    //    }
    }
}
