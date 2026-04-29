# Runbook: Daily partition sliding window on `MOE.dbo.Controller_Event_Log`

Sets up a rolling N-day daily-partitioned table with an automated daily
SQL Agent job that adds tomorrow's partition (SPLIT) and drops the
oldest partition (MERGE + TRUNCATE). Designed for ATSPM deployments where
the database is always `MOE` and the event log table is always
`dbo.Controller_Event_Log`.

Use this end-to-end each time we stand up partitioning for a new agency.

---

## Fixed-across-clients values

These names are baked into ATSPM and do not change between clients:

| Item | Value |
|------|-------|
| Database | `MOE` |
| Table | `dbo.Controller_Event_Log` |
| Partition column | `Timestamp` |
| Partition column type | `datetime` (yes, not `datetime2` — the EF migration is misleading) |
| Partition function name | `pf_controller_event_log_daily` |
| Partition scheme name | `ps_controller_event_log_daily` |
| Stored procedure name | `dbo.usp_SlideControllerEventLogPartition` |
| Job name | `[MOE] Slide Controller_Event_Log Partition` |
| Clustered index name | `IX_Clustered_Controller_Event_Log_Timestamp` (on `Timestamp`, non-unique) |
| Nonclustered index | on `(SignalID, Timestamp, EventCode, EventParam)` — confirm exact name per instance |

## Per-client decisions to make up front

| Decision | Typical value |
|----------|---------------|
| Retention (days) | 90 |
| Forward buffer at setup (days) | 3 (only needs to be ≥ 2; bump higher only if the daily job won't run immediately) |
| SQL Server version | confirm — needs `TRUNCATE ... WITH (PARTITIONS())` (2016+) |
| SQL Server edition | Enterprise → `ONLINE = ON` available; Standard → offline maintenance window |
| Job owner login | `sa` or the shop's standard job-owner login (never a personal account) |
| Schedule (UTC) | 00:15 |
| Error operator | shop's default app-failure / DBA operator |
| Info operator (optional) | shop's informational operator or none |

---

## Phase 0 — Discovery

Run against the target instance before touching anything:

```sql
-- Server version & edition
SELECT @@VERSION AS version, SERVERPROPERTY('Edition') AS edition;

-- Current row count and size of the table
USE MOE;
SELECT SUM(p.rows) AS total_rows,
       SUM(a.total_pages) * 8 / 1024.0 AS size_mb
FROM sys.partitions p
JOIN sys.allocation_units a ON a.container_id = p.partition_id
WHERE p.object_id = OBJECT_ID('dbo.Controller_Event_Log');

-- Existing indexes — capture the real nonclustered index name and any
-- non-stock indexes for this client
SELECT i.name, i.type_desc, i.is_unique,
       STUFF((SELECT ', ' + c.name
              FROM sys.index_columns ic
              JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
              ORDER BY ic.key_ordinal
              FOR XML PATH('')), 1, 2, '') AS key_columns
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID('dbo.Controller_Event_Log') AND i.type > 0;

-- FKs referencing this table (any hit blocks SWITCH — investigate before proceeding)
SELECT fk.name, OBJECT_NAME(fk.parent_object_id) AS from_table
FROM sys.foreign_keys fk
WHERE fk.referenced_object_id = OBJECT_ID('dbo.Controller_Event_Log');

-- Agent operators
SELECT name, email_address, enabled FROM msdb.dbo.sysoperators WHERE enabled = 1;

-- Database Mail configured?
SELECT name FROM msdb.dbo.sysmail_profile;   -- must return >= 1 row for emails to actually send

-- Check the job owner login you plan to use
-- Replace 'sa' with whichever login the shop expects
SELECT name, is_disabled, IS_SRVROLEMEMBER('sysadmin', name) AS is_sysadmin
FROM sys.server_principals WHERE name = 'sa';
```

Do not move forward until:

- [ ] You know the exact nonclustered index name(s) to rebuild
- [ ] No foreign keys reference the table
- [ ] An operator exists that routes to the DBA/app-failure team
- [ ] Database Mail has at least one profile
- [ ] Your chosen job owner login is enabled, sysadmin, and acceptable to the DBA team

---

## Phase 1 — Prune data older than retention

Any rows older than the retention window will land in the leftmost overflow
partition and will never be cleaned by the daily job. Prune them first, in
batches, to keep the transaction log from blowing up:

```sql
USE MOE;

DECLARE @retention_days int = 90;
DECLARE @cutoff datetime = DATEADD(DAY, -@retention_days, CAST(SYSUTCDATETIME() AS date));
DECLARE @rows int = 1;

WHILE @rows > 0
BEGIN
    DELETE TOP (50000) FROM dbo.Controller_Event_Log WHERE Timestamp < @cutoff;
    SET @rows = @@ROWCOUNT;
    WAITFOR DELAY '00:00:01';
END
```

This is online and interruptible. Run in a maintenance window anyway on
large tables — log backups need to keep up.

If the agency requires archiving old data instead of deleting, SWITCH it to
an archive table before this phase rather than deleting.

---

## Phase 2 — Create the partition function and scheme

```sql
USE MOE;
GO

DECLARE @retention_days  int = 90;
DECLARE @forward_buffer  int = 3;     -- only needs to be >= 2; bump if the agent job won't run for a while
DECLARE @start date = DATEADD(DAY, -@retention_days, CAST(SYSUTCDATETIME() AS date));
DECLARE @end   date = DATEADD(DAY,  @forward_buffer,  CAST(SYSUTCDATETIME() AS date));

DECLARE @sql nvarchar(max) =
    N'CREATE PARTITION FUNCTION pf_controller_event_log_daily (datetime) AS RANGE RIGHT FOR VALUES (';
DECLARE @d date = @start;
WHILE @d <= @end
BEGIN
    SET @sql += '''' + CONVERT(char(10), @d, 23) + 'T00:00:00'', ';
    SET @d = DATEADD(DAY, 1, @d);
END
SET @sql = LEFT(@sql, LEN(@sql) - 1) + N');';
EXEC sp_executesql @sql;
GO

CREATE PARTITION SCHEME ps_controller_event_log_daily
AS PARTITION pf_controller_event_log_daily ALL TO ([PRIMARY]);
GO
```

Notes:

- `RANGE RIGHT` + midnight boundaries means a row with `Timestamp = 2026-04-23 00:00:00.000` belongs to the 2026-04-23 partition (not the 2026-04-22 partition).
- Parameter type must be `datetime`, not `datetime2`. The column is `datetime` despite what the EF migration suggests.
- If the deployment uses multiple filegroups, adjust `ALL TO ([PRIMARY])`.

---

## Phase 3 — Rebuild indexes onto the partition scheme

Enterprise edition can add `ONLINE = ON` to these statements. Standard
edition cannot — the table is locked for the duration of the rebuild.
Plan a maintenance window on Standard.

```sql
USE MOE;
GO

-- Clustered — non-unique, on Timestamp only (matches ATSPM stock definition)
CREATE CLUSTERED INDEX IX_Clustered_Controller_Event_Log_Timestamp
ON dbo.Controller_Event_Log (Timestamp)
WITH (DROP_EXISTING = ON)   -- add ", ONLINE = ON" on Enterprise
ON ps_controller_event_log_daily(Timestamp);
GO

-- Nonclustered — replace <NCI_NAME> with the real name from Phase 0
CREATE NONCLUSTERED INDEX <NCI_NAME>
ON dbo.Controller_Event_Log (SignalID, Timestamp, EventCode, EventParam)
WITH (DROP_EXISTING = ON)   -- add ", ONLINE = ON" on Enterprise
ON ps_controller_event_log_daily(Timestamp);
GO
```

If the client has additional custom indexes on `Controller_Event_Log`, each
must be rebuilt with `ON ps_controller_event_log_daily(Timestamp)` and
have `Timestamp` in its key. Unaligned indexes will make `SWITCH` /
`TRUNCATE WITH (PARTITIONS())` fail with error 7733.

Verify alignment:

```sql
SELECT i.name, ds.name AS on_data_space, ds.type_desc
FROM sys.indexes i
JOIN sys.data_spaces ds ON ds.data_space_id = i.data_space_id
WHERE i.object_id = OBJECT_ID('dbo.Controller_Event_Log') AND i.type > 0;
```

Every index should show `type_desc = 'PARTITION_SCHEME'`.

---

## Phase 4 — Create the maintenance procedure

```sql
USE MOE;
GO

CREATE OR ALTER PROCEDURE dbo.usp_SlideControllerEventLogPartition
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @retention_days int = 90;    -- keep in sync with Phase 2
    DECLARE @forward_buffer int = 2;     -- minimum buffer to maintain daily

    DECLARE @today          date     = CAST(SYSUTCDATETIME() AS date);
    DECLARE @newBoundary    datetime = DATEADD(DAY,  @forward_buffer, @today);
    DECLARE @oldestKeep     datetime = DATEADD(DAY, -@retention_days, @today);
    DECLARE @boundaryToDrop datetime;
    DECLARE @partNum        int;

    -- SPLIT: add the next-day empty partition if not already present
    IF NOT EXISTS (
        SELECT 1 FROM sys.partition_range_values rv
        JOIN sys.partition_functions pf ON pf.function_id = rv.function_id
        WHERE pf.name = 'pf_controller_event_log_daily' AND rv.value = @newBoundary
    )
    BEGIN
        ALTER PARTITION SCHEME ps_controller_event_log_daily NEXT USED [PRIMARY];
        ALTER PARTITION FUNCTION pf_controller_event_log_daily() SPLIT RANGE (@newBoundary);
    END

    -- MERGE: drop every partition older than the retention window
    WHILE 1 = 1
    BEGIN
        SELECT TOP 1 @boundaryToDrop = CAST(rv.value AS datetime)
        FROM sys.partition_range_values rv
        JOIN sys.partition_functions pf ON pf.function_id = rv.function_id
        WHERE pf.name = 'pf_controller_event_log_daily'
          AND CAST(rv.value AS datetime) < @oldestKeep
        ORDER BY rv.value;

        IF @@ROWCOUNT = 0 BREAK;

        SET @partNum = $PARTITION.pf_controller_event_log_daily(@boundaryToDrop);

        TRUNCATE TABLE dbo.Controller_Event_Log WITH (PARTITIONS (@partNum));
        ALTER PARTITION FUNCTION pf_controller_event_log_daily() MERGE RANGE (@boundaryToDrop);
    END
END
GO
```

Properties of the proc:

- **Idempotent within a day** — safe to run manually, safe to run concurrently with its scheduled invocation in the sense that a second same-day execution is a no-op (not safe against simultaneous execution, but that shouldn't happen in practice).
- **SPLIT of an empty partition** is metadata-only (fast, minimally logged).
- **TRUNCATE ... WITH (PARTITIONS())** is minimally logged, unlike `DELETE`.
- **MERGE of an empty boundary** is metadata-only.
- `@forward_buffer = 2` here — once the function exists and the job runs daily, it only needs to stay two days ahead of "now". Phase 2's slightly larger setup buffer (3) is just a one-time cushion in case the first scheduled run is delayed.

### Pre-2016 fallback

On SQL Server 2014 and earlier there is no `TRUNCATE ... WITH (PARTITIONS())`.
Replace that line with a `SWITCH` to a structurally-identical staging table:

```sql
TRUNCATE TABLE dbo.Controller_Event_Log_Staging;
ALTER TABLE dbo.Controller_Event_Log
    SWITCH PARTITION @partNum TO dbo.Controller_Event_Log_Staging;
TRUNCATE TABLE dbo.Controller_Event_Log_Staging;
```

Create the staging table ahead of time — same columns, same indexes, same
filegroup, no foreign keys.

---

## Phase 5 — Smoke test manually

```sql
EXEC dbo.usp_SlideControllerEventLogPartition;

-- Partition window view — lower bound and upper bound per partition
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
  AND p.index_id = 1
ORDER BY p.partition_number DESC;

-- Confirm known rows land in the right partition
SELECT TOP 5
    SignalID, Timestamp, EventCode, EventParam,
    $PARTITION.pf_controller_event_log_daily(Timestamp) AS partition_num
FROM dbo.Controller_Event_Log
ORDER BY Timestamp DESC;
```

Expected:

- Top partition (highest number) shows `lower_inclusive = today + forward_buffer`, `upper_exclusive = NULL`, `rows = 0`
- Bottom partition shows `lower_inclusive = NULL`, `upper_exclusive = today - retention_days`, `rows = 0` if Phase 1 was run successfully
- Row-count concentration on recent dates, zero on "future" partitions

Run the proc a second time — it should complete immediately with no change
(idempotency check).

---

## Phase 6 — Create the SQL Agent job

Requires sysadmin at the moment of creation to set the owner to a non-personal
login. Do this while your sysadmin access is still in place.

```sql
USE msdb;
GO

DECLARE @jobId uniqueidentifier;

-- Fill these in per client
DECLARE @owner          sysname = N'sa';                     -- or the shop's standard job owner
DECLARE @errorOperator  sysname = N'AppJobFailures';         -- shop's app-failure operator
DECLARE @supportContact nvarchar(200) = N'<team email / DL>';

EXEC msdb.dbo.sp_add_job
    @job_name = N'[MOE] Slide Controller_Event_Log Partition',
    @enabled  = 1,
    @description = N'Purpose: Maintains a rolling 90-day daily-partition window on dbo.Controller_Event_Log. Runs SPLIT to add tomorrow''s empty partition and MERGE/TRUNCATE partitions older than 90 days.

Schedule: Daily at 00:15 UTC. Not time-critical; idempotent within a day and safe to run on demand.

Support contact: <team email / DL>
Customer impact on failure: After 2 consecutive failure days the forward buffer runs out. New inserts past the last boundary land in the rightmost partition (still correct, but not isolated). Old data stops being pruned. Notify ATMS team if red for >48 hours.',
    @category_name              = N'Database Maintenance',
    @owner_login_name           = @owner,
    @notify_level_email         = 2,            -- 2 = on failure only (meets standard)
    @notify_email_operator_name = @errorOperator,
    @job_id                     = @jobId OUTPUT;

EXEC msdb.dbo.sp_add_jobstep
    @job_id        = @jobId,
    @step_name     = N'Slide partition window',
    @subsystem     = N'TSQL',
    @database_name = N'MOE',
    @command       = N'EXEC dbo.usp_SlideControllerEventLogPartition;',
    @on_success_action = 1,    -- quit with success
    @on_fail_action    = 2;    -- quit with failure

EXEC msdb.dbo.sp_add_schedule
    @schedule_name     = N'Daily_0015',
    @freq_type         = 4,        -- daily
    @freq_interval     = 1,
    @active_start_time = 1500;     -- HHMMSS = 00:15:00

EXEC msdb.dbo.sp_attach_schedule
    @job_name      = N'[MOE] Slide Controller_Event_Log Partition',
    @schedule_name = N'Daily_0015';

EXEC msdb.dbo.sp_add_jobserver
    @job_name    = N'[MOE] Slide Controller_Event_Log Partition',
    @server_name = N'(local)';
```

**Remember to replace `<team email / DL>` inside the `@description` string
before executing.** The description is what on-call sees when the job pages
them — make it actually useful.

### Optional: informational notifications on success

If the agency wants success emails (Jeremiah's "ATMS Notices" pattern),
add a second step that calls `sp_notify_operator`. Info notices are not
required by the standard, and most teams prefer no noise.

```sql
EXEC msdb.dbo.sp_add_jobstep
    @job_name      = N'[MOE] Slide Controller_Event_Log Partition',
    @step_name     = N'Notify info operator on success',
    @subsystem     = N'TSQL',
    @database_name = N'msdb',
    @command       = N'EXEC msdb.dbo.sp_notify_operator
                          @name    = N''ATMS Notices'',
                          @subject = N''[MOE] Slide Partition job succeeded'',
                          @body    = N''Daily partition maintenance completed on '' + @@SERVERNAME + N'' at '' + CONVERT(varchar, SYSDATETIME(), 120);',
    @on_success_action = 1,
    @on_fail_action    = 2;
```

---

## Phase 7 — Verify the job configuration

```sql
-- Full job config
EXEC msdb.dbo.sp_help_job @job_name = N'[MOE] Slide Controller_Event_Log Partition';

-- Confirm notification settings actually landed.
-- SSMS's GUI silently drops notify_level_email sometimes; always verify via T-SQL.
SELECT j.name,
       j.notify_level_email,              -- must be 2
       o.name AS notify_operator,         -- must be the intended operator
       o.email_address
FROM msdb.dbo.sysjobs j
LEFT JOIN msdb.dbo.sysoperators o ON o.id = j.notify_email_operator_id
WHERE j.name = N'[MOE] Slide Controller_Event_Log Partition';

-- Run the job and watch history
EXEC msdb.dbo.sp_start_job @job_name = N'[MOE] Slide Controller_Event_Log Partition';

-- Give it a few seconds, then:
EXEC msdb.dbo.sp_help_jobhistory
    @job_name = N'[MOE] Slide Controller_Event_Log Partition',
    @mode = 'FULL';
-- run_status = 1 on the step means succeeded
```

### Optional but recommended — prove the failure-alert path end-to-end

Before signing off, confirm a real failure email arrives:

```sql
-- Deliberately break the job
EXEC sp_rename 'dbo.usp_SlideControllerEventLogPartition',
               'usp_SlideControllerEventLogPartition_disabled';

EXEC msdb.dbo.sp_start_job @job_name = N'[MOE] Slide Controller_Event_Log Partition';

-- Wait for the failure email to arrive at the operator distribution list.
-- Then restore:
EXEC sp_rename 'dbo.usp_SlideControllerEventLogPartition_disabled',
               'usp_SlideControllerEventLogPartition';
```

This is the single best way to catch a misconfigured Database Mail profile
before production relies on the alert.

---

## Phase 8 — Client hand-off package

Give the DBA team / operations:

1. **What's installed** — names of function, scheme, proc, job, plus the
   scripts used to create them (save a copy of this runbook with
   per-client values substituted).
2. **Healthy-state check** — the Phase 5 inspection query and what a good
   result looks like (all "future" partitions empty, rowcount concentrated
   on recent dates, partition count = `retention_days + forward_buffer + 1`).
3. **Failure modes:**
   - Red for 1 day: buffer absorbs it, no immediate impact
   - Red for 3+ days: forward buffer consumed, new rows land in rightmost
     partition without their own boundary, old data stops pruning
   - Red for weeks: disk pressure from unpruned history; SPLIT still works
     but MERGE lag grows
4. **Manual forward-extend script** (for when the job is paused, e.g.,
   permissions issue):

   ```sql
   USE MOE;
   DECLARE @target date = DATEADD(DAY, 10, CAST(SYSUTCDATETIME() AS date));
   DECLARE @next datetime = (
       SELECT DATEADD(DAY, 1, MAX(CAST(rv.value AS datetime)))
       FROM sys.partition_range_values rv
       JOIN sys.partition_functions pf ON pf.function_id = rv.function_id
       WHERE pf.name = 'pf_controller_event_log_daily');
   WHILE @next <= CAST(@target AS datetime)
   BEGIN
       ALTER PARTITION SCHEME ps_controller_event_log_daily NEXT USED [PRIMARY];
       ALTER PARTITION FUNCTION pf_controller_event_log_daily() SPLIT RANGE (@next);
       SET @next = DATEADD(DAY, 1, @next);
   END
   ```

5. **Rollback plan** — if partitioning needs to be undone:
   - Disable the job
   - Rebuild clustered index back onto `[PRIMARY]` (no partition scheme)
   - Rebuild each nonclustered index the same way
   - Drop the partition scheme, then the partition function
   - Drop the staging table if one was created (pre-2016 path)

---

## Pitfalls checklist (learned the hard way)

- [ ] Partition function parameter type **matches the column type exactly**
      (`datetime` vs `datetime2(7)` will throw error 7439)
- [ ] Every index on the table is **aligned** (on the partition scheme,
      with `Timestamp` somewhere in the key)
- [ ] No foreign keys reference `Controller_Event_Log`
- [ ] Data older than retention **pruned before** Phase 3 (avoids permanent
      data in the leftmost overflow partition)
- [ ] Rebuild performed with edition-appropriate options (Enterprise → ONLINE;
      Standard → offline maintenance window)
- [ ] Job owner is **not** a personal account
- [ ] Database Mail profile exists and the chosen operator has a real email
- [ ] `notify_level_email = 2` + operator assigned, verified via T-SQL query
      (the GUI occasionally silently drops this)
- [ ] Initial forward buffer ≥ 2 days (3 is the recommended default; only
      bump higher if you know the daily job won't be running for a while —
      e.g., still waiting on agent access)
- [ ] Retention window matches the business requirement (default 90 for ATSPM)
- [ ] Proc run manually twice in Phase 5 — once to seed, once to confirm
      idempotency
- [ ] Failure-alert path proved end-to-end in Phase 7 (deliberately break it
      and confirm email arrives)
- [ ] Disk on the server is actually healthy — check C: and the data drive;
      partition rebuild needs ~1.5–2x table size free during the operation

---

## References

- [`CREATE PARTITION FUNCTION`](https://learn.microsoft.com/sql/t-sql/statements/create-partition-function-transact-sql)
- [`ALTER PARTITION FUNCTION` (SPLIT / MERGE)](https://learn.microsoft.com/sql/t-sql/statements/alter-partition-function-transact-sql)
- [`TRUNCATE TABLE ... WITH (PARTITIONS)`](https://learn.microsoft.com/sql/t-sql/statements/truncate-table-transact-sql) (SQL 2016+)
- [`sp_add_job` / `sp_add_jobstep` / `sp_add_schedule`](https://learn.microsoft.com/sql/relational-databases/system-stored-procedures/sp-add-job-transact-sql)
