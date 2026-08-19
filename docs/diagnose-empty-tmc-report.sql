/*
    Why does a signal show data on other reports but nothing on Turning Movement Counts (TMC)?

    A detector only contributes to the TMC chart when ALL of the following hold
    (MOE.Common\Business\WCFServiceLibrary\TMCOptions.cs, TMC\TMCMetric.cs, Business\Detector.cs):

      1. It belongs to the signal version whose Start date is on or before the report date.
      2. Its approach has a DirectionType.
      3. It has the "Lane-by-lane Count" detection type.  That is the ONLY detection type
         mapped to MetricTypeID 5 (TMC).  Basic / Advanced Count / Advanced Speed /
         Stop Bar Presence feed the phase, PCD, speed and split-fail reports instead.
      4. It has a LaneType.
      5. Its MovementType is Left, Thru, Right, Thru-Left or Thru-Right.  "None" is never charted.
      6. Its LaneNumber is 1, 2, 3 or 4.  NULL, 0 and 5+ are silently skipped, and only one
         detector per lane number is charted per movement + lane type.
      7. Controller_Event_Log has EventCode 82 rows for its DetChannel in the date range.
      8. Approaches.SignalID is correct - the TMC volume query uses the approach's copy of the
         signal ID, not the Signals table.  If it is NULL or the text 'null', TMC/PCD/approach
         delay/split fail come back empty while phase-based reports still work.

    If nothing qualifies the report renders with no charts at all rather than blank charts.
*/

USE [MOE];
GO

DECLARE @SignalID  varchar(10) = '7115';        -- <== signal to check
DECLARE @ReportDay datetime    = '2026-08-17';  -- <== the day the report was run for

-------------------------------------------------------------------------------
-- 1. Which config version the TMC report actually loads for that date.
--    If this is not the version you just edited, the edits are on a version whose
--    Start date is after the report date.
-------------------------------------------------------------------------------
SELECT TOP 1 VersionID, SignalID, Start, Note, VersionActionId
FROM   dbo.Signals
WHERE  SignalID = @SignalID
AND    Start <= @ReportDay
AND    VersionActionId <> 3
ORDER BY Start DESC;

-------------------------------------------------------------------------------
-- 2. Every detector in that version, one column per TMC gate.
--    Scan left to right for the first column that is wrong.
-------------------------------------------------------------------------------
;WITH v AS (
    SELECT TOP 1 VersionID, SignalID
    FROM   dbo.Signals
    WHERE  SignalID = @SignalID
    AND    Start <= @ReportDay
    AND    VersionActionId <> 3
    ORDER BY Start DESC
)
SELECT  v.SignalID                          AS SignalSignalID,
        a.SignalID                          AS ApproachSignalID,   -- gate 8: must equal SignalSignalID
        dir.Description                     AS Direction,          -- gate 2: must not be NULL
        a.Description                       AS Approach,
        d.DetectorID,
        d.DetChannel,
        d.LaneNumber,                                              -- gate 6: must be 1-4
        mt.Description                      AS MovementType,       -- gate 5
        lt.Description                      AS LaneType,           -- gate 4: must not be NULL
        STUFF((SELECT ', ' + dt.Description
               FROM   dbo.DetectionTypeDetector dtd
               JOIN   dbo.DetectionTypes dt ON dt.DetectionTypeID = dtd.DetectionTypeID
               WHERE  dtd.ID = d.ID
               ORDER BY dt.DetectionTypeID
               FOR XML PATH('')), 1, 2, '') AS DetectionTypes,     -- gate 3: needs Lane-by-lane Count
        (SELECT COUNT(*)
         FROM   dbo.Controller_Event_Log cel
         WHERE  cel.SignalID   = v.SignalID
         AND    cel.EventCode  = 82
         AND    cel.EventParam = d.DetChannel
         AND    cel.Timestamp >= @ReportDay
         AND    cel.Timestamp <  DATEADD(day, 1, @ReportDay))
                                            AS Code82Events        -- gate 7: must be > 0
FROM    v
JOIN    dbo.Approaches       a   ON a.VersionID       = v.VersionID
JOIN    dbo.Detectors        d   ON d.ApproachID      = a.ApproachID
LEFT JOIN dbo.DirectionTypes dir ON dir.DirectionTypeID = a.DirectionTypeID
LEFT JOIN dbo.MovementTypes  mt  ON mt.MovementTypeID   = d.MovementTypeID
LEFT JOIN dbo.LaneTypes      lt  ON lt.LaneTypeID       = d.LaneTypeID
ORDER BY dir.Description, mt.Description, d.LaneNumber;

/*
    Note on gate 7: for older dates the event log may have been rolled into the parquet
    archive, in which case Code82Events is 0 here but the report still finds data.
    Compare against a date you know is still in Controller_Event_Log.
*/
