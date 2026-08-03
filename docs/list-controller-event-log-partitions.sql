-- =============================================================================
-- List partitions and row counts for dbo.Controller_Event_Log
--
-- Run against the MOE database after partitioning has been set up per
-- docs/partition-sliding-window-runbook.md.
--
-- Output columns:
--   partition_number  - SQL Server's internal partition id (1-based)
--   lower_inclusive   - smallest Timestamp this partition accepts (NULL for the leftmost overflow)
--   upper_exclusive   - first Timestamp that belongs to the NEXT partition (NULL for the rightmost overflow)
--   rows              - row count in this partition
--
-- The order is DESC so the newest partitions (and the empty forward buffer)
-- appear at the top. Healthy state:
--   - Top 2-3 rows show rows = 0 (empty forward buffer)
--   - Middle rows show row counts concentrated on recent days
--   - Bottom row (lower_inclusive = NULL) shows rows = 0 if Phase 1 pruning ran cleanly
-- =============================================================================

USE MOE;

SELECT
    p.partition_number,
    lo.value AS lower_inclusive,
    hi.value AS upper_exclusive,
    p.rows
FROM sys.partitions p
LEFT JOIN sys.partition_range_values lo
    ON lo.function_id = (SELECT function_id FROM sys.partition_functions WHERE name = 'pf_controller_event_log_daily')
    AND lo.boundary_id = p.partition_number - 1
LEFT JOIN sys.partition_range_values hi
    ON hi.function_id = lo.function_id
    AND hi.boundary_id = p.partition_number
WHERE p.object_id = OBJECT_ID('dbo.Controller_Event_Log')
  AND p.index_id IN (0, 1)   -- 0 = heap (shouldn't occur post-setup); 1 = clustered (rowstore or columnstore)
ORDER BY p.partition_number DESC;


-- -----------------------------------------------------------------------------
-- Bonus: verify the 5 newest rows are in the partition you'd expect.
-- Useful when debugging a "where did this row land?" question.
-- -----------------------------------------------------------------------------

-- SELECT TOP 5
--     SignalID, Timestamp, EventCode, EventParam,
--     $PARTITION.pf_controller_event_log_daily(Timestamp) AS partition_num
-- FROM dbo.Controller_Event_Log
-- ORDER BY Timestamp DESC;
