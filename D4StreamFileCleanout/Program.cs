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
using CsvHelper;
using MOE.Common.Business.LogDecoder;
using MOE.Common.Business;
using Microsoft.Data.SqlClient;
using SqlCommand = Microsoft.Data.SqlClient.SqlCommand;
using SqlConnection = Microsoft.Data.SqlClient.SqlConnection; // Add this at the top of your file

namespace D4StreamFileCleanout
{
    internal class Program
    {
        private static string _connectionString;
        private static bool _uploadEnabled;
        private static bool _deleteEnabled;
        private static bool _individualDirectory;
        private static bool _usesSignalId;
        private static string _logsPath;
        private static string _destinationTableName;
        private static int _daysToKeep;
        private static DateTime earliestAcceptableDate;
        private static int _maxDegreeOfParallelism;
        private static int _batchSize;

        static void Main(string[] args)
        {
            try
            {
                var appSettings = ConfigurationManager.AppSettings;
                LoadConfiguration(appSettings);
                var signals = LoadSignalsFromDatabase();
                ProcessCsvFilesSequential(signals, appSettings);
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
            Console.WriteLine($"IndividualDirectory: {_individualDirectory}");
            Console.WriteLine($"Days to Keep: {_daysToKeep}");
            Console.WriteLine($"Max Parallel Tasks: {_maxDegreeOfParallelism}");
            Console.WriteLine($"Batch Size: {_batchSize}");
            Console.WriteLine($"Connection String: {MaskConnectionString(_connectionString)}");
            Console.WriteLine();
        }

        private static string MaskConnectionString(string connectionString)
        {
            return System.Text.RegularExpressions.Regex.Replace(connectionString, 
                @"(password|pwd)\s*=\s*[^;]+", "Password=***", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static Dictionary<string, string> LoadSignalsFromDatabase()
        {
            var signals = new ConcurrentDictionary<string, string>();
            try
            {
                Console.WriteLine("Attempting to connect to database...");

                Thread.Sleep(1000);
                using (var connection = new SqlConnection(_connectionString))
                {
                    connection.Open();
                    Console.WriteLine("Database connection successful!");

                    var query = "SELECT SignalID, IPAddress FROM Signals WHERE IPAddress IS NOT NULL";

                    using (var command = new SqlCommand(query, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var signalId = reader.GetString(reader.GetOrdinal("SignalID"));
                            var ipAddress = reader.GetString(reader.GetOrdinal("IPAddress"));

                            signals.TryAdd(ipAddress, signalId);
                        }
                    }
                }

                Console.WriteLine($"Loaded {signals.Count} signals from database");
                return new Dictionary<string, string>(signals);
            }
            catch (Exception ex)
            {
                Thread.Sleep(10000);
                Console.WriteLine($"Database error: {ex.Message}");
                throw;
            }
        }

        private static void ProcessCsvFilesSequential(Dictionary<string, string> signals, NameValueCollection appSettings)
        {
            if(_individualDirectory)
            {
                var subDirectories = Directory.GetDirectories(_logsPath);
                foreach (var dir in subDirectories)
                {
                    ProcessCsvFilesParallel(dir, signals, appSettings);
                }
            }
            else
            {
                ProcessCsvFilesParallel(_logsPath, signals, appSettings);
            }
        }

        private static void ProcessCsvFilesParallel(string directory, Dictionary<string, string> signals, NameValueCollection appSettings)
        {
            if (!Directory.Exists(directory))
            {
                Console.WriteLine($"Directory does not exist: {directory}");
                Thread.Sleep(10000);
                return;
            }


            var csvFiles = Directory.GetFiles(directory, "*.csv");

            Console.WriteLine($"Found {csvFiles.Length} CSV files in logs path: {directory}");
            Console.WriteLine();

            Thread.Sleep(10000);
            var filePattern = new Regex("");
            if (_usesSignalId)
            {
                filePattern = new Regex(@"^(\d+)__(.+?)_(\d{8})-(\d{4})\.csv$", RegexOptions.IgnoreCase);
            }
            else
            {
                filePattern = new Regex(@"^(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})_(\d+)_(\d{1,2})_(\d{1,2})_(\d{4})\.csv$", RegexOptions.IgnoreCase);
            }
            
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
                
                Parallel.ForEach(batch, parallelOptions, filePath =>
                {
                    ProcessSingleFile(filePath, filePattern, signals, appSettings);
                });

                // Optional: Add a small delay between batches to prevent overwhelming the database
                System.Threading.Thread.Sleep(100);
            }
        }

        private static void ProcessSingleFile(string filePath, Regex filePattern, Dictionary<string, string> signals, NameValueCollection appSettings)
        {
            var fileName = Path.GetFileName(filePath);
            var threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            
            Console.WriteLine($"[Thread {threadId}] Processing: {fileName}");

            try
            {
                if (_uploadEnabled)
                {
                    var match = filePattern.Match(fileName);
                    if (!match.Success)
                    {
                        Console.WriteLine($"[Thread {threadId}] File {fileName} does not match expected pattern");
                        return;
                    }

                    var fileInfo = ParseFileInfo(match, filePath, fileName, signals);
                    if (fileInfo == null || fileInfo.SignalID == null) return;

                    // Process CSV with optimized reading
                    var records = ReadCsvFileOptimized(filePath, fileName, threadId);
                    if (records.Count == 0) return;

                    // Create and populate data table
                    var table = new MOE.Common.Data.MOE.Controller_Event_LogDataTable();
                    PopulateDataTable(records, fileInfo, table, threadId);

                    // Bulk insert to database
                    BulkInsertToDatabase(table, appSettings, threadId, fileName);
                }

                if (_deleteEnabled)
                {
                    DeleteFile(new FileInfo { FilePath = filePath, FileName = fileName });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Thread {threadId}] Error processing {fileName}: {ex.Message}");
            }
        }

        private static FileInfo ParseFileInfo(Match match, string filePath, string fileName, Dictionary<string, string> signals)
        {
            try
            {
                if (_usesSignalId)
                {
                    // New format: ID__Location_YYYYMMDD-HHMM
                    var signalId = match.Groups[1].Value;
                    var dateString = match.Groups[3].Value;
                    var timeString = match.Groups[4].Value;

                    var year = int.Parse(dateString.Substring(0, 4));
                    var month = int.Parse(dateString.Substring(4, 2));
                    var day = int.Parse(dateString.Substring(6, 2));
                    var hour = int.Parse(timeString.Substring(0, 2));
                    var minute = int.Parse(timeString.Substring(2, 2));

                    var fileDate = new DateTime(year, month, day, hour, minute, 0);

                    return new FileInfo
                    {
                        FilePath = filePath,
                        FileName = fileName,
                        SignalID = signalId,
                        FileDate = fileDate,
                        IPAddress = null,
                        Port = null
                    };
                }
                else
                {
                    // Old format: IP_Port_M_D_YYYY
                    var ipAddress = match.Groups[1].Value;
                    var port = match.Groups[2].Value;
                    var month = int.Parse(match.Groups[3].Value);
                    var day = int.Parse(match.Groups[4].Value);
                    var year = int.Parse(match.Groups[5].Value);
                    var fileDate = new DateTime(year, month, day);

                    return new FileInfo
                    {
                        FilePath = filePath,
                        FileName = fileName,
                        IPAddress = ipAddress,
                        Port = port,
                        FileDate = fileDate,
                        SignalID = signals.ContainsKey(ipAddress) ? signals[ipAddress] : null
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing file info for {fileName}: {ex.Message}");
                return null;
            }
        }

        private static List<D4RecordStreamed> ReadCsvFileOptimized(string filePath, string fileName, int threadId)
        {
            var records = new List<D4RecordStreamed>();
            var genericRecords = new List<D4Record>();

            try
            {
                using (var reader = new StreamReader(filePath))
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    
                    try
                    {
                        records = csv.GetRecords<D4RecordStreamed>().ToList();
                    }
                    catch (Exception)
                    {
                        try
                        {
                            Console.WriteLine(
                                $"[Thread {threadId}] Error reading {fileName} as D4RecordStreamed, trying D4Record");
                            genericRecords = csv.GetRecords<D4Record>().ToList();
                            //convert to streamed log
                            records = genericRecords.Select(r => new D4RecordStreamed
                            {
                                EventCode = r.EventCode,
                                EventName = "GenericD4Record",
                                Param = r.Param,
                                DateTime = r.DateTime,
                                MsgIdx = r.MsgIdx
                            }).ToList();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Thread {threadId}] Error reading {fileName}, trying line-by-line");
                            reader.BaseStream.Position = 0;
                            reader.DiscardBufferedData();

                            while (csv.Read())
                            {
                                try
                                {
                                    var record = new D4RecordStreamed
                                    {
                                        EventCode = csv.GetField<int>(0),
                                        EventName = csv.GetField<string>(1),
                                        Param = csv.GetField<int>(2),
                                        DateTime = csv.GetField<DateTime>(3),
                                        MsgIdx = csv.GetField<int>(4)
                                    };
                                    records.Add(record);
                                }
                                catch
                                {
                                    // Skip malformed lines
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Thread {threadId}] Error reading CSV file {fileName}: {ex.Message}");
            }

            return records;
        }

        private static void PopulateDataTable(List<D4RecordStreamed> records, FileInfo fileInfo, 
            MOE.Common.Data.MOE.Controller_Event_LogDataTable table, int threadId)
        {
            var validRecords = records.Where(r => r.DateTime > earliestAcceptableDate).ToList();
            
            Console.WriteLine($"[Thread {threadId}] Processing {validRecords.Count} valid records from {records.Count} total");

            foreach (var record in validRecords)
            {
                try
                {
                    table.AddController_Event_LogRow(fileInfo.SignalID, record.DateTime, 
                        record.EventCode, record.Param);
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
}