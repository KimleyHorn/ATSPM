using System.Collections.Specialized;
using System.Configuration;
using System.Globalization;
using System.Text.RegularExpressions;
using ATSPM.Application.Models;
using CsvHelper;
using CsvHelper.Configuration;
using Serilog;

namespace CsvLogReader;

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
        ConfigureSerilog();

        try
        {
            Log.Information("Application starting");
            var appSettings = ConfigurationManager.AppSettings;
            LoadConfiguration(appSettings);
            ProcessCsvFilesSequential(appSettings);
            Log.Information("Application completed successfully");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureSerilog()
    {
        var logPath = ConfigurationManager.AppSettings["LogFilePath"] ?? @"C:\Logs\CsvLogReader";

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", "CsvLogReader")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ThreadId}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: Path.Combine(logPath, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{ThreadId}] {Message:lj}{NewLine}{Exception}",
                shared: true)
            .CreateLogger();
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
        _maxDegreeOfParallelism = int.Parse(appSettings["MaxDegreeOfParallelism"] ?? Environment.ProcessorCount.ToString());
        _batchSize = int.Parse(appSettings["BatchSize"] ?? "1000");

        Log.Information("Configuration loaded: {@Config}", new
        {
            LogsPath = _logsPath,
            UploadEnabled = _uploadEnabled,
            DeleteEnabled = _deleteEnabled,
            MovePath = _movedLocation,
            IndividualDirectory = _individualDirectory,
            DaysToKeep = _daysToKeep,
            MaxParallelTasks = _maxDegreeOfParallelism,
            BatchSize = _batchSize,
            DestinationTable = _destinationTableName,
            EarliestAcceptableDate = earliestAcceptableDate
        });

        if (!_deleteEnabled && !Directory.Exists(_movedLocation))
        {
            Directory.CreateDirectory(_movedLocation);
            Log.Information("Created moved location directory: {MovedLocation}", _movedLocation);
        }
    }

    private static string MaskConnectionString(string connectionString)
    {
        return Regex.Replace(connectionString,
            @"(password|pwd)\s*=\s*[^;]+", "Password=***",
            RegexOptions.IgnoreCase);
    }

    private static void ProcessCsvFilesSequential(NameValueCollection appSettings)
    {
        if (_individualDirectory)
        {
            var subDirectories = Directory.GetDirectories(_logsPath);
            Log.Information("Processing {Count} subdirectories in {Path}", subDirectories.Length, _logsPath);
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
            Log.Warning("Directory does not exist: {Directory}", directory);
            Thread.Sleep(10000);
            return;
        }

        var filename = Path.GetFileName(directory);
        var signalid = Regex.Replace(filename, @"^Ctrl0*", "");
        var csvFiles = Directory.GetFiles(directory, "*.csv");

        Log.Information("Found {Count} CSV files in {Directory} for SignalId {SignalId}",
            csvFiles.Length, directory, signalid);

        Thread.Sleep(10000);

        var fileBatches = csvFiles
            .Select((file, index) => new { file, index })
            .GroupBy(x => x.index / _batchSize)
            .Select(g => g.Select(x => x.file).ToArray())
            .ToArray();

        Log.Debug("Split files into {BatchCount} batches", fileBatches.Length);

        foreach (var batch in fileBatches)
        {
            Log.Information("Processing batch of {Count} files for SignalId {SignalId}", batch.Length, signalid);

            foreach (var filePath in batch)
            {
                ProcessSingleFile(filename, signalid, filePath, appSettings);
            }

            Thread.Sleep(100);
        }
    }

    private static void ProcessSingleFile(string directory, string signalid, string filePath, NameValueCollection appSettings)
    {
        var fileName = Path.GetFileName(filePath);
        var threadId = Environment.CurrentManagedThreadId;

        Log.Debug("Starting to process file {FileName} for SignalId {SignalId}", fileName, signalid);

        try
        {
            if (_uploadEnabled)
            {
                var records = ReadCsvFileOptimized(signalid, filePath, fileName, threadId);

                if (records.Count == 0)
                {
                    Log.Warning("No records read from file {FileName}", fileName);
                    return;
                }

                Log.Debug("Read {RecordCount} records from {FileName}", records.Count, fileName);

                var table = new MOE.Common.Data.MOE.Controller_Event_LogDataTable();
                PopulateDataTable(signalid, records, table, threadId);

                var firstRecord = records[0];
                Log.Debug("Checking for existing records - SignalId: {SignalId}, Timestamp: {Timestamp}, EventCode: {EventCode}, EventParam: {EventParam}",
                    signalid, firstRecord.Timestamp, firstRecord.EventCode, firstRecord.EventParam);

                var recordExists = CELRepository.CheckIfSingleRecordExists(
                    signalid, firstRecord.Timestamp, firstRecord.EventCode, firstRecord.EventParam);

                if (recordExists)
                {
                    Log.Warning("DUPLICATE DETECTED - Records already exist for {FileName}. SignalId: {SignalId}, FirstTimestamp: {Timestamp}, EventCode: {EventCode}, EventParam: {EventParam}",
                        fileName, signalid, firstRecord.Timestamp, firstRecord.EventCode, firstRecord.EventParam);
                    return;
                }

                Log.Information("No existing records found, proceeding with insert for {FileName}", fileName);
                BulkInsertToDatabase(table, appSettings, threadId, fileName);
            }

            if (_deleteEnabled)
            {
                DeleteFile(new CsvFileInfo { FilePath = filePath, FileName = fileName });
            }
            else
            {
                MoveFile(directory, new CsvFileInfo { FilePath = filePath, FileName = fileName });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing {FileName} for SignalId {SignalId}", fileName, signalid);
        }
    }

    private static List<ControllerEventLog> ReadCsvFileOptimized(string signalID, string filePath, string fileName, int threadId)
    {
        var records = new List<ControllerEventLog>();

        try
        {
            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false
            });

            csv.Context.RegisterClassMap<ControllerEventLogMap>();

            for (var i = 0; i < 7; i++)
            {
                csv.Read();
            }

            try
            {
                records = csv.GetRecords<ControllerEventLog>()
                    .Select(record =>
                    {
                        record.SignalId = signalID;
                        return record;
                    })
                    .ToList();

                Log.Debug("Successfully parsed {Count} records from {FileName}", records.Count, fileName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error parsing CSV records from {FileName}", fileName);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error reading CSV file {FileName}", fileName);
        }

        return records;
    }

    private static void PopulateDataTable(string signalId, List<ControllerEventLog> records,
        MOE.Common.Data.MOE.Controller_Event_LogDataTable table, int threadId)
    {
        var validRecords = records.Where(r => r.Timestamp > earliestAcceptableDate).ToList();
        var filteredCount = records.Count - validRecords.Count;

        Log.Debug("Filtered records for SignalId {SignalId}: {ValidCount} valid, {FilteredCount} filtered (before {EarliestDate})",
            signalId, validRecords.Count, filteredCount, earliestAcceptableDate);

        if (validRecords.Count > 0)
        {
            var minTimestamp = validRecords.Min(r => r.Timestamp);
            var maxTimestamp = validRecords.Max(r => r.Timestamp);
            Log.Debug("Record timestamp range for SignalId {SignalId}: {MinDate} to {MaxDate}",
                signalId, minTimestamp, maxTimestamp);
        }

        foreach (var record in validRecords)
        {
            try
            {
                table.AddController_Event_LogRow(signalId, record.Timestamp,
                    record.EventCode, record.EventParam);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error adding record - SignalId: {SignalId}, Timestamp: {Timestamp}, EventCode: {EventCode}, EventParam: {EventParam}",
                    signalId, record.Timestamp, record.EventCode, record.EventParam);
            }
        }
    }

    private static void BulkInsertToDatabase(MOE.Common.Data.MOE.Controller_Event_LogDataTable table,
        NameValueCollection appSettings, int threadId, string fileName)
    {
        if (table.Rows.Count == 0)
        {
            Log.Warning("No data to insert for {FileName}", fileName);
            return;
        }

        Log.Information("Attempting bulk insert of {RowCount} rows from {FileName} to {Table}",
            table.Rows.Count, fileName, _destinationTableName);

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
            Log.Information("Successfully inserted {RowCount} records from {FileName}", table.Rows.Count, fileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database insert error for {FileName}", fileName);
            throw;
        }
    }

    private static void MoveFile(string directory, CsvFileInfo file)
    {
        var location = Path.Combine(_movedLocation, directory);
        if (!Directory.Exists(location))
        {
            Directory.CreateDirectory(location);
            Log.Debug("Created directory {Location}", location);
        }

        try
        {
            if (File.Exists(file.FilePath))
            {
                var destPath = Path.Combine(location, file.FileName);
                File.Move(file.FilePath, destPath);
                Log.Debug("File moved: {FileName} to {Destination}", file.FileName, destPath);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Move failed for {FileName}", file.FileName);
        }
    }

    private static void DeleteFile(CsvFileInfo file)
    {
        try
        {
            if (File.Exists(file.FilePath))
            {
                File.Delete(file.FilePath);
                Log.Debug("File deleted: {FileName}", file.FileName);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Delete failed for {FileName}", file.FileName);
        }
    }
}

public class CsvFileInfo
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
        Map(m => m.Timestamp).Index(0);
        Map(m => m.EventCode).Index(1);
        Map(m => m.EventParam).Index(2);
        Map(m => m.SignalId).Ignore();
    }
}