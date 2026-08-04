IF COL_LENGTH('dbo.Jurisdictions', 'JurisdictionKey') IS NULL
BEGIN
    ALTER TABLE dbo.Jurisdictions
        ADD JurisdictionKey nvarchar(255) NULL;
END;
GO

/*
    Set each jurisdiction to the PPK file path or file name that should be used
    for signals assigned to that jurisdiction.

    Absolute path example:

    UPDATE dbo.Jurisdictions
    SET JurisdictionKey = 'D:\ATSPM\Keys\Default.ppk'
    WHERE JurisdictionName = 'Default';

    Relative file-name example:

    UPDATE dbo.Jurisdictions
    SET JurisdictionKey = 'Default.ppk'
    WHERE JurisdictionName = 'Default';

    When JurisdictionKey is relative, SCPFromD4Controllers resolves it under
    the configured PPKLocation directory. If PPKLocation points to a file, the
    file's containing directory is used.
*/
