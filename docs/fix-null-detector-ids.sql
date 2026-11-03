/*
    Repairs detectors whose DetectorID was generated from a bad Approaches.SignalID.

    DetectorID is derived as <SignalID> + <DetChannel formatted "D2">.  When the approach's
    denormalized SignalID column holds the literal text 'null' (or a real NULL), every detector
    under that approach is rewritten on each save as 'null02', 'null24', 'null25', ...

    Run sections 1 and 2 first and review the output before running section 3.
    Take a backup of MOE (or at least Approaches + Detectors) before the update.
    Section 3 rolls back by default. Set @ApplyChanges to 1 only after reviewing
    the collision check and running the repair once in dry-run mode.
*/

USE [MOE];
GO

-------------------------------------------------------------------------------
-- 1. What is affected, and where it came from
-------------------------------------------------------------------------------
SELECT  d.ID,
        d.DetectorID,
        d.DetChannel,
        d.DateAdded,
        a.ApproachID,
        a.SignalID          AS ApproachSignalID,
        a.Description       AS ApproachDescription,
        s.VersionID,
        s.SignalID          AS SignalSignalID,
        s.PrimaryName,
        s.SecondaryName
FROM    dbo.Detectors  d
JOIN    dbo.Approaches a ON a.ApproachID = d.ApproachID
JOIN    dbo.Signals    s ON s.VersionID  = a.VersionID
WHERE   d.DetectorID LIKE 'null%'
ORDER BY s.SignalID, a.ApproachID, d.DetChannel;

-- Approaches carrying a bad SignalID (this is the row that keeps re-corrupting the detectors)
SELECT  a.ApproachID, a.VersionID, a.SignalID AS ApproachSignalID,
        s.SignalID AS SignalSignalID, a.Description
FROM    dbo.Approaches a
JOIN    dbo.Signals    s ON s.VersionID = a.VersionID
WHERE   a.SignalID IS NULL
     OR a.SignalID = 'null'
     OR a.SignalID <> s.SignalID
ORDER BY s.SignalID, a.ApproachID;

-- Any signal version itself carrying the text 'null'.
-- Deleting a signal in the config tool is a SOFT delete - SignalsController.Delete calls
-- SetAllVersionsOfASignalToDeleted, which only sets VersionActionId = 3.  The Signals,
-- Approaches and Detectors rows all remain, so a 'null' signal that was "deleted" months
-- ago is still sitting in these tables.
SELECT VersionID, SignalID, VersionActionId,
       CASE VersionActionId WHEN 3 THEN 'SOFT DELETED' ELSE 'active' END AS VersionState,
       PrimaryName, SecondaryName, Start, Note,
       (SELECT COUNT(*) FROM dbo.Approaches a2 WHERE a2.VersionID = s2.VersionID) AS Approaches,
       (SELECT COUNT(*) FROM dbo.Approaches a2
        JOIN dbo.Detectors d2 ON d2.ApproachID = a2.ApproachID
        WHERE a2.VersionID = s2.VersionID)                                       AS Detectors
FROM   dbo.Signals s2
WHERE  SignalID = 'null';

-- Detectors whose OWNING SIGNAL is itself 'null'.  These cannot be repaired by rebuilding
-- the ID - there is no correct signal ID to rebuild from.  They are leftovers of the deleted
-- signal and should be removed, not renamed.  Sections 3 and 4 deliberately skip them.
SELECT d.ID, d.DetectorID, d.DetChannel, d.DateAdded,
       a.ApproachID, a.Description AS Approach,
       s.VersionID, s.VersionActionId
FROM   dbo.Detectors  d
JOIN   dbo.Approaches a ON a.ApproachID = d.ApproachID
JOIN   dbo.Signals    s ON s.VersionID  = a.VersionID
WHERE  d.DetectorID LIKE 'null%'
AND    s.SignalID = 'null'
ORDER BY d.ID;

-------------------------------------------------------------------------------
-- 2. Collision check - would the repair produce a DetectorID that already exists?
-------------------------------------------------------------------------------
SELECT  d.ID, d.DetectorID AS CurrentDetectorID,
        s.SignalID + CASE WHEN d.DetChannel < 10
                          THEN '0' + CAST(d.DetChannel AS varchar(10))
                          ELSE CAST(d.DetChannel AS varchar(10)) END AS RepairedDetectorID
FROM    dbo.Detectors  d
JOIN    dbo.Approaches a ON a.ApproachID = d.ApproachID
JOIN    dbo.Signals    s ON s.VersionID  = a.VersionID
WHERE   d.DetectorID LIKE 'null%'
AND EXISTS (
        SELECT 1
        FROM   dbo.Detectors  d2
        JOIN   dbo.Approaches a2 ON a2.ApproachID = d2.ApproachID
        WHERE  d2.ID <> d.ID
        AND    a2.VersionID = a.VersionID
        AND    d2.DetectorID = s.SignalID + CASE WHEN d.DetChannel < 10
                                                 THEN '0' + CAST(d.DetChannel AS varchar(10))
                                                 ELSE CAST(d.DetChannel AS varchar(10)) END);

-------------------------------------------------------------------------------
-- 3. Repair Approaches.SignalID and then rebuild affected DetectorIDs.
--    Skips rows whose owning signal is itself 'null'. The whole repair is one
--    transaction and rolls back unless @ApplyChanges is deliberately set to 1.
-------------------------------------------------------------------------------
DECLARE @ApplyChanges bit = 0;
DECLARE @ApproachesUpdated int;
DECLARE @DetectorsUpdated int;

SET XACT_ABORT ON;
BEGIN TRANSACTION;

UPDATE  a
SET     a.SignalID = s.SignalID
FROM    dbo.Approaches a
JOIN    dbo.Signals    s ON s.VersionID = a.VersionID
WHERE  (a.SignalID IS NULL OR a.SignalID = 'null')
AND     s.SignalID <> 'null';

SET @ApproachesUpdated = @@ROWCOUNT;

UPDATE  d
SET     d.DetectorID = s.SignalID + CASE WHEN d.DetChannel < 10
                                         THEN '0' + CAST(d.DetChannel AS varchar(10))
                                         ELSE CAST(d.DetChannel AS varchar(10)) END
FROM    dbo.Detectors  d
JOIN    dbo.Approaches a ON a.ApproachID = d.ApproachID
JOIN    dbo.Signals    s ON s.VersionID  = a.VersionID
WHERE   d.DetectorID LIKE 'null%'
AND     s.SignalID <> 'null';

SET @DetectorsUpdated = @@ROWCOUNT;

SELECT @ApproachesUpdated AS ApproachesUpdated,
       @DetectorsUpdated AS DetectorsUpdated,
       CASE WHEN @ApplyChanges = 1 THEN 'COMMIT' ELSE 'ROLLBACK (dry run)' END AS Action;

IF @ApplyChanges = 1
    COMMIT TRANSACTION;
ELSE
    ROLLBACK TRANSACTION;

-------------------------------------------------------------------------------
-- 4. Optional - historical rows keyed by the old DetectorID string.
--    Only needed if these detectors have already logged data under 'null##'.
--    Check the counts first; for detectors added recently these are usually empty.
-------------------------------------------------------------------------------
SELECT 'Speed_Events'          AS TableName, COUNT(*) AS Rows FROM dbo.Speed_Events          WHERE DetectorID LIKE 'null%'
UNION ALL
SELECT 'SPMWatchDogErrorEvent' AS TableName, COUNT(*) AS Rows FROM dbo.SPMWatchDogErrorEvent WHERE DetectorID LIKE 'null%';
