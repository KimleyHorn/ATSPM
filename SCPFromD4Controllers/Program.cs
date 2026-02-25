using System.Configuration;
using Dapper;
using FluentFTP;
using Microsoft.Data.SqlClient;
using MOE.Common.Business;
using Renci.SshNet;
using Serilog;
using MOE.Common.Models;

namespace SCPFromD4Controllers
{
    class Program
    {
        static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "d4-controllers-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30)
                .CreateLogger();

            try
            {
                Log.Information("Application starting up at {Time}", DateTime.Now);

                var connectionString = ConfigurationManager.ConnectionStrings["SPM"].ConnectionString;
                var credentialsPath = ConfigurationManager.AppSettings["SFTP_CREDENTIALS_FILE_PATH"];

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
                    Convert.ToBoolean(ConfigurationManager.AppSettings["IsGzip"])
                );

                Log.Information("Querying signal list from database...");
                var signalList = GetLatestVersionOfAllSignalsForD4Sftp(connectionString);
                var maxThreads = Convert.ToInt32(ConfigurationManager.AppSettings["MaxThreads"]);
                Log.Information("Found {Count} signal(s) to process.", signalList.Count);

                if (signalFtpOptions.RequiresPpk)
                {
                    foreach (var signal in signalList)
                    {
                        try
                        {
                            EnsureLocalDirectory(signalFtpOptions.LocalDirectory, signal.SignalID);

                            try
                            {
                                //var signalFtp = new SignalFtp(signal, signalFtpOptions);

                                if (!Directory.Exists(signalFtpOptions.LocalDirectory + signal.SignalID))
                                {
                                    Directory.CreateDirectory(signalFtpOptions.LocalDirectory + signal.SignalID);
                                }

                                try
                                {
                                    //signalFtp.GetCubicFilesAsyncPpk(signalFtpOptions.PpkLocation,
                                    //    true);
                                }
                                catch (AggregateException ex)
                                {
                                    Console.WriteLine("Error At Highest Level for signal " + ex.Message);
                                }

                            }
                            catch (AggregateException ex)
                            {
                                Console.WriteLine("Error At Highest Level for signal " + ex.Message);

                            }

                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Signal {SignalID}: Error in PPK path.", signal.SignalID);
                        }
                    }
                }
                else
                {
                    var options = new ParallelOptions { MaxDegreeOfParallelism = maxThreads };

                    Parallel.ForEach(signalList, options, signal =>
                    {
                        try
                        {
                            EnsureLocalDirectory(signalFtpOptions.LocalDirectory, signal.SignalID);
                            GetD4Files(signal, signalFtpOptions, credentialsPath, connectionString);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Error at highest level for signal {SignalID}.", signal.SignalID);
                        }
                    });
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

        // -------------------------------------------------------------------------
        // DB
        // -------------------------------------------------------------------------

        public static List<Signal> GetLatestVersionOfAllSignalsForD4Sftp(string connectionString)
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
                    ct.Password
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
                  AND s.ControllerTypeID = 30";

            using var db = new SqlConnection(connectionString);
            return db.Query<Signal, ControllerType, Signal>(
                sql,
                (signal, controllerType) =>
                {
                    signal.ControllerType = controllerType;
                    return signal;
                },
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

        // -------------------------------------------------------------------------
        // Connection detection
        // -------------------------------------------------------------------------

        private static string GetOrDetectConnType(
            Signal signal, string connectionString, string host, string username, string password)
        {
            try
            {
                using var db = new SqlConnection(connectionString);
                var existing = db.QueryFirstOrDefault<string>(
                    "SELECT ConnType FROM dbo.Signals WHERE SignalID = @SignalID",
                    new { signal.SignalID });

                if (!string.IsNullOrWhiteSpace(existing))
                {
                    Log.Information("Signal {SignalID}: ConnType already '{ConnType}', skipping detection.",
                        signal.SignalID, existing);
                    return existing;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Signal {SignalID}: Failed to query ConnType.", signal.SignalID);
                return null;
            }

            Log.Information("Signal {SignalID}: ConnType is null, testing connections...", signal.SignalID);

            string detected = null;

            if (TestSftpConnection(signal, host, username, password))
                detected = "sftp";
            else if (TestFtpConnection(signal, host, username, password))
                detected = "ftp";

            if (detected != null)
            {
                try
                {
                    using var db = new SqlConnection(connectionString);
                    int rows = db.Execute(
                        "UPDATE dbo.Signals SET ConnType = @ConnType WHERE SignalID = @SignalID",
                        new { ConnType = detected, signal.SignalID });
                    Log.Information("Signal {SignalID}: ConnType set to '{ConnType}' ({Rows} row(s) affected).",
                        signal.SignalID, detected, rows);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Signal {SignalID}: Failed to update ConnType.", signal.SignalID);
                }
            }
            else
            {
                Log.Warning("Signal {SignalID} @ {Host}: Neither SFTP nor FTP responded.",
                    signal.SignalID, host);
            }

            return detected;
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
            Signal signal, SignalFtpOptions options, string credentialsPath, string connectionString)
        {
            string username = null;
            string password = null;

            try
            {
                using var reader = new StreamReader(credentialsPath);
                username = reader.ReadLine();
                password = reader.ReadLine();
            }
            catch (FileNotFoundException ex)
            {
                Log.Error(ex, "Credentials file not found: {Path}", credentialsPath);
                return;
            }
            catch (IOException ex)
            {
                Log.Error(ex, "I/O error reading credentials: {Path}", credentialsPath);
                return;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error reading credentials: {Path}", credentialsPath);
                return;
            }

            string host = signal.IPAddress;
            string remoteDirectory = signal.ControllerType.FTPDirectory;
            string localDirectory = Path.Combine(options.LocalDirectory, signal.SignalID) + @"\";
            string connType = GetOrDetectConnType(signal, connectionString, host, username, password);

            if (connType == null)
            {
                Log.Warning("Signal {SignalID} @ {Host}: No connection type detected. Skipping.",
                    signal.SignalID, host);
                return;
            }

            if (connType.Equals("sftp", StringComparison.OrdinalIgnoreCase))
                FetchViaSftp(signal, host, username, password, remoteDirectory, localDirectory);
            else if (connType.Equals("ftp", StringComparison.OrdinalIgnoreCase))
                FetchViaFtp(signal, options, host, username, password, remoteDirectory, localDirectory);
        }

        // -------------------------------------------------------------------------
        // SFTP
        // -------------------------------------------------------------------------

        private static void FetchViaSftp(
            Signal signal, string host, string username, string password,
            string remoteDirectory, string localDirectory)
        {
            using var sftp = new SftpClient(host, username, password);
            try
            {
                Log.Information("Signal {SignalID}: Connecting via SFTP to {Host}.", signal.SignalID, host);
                sftp.Connect();

                var files = sftp.ListDirectory(remoteDirectory)
                    .Where(x => x.FullName.Contains(".dat")
                                || x.FullName.Contains(".datZ")
                                || x.FullName.Contains(".gz"))
                    .ToList();

                Log.Information("Signal {SignalID}: Found {Count} file(s) via SFTP.", signal.SignalID, files.Count);

                foreach (var file in files)
                {
                    try
                    {
                        string localPath = Path.Combine(localDirectory, file.Name);
                        using var fs = File.OpenWrite(localPath);
                        sftp.DownloadFile(file.FullName, fs);
                        Log.Information("Signal {SignalID}: Downloaded {File}.", signal.SignalID, file.Name);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Signal {SignalID}: Failed to download {File}.", signal.SignalID, file.Name);
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

        // -------------------------------------------------------------------------
        // FTP
        // -------------------------------------------------------------------------

        private static void FetchViaFtp(
            Signal signal, SignalFtpOptions options, string host, string username, string password,
            string remoteDirectory, string localDirectory)
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
                    Log.Information("Signal {SignalID}: Checking configured remote directory: '{RemoteDir}'",
                        signal.SignalID, remoteDirectory);

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
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Signal {SignalID}: Error downloading {File}.", signal.SignalID, fileName);
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
//using MOE.Common.Business;
//using MOE.Common.Models;
//using System.Configuration;
//using System.Data;
//using System.Net;
//using Dapper;
//using FluentFTP;
//using Microsoft.Data.SqlClient;
//using Serilog;

//namespace SCPFromD4Controllers
//{
//    class Program
//    {

//        static void Main(string[] args)
//        {

//            try
//            {
//                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "alive.txt"),
//                    "App started at: " + DateTime.Now);

//                // BREADCRUMB 1 - before config reads
//                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "step1.txt"),
//                    "Reading config at: " + DateTime.Now);

//                var connectionString = ConfigurationManager.ConnectionStrings["SPM"].ConnectionString;
//                var credentialsPath = ConfigurationManager.AppSettings["SFTP_CREDENTIALS_FILE_PATH"];
//                var localDirectory = ConfigurationManager.AppSettings["LocalDirectory"];

//                // BREADCRUMB 2 - config read successfully
//                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "step2.txt"),
//                    $"Config read OK at: {DateTime.Now}\nConnString null: {connectionString == null}\nCredPath: {credentialsPath}\nLocalDir: {localDirectory}");

//                // BREADCRUMB 3 - before Serilog init
//                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "step3.txt"),
//                    "Initializing Serilog at: " + DateTime.Now);

//                Log.Logger = new LoggerConfiguration()
//                    .MinimumLevel.Debug()
//                    .WriteTo.File(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "startup-.log"),
//                        rollingInterval: RollingInterval.Day)
//                    .CreateLogger();

//                // BREADCRUMB 4 - Serilog initialized
//                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "step4.txt"),
//                    "Serilog initialized at: " + DateTime.Now);

//                Log.Information("Application starting up...");

//                // BREADCRUMB 5 - before DB call
//                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "step5.txt"),
//                    "About to query DB at: " + DateTime.Now);
//                var signalFtpOptions = new SignalFtpOptions(
//                    Convert.ToInt32(ConfigurationManager.AppSettings["SNMPTimeout"]),
//                    Convert.ToInt32(ConfigurationManager.AppSettings["SNMPRetry"]),
//                    Convert.ToInt32(ConfigurationManager.AppSettings["SNMPPort"]),
//                    Convert.ToBoolean(ConfigurationManager.AppSettings["DeleteFilesAfterFTP"]),
//                    ConfigurationManager.AppSettings["LocalDirectory"],
//                    Convert.ToInt32(ConfigurationManager.AppSettings["FTPConnectionTimeoutInSeconds"]),
//                    Convert.ToInt32(ConfigurationManager.AppSettings["FTPReadTimeoutInSeconds"]),
//                    Convert.ToBoolean(ConfigurationManager.AppSettings["skipCurrentLog"]),
//                    Convert.ToBoolean(ConfigurationManager.AppSettings["RenameDuplicateFiles"]),
//                    Convert.ToInt32(ConfigurationManager.AppSettings["waitBetweenFileDownloadMilliseconds"]),
//                    Convert.ToInt32(ConfigurationManager.AppSettings["MaximumNumberOfFilesTransferAtOneTime"]),
//                    Convert.ToBoolean(ConfigurationManager.AppSettings["RequiresPPK"]),
//                    ConfigurationManager.AppSettings["PPKLocation"],
//                    Convert.ToInt32(ConfigurationManager.AppSettings["RegionalControllerType"]),
//                    ConfigurationManager.AppSettings["SshFingerprint"],
//                    Convert.ToBoolean(ConfigurationManager.AppSettings["IsGzip"])
//                );
//                var connection = (ConfigurationManager.ConnectionStrings["SPM"].ConnectionString);
//                var signalList = GetLatestVersionOfAllSignalsForD4Sftp(connection);
//                // your existing signal list query here

//                // BREADCRUMB 6 - DB query succeeded
//                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "step6.txt"),
//                    $"DB query OK at: {DateTime.Now}, Signal count: {signalList.Count}");

//                // rest of your existing code...
//                //get the
//                var maxThreads = Convert.ToInt32(ConfigurationManager.AppSettings["MaxThreads"]);

//                var options = new ParallelOptions { MaxDegreeOfParallelism = maxThreads };
//                if (signalFtpOptions.RequiresPpk)
//                {

//                    //Parallel.ForEach(signals.AsEnumerable(), options, signal =>
//                    foreach (var signal in signalList)
//                    {
//                        try
//                        {
//                            //var signalFtp = new SignalFtp(signal, signalFtpOptions);

//                            if (!Directory.Exists(signalFtpOptions.LocalDirectory + signal.SignalID))
//                            {
//                                Directory.CreateDirectory(signalFtpOptions.LocalDirectory + signal.SignalID);
//                            }

//                            try
//                            {
//                                //signalFtp.GetCubicFilesAsyncPpk(signalFtpOptions.PpkLocation,
//                                //    true);
//                            }
//                            catch (AggregateException ex)
//                            {
//                                Console.WriteLine("Error At Highest Level for signal " + ex.Message);
//                            }

//                        }
//                        catch (AggregateException ex)
//                        {
//                            Console.WriteLine("Error At Highest Level for signal " + ex.Message);

//                        }
//                    }
//                }
//                else
//                {
//                    foreach (var signal in signalList)
//                    {
//                        try
//                        {
//                            var signalFtp = new SignalFtp(signal, signalFtpOptions);

//                            if (!Directory.Exists(signalFtpOptions.LocalDirectory + signal.SignalID))
//                            {
//                                Directory.CreateDirectory(signalFtpOptions.LocalDirectory + signal.SignalID);
//                            }

//                            signalFtp.GetD4Files(
//                                ConfigurationManager.AppSettings["SFTP_CREDENTIALS_FILE_PATH"],
//                                ConfigurationManager.ConnectionStrings["SPM"].ConnectionString);
//                        }
//                        catch (Exception ex)
//                        {
//                            Log.Error(ex, "Error at highest level for signal {SignalID}.", signal.SignalID);
//                        }
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.txt"),
//                    $"Crashed at: {DateTime.Now}\n\nException:\n{ex.ToString()}\n\nInner:\n{ex.InnerException?.ToString()}");
//            }

//            Log.Logger = new LoggerConfiguration()
//                .MinimumLevel.Debug()
//                .WriteTo.File(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "startup-crash-.log"),
//                    rollingInterval: RollingInterval.Day,
//                    retainedFileCountLimit: 7).CreateLogger();

//        }

//        public static List<MOE.Common.Models.Signal> GetLatestVersionOfAllSignalsForD4Sftp(string connectionString)
//        {
//            const string sql = @"
//        SELECT 
//            s.SignalID,
//            s.Latitude,
//            s.Longitude,
//            s.PrimaryName,
//            s.SecondaryName,
//            s.IPAddress,
//            s.RegionID,
//            s.ControllerTypeID,
//            s.Enabled,
//            s.VersionID,
//            s.VersionActionId,
//            s.Note,
//            s.Start,
//            s.JurisdictionId,
//            s.Pedsare1to1,
//            s.ConnType,
//            ct.ControllerTypeID,
//            ct.Description,
//            ct.SNMPPort,
//            ct.FTPDirectory,
//            ct.ActiveFTP,
//            ct.UserName,
//            ct.Password
//        FROM dbo.Signals s
//        INNER JOIN dbo.ControllerTypes ct ON ct.ControllerTypeID = s.ControllerTypeID
//        INNER JOIN (
//            SELECT SignalID, MAX(Start) AS LatestStart
//            FROM dbo.Signals
//            WHERE VersionActionId != 3
//            GROUP BY SignalID
//        ) latest ON s.SignalID = latest.SignalID
//               AND s.Start = latest.LatestStart
//        WHERE s.VersionActionId != 3
//          AND s.ControllerTypeID = 30";

//            using (var db = new SqlConnection(connectionString))
//            {
//                var signals = db.Query<MOE.Common.Models.Signal, ControllerType, MOE.Common.Models.Signal>(
//                    sql,
//                    (signal, controllerType) =>
//                    {
//                        signal.ControllerType = controllerType;
//                        return signal;
//                    },
//                    splitOn: "ControllerTypeID"
//                ).ToList();

//                return signals;
//            }
//        }
        

//        public void GetD4Files(string filePath, string connectionString)
//        {
//            string username = null;
//            string password = null;

//            try
//            {
//                using (var reader = new StreamReader(filePath))
//                {
//                    username = reader.ReadLine();
//                    password = reader.ReadLine();
//                }
//            }
//            catch (FileNotFoundException ex)
//            {
//                Log.Error(ex, "Credentials file not found at path: {FilePath}", filePath);
//                return;
//            }
//            catch (IOException ex)
//            {
//                Log.Error(ex, "I/O error reading credentials file at path: {FilePath}", filePath);
//                return;
//            }
//            catch (Exception ex)
//            {
//                Log.Error(ex, "Unexpected error reading credentials file at path: {FilePath}", filePath);
//                return;
//            }

//            string host = Signal.IPAddress;
//            string remoteDirectory = Signal.ControllerType.FTPDirectory;
//            string localDirectory = SignalFtpOptions.LocalDirectory + Signal.SignalID + @"\";

//            string connType = GetOrDetectConnType(connectionString, host, username, password);

//            if (connType == null)
//            {
//                Log.Warning("Signal {SignalID} @ {Host}: Could not establish FTP or SFTP connection. Skipping.",
//                    Signal.SignalID, host);
//                return;
//            }

//            if (connType.Equals("sftp", StringComparison.OrdinalIgnoreCase))
//            {
//                FetchViaSftp(host, username, password, remoteDirectory, localDirectory);
//            }
//            else if (connType.Equals("ftp", StringComparison.OrdinalIgnoreCase))
//            {
//                FetchViaFtp(host, username, password, remoteDirectory, localDirectory);
//            }
//        }

//        private string GetOrDetectConnType(string connectionString, string host, string username, string password)
//        {
//            try
//            {
//                using (var db = new SqlConnection(connectionString))
//                {
//                    string existingConnType = db.QueryFirstOrDefault<string>(
//                        "SELECT ConnType FROM dbo.Signals WHERE SignalID = @SignalID",
//                        new { SignalID = Signal.SignalID });

//                    if (!string.IsNullOrWhiteSpace(existingConnType))
//                    {
//                        Log.Information("Signal {SignalID}: ConnType already set to '{ConnType}', skipping detection.",
//                            Signal.SignalID, existingConnType);
//                        return existingConnType;
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                Log.Error(ex, "Signal {SignalID}: Failed to query ConnType from database.", Signal.SignalID);
//                return null;
//            }

//            Log.Information("Signal {SignalID}: ConnType is null, testing connections...", Signal.SignalID);

//            string detectedType = null;

//            if (TestSftpConnection(host, username, password))
//            {
//                detectedType = "sftp";
//            }
//            else if (TestFtpConnection(host, username, password))
//            {
//                detectedType = "ftp";
//            }

//            if (detectedType != null)
//            {
//                try
//                {
//                    using (var db = new SqlConnection(connectionString))
//                    {
//                        int rows = db.Execute(
//                            "UPDATE dbo.Signals SET ConnType = @ConnType WHERE SignalID = @SignalID",
//                            new { ConnType = detectedType, SignalID = Signal.SignalID });

//                        Log.Information("Signal {SignalID}: Updated ConnType to '{ConnType}' ({Rows} row(s) affected).",
//                            Signal.SignalID, detectedType, rows);
//                    }
//                }
//                catch (Exception ex)
//                {
//                    Log.Error(ex, "Signal {SignalID}: Failed to update ConnType in database.", Signal.SignalID);
//                }
//            }
//            else
//            {
//                Log.Warning("Signal {SignalID} @ {Host}: Neither SFTP nor FTP responded. ConnType left as null.",
//                    Signal.SignalID, host);
//            }

//            return detectedType;
//        }

//        private bool TestSftpConnection(string host, string username, string password)
//        {
//            try
//            {
//                using (var sftp = new SftpClient(host, username, password))
//                {
//                    sftp.Connect();
//                    bool connected = sftp.IsConnected;
//                    sftp.Disconnect();
//                    Log.Information("Signal {SignalID}: SFTP connection test {Result}.",
//                        Signal.SignalID, connected ? "PASSED" : "FAILED");
//                    return connected;
//                }
//            }
//            catch (Exception ex)
//            {
//                Log.Warning(ex, "Signal {SignalID} @ {Host}: SFTP connection test failed.", Signal.SignalID, host);
//                return false;
//            }
//        }

//        private bool TestFtpConnection(string host, string username, string password)
//        {
//            try
//            {
//                var request = (FtpWebRequest)WebRequest.Create($"ftp://{host}/");
//                request.Method = WebRequestMethods.Ftp.ListDirectory;
//                request.Credentials = new NetworkCredential(username, password);
//                request.Timeout = 5000;

//                using (var response = (FtpWebResponse)request.GetResponse())
//                {
//                    bool success = response.StatusCode == FtpStatusCode.OpeningData
//                                   || response.StatusCode == FtpStatusCode.DataAlreadyOpen;
//                    Log.Information("Signal {SignalID}: FTP connection test {Result} — Status: {StatusDescription}",
//                        Signal.SignalID, success ? "PASSED" : "FAILED", response.StatusDescription);
//                    return success;
//                }
//            }
//            catch (Exception ex)
//            {
//                Log.Warning(ex, "Signal {SignalID} @ {Host}: FTP connection test failed.", Signal.SignalID, host);
//                return false;
//            }
//        }

//        private void FetchViaSftp(string host, string username, string password, string remoteDirectory,
//            string localDirectory)
//        {

//            using (var sftp = new SftpClient(host, username, password))
//            {
//                try
//                {
//                    Log.Information("Signal {SignalID}: Connecting via SFTP to {Host}.", Signal.SignalID, host);
//                    sftp.Connect();

//                    var files = sftp.ListDirectory(remoteDirectory)
//                        .Where(x => x.FullName.Contains(".dat")
//                                    || x.FullName.Contains(".datZ")
//                                    || x.FullName.Contains(".gz"))
//                        .ToList();

//                    Log.Information("Signal {SignalID}: Found {FileCount} D4 file(s) via SFTP.", Signal.SignalID,
//                        files.Count);
//                    TransferCubicFiles(files, localDirectory, sftp);
//                    sftp.Disconnect();
//                    Log.Information("Signal {SignalID}: SFTP transfer complete.", Signal.SignalID);
//                }
//                catch (Exception ex)
//                {
//                    Log.Error(ex, "Signal {SignalID} @ {Host}: Error during SFTP file fetch.", Signal.SignalID, host);
//                }
//            }
//        }
//        private void FetchViaFtp(string host, string username, string password, string remoteDirectory,
//            string localDirectory)
//        {
//            DiagnoseFtpConnection(host, username, password);
//            try
//            {
//                Log.Information("Signal {SignalID}: Connecting via FTP to {Host}.", Signal.SignalID, host);

//                var config = new FtpConfig
//                {
//                    ConnectTimeout = 30000,
//                    ReadTimeout = 180000,
//                    DataConnectionConnectTimeout = 30000,
//                    DataConnectionReadTimeout = 180000,
//                    SocketKeepAlive = true,
//                    RetryAttempts = 3
//                };

//                using (var ftp = new FtpClient(host, username, password, config: config))
//                {
//                    // Use AutoConnect instead of Connect — it probes the server 
//                    // for the best connection settings rather than assuming defaults
//                    ftp.AutoConnect();

//                    if (!ftp.IsConnected)
//                    {
//                        Log.Warning("Signal {SignalID}: FTP client failed to connect to {Host}.", Signal.SignalID,
//                            host);
//                        return;
//                    }

//                    Log.Information("Signal {SignalID}: FTP connected to {Host}.", Signal.SignalID, host);

//                    var files = ftp.GetListing(remoteDirectory)
//                        .Where(x => x.Type == FtpObjectType.File &&
//                                    (x.Name.Contains(".dat") ||
//                                     x.Name.Contains(".datZ") ||
//                                     x.Name.Contains(".gz")))
//                        .ToList();

//                    Log.Information("Signal {SignalID}: Found {FileCount} D4 file(s) via FTP.",
//                        Signal.SignalID, files.Count);

//                    foreach (var file in files)
//                    {
//                        try
//                        {
//                            string localFilePath = Path.Combine(localDirectory, file.Name);
//                            var status = ftp.DownloadFile(localFilePath, file.FullName);

//                            if (status == FtpStatus.Success)
//                            {
//                                Log.Information("Signal {SignalID}: Downloaded {FileName}.",
//                                    Signal.SignalID, file.Name);

//                                if (ConfigurationManager.AppSettings["DeleteFilesAfterFTP"] == "true")
//                                {
//                                    ftp.DeleteFile(file.FullName);
//                                    Log.Information("Signal {SignalID}: Deleted remote file {FileName}.",
//                                        Signal.SignalID, file.Name);
//                                }
//                            }
//                            else
//                            {
//                                Log.Warning("Signal {SignalID}: Failed to download {FileName}, status: {Status}.",
//                                    Signal.SignalID, file.Name, status);
//                            }
//                        }
//                        catch (Exception ex)
//                        {
//                            Log.Error(ex, "Signal {SignalID}: Error downloading file {FileName}.",
//                                Signal.SignalID, file.Name);
//                        }
//                    }

//                    ftp.Disconnect();
//                    Log.Information("Signal {SignalID}: FTP transfer complete.", Signal.SignalID);
//                }
//            }
//            catch (Exception ex)
//            {
//                Log.Error(ex, "Signal {SignalID} @ {Host}: Error during FTP file fetch.", Signal.SignalID, host);
//            }
//        }
//        private void DiagnoseFtpConnection(string host, string username, string password)
//        {
//            Log.Information("Signal {SignalID}: Starting FTP diagnostics for {Host}", Signal.SignalID, host);

//            // Test 1: Raw TCP connection
//            try
//            {
//                using (var tcp = new System.Net.Sockets.TcpClient())
//                {
//                    tcp.Connect(host, 21);
//                    Log.Information("Signal {SignalID}: TCP port 21 is OPEN on {Host}", Signal.SignalID, host);
//                }
//            }
//            catch (Exception ex)
//            {
//                Log.Error(ex, "Signal {SignalID}: TCP port 21 is CLOSED or unreachable on {Host}", Signal.SignalID,
//                    host);
//            }

//            // Test 2: Try every combination FluentFTP supports
//            var encryptionModes = new[]
//            {
//                FtpEncryptionMode.None,
//                FtpEncryptionMode.Auto,
//                FtpEncryptionMode.Explicit,
//                FtpEncryptionMode.Implicit
//            };

//            var dataConnTypes = new[]
//            {
//                FtpDataConnectionType.PASV,
//                FtpDataConnectionType.PORT,
//                FtpDataConnectionType.EPSV,
//                FtpDataConnectionType.AutoPassive,
//                FtpDataConnectionType.AutoActive
//            };

//            foreach (var encryption in encryptionModes)
//            {
//                foreach (var dataConn in dataConnTypes)
//                {
//                    try
//                    {
//                        var config = new FtpConfig
//                        {
//                            EncryptionMode = encryption,
//                            DataConnectionType = dataConn,
//                            ConnectTimeout = 5000,
//                            ReadTimeout = 5000,
//                            ValidateAnyCertificate = true
//                        };

//                        using (var ftp = new FtpClient(host, username, password, config: config))
//                        {
//                            ftp.Connect();
//                            if (ftp.IsConnected)
//                            {
//                                Log.Information(
//                                    "Signal {SignalID}: SUCCESS with Encryption={Encryption}, DataConn={DataConn}",
//                                    signal.SignalID, encryption, dataConn);
//                                ftp.Disconnect();
//                                return; // Log the first working combo and stop
//                            }
//                        }
//                    }
//                    catch (Exception ex)
//                    {
//                        Log.Warning(
//                            "Signal {SignalID}: FAILED with Encryption={Encryption}, DataConn={DataConn} — {Message}",
//                            Signal.SignalID, encryption, dataConn, ex.Message);
//                    }
//                }
//            }

//            Log.Error("Signal {SignalID}: All connection combinations failed for {Host}", Signal.SignalID, host);
//        }


//    }
//}