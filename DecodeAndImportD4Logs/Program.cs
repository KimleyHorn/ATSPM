using MOE.Common.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MOE.Common.Business.LogDecoder;
using Serilog;


namespace DecodeAndImportD4Logs
{
    internal class Program
    {
        public static string _cwd = "";

        static void Main(string[] args)
        {
            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .MinimumLevel.Debug()
                .WriteTo.File("log.txt", 
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            try
            {
                
                Log.Information("Application started");

                var appSettings = ConfigurationManager.AppSettings;
                var dirList = new List<string>();
                var cwd = appSettings["D4LogsPath"];
                _cwd = cwd;
                var maxFilesToImportPerSignal = Convert.ToInt32(appSettings["MaxFilesPerSignalToImport"]);
                bool isGzipAgency = Convert.ToBoolean(appSettings["IsGzipAgency"]);
                string startSignal = null;
                string endSignal = null;
                if (args.Length == 2)
                {
                    startSignal = args[0];
                    endSignal = args[1];
                }

                //Log.Information("Processing nested directories");
                //foreach (var s in Directory.GetDirectories(cwd))
                //{
                //    ProcessDirectoryDepthFirst(cwd, cwd);
                //}

                foreach (var s in Directory.GetDirectories(cwd))
                {
                    dirList.Add(s);
                }

                var sp = new SimplePartitioner<string>(dirList);
                var optionsMain = new ParallelOptions
                    { MaxDegreeOfParallelism = Convert.ToInt32(appSettings["MaxThreads"]) };
                //Parallel.ForEach(sp, optionsMain, dir =>
                //{
                foreach (var dir in dirList)
                {
                    string signalId = "";
                    string[] fileNames;
                    if (isGzipAgency)
                    {
                        Log.Information("Checking GZIP files for dir: {Directory}", dir);
                        GetFileNamesAndSignalIdGzip(dir, out signalId, out fileNames);
                    }
                    else
                    {
                        GetFileNamesAndSignalId(dir, out signalId, out fileNames);
                    }

                    if ((args.Length == 2 &&
                         (string.Compare(signalId, startSignal, comparisonType: StringComparison.OrdinalIgnoreCase) > 0 ||
                          string.Compare(signalId, startSignal, comparisonType: StringComparison.OrdinalIgnoreCase) == 0) &&
                         (string.Compare(signalId, endSignal, comparisonType: StringComparison.OrdinalIgnoreCase) < 0 ||
                          string.Compare(signalId, endSignal, comparisonType: StringComparison.OrdinalIgnoreCase) == 0)) ||
                        args.Length == 0)
                    {
                        var toDelete = new ConcurrentBag<string>();
                        var mergedEventsTable = new BlockingCollection<MOE.Common.Data.MOE.Controller_Event_LogRow>();
                        if (Convert.ToBoolean(appSettings["WriteToConsole"]))
                        {
                            Log.Information("-----------------------------Starting Signal {Directory}", dir);
                        }

                        if (isGzipAgency)
                        {
                            for (var i = 0; i < maxFilesToImportPerSignal && i < fileNames.Length; i++)
                            {
                                try
                                {
                                    var fileList = D4Decoder.DecodeD4GzipFile(fileNames[i], signalId, mergedEventsTable,
                                        Convert.ToDateTime(appSettings["EarliestAcceptableDate"]), cwd);
                                    foreach (var fileName in fileList)
                                    {
                                        toDelete.Add(fileName);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, "Error processing GZIP file: {FileName}", fileNames[i]);
                                }
                            }

                            var elTable = CreateDataTableForImport();
                            AddEventsToImportTable(mergedEventsTable, elTable);
                            mergedEventsTable.Dispose();
                            BulkImportRecordsAndDeleteFiles(appSettings, toDelete, elTable);
                        }
                        else
                        {
                            for (var i = 0; i < maxFilesToImportPerSignal && i < fileNames.Length; i++)
                            {
                                try
                                {
                                    D4Decoder.DecodeD4File(fileNames[i], signalId, mergedEventsTable,
                                        Convert.ToDateTime(appSettings["EarliestAcceptableDate"]));
                                    toDelete.Add(fileNames[i]);
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, "Error processing D4 file: {FileName}", fileNames[i]);
                                }
                            }

                            var elTable = CreateDataTableForImport();
                            AddEventsToImportTable(mergedEventsTable, elTable);
                            mergedEventsTable.Dispose();
                            BulkImportRecordsAndDeleteFiles(appSettings, toDelete, elTable);
                        }

                    }
                }
                //});

                Log.Information("Application completed successfully");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static void ProcessDirectoryDepthFirst(string currentDir, string rootDir)
        {
            // Check if directory still exists before processing
            if (!Directory.Exists(currentDir))
            {
                Log.Warning("Directory no longer exists (already processed): {Directory}", currentDir);
                return;
            }

            // Get subdirectories and store in array to avoid enumeration issues
            string[] subDirs;
            try
            {
                subDirs = Directory.GetDirectories(currentDir);
            }
            catch (DirectoryNotFoundException)
            {
                Log.Warning("Directory disappeared during enumeration: {Directory}", currentDir);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                Log.Warning("Access denied to directory: {Directory}", currentDir);
                return;
            }

            // Process all subdirectories (depth-first)
            foreach (var subDir in subDirs)
            {
                Log.Debug("Descending into subdirectory: {SubDirectory}", subDir);
                ProcessDirectoryDepthFirst(subDir, rootDir);
            }

            // Double-check directory still exists after processing children
            if (!Directory.Exists(currentDir))
            {
                Log.Warning("Directory was moved/deleted during child processing: {Directory}", currentDir);
                return;
            }

            if (currentDir == rootDir)
                return;

            Log.Information("Processing directory: {Directory}", currentDir);
            var folderName = Path.GetFileName(currentDir);
            var destFolderPath = Path.Combine(rootDir, folderName);

            if (!Directory.Exists(destFolderPath))
            {
                Log.Information("Moving directory {Source} to {Destination}", currentDir, destFolderPath);

                if (!TryMoveDirectory(currentDir, destFolderPath))
                {
                    Log.Warning("Cannot move directory (contains locked files). Processing files individually...");

                    try
                    {
                        Directory.CreateDirectory(destFolderPath);
                        ProcessFilesIndividually(currentDir, destFolderPath, rootDir);
                    }
                    catch (DirectoryNotFoundException)
                    {
                        Log.Warning("Source directory disappeared: {Directory}", currentDir);
                    }
                }
            }
            else
            {
                Log.Information("Merging files from {Source} into existing directory {Destination}", currentDir, destFolderPath);
                try
                {
                    ProcessFilesIndividually(currentDir, destFolderPath, rootDir);
                }
                catch (DirectoryNotFoundException)
                {
                    Log.Warning("Source directory disappeared: {Directory}", currentDir);
                }
            }
        }

        private static void ProcessFilesIndividually(string sourceDir, string destDir, string rootDir)
        {
            int skippedFiles = 0;
            int movedFiles = 0;

            string[] files;
            try
            {
                files = Directory.GetFiles(sourceDir);
            }
            catch (DirectoryNotFoundException)
            {
                Log.Warning("Source directory no longer exists: {Directory}", sourceDir);
                return;
            }

            foreach (var file in files)
            {
                if (!File.Exists(file))
                {
                    Log.Warning("File disappeared: {FileName}", Path.GetFileName(file));
                    continue;
                }

                var fileName = Path.GetFileName(file);
                var destPath = Path.Combine(destDir, fileName);

                if (File.Exists(destPath))
                {
                    try
                    {
                        Log.Information("TESTING Deleting duplicate file: {FileName}", fileName);
                        //File.Delete(file);
                    }
                    catch (IOException ex)
                    {
                        Log.Warning(ex, "Cannot delete duplicate file {FileName}", fileName);
                        skippedFiles++;
                    }
                }
                else
                {
                    if (TryMoveFile(file, destPath))
                    {
                        movedFiles++;
                    }
                    else
                    {
                        Log.Warning("Skipping locked file: {FileName}", fileName);
                        skippedFiles++;
                    }
                }
            }

            Log.Information("Moved {MovedFiles} file(s), skipped {SkippedFiles} file(s)", movedFiles, skippedFiles);

            TryDeleteEmptyDirectory(sourceDir, rootDir);
        }

        private static bool TryMoveDirectory(string source, string destination)
        {
            try
            {
                Directory.Move(source, destination);
                return true;
            }
            catch (IOException ex)
            {
                Log.Debug(ex, "IOException when moving directory {Source} to {Destination}", source, destination);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Debug(ex, "UnauthorizedAccessException when moving directory {Source} to {Destination}", source, destination);
                return false;
            }
        }

        private static bool TryMoveFile(string source, string destination)
        {
            try
            {
                File.Move(source, destination);
                return true;
            }
            catch (IOException ex)
            {
                Log.Debug(ex, "IOException when moving file {Source} to {Destination}", source, destination);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Debug(ex, "UnauthorizedAccessException when moving file {Source} to {Destination}", source, destination);
                return false;
            }
        }

        private static void TryDeleteEmptyDirectory(string directory, string rootDir)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    return;
                }

                if (Directory.GetFiles(directory).Length == 0 &&
                    Directory.GetDirectories(directory).Length == 0 &&
                    directory != rootDir)
                {
                    Directory.Delete(directory);
                    Log.Information("Deleted empty directory: {Directory}", directory);
                }
                else if (Directory.GetFiles(directory).Length > 0)
                {
                    Log.Information("Directory not empty (has locked files): {Directory}", directory);
                }
            }
            catch (DirectoryNotFoundException)
            {
                // Already gone, that's fine
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not delete directory {Directory}", directory);
            }
        }


        private static void GetFileNamesAndSignalId(string dir, out string signalId, out string[] fileNames)
        {
            var split = dir.Split('\\');
            signalId = split.Last();
            fileNames = Directory.GetFiles(dir, "*.csv?");
        }

        private static void GetFileNamesAndSignalIdGzip(string dir, out string signalId, out string[] fileNames)
        {

            var appSettings = ConfigurationManager.AppSettings;
            var split = dir.Split('\\');
            signalId = split.Last();
            Thread.Sleep(1000);
            fileNames = Directory.GetFiles(dir, "*.gz");
            //list the directory
            foreach (var file in fileNames)
            {
                Log.Debug("Found file: {FileName}", file);
            }

        }

        private static void AddEventsToImportTable(
            BlockingCollection<MOE.Common.Data.MOE.Controller_Event_LogRow> mergedEventsTable,
            MOE.Common.Data.MOE.Controller_Event_LogDataTable elTable)
        {
            foreach (var r in mergedEventsTable)
            {
                try
                {
                    elTable.AddController_Event_LogRow(r.SignalID, r.Timestamp, r.EventCode,
                        r.EventParam);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error adding event to import table for SignalID: {SignalID}", r.SignalID);
                }
            }
        }

        private static MOE.Common.Data.MOE.Controller_Event_LogDataTable CreateDataTableForImport()
        {
            var elTable = new MOE.Common.Data.MOE.Controller_Event_LogDataTable();
            return elTable;
        }

        private static void BulkImportRecordsAndDeleteFiles(NameValueCollection appSettings,
            ConcurrentBag<string> toDelete, MOE.Common.Data.MOE.Controller_Event_LogDataTable elTable)
        {
            var connectionString = ConfigurationManager.ConnectionStrings["SPM"].ConnectionString;
            var destTable = appSettings["DestinationTableNAme"];
            var options = new MOE.Common.Business.BulkCopyOptions(connectionString, destTable,
                Convert.ToBoolean(appSettings["WriteToConsole"]),
                Convert.ToBoolean(appSettings["forceNonParallel"]),
                Convert.ToInt32(appSettings["MaxThreads"]),
                Convert.ToBoolean(appSettings["DeleteFile"]),
                Convert.ToDateTime(appSettings["EarliestAcceptableDate"]),
                Convert.ToInt32(appSettings["BulkCopyBatchSize"]),
                Convert.ToInt32(appSettings["BulkCopyTimeOut"]));
            if (elTable.Count > 0)
            {
                if (MOE.Common.Business.SignalFtp.BulktoDb(elTable, options, destTable) &&
                    Convert.ToBoolean(appSettings["DeleteFile"]))
                {
                    DeleteFiles(toDelete);
                }
            }
            else
            {
                var td = new ConcurrentBag<string>();
                foreach (var s in toDelete)
                {
                    if (s.Contains("1970_01_01"))
                    {
                        td.Add(s);
                    }
                }

                if (td.Count > 0)
                {
                    DeleteFiles(td);
                }
            }
        }

        public static void DeleteFiles(ConcurrentBag<string> files)
        {
            foreach (var f in files)
            {
                try
                {
                    if (File.Exists(f))
                    {
                        File.Delete(f);
                        Log.Information("Deleted file: {FileName}", f);
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error deleting file: {FileName}", f);
                }
            }
        }
    }
}