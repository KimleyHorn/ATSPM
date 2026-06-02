using System.Configuration;
using Dapper;
using FluentFTP;
using Microsoft.Data.SqlClient;
using MOE.Common.Business;
using Renci.SshNet;
using Serilog;
using MOE.Common.Models;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

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

                Log.Information("Querying signal list from database...");
                var signalList = GetLatestVersionOfAllSignalsForD4Sftp(connectionString);
                var maxThreads = Convert.ToInt32(ConfigurationManager.AppSettings["MaxThreads"]);
                Log.Information("Found {Count} signal(s) to process.", signalList.Count);

                try
                {
                    var options = new ParallelOptions { MaxDegreeOfParallelism = maxThreads };

                    Parallel.ForEach(signalList, options, signal =>
                    {
                        try
                        {
                            EnsureLocalDirectory(signalFtpOptions.LocalDirectory, signal.SignalID);
                            GetD4Files(signal, signalFtpOptions, connectionString);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Error at highest level for signal {SignalID}.", signal.SignalID);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error while processing signal list.");
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
            Signal signal, SignalFtpOptions options, string connectionString)
        {
            string username = signal.ControllerType.UserName;
            string password = signal.ControllerType.Password;
            string host = signal.IPAddress;
            string remoteDirectory = signal.ControllerType.FTPDirectory;
            string localDirectory = Path.Combine(options.LocalDirectory, signal.SignalID) + @"\";
            string connType = GetOrDetectConnType(signal, connectionString, host, username, password);

            //here is the switch on the physical location. If the UsePhysicalLocation flag is set, we will override the remote directory with the physical location of the controller
            if (options.UsePhysicalLocation)
            {
                remoteDirectory = signal.ControllerType.PhysicalLocation;
                Log.Information("Signal {SignalID}: Using physical location for connection: {Host}",
                    signal.SignalID, host);
            }

            if (connType == null)
            {
                Log.Warning("Signal {SignalID} @ {Host}: No connection type detected. Skipping.",
                    signal.SignalID, host);
                return;
            }

            if (connType.Equals("sftp", StringComparison.OrdinalIgnoreCase))
            {
                string ppkLocation = options.PpkLocation;

                if (options.RequiresPpk)
                {
                    if (string.IsNullOrWhiteSpace(ppkLocation) || !File.Exists(ppkLocation))
                    {
                        Log.Error("PPK is required, but the file was not found at path: {PpkLocation}. Skipping signal {SignalID}.",
                            ppkLocation, signal.SignalID);
                        return;
                    }

                    FetchViaSftp(signal, host, username, password, remoteDirectory, localDirectory, options.DeleteAfterFtp, ppkLocation);
                }
                else
                {
                    FetchViaSftp(signal, host, username, password, remoteDirectory, localDirectory, options.DeleteAfterFtp);
                }
            }
            else if (connType.Equals("ftp", StringComparison.OrdinalIgnoreCase))
            {
                FetchViaFtp(signal, options, host, username, password, remoteDirectory, localDirectory);
            }
        }

        private static void FetchViaSftp(
            Signal signal, string host, string username, string password,
            string remoteDirectory, string localDirectory, bool deleteAfterDownload, string ppkLocation = null)
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

                    string resolvedRemoteDirectory = ResolveSftpDirectory(signal, sftp, remoteDirectory);

                    if (string.IsNullOrWhiteSpace(resolvedRemoteDirectory))
                    {
                        Log.Error("Signal {SignalID}: Could not resolve SFTP directory from configured path '{RemoteDirectory}'.",
                            signal.SignalID, remoteDirectory);
                        return;
                    }

                    Log.Information("Signal {SignalID}: Searching SFTP directory tree from {RemoteDirectory}.",
                        signal.SignalID, resolvedRemoteDirectory);

                    var files = FindSftpLogFiles(signal, sftp, resolvedRemoteDirectory).ToList();

                    Log.Information("Signal {SignalID}: Found {Count} file(s) via SFTP search.", signal.SignalID, files.Count);

                    foreach (var file in files)
                    {
                        try
                        {
                            string localPath = Path.Combine(localDirectory, file.Name);
                            using var fs = File.OpenWrite(localPath);
                            sftp.DownloadFile(file.FullName, fs);
                            Log.Information("Signal {SignalID}: Downloaded {File}.", signal.SignalID, file.Name);

                            if (deleteAfterDownload)
                            {
                                sftp.DeleteFile(file.FullName);
                                Log.Information("Signal {SignalID}: Deleted remote SFTP file {File}.",
                                    signal.SignalID, file.FullName);
                            }
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
        }

        private static string ResolveSftpDirectory(Signal signal, SftpClient sftp, string remoteDirectory)
        {
            foreach (var candidate in BuildSftpDirectoryCandidates(remoteDirectory, sftp.WorkingDirectory))
            {
                if (SftpDirectoryExists(sftp, candidate))
                {
                    Log.Information("Signal {SignalID}: Resolved SFTP directory '{ConfiguredPath}' to '{ResolvedPath}'.",
                        signal.SignalID, remoteDirectory, candidate);
                    return candidate;
                }

                if (TryResolveSftpDirectoryCaseInsensitive(sftp, candidate, out string resolvedPath))
                {
                    Log.Warning("Signal {SignalID}: SFTP directory '{ConfiguredPath}' was resolved case-insensitively as '{ResolvedPath}'.",
                        signal.SignalID, remoteDirectory, resolvedPath);
                    return resolvedPath;
                }
            }

            return null;
        }

        private static IEnumerable<ISftpFile> FindSftpLogFiles(Signal signal, SftpClient sftp, string rootDirectory, int maxDepth = 8)
        {
            var pending = new Queue<(string Path, int Depth)>();
            var visitedDirectories = new HashSet<string>(StringComparer.Ordinal);
            var yieldedFiles = new HashSet<string>(StringComparer.Ordinal);

            pending.Enqueue((rootDirectory, 0));

            while (pending.Count > 0)
            {
                var (currentDirectory, depth) = pending.Dequeue();
                var normalizedCurrentDirectory = NormalizeUnixPath(currentDirectory);

                if (!visitedDirectories.Add(normalizedCurrentDirectory))
                    continue;

                IReadOnlyCollection<ISftpFile> entries;
                try
                {
                    entries = sftp.ListDirectory(currentDirectory).ToList();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Signal {SignalID}: Failed to list SFTP directory {RemoteDirectory}.",
                        signal.SignalID, currentDirectory);
                    continue;
                }

                Log.Information("Signal {SignalID}: Scanned SFTP directory {RemoteDirectory} at depth {Depth}.",
                    signal.SignalID, currentDirectory, depth);

                foreach (var entry in entries)
                {
                    if (entry.Name is "." or "..")
                        continue;

                    if (IsSftpLogFile(entry) && yieldedFiles.Add(NormalizeUnixPath(entry.FullName)))
                    {
                        yield return entry;
                        continue;
                    }

                    if (depth >= maxDepth || !IsSftpDirectoryEntry(sftp, entry))
                        continue;

                    var nextDirectory = NormalizeUnixPath(entry.FullName);
                    if (!visitedDirectories.Contains(nextDirectory))
                        pending.Enqueue((nextDirectory, depth + 1));
                }
            }
        }

        private static IEnumerable<string> BuildSftpDirectoryCandidates(string remoteDirectory, string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(remoteDirectory))
                yield break;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var configuredPath = remoteDirectory.Trim().Replace('\\', '/');

            foreach (var candidate in new[]
            {
                configuredPath,
                configuredPath.TrimEnd('/'),
                configuredPath.TrimStart('~'),
                configuredPath.TrimStart('~').TrimStart('/'),
                configuredPath.StartsWith("~/", StringComparison.Ordinal)
                    ? CombineUnixPath(workingDirectory, configuredPath.Substring(2))
                    : null,
                !configuredPath.StartsWith("/", StringComparison.Ordinal)
                    ? CombineUnixPath(workingDirectory, configuredPath.TrimStart('/'))
                    : null,
                !configuredPath.StartsWith("/", StringComparison.Ordinal)
                    ? "/" + configuredPath.TrimStart('/')
                    : null
            })
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                var normalized = candidate.Replace('\\', '/');
                if (seen.Add(normalized))
                    yield return normalized;
            }
        }

        private static bool SftpDirectoryExists(SftpClient sftp, string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !sftp.Exists(path))
                    return false;

                var attributes = sftp.GetAttributes(path);
                return attributes.IsDirectory
                    || attributes.IsSymbolicLink
                    || SftpCanListDirectory(sftp, path);
            }
            catch (SftpPathNotFoundException)
            {
                return false;
            }
        }

        private static bool TryResolveSftpDirectoryCaseInsensitive(SftpClient sftp, string path, out string resolvedPath)
        {
            resolvedPath = null;

            if (string.IsNullOrWhiteSpace(path))
                return false;

            var normalizedPath = path.Replace('\\', '/');
            var isAbsolute = normalizedPath.StartsWith("/", StringComparison.Ordinal);
            var currentPath = isAbsolute ? "/" : sftp.WorkingDirectory;
            var segments = normalizedPath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (!isAbsolute && string.IsNullOrWhiteSpace(currentPath))
                return false;

            foreach (var segment in segments)
            {
                if (segment == ".")
                    continue;

                if (segment == "..")
                {
                    currentPath = GetUnixParentPath(currentPath);
                    continue;
                }

                IReadOnlyCollection<ISftpFile> entries;
                try
                {
                    entries = sftp.ListDirectory(currentPath).ToList();
                }
                catch (SftpPathNotFoundException)
                {
                    return false;
                }

                var match = entries.FirstOrDefault(x => x.Name.Equals(segment, StringComparison.Ordinal))
                    ?? entries.FirstOrDefault(x => x.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                    return false;

                if (!SftpDirectoryExists(sftp, match.FullName))
                    return false;

                currentPath = match.FullName;
            }

            if (!SftpDirectoryExists(sftp, currentPath))
                return false;

            resolvedPath = currentPath;
            return true;
        }

        private static bool SftpCanListDirectory(SftpClient sftp, string path)
        {
            try
            {
                sftp.ListDirectory(path).FirstOrDefault();
                return true;
            }
            catch (SftpPathNotFoundException)
            {
                return false;
            }
        }

        private static bool IsSftpDirectoryEntry(SftpClient sftp, ISftpFile entry)
        {
            try
            {
                return entry.IsDirectory
                    || entry.Attributes.IsSymbolicLink
                    || SftpCanListDirectory(sftp, entry.FullName);
            }
            catch (SftpPathNotFoundException)
            {
                return false;
            }
        }

        private static bool IsSftpLogFile(ISftpFile file)
        {
            if (file.Name is "." or "..")
                return false;

            var name = file.Name;
            return name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".datz", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".csv.gz", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeUnixPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            var normalized = path.Replace('\\', '/');
            return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
        }

        private static string CombineUnixPath(string basePath, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return basePath;

            if (string.IsNullOrWhiteSpace(basePath))
                return relativePath;

            return $"{basePath.TrimEnd('/')}/{relativePath.TrimStart('/')}";
        }

        private static string GetUnixParentPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path == "/")
                return "/";

            var trimmed = path.TrimEnd('/');
            var separatorIndex = trimmed.LastIndexOf('/');

            if (separatorIndex <= 0)
                return "/";

            return trimmed.Substring(0, separatorIndex);
        }

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

                    var resolvedRemoteDirectory = await ResolveFtpDirectory(ftp, signal, remoteDirectory);

                    if (string.IsNullOrWhiteSpace(resolvedRemoteDirectory))
                    {
                        Log.Error("Signal {SignalID}: Could not resolve FTP directory from configured path '{RemoteDir}'.",
                            signal.SignalID, remoteDirectory);
                        return;
                    }

                    // --- Step 3: Get listing with full symlink resolution ---
                    var filesToDownload = await ResolveFilesFromDirectory(ftp, signal, resolvedRemoteDirectory);

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
        private static async Task<string> ResolveFtpDirectory(AsyncFtpClient ftp, Signal signal, string remoteDirectory)
        {
            string workingDirectory;

            try
            {
                workingDirectory = await ftp.GetWorkingDirectory();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Signal {SignalID}: Could not read FTP working directory. Falling back to '/'.",
                    signal.SignalID);
                workingDirectory = "/";
            }

            foreach (var candidate in BuildSftpDirectoryCandidates(remoteDirectory, workingDirectory))
            {
                if (await FtpDirectoryExists(ftp, candidate))
                {
                    Log.Information("Signal {SignalID}: Resolved FTP directory '{ConfiguredPath}' to '{ResolvedPath}'.",
                        signal.SignalID, remoteDirectory, candidate);
                    return NormalizeUnixPath(candidate);
                }

                var resolvedPath = await TryResolveFtpDirectoryCaseInsensitive(ftp, candidate, workingDirectory);
                if (!string.IsNullOrWhiteSpace(resolvedPath))
                {
                    Log.Warning("Signal {SignalID}: FTP directory '{ConfiguredPath}' was resolved case-insensitively as '{ResolvedPath}'.",
                        signal.SignalID, remoteDirectory, resolvedPath);
                    return resolvedPath;
                }
            }

            return null;
        }

        private static async Task<bool> FtpDirectoryExists(AsyncFtpClient ftp, string path)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(path) && await ftp.DirectoryExists(path);
            }
            catch
            {
                return false;
            }
        }

        private static async Task<string> TryResolveFtpDirectoryCaseInsensitive(
            AsyncFtpClient ftp,
            string path,
            string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var normalizedPath = NormalizeUnixPath(path);
            var isAbsolute = normalizedPath.StartsWith("/", StringComparison.Ordinal);
            var currentPath = isAbsolute ? "/" : NormalizeUnixPath(workingDirectory);
            var segments = normalizedPath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (!isAbsolute && string.IsNullOrWhiteSpace(currentPath))
                currentPath = "/";

            foreach (var segment in segments)
            {
                if (segment == ".")
                    continue;

                if (segment == "..")
                {
                    currentPath = GetUnixParentPath(currentPath);
                    continue;
                }

                FtpListItem[] listing;
                try
                {
                    listing = await ftp.GetListing(currentPath, FtpListOption.ForceList | FtpListOption.Auto);
                }
                catch
                {
                    return null;
                }

                var match = listing.FirstOrDefault(x => x.Name.Equals(segment, StringComparison.Ordinal))
                    ?? listing.FirstOrDefault(x => x.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                    return null;

                string nextPath = match.FullName;

                if (match.Type == FtpObjectType.Link && !string.IsNullOrWhiteSpace(match.LinkTarget))
                {
                    nextPath = match.LinkTarget.StartsWith("/", StringComparison.Ordinal)
                        ? match.LinkTarget
                        : CombineUnixPath(currentPath, match.LinkTarget);
                }

                nextPath = NormalizeUnixPath(nextPath);

                if (match.Type != FtpObjectType.Directory && !await FtpDirectoryExists(ftp, nextPath))
                    return null;

                currentPath = nextPath;
            }

            return await FtpDirectoryExists(ftp, currentPath) ? NormalizeUnixPath(currentPath) : null;
        }

        private static async Task<List<(string RemotePath, string FileName)>> ResolveFilesFromDirectory(
            AsyncFtpClient ftp, Signal signal, string directory, int maxDepth = 8)
        {
            return await ResolveFilesFromDirectory(
                ftp,
                signal,
                directory,
                depth: 0,
                maxDepth: maxDepth,
                visitedDirectories: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                yieldedFiles: new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private static async Task<List<(string RemotePath, string FileName)>> ResolveFilesFromDirectory(
            AsyncFtpClient ftp,
            Signal signal,
            string directory,
            int depth,
            int maxDepth,
            HashSet<string> visitedDirectories,
            HashSet<string> yieldedFiles)
        {
            var results = new List<(string, string)>();
            var normalizedDirectory = NormalizeUnixPath(directory);

            if (depth > maxDepth)
            {
                Log.Warning("Signal {SignalID}: Reached max FTP traversal depth at '{Dir}'.",
                    signal.SignalID, directory);
                return results;
            }

            if (!visitedDirectories.Add(normalizedDirectory))
            {
                Log.Debug("Signal {SignalID}: Skipping already-visited FTP directory '{Dir}'.",
                    signal.SignalID, directory);
                return results;
            }

            try
            {
                var listing = await ftp.GetListing(directory, FtpListOption.ForceList | FtpListOption.Auto);

                Log.Information("Signal {SignalID}: Listed '{Dir}' at depth {Depth} — {Count} item(s) found.",
                    signal.SignalID, directory, depth, listing.Length);

                foreach (var item in listing)
                {
                    Log.Debug("Signal {SignalID}: Item — Name={Name}, Type={Type}, LinkTarget={LinkTarget}",
                        signal.SignalID, item.Name, item.Type, item.LinkTarget ?? "N/A");

                    if (item.Type == FtpObjectType.File && IsTargetFile(item.Name))
                    {
                        // Plain file — add directly
                        if (yieldedFiles.Add(NormalizeUnixPath(item.FullName)))
                        {
                            Log.Debug("Signal {SignalID}: Adding plain file {File}", signal.SignalID, item.FullName);
                            results.Add((item.FullName, item.Name));
                        }
                    }
                    else if (item.Type == FtpObjectType.Directory)
                    {
                        var subFiles = await ResolveFilesFromDirectory(
                            ftp, signal, item.FullName, depth + 1, maxDepth, visitedDirectories, yieldedFiles);
                        results.AddRange(subFiles);
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

                        target = NormalizeUnixPath(target);

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
                            var subFiles = await ResolveFilesFromDirectory(
                                ftp, signal, target, depth + 1, maxDepth, visitedDirectories, yieldedFiles);
                            results.AddRange(subFiles);
                        }
                        else
                        {
                            // Symlink points to a file — check if it matches
                            if (IsTargetFile(item.Name) || IsTargetFile(target))
                            {
                                if (yieldedFiles.Add(target))
                                {
                                    Log.Debug("Signal {SignalID}: Adding symlinked file {Link} -> {Target}",
                                        signal.SignalID, item.FullName, target);
                                    results.Add((target, item.Name));
                                }
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

