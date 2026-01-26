using MOE.Common.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ATSPM.Application.Models;
using CsvHelper;
using CsvHelper.Configuration;
using MOE.Common.Business.LogDecoder;
using MOE.Common.Business;
using Microsoft.Data.SqlClient;
using MOE.Common.Models;
using SqlCommand = Microsoft.Data.SqlClient.SqlCommand;
using SqlConnection = Microsoft.Data.SqlClient.SqlConnection; // Add this at the top of your file

namespace CsvLogReader
{
    internal class Program
    {
        private static string _connectionString;
        private static bool _uploadEnabled;
        private static bool _deleteEnabled;
        private static string _movedLocation;
        private static bool _individualDirectory;
        private static bool _usesSignalId;
        private static string _logsPath;
        private static string _destinationTableName;
        private static int _daysToKeep;
        private static DateTime earliestAcceptableDate;
        private static int _maxDegreeOfParallelism;
        private static int _batchSize;
        public static MOE.Common.Models.Repositories.IControllerEventLogRepository CELRepository = MOE.Common.Models.Repositories.ControllerEventLogRepositoryFactory.Create();

        static void Main(string[] args)
        {
            try
            {
                var appSettings = ConfigurationManager.AppSettings;
                LoadConfiguration(appSettings);
                ProcessCsvFilesSequential(appSettings);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        private static void LoadConfiguration(NameValueCollection appSettings)
        {
            _connectionString = ConfigurationManager.ConnectionStrings["SPM"].ConnectionString;
            _logsPath = appSettings["D4LogsPath"];
            _uploadEnabled = bool.Parse(appSettings["upload"] ?? "False");
            _deleteEnabled = bool.Parse(appSettings["delete"] ?? "False");
            _movedLocation = appSettings["moveLocation"];
            _individualDirectory = bool.Parse(appSettings["individualDirectory"] ?? "False");
            _usesSignalId = bool.Parse(appSettings["usesSignalId"] ?? "False");
            _daysToKeep = int.Parse(appSettings["daysToKeep"] ?? "4");
            _destinationTableName = appSettings["DestinationTableName"] ?? "_Controller_Event_Log";
            earliestAcceptableDate = DateTime.Parse(appSettings["earliestAcceptableDate"] ?? "10-4-2025");
            
            // New performance settings
            _maxDegreeOfParallelism = int.Parse(appSettings["MaxDegreeOfParallelism"] ?? Environment.ProcessorCount.ToString());
            _batchSize = int.Parse(appSettings["BatchSize"] ?? "1000");

            Console.WriteLine("Configuration loaded:");
            Console.WriteLine($"Logs Path: {_logsPath}");
            Console.WriteLine($"Upload Enabled: {_uploadEnabled}");
            Console.WriteLine($"Delete Enabled: {_deleteEnabled}");
            Console.WriteLine($"Move Path: {_movedLocation}");
            Console.WriteLine($"IndividualDirectory: {_individualDirectory}");
            Console.WriteLine($"Days to Keep: {_daysToKeep}");
            Console.WriteLine($"Max Parallel Tasks: {_maxDegreeOfParallelism}");
            Console.WriteLine($"Batch Size: {_batchSize}");
            Console.WriteLine($"Connection String: {MaskConnectionString(_connectionString)}");
            Console.WriteLine();

            //if the delete is disabled, create the moved location folder if it does not exist
            if (!_deleteEnabled && !Directory.Exists(_movedLocation))
            {
                Directory.CreateDirectory(_movedLocation);
                Console.WriteLine($"Created moved location directory: {_movedLocation}");
            }
        }

        private static string MaskConnectionString(string connectionString)
        {
            return System.Text.RegularExpressions.Regex.Replace(connectionString, 
                @"(password|pwd)\s*=\s*[^;]+", "Password=***", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static void ProcessCsvFilesSequential(NameValueCollection appSettings)
        {
            if(_individualDirectory)
            {
                var subDirectories = Directory.GetDirectories(_logsPath);
                foreach (var dir in subDirectories)
                {
                    ProcessCsvFilesParallel(dir, appSettings);
                }
            }
            else
            {
                ProcessCsvFilesParallel(_logsPath, appSettings);
            }
        }

        private static void ProcessCsvFilesParallel(string directory, NameValueCollection appSettings)
        {
            if (!Directory.Exists(directory))
            {
                Console.WriteLine($"Directory does not exist: {directory}");
                Thread.Sleep(10000);
                return;
            }
            string filename = Path.GetFileName(directory);
            string signalid = Regex.Replace(filename, @"^Ctrl0*", "");

            var csvFiles = Directory.GetFiles(directory, "*.csv");

            Console.WriteLine($"Found {csvFiles.Length} CSV files in logs path: {directory}");
            Console.WriteLine();

            Thread.Sleep(10000);

            // Process files in parallel batches
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxDegreeOfParallelism
            };

            // Group files into batches for better memory management
            var fileBatches = csvFiles.Select((file, index) => new { file, index })
                                    .GroupBy(x => x.index / _batchSize)
                                    .Select(g => g.Select(x => x.file).ToArray())
                                    .ToArray();

            foreach (var batch in fileBatches)
            {
                Console.WriteLine($"Processing batch of {batch.Length} files...");

                foreach (var filePath in batch)
                {
                    ProcessSingleFile(filename, signalid, filePath, appSettings);
                }

                // Optional: Add a small delay between batches to prevent overwhelming the database
                System.Threading.Thread.Sleep(100);
            }
        }

        private static void ProcessSingleFile(string directory, string signalid, string filePath, NameValueCollection appSettings)
        {
            var fileName = Path.GetFileName(filePath);
            var threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            
            Console.WriteLine($"[Thread {threadId}] Processing: {fileName}");

            try
            {
                if (_uploadEnabled)
                {

                    var records = ReadCsvFileOptimized(signalid,filePath, fileName, threadId);
                    if (records.Count == 0) return;

                    // Create and populate data table
                    var table = new MOE.Common.Data.MOE.Controller_Event_LogDataTable();
                    PopulateDataTable(signalid, records, table, threadId);

                    //check if there are rows already inserted in the database for this signal and date

                    if(CELRepository.CheckIfSingleRecordExists(signalid, records.First().Timestamp, records.First().EventCode, records.First().EventParam))
                    {
                        Console.WriteLine($"[Thread {threadId}] Records already exist in database for {fileName}, skipping insert.");
                        return;
                    }

                    // Bulk insert to database
                    BulkInsertToDatabase(table, appSettings, threadId, fileName);
                }

                if (_deleteEnabled)
                {
                    DeleteFile(new FileInfo { FilePath = filePath, FileName = fileName });
                }
                else //MOVE THE FILE out of the live logs folder
                {
                    MoveFile(directory, new FileInfo { FilePath = filePath, FileName = fileName });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Thread {threadId}] Error processing {fileName}: {ex.Message}");
            }
        }

        private static List<ControllerEventLog> ReadCsvFileOptimized(string signalID, string filePath, string fileName, int threadId)
        {
            var records = new List<ControllerEventLog>();

            try
            {
                using (var reader = new StreamReader(filePath))
                using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = false // No header row
                }))
                {

                     csv.Context.RegisterClassMap<ControllerEventLogMap>();
                    // Skip the first 6 rows
                    for (int i = 0; i < 6; i++)
                    {
                        csv.Read();
                    }

                    // Skip row 7 (the numbers 2, 3, 4, 5, 6...)
                    csv.Read();

                    try
                    {
                        records = csv.GetRecords<ControllerEventLog>()
                            .Select(record =>
                            {
                                record.SignalId = signalID;
                                return record;
                            })
                            .ToList();
                    }
                    catch (Exception ex)
                    {
                        // Handle exception
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Thread {threadId}] Error reading CSV file {fileName}: {ex.Message}");
            }

            return records;
        }
        private static void PopulateDataTable(string signalId, List<ControllerEventLog> records, 
            MOE.Common.Data.MOE.Controller_Event_LogDataTable table, int threadId)
        {
            var validRecords = records.Where(r => r.Timestamp > earliestAcceptableDate).ToList();
            
            Console.WriteLine($"[Thread {threadId}] Processing {validRecords.Count} valid records from {records.Count} total");

            foreach (var record in validRecords)
            {
                try
                {
                    table.AddController_Event_LogRow(signalId, record.Timestamp, 
                        record.EventCode, record.EventParam);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Thread {threadId}] Error adding record: {ex.Message}");
                }
            }
        }

        private static void BulkInsertToDatabase(MOE.Common.Data.MOE.Controller_Event_LogDataTable table, 
            NameValueCollection appSettings, int threadId, string fileName)
        {
            if (table.Rows.Count == 0)
            {
                Console.WriteLine($"[Thread {threadId}] No data to insert for {fileName}");
                return;
            }

            try
            {
                var options = new MOE.Common.Business.BulkCopyOptions(
                    _connectionString, 
                    _destinationTableName,
                    Convert.ToBoolean(appSettings["WriteToConsole"]),
                    Convert.ToBoolean(appSettings["forceNonParallel"]),
                    Convert.ToInt32(appSettings["MaxThreads"]),
                    Convert.ToBoolean(appSettings["DeleteFile"]),
                    Convert.ToDateTime(appSettings["EarliestAcceptableDate"]),
                    Convert.ToInt32(appSettings["BulkCopyBatchSize"]),
                    Convert.ToInt32(appSettings["BulkCopyTimeOut"]));

                MOE.Common.Business.SignalFtp.BulktoDb(table, options, _destinationTableName);
                Console.WriteLine($"[Thread {threadId}] Successfully inserted {table.Rows.Count} records from {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Thread {threadId}] Database insert error for {fileName}: {ex.Message}");
                throw;
            }
        }

        private static void MoveFile(string directory, FileInfo file)
        {
            var location = _movedLocation + directory ;
            if (!Directory.Exists(location))
            {
                Directory.CreateDirectory(location);
            }

            try
            {
                if (File.Exists(file.FilePath))
                {
                    File.Move(file.FilePath, location + "\\" + file.FileName);
                    Console.WriteLine($"  File moved successfully: {file.FileName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Delete failed for {file.FileName}: {ex.Message}");
            }
        }

        private static void DeleteFile(FileInfo file)
        {
            try
            {
                if (File.Exists(file.FilePath))
                {
                    File.Delete(file.FilePath);
                    Console.WriteLine($"  File deleted successfully: {file.FileName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Delete failed for {file.FileName}: {ex.Message}");
            }
        }
    }

    public class FileInfo
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string IPAddress { get; set; }
        public string Port { get; set; }
        public DateTime FileDate { get; set; }
        public string SignalID { get; set; }
    }

    public class ControllerEventLogMap : ClassMap<ControllerEventLog>
    {
        public ControllerEventLogMap()
        {
            Map(m => m.Timestamp).Index(0); // Column A (15:00.2)
            Map(m => m.EventCode).Index(1); // Column B (173)
            Map(m => m.EventParam).Index(2); // Column C (5)
            Map(m => m.SignalId).Ignore(); // Will be set programmatically
        }
    }
}