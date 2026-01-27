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

namespace DecodeAndImportD4Logs
{
    internal class Program
    {
        public static string _cwd = "";
        static void Main(string[] args)
        {
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
            

            foreach (var s in Directory.GetDirectories(cwd))
            {
                ProcessDirectoryDepthFirst(cwd, cwd);
            }

            foreach (var s in Directory.GetDirectories(cwd))
            {
                dirList.Add(s);
            }
                var sp = new SimplePartitioner<string>(dirList);
            var optionsMain = new ParallelOptions { MaxDegreeOfParallelism = Convert.ToInt32(appSettings["MaxThreads"]) };
            //Parallel.ForEach(sp, optionsMain, dir =>
            //{
            foreach (var dir in dirList)
            {
                string signalId = "";
                string[] fileNames;
                if (isGzipAgency)
                {
                    Console.WriteLine("Checking GZIP files for dir: " + dir);
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
                        Console.WriteLine("-----------------------------Starting Signal " + dir);
                    }

                    if (isGzipAgency)
                    {
                        for (var i = 0; i < maxFilesToImportPerSignal && i < fileNames.Length; i++)
                        {
                            try
                            {
                                var fileList = D4Decoder.DecodeD4GzipFile(fileNames[i], signalId, mergedEventsTable,
                                    Convert.ToDateTime(appSettings["EarliestAcceptableDate"]),cwd);
                                foreach (var fileName in fileList)
                                {
                                    toDelete.Add(fileName);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.Message);
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
                                Console.WriteLine(ex.Message);
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
        }

        private static void ProcessDirectoryDepthFirst(string currentDir, string rootDir)
        {
            // First, recursively process all subdirectories (depth-first)
            foreach (var subDir in Directory.GetDirectories(currentDir))
            {
                ProcessDirectoryDepthFirst(subDir, rootDir);
            }

            // Then process the current directory (after all children are processed)
            // Skip if this is the root directory itself
            if (currentDir == rootDir)
                return;

            var folderName = Path.GetFileName(currentDir);
            var destFolderPath = Path.Combine(rootDir, folderName);

            // If folder doesn't exist in main directory, move the entire folder
            if (!Directory.Exists(destFolderPath))
            {
                Directory.Move(currentDir, destFolderPath);
            }
            else
            {
                // Folder exists, so move files from current folder to existing folder
                foreach (var file in Directory.GetFiles(currentDir))
                {
                    var fileName = Path.GetFileName(file);
                    var destPath = Path.Combine(destFolderPath, fileName);

                    // If file already exists, delete the source file (keep existing)
                    if (File.Exists(destPath) && rootDir != _cwd)
                    {
                        File.Delete(file);
                    }
                    else
                    {
                        File.Move(file, destPath);
                    }
                }

                // Delete the now-empty subdirectory
                if (Directory.GetFiles(currentDir).Length == 0 && Directory.GetDirectories(currentDir).Length == 0 && currentDir!= rootDir)
                {
                    Directory.Delete(currentDir);
                }
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
                Console.WriteLine(file);
            }

        }

        private static void AddEventsToImportTable(BlockingCollection<MOE.Common.Data.MOE.Controller_Event_LogRow> mergedEventsTable, MOE.Common.Data.MOE.Controller_Event_LogDataTable elTable)
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
                    Console.WriteLine(ex.Message);
                }
            }
        }

        private static MOE.Common.Data.MOE.Controller_Event_LogDataTable CreateDataTableForImport()
        {
            var elTable = new MOE.Common.Data.MOE.Controller_Event_LogDataTable();
            return elTable;
        }

        private static void BulkImportRecordsAndDeleteFiles(NameValueCollection appSettings, ConcurrentBag<string> toDelete, MOE.Common.Data.MOE.Controller_Event_LogDataTable elTable)
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
                if (MOE.Common.Business.SignalFtp.BulktoDb(elTable, options, destTable) && Convert.ToBoolean(appSettings["DeleteFile"]))
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
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }
    }
}
