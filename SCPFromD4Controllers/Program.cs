using System.Configuration;
using Dapper;
using FluentFTP;
using Microsoft.Data.SqlClient;
using MOE.Common.Business;
using Renci.SshNet;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using MOE.Common.Models;

namespace SCPFromD4Controllers
{
    class Program
    {
        static void Main(string[] args)
        {
            var connectionString = ConfigurationManager.ConnectionStrings["SPM"].ConnectionString;

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "d4-controllers-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    restrictedToMinimumLevel: LogEventLevel.Error)
                .WriteTo.Sink(new ApplicationEventsSink(connectionString), restrictedToMinimumLevel: LogEventLevel.Error)
                .CreateLogger();

            try
            {
                Log.Information("Application starting up at {Time}", DateTime.Now);

                var signalFtpOptions = new SignalFtpOptions(
                    Convert.ToInt32(ConfigurationManager.AppSettings["SNMPTimeout"]),
                    Convert.ToInt32(ConfigurationManager.AppSettings["SNMPRetry"]),
                    Convert.ToInt32(ConfigurationManager.AppSettings["SNMPPort"]),
                    Convert.ToBoolean(ConfigurationManager.AppSettings["DeleteFilesAfterFTP"]),
                    ConfigurationManager.AppSettings["LocalDirectory"],
                    Convert.ToInt32(ConfigurationManager.AppSettings["FTPConnectionTimeoutInSeconds"]),
                    Convert.ToInt32(ConfigurationManager.AppSettings["FTPReadTimeoutInSeconds"]),
                    Convert.ToBoolean(ConfigurationManager.AppSettings["SkipCurrentLog"]),
                    Convert.ToBoolean(ConfigurationManager.AppSettings["RenameDuplicateFiles"]),
                    Convert.ToInt32(ConfigurationManager.AppSettings["waitBetweenFileDownloadMilliseconds"]),
                    Convert.ToInt32(ConfigurationManager.AppSettings["MaximumNumberOfFilesTransferAtOneTime"]),
                    Convert.ToBoolean(ConfigurationManager.AppSettings["RequiresPPK"]),
                    ConfigurationManager.AppSettings["PPKLocation"],
                    ConfigurationManager.AppSettings["RegionalControllerType"] != null
                        ? Convert.ToInt32(ConfigurationManager.AppSettings["RegionalControllerType"])
                        : 0,
                    ConfigurationManager.AppSettings["SshFingerprint"],
                    Convert.ToBoolean(ConfigurationManager.AppSettings["IsGzip"]),
                    Convert.ToBoolean(ConfigurationManager.AppSettings["UsePhysicalLocation"])
                );
                var physicalLocationFallbacks = ParsePhysicalLocationFallbacks(
                    ConfigurationManager.AppSettings["PhysicalLocationFallbacks"]);

                Log.Information("Querying signal list from database...");
                var signalList = GetLatestVersionOfAllSignalsForD4Sftp(
                    connectionString,
                    signalFtpOptions.RegionControllerType);
                var maxThreads = Convert.ToInt32(ConfigurationManager.AppSettings["MaxThreads"]);
                Log.Information("Found {Count} signal(s) to process.", signalList.Count);

                foreach (var signal in signalList)
                {
                    try
                    {

                        var options = new ParallelOptions { MaxDegreeOfParallelism = maxThreads };

                        Parallel.ForEach(signalList, options, signal =>
                        {
                            try
                            {
                                EnsureLocalDirectory(signalFtpOptions.LocalDirectory, signal.SignalID);
                                GetD4Files(signal, signalFtpOptions, connectionString, physicalLocationFallbacks);
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "Error at highest level for signal {SignalID}.", signal.SignalID);
                            }
                        });

                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Signal {SignalID}: Error in PPK path.", signal.SignalID);
                    }
                }


                Log.Information("Processing complete.");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application failed.");
                File.WriteAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.txt"),
                    $"Crashed at: {DateTime.Now}\n\n{ex}\n\nInner:\n{ex.InnerException}");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        private static void EnsureLocalDirectory(string localDirectory, string signalId)
        {
            var path = Path.Combine(localDirectory, signalId);
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        private static IReadOnlyList<string> ParsePhysicalLocationFallbacks(string? configuredLocations)
        {
            if (string.IsNullOrWhiteSpace(configuredLocations))
                return Array.Empty<string>();

            return configuredLocations
                .Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // -------------------------------------------------------------------------
        // DB
        // -------------------------------------------------------------------------

        public static List<Signal> GetLatestVersionOfAllSignalsForD4Sftp(
            string connectionString,
            int regionalControllerType)
        {
            const string sql = @"
                SELECT 
                    s.SignalID,
                    s.Latitude,
                    s.Longitude,
                    s.PrimaryName,
                    s.SecondaryName,
                    s.IPAddress,
                    s.RegionID,
                    s.ControllerTypeID,
                    s.Enabled,
                    s.VersionID,
                    s.VersionActionId,
                    s.Note,
                    s.Start,
                    s.JurisdictionId,
                    s.Pedsare1to1,
                    s.ConnType,
                    ct.ControllerTypeID,
                    ct.Description,
                    ct.SNMPPort,
                    ct.FTPDirectory,
                    ct.ActiveFTP,
                    ct.UserName,
                    ct.Password,
                    ct.PhysicalLocation
                FROM dbo.Signals s
                INNER JOIN dbo.ControllerTypes ct ON ct.ControllerTypeID = s.ControllerTypeID
                INNER JOIN (
                    SELECT SignalID, MAX(Start) AS LatestStart
                    FROM dbo.Signals
                    WHERE VersionActionId != 3
                    GROUP BY SignalID
                ) latest ON s.SignalID = latest.SignalID
                       AND s.Start = latest.LatestStart
                WHERE s.VersionActionId != 3
                  AND s.ControllerTypeID = @RegionalControllerType";

            using var db = new SqlConnection(connectionString);
            return db.Query<Signal, ControllerType, Signal>(
                sql,
                (signal, controllerType) =>
                {
                    signal.ControllerType = controllerType;
                    return signal;
                },
                new { RegionalControllerType = regionalControllerType },
                splitOn: "ControllerTypeID"
            ).ToList();
        }

        private static void LogEventToDb(string connectionString, string signalId, string function, string description)
        {
            try
            {
                using var db = new SqlConnection(connectionString);
                db.Execute(@"
                    INSERT INTO dbo.ApplicationEvents 
                        (ApplicationName, Class, Function, SeverityLevel, Description, Timestamp)
                    VALUES 
                        (@ApplicationName, @Class, @Function, @SeverityLevel, @Description, @Timestamp)",
                    new
                    {
                        ApplicationName = "SCPFromD4Controllers",
                        Class = "Program",
                        Function = function,
                        SeverityLevel = "Medium",
                        Description = description,
                        Timestamp = DateTime.Now
                    });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Signal {SignalID}: Failed to write to ApplicationEvents.", signalId);
            }
        }

        private sealed class ApplicationEventsSink : ILogEventSink
        {
            private const string ApplicationName = "SCPFromD4Controllers";
            private readonly string _connectionString;

            public ApplicationEventsSink(string connectionString)
            {
                _connectionString = connectionString;
            }

            public void Emit(LogEvent logEvent)
            {
                try
                {
                    using var db = new SqlConnection(_connectionString);
                    db.Execute(@"
                        INSERT INTO dbo.ApplicationEvents
                            (Timestamp, ApplicationName, Description, SeverityLevel, Class, Function)
                        VALUES
                            (@Timestamp, @ApplicationName, @Description, @SeverityLevel, @Class, @Function)",
                        new
                        {
                            Timestamp = logEvent.Timestamp.LocalDateTime,
                            ApplicationName,
                            Description = GetDescription(logEvent),
                            SeverityLevel = (int)ApplicationEvent.SeverityLevels.High,
                            Class = "Program",
                            Function = "ApplicationError"
                        });
                }
                catch
                {
                    // Avoid recursive logging if the database sink itself fails.
                }
            }

            private static string GetDescription(LogEvent logEvent)
            {
                var description = logEvent.RenderMessage();

                if (logEvent.Exception != null)
                    description += Environment.NewLine + logEvent.Exception;

                return description;
            }
        }

        // -------------------------------------------------------------------------
        // Connection detection
        // -------------------------------------------------------------------------

        private static string? GetOrDetectConnType(
            Signal signal, string connectionString, string host, string username, string password, bool requiresPpk)
        {
            string? existing = null;

            try
            {
                using var db = new SqlConnection(connectionString);
                existing = db.QueryFirstOrDefault<string>(
                    "SELECT ConnType FROM dbo.Signals WHERE SignalID = @SignalID",
                    new { signal.SignalID });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Signal {SignalID}: Failed to query ConnType.", signal.SignalID);
                return requiresPpk ? "sftp" : null;
            }

            if (requiresPpk)
            {
                if (!string.Equals(existing, "sftp", StringComparison.OrdinalIgnoreCase))
                    UpdateSignalConnType(signal, connectionString, "sftp");

                Log.Information("Signal {SignalID}: PPK is enabled; using ConnType 'sftp'.",
                    signal.SignalID);
                return "sftp";
            }

            if (!string.IsNullOrWhiteSpace(existing))
            {
                Log.Information("Signal {SignalID}: ConnType already '{ConnType}', skipping detection.",
                    signal.SignalID, existing);
                return existing;
            }

            Log.Information("Signal {SignalID}: ConnType is null, testing connections...", signal.SignalID);

            string? detected = null;

            if (TestSftpConnection(signal, host, username, password))
                detected = "sftp";
            else if (TestFtpConnection(signal, host, username, password))
                detected = "ftp";

            if (detected != null)
                UpdateSignalConnType(signal, connectionString, detected);
            else
            {
                Log.Warning("Signal {SignalID} @ {Host}: Neither SFTP nor FTP responded.",
                    signal.SignalID, host);
            }

            return detected;
        }

        private static void UpdateSignalConnType(Signal signal, string connectionString, string connType)
        {
            try
            {
                using var db = new SqlConnection(connectionString);
                int rows = db.Execute(
                    "UPDATE dbo.Signals SET ConnType = @ConnType WHERE SignalID = @SignalID",
                    new { ConnType = connType, signal.SignalID });
                Log.Information("Signal {SignalID}: ConnType set to '{ConnType}' ({Rows} row(s) affected).",
                    signal.SignalID, connType, rows);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Signal {SignalID}: Failed to update ConnType.", signal.SignalID);
            }
        }

        private static bool TestSftpConnection(Signal signal, string host, string username, string password)
        {
            try
            {
                using var sftp = new SftpClient(host, username, password);
                sftp.Connect();
                bool connected = sftp.IsConnected;
                sftp.Disconnect();
                Log.Information("Signal {SignalID}: SFTP test {Result}.",
                    signal.SignalID, connected ? "PASSED" : "FAILED");
                return connected;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Signal {SignalID} @ {Host}: SFTP test failed.", signal.SignalID, host);
                return false;
            }
        }

        private static bool TestFtpConnection(Signal signal, string host, string username, string password)
        {
            try
            {
                using var ftp = new AsyncFtpClient(host, username, password, config: new FtpConfig
                {
                    ConnectTimeout = 5000,
                    ReadTimeout = 5000,
                    ValidateAnyCertificate = true
                });

                Task.Run(async () => { await ftp.AutoConnect(); }).GetAwaiter().GetResult();

                bool connected = ftp.IsConnected;
                Task.Run(async () => await ftp.Disconnect()).GetAwaiter().GetResult();

                Log.Information("Signal {SignalID}: FTP test {Result}.",
                    signal.SignalID, connected ? "PASSED" : "FAILED");
                return connected;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Signal {SignalID} @ {Host}: FTP test failed.", signal.SignalID, host);
                return false;
            }
        }

        // -------------------------------------------------------------------------
        // Main entry per signal
        // -------------------------------------------------------------------------

        private static void GetD4Files(
            Signal signal, SignalFtpOptions options, string connectionString,
            IReadOnlyList<string> physicalLocationFallbacks)
        {
            string username = signal.ControllerType.UserName;
            string password = signal.ControllerType.Password;
            string host = signal.IPAddress;
            string localDirectory = Path.Combine(options.LocalDirectory, signal.SignalID) + @"\";
            string? connType = GetOrDetectConnType(signal, connectionString, host, username, password, options.RequiresPpk);
            var remoteDirectories = GetRemoteDirectories(signal, options, physicalLocationFallbacks);

            if (connType == null)
            {
                Log.Warning("Signal {SignalID} @ {Host}: No connection type detected. Skipping.",
                    signal.SignalID, host);
                return;
            }

            if (remoteDirectories.Count == 0)
            {
                Log.Warning("Signal {SignalID} @ {Host}: No remote directories configured. Skipping.",
                    signal.SignalID, host);
                return;
            }

            if (connType.Equals("sftp", StringComparison.OrdinalIgnoreCase))
            {
                string ppkLocation = options.PpkLocation;

                if (options.RequiresPpk)
                {
                    if (string.IsNullOrWhiteSpace(ppkLocation))
                    {
                        Log.Error("PPK is required but no PPK path is configured. Skipping signal {SignalID}.",
                            signal.SignalID);
                        return;
                    }

                    if (!File.Exists(ppkLocation))
                    {
                        Log.Error("PPK file not found at path: {PpkLocation}. Skipping signal {SignalID}.",
                            ppkLocation, signal.SignalID);
                        return;
                    }

                    FetchViaSftp(signal, options, host, username, password, remoteDirectories, localDirectory,
                        ppkLocation);
                }
                else
                {
                    FetchViaSftp(signal, options, host, username, password, remoteDirectories, localDirectory);
                }
            }
            else if (connType.Equals("ftp", StringComparison.OrdinalIgnoreCase))
            {
                FetchViaFtp(signal, options, host, username, password, remoteDirectories, localDirectory);
            }
        }

        private static IReadOnlyList<string> GetRemoteDirectories(
            Signal signal, SignalFtpOptions options, IReadOnlyList<string> physicalLocationFallbacks)
        {
            var remoteDirectories = new List<string>();

            if (!options.UsePhysicalLocation)
            {
                AddRemoteDirectory(remoteDirectories, signal.ControllerType.FTPDirectory);
                return remoteDirectories;
            }

            if (!string.IsNullOrWhiteSpace(signal.ControllerType.PhysicalLocation))
            {
                AddRemoteDirectory(remoteDirectories, signal.ControllerType.PhysicalLocation);
                Log.Information("Signal {SignalID}: Using controller physical location.",
                    signal.SignalID);
                return remoteDirectories;
            }

            foreach (var configuredLocation in physicalLocationFallbacks)
                AddRemoteDirectory(remoteDirectories, configuredLocation);

            if (remoteDirectories.Count > 0)
            {
                Log.Information(
                    "Signal {SignalID}: Controller physical location is null; using configured fallback location(s): {Locations}",
                    signal.SignalID, string.Join(", ", remoteDirectories));
            }
            else
            {
                Log.Warning(
                    "Signal {SignalID}: Controller physical location is null and no configured fallback locations were provided.",
                    signal.SignalID);
            }

            return remoteDirectories;
        }

        private static void AddRemoteDirectory(List<string> remoteDirectories, string? remoteDirectory)
        {
            if (string.IsNullOrWhiteSpace(remoteDirectory))
                return;

            var trimmedDirectory = remoteDirectory.Trim();

            if (!remoteDirectories.Contains(trimmedDirectory, StringComparer.OrdinalIgnoreCase))
                remoteDirectories.Add(trimmedDirectory);
        }

        private static void FetchViaSftp(
            Signal signal, SignalFtpOptions options, string host, string username, string password,
            IReadOnlyList<string> remoteDirectories, string localDirectory, string? ppkLocation = null)
        {
            SftpClient sftp;

            if (!string.IsNullOrWhiteSpace(ppkLocation))
            {
                try
                {
                    var keyFile = new PrivateKeyFile(ppkLocation, password);
                    var keyAuth = new PrivateKeyAuthenticationMethod(username, keyFile);
                    var connectionInfo = new ConnectionInfo(host, username, keyAuth);
                    sftp = new SftpClient(connectionInfo);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Signal {SignalID}: Failed to load PPK file from {PpkLocation}.",
                        signal.SignalID, ppkLocation);
                    return;
                }
            }
            else
            {
                sftp = new SftpClient(host, username, password);
            }

            using (sftp)
            {
                try
                {
                    Log.Information("Signal {SignalID}: Connecting via SFTP to {Host} {AuthMethod}.",
                        signal.SignalID, host,
                        ppkLocation != null ? "using PPK key" : "using password");

                    sftp.Connect();

                    var remoteDirectory = FindExistingSftpDirectory(sftp, signal, remoteDirectories);
                    if (remoteDirectory == null)
                        return;

                    var files = sftp.ListDirectory(remoteDirectory)
                        .Where(x => x.FullName.Contains(".dat")
                                    || x.FullName.Contains(".datZ")
                                    || x.FullName.Contains(".gz"))
                        .ToList();

                    Log.Information("Signal {SignalID}: Found {Count} file(s) via SFTP.", signal.SignalID, files.Count);

                    var currentLog = options.SkipCurrentLog
                        ? files.OrderByDescending(x => x.FullName).FirstOrDefault()
                        : null;

                    foreach (var file in files)
                    {
                        try
                        {
                            string localPath = Path.Combine(localDirectory, file.Name);
                            using var fs = File.OpenWrite(localPath);
                            sftp.DownloadFile(file.FullName, fs);
                            Log.Information("Signal {SignalID}: Downloaded {File}.", signal.SignalID, file.Name);

                            if (!options.DeleteAfterFtp)
                                continue;

                            if (options.SkipCurrentLog && currentLog == file)
                            {
                                Log.Information("Signal {SignalID}: Skipped deleting current SFTP log {File}.",
                                    signal.SignalID, file.FullName);
                                continue;
                            }

                            if (sftp.Exists(file.FullName))
                            {
                                sftp.DeleteFile(file.FullName);
                                Log.Information("Signal {SignalID}: Deleted remote SFTP file {File}.",
                                    signal.SignalID, file.FullName);
                            }
                            else
                            {
                                Log.Warning("Signal {SignalID}: Remote SFTP file {File} no longer exists.",
                                    signal.SignalID, file.FullName);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Signal {SignalID}: Failed to download {File}.", signal.SignalID, file.Name);
                            return;
                        }
                    }

                    sftp.Disconnect();
                    Log.Information("Signal {SignalID}: SFTP transfer complete.", signal.SignalID);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Signal {SignalID} @ {Host}: SFTP fetch error.", signal.SignalID, host);
                }
            }
        }

        private static string? FindExistingSftpDirectory(
            SftpClient sftp, Signal signal, IReadOnlyList<string> remoteDirectories)
        {
            foreach (var remoteDirectory in remoteDirectories)
            {
                try
                {
                    Log.Information("Signal {SignalID}: Checking remote directory: '{RemoteDir}'",
                        signal.SignalID, remoteDirectory);

                    if (sftp.Exists(remoteDirectory))
                    {
                        var attributes = sftp.GetAttributes(remoteDirectory);
                        if (attributes.IsDirectory)
                        {
                            Log.Information("Signal {SignalID}: Using remote directory: '{RemoteDir}'",
                                signal.SignalID, remoteDirectory);
                            return remoteDirectory;
                        }

                        Log.Warning("Signal {SignalID}: Remote path '{RemoteDir}' exists but is not a directory.",
                            signal.SignalID, remoteDirectory);
                    }
                    else
                    {
                        Log.Warning("Signal {SignalID}: Remote directory '{RemoteDir}' does not exist.",
                            signal.SignalID, remoteDirectory);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Signal {SignalID}: Could not check remote directory '{RemoteDir}'.",
                        signal.SignalID, remoteDirectory);
                }
            }

            Log.Warning("Signal {SignalID}: None of the remote directories exist: {RemoteDirs}",
                signal.SignalID, string.Join(", ", remoteDirectories));
            return null;
        }

        private static void FetchViaFtp(
            Signal signal, SignalFtpOptions options, string host, string username, string password,
            IReadOnlyList<string> remoteDirectories, string localDirectory)
        {
            try
            {
                Log.Information("Signal {SignalID}: Connecting via FTP to {Host}.", signal.SignalID, host);

                Task.Run(async () =>
                {
                    await using var ftp = new AsyncFtpClient(host, username, password, config: new FtpConfig
                    {
                        ConnectTimeout = 30000,
                        ReadTimeout = 180000,
                        DataConnectionConnectTimeout = 30000,
                        DataConnectionReadTimeout = 180000,
                        SocketKeepAlive = true,
                        RetryAttempts = 3,
                        ValidateAnyCertificate = true
                    });

                    await ftp.AutoConnect();

                    if (!ftp.IsConnected)
                    {
                        Log.Warning("Signal {SignalID}: FTP failed to connect to {Host}.", signal.SignalID, host);
                        return;
                    }

                    Log.Information("Signal {SignalID}: FTP connected. Exploring server structure...", signal.SignalID);

                    // --- Step 1: Log the root to understand the full directory structure ---
                    //await LogDirectoryStructure(ftp, signal, "/", 0, maxDepth: 3);

                    // --- Step 2: Resolve the configured remote directory ---
                    var remoteDirectory = await FindExistingFtpDirectory(ftp, signal, remoteDirectories);
                    if (remoteDirectory == null)
                        return;

                    // --- Step 3: Get listing with full symlink resolution ---
                    var filesToDownload = await ResolveFilesFromDirectory(ftp, signal, remoteDirectory);

                    Log.Information("Signal {SignalID}: Total files resolved for download: {Count}",
                        signal.SignalID, filesToDownload.Count);

                    if (filesToDownload.Count == 0)
                    {
                        Log.Warning("Signal {SignalID}: No matching files found. " +
                                    "Check logs above for directory structure and symlink targets.",
                            signal.SignalID);
                        return;
                    }

                    // --- Step 4: Download ---
                    foreach (var (remotePath, fileName) in filesToDownload)
                    {
                        try
                        {
                            string localFilePath = Path.Combine(localDirectory, fileName);
                            Log.Information("Signal {SignalID}: Downloading {RemotePath} -> {LocalPath}",
                                signal.SignalID, remotePath, localFilePath);

                            var status = await ftp.DownloadFile(localFilePath, remotePath);

                            if (status == FtpStatus.Success)
                            {
                                Log.Information("Signal {SignalID}: Downloaded {File} successfully.",
                                    signal.SignalID, fileName);

                                if (options.DeleteAfterFtp)
                                {
                                    await ftp.DeleteFile(remotePath);
                                    Log.Information("Signal {SignalID}: Deleted remote file {File}.",
                                        signal.SignalID, remotePath);
                                }
                            }
                            else
                            {
                                Log.Warning("Signal {SignalID}: Download of {File} returned status {Status}.",
                                    signal.SignalID, fileName, status);
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Signal {SignalID}: Error downloading {File}.", signal.SignalID, fileName);
                            return;
                        }
                    }

                    await ftp.Disconnect();
                    Log.Information("Signal {SignalID}: FTP transfer complete.", signal.SignalID);

                }).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Signal {SignalID} @ {Host}: FTP fetch error.", signal.SignalID, host);
            }
        }

        private static async Task<string?> FindExistingFtpDirectory(
            AsyncFtpClient ftp, Signal signal, IReadOnlyList<string> remoteDirectories)
        {
            foreach (var remoteDirectory in remoteDirectories)
            {
                try
                {
                    Log.Information("Signal {SignalID}: Checking remote directory: '{RemoteDir}'",
                        signal.SignalID, remoteDirectory);

                    if (await ftp.DirectoryExists(remoteDirectory))
                    {
                        Log.Information("Signal {SignalID}: Using remote directory: '{RemoteDir}'",
                            signal.SignalID, remoteDirectory);
                        return remoteDirectory;
                    }

                    Log.Warning("Signal {SignalID}: Remote directory '{RemoteDir}' does not exist.",
                        signal.SignalID, remoteDirectory);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Signal {SignalID}: Could not check remote directory '{RemoteDir}'.",
                        signal.SignalID, remoteDirectory);
                }
            }

            Log.Warning("Signal {SignalID}: None of the remote directories exist: {RemoteDirs}",
                signal.SignalID, string.Join(", ", remoteDirectories));
            return null;
        }

        /// <summary>
        /// Recursively logs the directory structure so you can see exactly 
        /// what the FTP server looks like, including symlinks and their targets.
        /// </summary>
        private static async Task LogDirectoryStructure(
            AsyncFtpClient ftp, Signal signal, string path, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;

            string indent = new string(' ', depth * 2);

            try
            {
                var listing = await ftp.GetListing(path, FtpListOption.ForceList | FtpListOption.Auto);

                if (!listing.Any())
                {
                    Log.Information("Signal {SignalID}: {Indent}[EMPTY] {Path}", signal.SignalID, indent, path);
                    return;
                }

                foreach (var item in listing)
                {
                    switch (item.Type)
                    {
                        case FtpObjectType.File:
                            Log.Information(
                                "Signal {SignalID}: {Indent}[FILE] {Name} ({Size} bytes, Modified: {Modified})",
                                signal.SignalID, indent, item.FullName, item.Size, item.Modified);
                            break;

                        case FtpObjectType.Directory:
                            Log.Information(
                                "Signal {SignalID}: {Indent}[DIR]  {Name}",
                                signal.SignalID, indent, item.FullName);
                            // Recurse into subdirectories
                            await LogDirectoryStructure(ftp, signal, item.FullName, depth + 1, maxDepth);
                            break;

                        case FtpObjectType.Link:
                            string target = item.LinkTarget ?? "UNKNOWN TARGET";
                            Log.Information(
                                "Signal {SignalID}: {Indent}[LINK] {Name} -> {Target}",
                                signal.SignalID, indent, item.FullName, target);

                            // Try to determine what the symlink points to
                            if (!string.IsNullOrWhiteSpace(item.LinkTarget))
                            {
                                try
                                {
                                    // Check if symlink target is a directory
                                    bool isDir = await ftp.DirectoryExists(item.LinkTarget);
                                    if (isDir)
                                    {
                                        Log.Information(
                                            "Signal {SignalID}: {Indent}       ^ symlink target is a DIRECTORY, listing...",
                                            signal.SignalID, indent);
                                        await LogDirectoryStructure(ftp, signal, item.LinkTarget, depth + 1, maxDepth);
                                    }
                                    else
                                    {
                                        Log.Information(
                                            "Signal {SignalID}: {Indent}       ^ symlink target appears to be a FILE",
                                            signal.SignalID, indent);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Warning(ex,
                                        "Signal {SignalID}: {Indent}       ^ could not resolve symlink target {Target}",
                                        signal.SignalID, indent, item.LinkTarget);
                                }
                            }

                            break;

                        default:
                            Log.Information(
                                "Signal {SignalID}: {Indent}[{Type}] {Name}",
                                signal.SignalID, indent, item.Type, item.FullName);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Signal {SignalID}: Could not list directory '{Path}'.", signal.SignalID, path);
            }
        }

        /// <summary>
        /// Resolves all downloadable .dat/.datZ/.gz files from a directory,
        /// following symlinks whether they point to files or directories.
        /// Returns a list of (remotePath, fileName) tuples ready for download.
        /// </summary>
        private static async Task<List<(string RemotePath, string FileName)>> ResolveFilesFromDirectory(
            AsyncFtpClient ftp, Signal signal, string directory)
        {
            var results = new List<(string, string)>();

            try
            {
                var listing = await ftp.GetListing(directory, FtpListOption.ForceList | FtpListOption.Auto);

                Log.Information("Signal {SignalID}: Listed '{Dir}' — {Count} item(s) found.",
                    signal.SignalID, directory, listing.Length);

                foreach (var item in listing)
                {
                    Log.Debug("Signal {SignalID}: Item — Name={Name}, Type={Type}, LinkTarget={LinkTarget}",
                        signal.SignalID, item.Name, item.Type, item.LinkTarget ?? "N/A");

                    if (item.Type == FtpObjectType.File && IsTargetFile(item.Name))
                    {
                        // Plain file — add directly
                        Log.Debug("Signal {SignalID}: Adding plain file {File}", signal.SignalID, item.FullName);
                        results.Add((item.FullName, item.Name));
                    }
                    else if (item.Type == FtpObjectType.Link)
                    {
                        string target = item.LinkTarget;

                        if (string.IsNullOrWhiteSpace(target))
                        {
                            Log.Warning("Signal {SignalID}: Symlink {Name} has no resolvable target. Skipping.",
                                signal.SignalID, item.Name);
                            continue;
                        }

                        // Normalize relative symlink paths
                        if (!target.StartsWith("/"))
                        {
                            target = $"{directory.TrimEnd('/')}/{target}";
                            Log.Debug("Signal {SignalID}: Resolved relative symlink to {Target}",
                                signal.SignalID, target);
                        }

                        // Check if symlink target is a directory
                        bool targetIsDirectory = false;
                        try
                        {
                            targetIsDirectory = await ftp.DirectoryExists(target);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex,
                                "Signal {SignalID}: Could not check if symlink target is directory: {Target}",
                                signal.SignalID, target);
                        }

                        if (targetIsDirectory)
                        {
                            // Symlink points to a directory — recurse into it
                            Log.Information(
                                "Signal {SignalID}: Symlink {Name} points to directory {Target}, recursing...",
                                signal.SignalID, item.Name, target);
                            var subFiles = await ResolveFilesFromDirectory(ftp, signal, target);
                            results.AddRange(subFiles);
                        }
                        else
                        {
                            // Symlink points to a file — check if it matches
                            if (IsTargetFile(item.Name) || IsTargetFile(target))
                            {
                                Log.Debug("Signal {SignalID}: Adding symlinked file {Link} -> {Target}",
                                    signal.SignalID, item.FullName, target);
                                results.Add((target, item.Name));
                            }
                            else
                            {
                                Log.Debug(
                                    "Signal {SignalID}: Symlink {Name} -> {Target} is not a target file type, skipping.",
                                    signal.SignalID, item.Name, target);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Signal {SignalID}: Error resolving files from directory '{Dir}'.",
                    signal.SignalID, directory);
            }

            return results;
        }

        private static bool IsTargetFile(string name)
        {
            return name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                   || name.EndsWith(".datZ", StringComparison.OrdinalIgnoreCase)
                   || name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
        }
    }
}
