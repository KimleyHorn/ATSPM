using System.Diagnostics;
using System.Text.Json;

namespace ParquetToBlobStorage;

internal static class Program
{
    private const string DefaultSourceRoot = @"C:\ParquetArchive";

    private static async Task<int> Main(string[] args)
    {
        var options = LoadOptions(args);

        if (string.IsNullOrWhiteSpace(options.SasUrl))
        {
            Console.Error.WriteLine("Missing required SAS URL. Set SasUrl in appsettings.json, AZURE_BLOB_SAS_URL, or pass --sas-url.");
            return 1;
        }

        if (!Directory.Exists(options.SourceRoot))
        {
            Console.Error.WriteLine($"Source root does not exist: {options.SourceRoot}");
            return 1;
        }

        var archiveDateFolder = options.ArchiveDate.ToString(options.ArchiveDateFolderFormat);
        var signalDirectories = Directory.EnumerateDirectories(options.SourceRoot).OrderBy(path => path).ToList();

        if (signalDirectories.Count == 0)
        {
            Console.WriteLine($"No signal folders found under {options.SourceRoot}.");
            return 0;
        }

        var uploaded = 0;
        var failed = 0;

        foreach (var signalDirectory in signalDirectories)
        {
            var signalId = Path.GetFileName(signalDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var dateDirectory = Path.Combine(signalDirectory, archiveDateFolder);

            if (!Directory.Exists(dateDirectory))
            {
                Console.WriteLine($"Skipping {signalId}: no folder for {archiveDateFolder}.");
                continue;
            }

            var parquetFiles = Directory.EnumerateFiles(dateDirectory, "*.parquet", SearchOption.TopDirectoryOnly).ToList();
            if (parquetFiles.Count == 0)
            {
                Console.WriteLine($"Skipping {signalId}: no parquet files in {dateDirectory}.");
                continue;
            }

            var destinationUrl = AppendPathSegmentsToSasUrl(options.SasUrl, signalId, archiveDateFolder);

            foreach (var parquetFile in parquetFiles)
            {
                var result = await RunAzCopy(options.AzCopyPath, parquetFile, destinationUrl, options.Overwrite, options.DryRun);
                if (result == 0)
                {
                    uploaded++;
                }
                else
                {
                    failed++;
                    Console.Error.WriteLine($"AzCopy failed for {parquetFile} with exit code {result}.");
                }
            }
        }

        Console.WriteLine($"Finished AzCopy upload for {archiveDateFolder}. Uploaded: {uploaded}. Failed: {failed}.");
        return failed == 0 ? 0 : 1;
    }

    private static UploadOptions LoadOptions(string[] args)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var options = UploadOptions.FromConfigFile(configPath);

        options.SourceRoot = Environment.GetEnvironmentVariable("PARQUET_SOURCE_ROOT") ?? options.SourceRoot;
        options.SasUrl = Environment.GetEnvironmentVariable("AZURE_BLOB_SAS_URL") ?? options.SasUrl;
        options.AzCopyPath = Environment.GetEnvironmentVariable("AZCOPY_PATH") ?? options.AzCopyPath;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--source-root":
                    options.SourceRoot = ReadNext(args, ref i);
                    break;
                case "--sas-url":
                    options.SasUrl = ReadNext(args, ref i);
                    break;
                case "--azcopy-path":
                    options.AzCopyPath = ReadNext(args, ref i);
                    break;
                case "--archive-date":
                    options.ArchiveDate = DateOnly.Parse(ReadNext(args, ref i));
                    break;
                case "--archive-date-format":
                    options.ArchiveDateFolderFormat = ReadNext(args, ref i);
                    break;
                case "--overwrite":
                    options.Overwrite = ReadNext(args, ref i);
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
            }
        }

        return options;
    }

    private static string ReadNext(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {args[index]}.");
        }

        index++;
        return args[index];
    }

    private static async Task<int> RunAzCopy(string azCopyPath, string sourceFile, string destinationUrl, string overwrite, bool dryRun)
    {
        Console.WriteLine($"Uploading {sourceFile}");

        if (dryRun)
        {
            Console.WriteLine($"DRY RUN: {azCopyPath} copy \"{sourceFile}\" \"{destinationUrl}\" --overwrite={overwrite}");
            return 0;
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = azCopyPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        processStartInfo.ArgumentList.Add("copy");
        processStartInfo.ArgumentList.Add(sourceFile);
        processStartInfo.ArgumentList.Add(destinationUrl);
        processStartInfo.ArgumentList.Add($"--overwrite={overwrite}");

        using var process = Process.Start(processStartInfo);
        if (process is null)
        {
            Console.Error.WriteLine("Could not start AzCopy.");
            return 1;
        }

        process.OutputDataReceived += (_, eventArgs) => WriteIfPresent(eventArgs.Data, Console.WriteLine);
        process.ErrorDataReceived += (_, eventArgs) => WriteIfPresent(eventArgs.Data, Console.Error.WriteLine);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static void WriteIfPresent(string? line, Action<string> writeLine)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            writeLine(line);
        }
    }

    private static string AppendPathSegmentsToSasUrl(string sasUrl, params string[] pathSegments)
    {
        var queryStart = sasUrl.IndexOf('?');
        var baseUrl = queryStart >= 0 ? sasUrl[..queryStart] : sasUrl;
        var query = queryStart >= 0 ? sasUrl[queryStart..] : string.Empty;
        var cleanBaseUrl = baseUrl.TrimEnd('/');
        var cleanSegments = pathSegments.Select(Uri.EscapeDataString);

        return $"{cleanBaseUrl}/{string.Join('/', cleanSegments)}{query}";
    }

    private sealed class UploadOptions
    {
        public string SourceRoot { get; set; } = DefaultSourceRoot;
        public string SasUrl { get; set; } = string.Empty;
        public string AzCopyPath { get; set; } = "azcopy";
        public DateOnly ArchiveDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        public string ArchiveDateFolderFormat { get; set; } = "yyyy-MM-dd";
        public string Overwrite { get; set; } = "ifSourceNewer";
        public bool DryRun { get; set; }

        public static UploadOptions FromConfigFile(string fileName)
        {
            if (!File.Exists(fileName))
            {
                return new UploadOptions();
            }

            var json = File.ReadAllText(fileName);
            return JsonSerializer.Deserialize<UploadOptions>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new UploadOptions();
        }
    }
}
