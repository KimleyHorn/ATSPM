-- Backfill Approach speed limit (Approaches.MPH) and advance-detector distance
-- (Detectors.DistanceFromStopBar) for the US-95 corridor signals in Lake Havasu.
-- Run against the MOE database in SSMS. Run step 1, sanity-check the rows,
-- then run steps 2 and 3.
--
-- Detection-driven, any approach direction (mainline and side streets):
--   * DistanceFromStopBar = 350 on every detector tagged "Advanced Count" (type 2)
--   * MPH = 45 on every approach that has at least one detector whose detection
--     type consumes MPH: Advanced Count (2) or Advanced Speed (3)
-- Approaches with neither type are left untouched (MPH is unused there).
--
-- VersionActionId 3 = deleted signal version; all other versions are updated so
-- charts run for past dates pick up the values too (ATSPM selects the signal
-- version by chart date, not just the latest).

-------------------------------------------------------------------------------
-- 1) REVIEW: every advance detector (count or speed) at the "95" signals,
--    any direction, with the approach's current MPH
-------------------------------------------------------------------------------
SELECT s.SignalID, s.VersionID, s.PrimaryName, s.SecondaryName,
       dir.Abbreviation AS Dir, a.ApproachID, a.ProtectedPhaseNumber, a.MPH,
       d.DetChannel, d.DistanceFromStopBar, dt.Description AS DetectionType
FROM dbo.Signals s
JOIN dbo.Approaches a              ON a.VersionID = s.VersionID
JOIN dbo.Detectors d               ON d.ApproachID = a.ApproachID
JOIN dbo.DetectionTypeDetector dtd ON dtd.ID = d.ID AND dtd.DetectionTypeID IN (2, 3)
JOIN dbo.DetectionTypes dt         ON dt.DetectionTypeID = dtd.DetectionTypeID
JOIN dbo.DirectionTypes dir        ON dir.DirectionTypeID = a.DirectionTypeID
WHERE s.VersionActionId <> 3
  AND (s.PrimaryName LIKE '%95%' OR s.SecondaryName LIKE '%95%')  -- adjust to your naming, or swap for an explicit SignalID list
ORDER BY s.SignalID, s.VersionID, dir.DisplayOrder, d.DetChannel;

-------------------------------------------------------------------------------
-- 2) 350 ft on every Advanced Count detector at those signals — the detection
--    type join is the gate, so side streets are included only when they
--    actually have an advance count zone configured
-------------------------------------------------------------------------------
UPDATE d
SET d.DistanceFromStopBar = 350
FROM dbo.Detectors d
JOIN dbo.DetectionTypeDetector dtd ON dtd.ID = d.ID AND dtd.DetectionTypeID = 2
JOIN dbo.Approaches a              ON a.ApproachID = d.ApproachID
JOIN dbo.Signals s                 ON s.VersionID = a.VersionID
WHERE s.VersionActionId <> 3
  AND (s.PrimaryName LIKE '%95%' OR s.SecondaryName LIKE '%95%');

-------------------------------------------------------------------------------
-- 3) MPH = 45 on every approach (any direction) that has detection configured
--    which consumes the value. Delete the EXISTS clause if you'd rather stamp
--    45 on every approach regardless of detection.
-------------------------------------------------------------------------------
UPDATE a
SET a.MPH = 45
FROM dbo.Approaches a
JOIN dbo.Signals s ON s.VersionID = a.VersionID
WHERE s.VersionActionId <> 3
  AND (s.PrimaryName LIKE '%95%' OR s.SecondaryName LIKE '%95%')
  AND EXISTS (SELECT 1
              FROM dbo.Detectors d
              JOIN dbo.DetectionTypeDetector dtd ON dtd.ID = d.ID
              WHERE d.ApproachID = a.ApproachID
                AND dtd.DetectionTypeID IN (2, 3));  -- Advanced Count, Advanced Speed
