using MOE.Common.Business;
using MOE.Common.Models.Repositories;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SFTPFromAllControllers
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                WriteStartupBanner();

                var validationErrors = new List<string>();
                var signalFtpOptions = BuildSignalFtpOptions(validationErrors);
                var maxThreads = GetRequiredIntSetting("MaxThreads", validationErrors);
                var credentialsFilePath = GetOptionalSetting("SFTP_CREDENTIALS_FILE_PATH");
                var checkIpAddress = GetOptionalBoolSetting("CheckIPAddress", true);

                ValidateStartup(signalFtpOptions, credentialsFilePath, validationErrors);

                if (validationErrors.Any())
                {
                    Console.WriteLine("Startup validation failed:");
                    foreach (var error in validationErrors)
                    {
                        Console.WriteLine(" - " + error);
                    }

                    return 1;
                }

                Console.WriteLine("Config loaded successfully.");
                Console.WriteLine(" - Current directory: " + Environment.CurrentDirectory);
                Console.WriteLine(" - Config file: " + AppDomain.CurrentDomain.SetupInformation.ConfigurationFile);
                Console.WriteLine(" - LocalDirectory: " + signalFtpOptions.LocalDirectory);
                Console.WriteLine(" - RequiresPPK: " + signalFtpOptions.RequiresPpk);
                Console.WriteLine(" - SFTP_CREDENTIALS_FILE_PATH: " + (string.IsNullOrWhiteSpace(credentialsFilePath) ? "<not set>" : credentialsFilePath));
                Console.WriteLine(" - CheckIPAddress: " + checkIpAddress);
                Console.WriteLine(" - MaxThreads: " + maxThreads);

                var errorRepository = ApplicationEventRepositoryFactory.Create();

                Console.WriteLine("Connecting to SPM database...");
                var db = new MOE.Common.Models.SPM();
                var signalsRepository = SignalsRepositoryFactory.Create(db);
                Console.WriteLine("Querying signals for SFTP...");

                var options = new ParallelOptions { MaxDegreeOfParallelism = maxThreads };
                var counters = new RunCounters();
                if (signalFtpOptions.RequiresPpk)
                {
                    var signals =
                        signalsRepository.GetLatestVersionOfAllSignalsForSftp(signalFtpOptions.RegionControllerType);
                    counters.TotalSignals = signals.Count;
                    Console.WriteLine("Found " + counters.TotalSignals + " signals for RegionalControllerType " + signalFtpOptions.RegionControllerType + ".");
                    if (counters.TotalSignals == 0)
                    {
                        Console.WriteLine("Fatal: 0 signals found for the configured controller type.");
                        return 2;
                    }

                    //Parallel.ForEach(signals.AsEnumerable(), options, signal =>
                    foreach (var signal in signals)
                    {
                        try
                        {
                            Console.WriteLine("Starting signal " + signal.SignalID + " @ " + signal.IPAddress + " using PPK mode.");
                            var signalFtp =
                                new SignalFtp(signal, signalFtpOptions);

                            EnsureSignalDirectoryExists(signalFtpOptions.LocalDirectory, signal.SignalID);

                            //Get the records over FTP
                            if (CheckIfIPAddressIsValid(signal, checkIpAddress))
                            {
                                Interlocked.Increment(ref counters.SignalsQueuedForTransfer);
                                try
                                {
                                    signalFtp.GetCubicFilesAsyncPpk(signalFtpOptions.PpkLocation,
                                        true);
                                }
                                catch (Exception ex)
                                {
                                    Interlocked.Increment(ref counters.SignalFailures);
                                    Console.WriteLine("Error starting transfer for signal " + signal.SignalID + ": " + ex);
                                    errorRepository.QuickAdd("SFTPFromAllControllers", "Main", "Main Loop",
                                        MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                                        "Error starting transfer for signal " + signal.SignalID + ": " + ex.Message);
                                }
                            }
                            else
                            {
                                Interlocked.Increment(ref counters.SignalsSkippedByIpValidation);
                                Console.WriteLine("Signal " + signal.SignalID + " has failed IP validation. Check IP config and if the signal is pingable.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref counters.SignalFailures);
                            Console.WriteLine("Error preparing signal " + signal.SignalID + ": " + ex);
                            errorRepository.QuickAdd("SFTPFromAllControllers", "Main", "Main Loop",
                                MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                                "Error preparing signal " + signal.SignalID + ": " + ex.Message);

                        }

                        //}
                    }
                }
                else
                {
                    var signals = signalsRepository.GetLatestVersionOfAllSignalsForSftp();
                    counters.TotalSignals = signals.Count;
                    Console.WriteLine("Found " + counters.TotalSignals + " signals for SFTP.");
                    if (counters.TotalSignals == 0)
                    {
                        Console.WriteLine("Fatal: 0 signals found for the configured controller type.");
                        return 2;
                    }

                    Parallel.ForEach(signals.AsEnumerable(), options, signal =>
                    {
                        try
                        {
                            Console.WriteLine("Starting signal " + signal.SignalID + " @ " + signal.IPAddress + " using credential file mode.");
                            var signalFtp =
                                new SignalFtp(signal, signalFtpOptions);

                            EnsureSignalDirectoryExists(signalFtpOptions.LocalDirectory, signal.SignalID);

                            //Get the records over FTP
                            if (CheckIfIPAddressIsValid(signal, checkIpAddress))
                            {
                                Interlocked.Increment(ref counters.SignalsQueuedForTransfer);
                                try
                                {
                                    signalFtp.GetCubicFilesAsync(credentialsFilePath);
                                }
                                catch (Exception ex)
                                {
                                    Interlocked.Increment(ref counters.SignalFailures);
                                    Console.WriteLine("Error starting transfer for signal " + signal.SignalID + ": " + ex);
                                    errorRepository.QuickAdd("SFTPFromAllControllers", "Main", "Main Loop",
                                        MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                                        "Error starting transfer for signal " + signal.SignalID + ": " + ex.Message);
                                }

                            }
                            else
                            {
                                Interlocked.Increment(ref counters.SignalsSkippedByIpValidation);
                                Console.WriteLine("Signal " + signal.SignalID + " has failed IP validation. Check IP config and if the signal is pingable.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref counters.SignalFailures);
                            Console.WriteLine("Error preparing signal " + signal.SignalID + ": " + ex);
                            errorRepository.QuickAdd("SFTPFromAllControllers", "Main", "Main Loop",
                                MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                                "Error preparing signal " + signal.SignalID + ": " + ex.Message);
                        }
                    });
                }

                Console.WriteLine("Run complete.");
                Console.WriteLine(" - Signals found: " + counters.TotalSignals);
                Console.WriteLine(" - Transfers queued: " + counters.SignalsQueuedForTransfer);
                Console.WriteLine(" - Signals skipped by IP validation: " + counters.SignalsSkippedByIpValidation);
                Console.WriteLine(" - Signal setup failures: " + counters.SignalFailures);

                return counters.SignalFailures > 0 ? 3 : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Fatal startup error:");
                Console.WriteLine(ex);
                return 1;
            }
        }

        private static SignalFtpOptions BuildSignalFtpOptions(List<string> validationErrors)
        {
            return new SignalFtpOptions(
                GetRequiredIntSetting("SNMPTimeout", validationErrors),
                GetRequiredIntSetting("SNMPRetry", validationErrors),
                GetRequiredIntSetting("SNMPPort", validationErrors),
                GetRequiredBoolSetting("DeleteFilesAfterFTP", validationErrors),
                GetRequiredStringSetting("LocalDirectory", validationErrors),
                GetRequiredIntSetting("FTPConnectionTimeoutInSeconds", validationErrors),
                GetRequiredIntSetting("FTPReadTimeoutInSeconds", validationErrors),
                GetOptionalBoolSetting(new[] { "SkipCurrentLog", "skipCurrentLog" }, true),
                GetRequiredBoolSetting("RenameDuplicateFiles", validationErrors),
                GetRequiredIntSetting("waitBetweenFileDownloadMilliseconds", validationErrors),
                GetRequiredIntSetting("MaximumNumberOfFilesTransferAtOneTime", validationErrors),
                GetRequiredBoolSetting("RequiresPPK", validationErrors),
                GetOptionalSetting("PPKLocation"),
                GetOptionalIntSetting("RegionalControllerType", 20),
                GetOptionalSetting("SshFingerprint"),
                GetOptionalBoolSetting("IsGzip", false),
                GetOptionalBoolSetting("UsePhysicalLocation", false)
            );
        }

        private static void ValidateStartup(SignalFtpOptions signalFtpOptions, string credentialsFilePath, List<string> validationErrors)
        {
            if (!string.IsNullOrWhiteSpace(signalFtpOptions.LocalDirectory))
            {
                try
                {
                    Directory.CreateDirectory(signalFtpOptions.LocalDirectory);
                }
                catch (Exception ex)
                {
                    validationErrors.Add("LocalDirectory is invalid or not writable: " + signalFtpOptions.LocalDirectory + " - " + ex.Message);
                }
            }

            if (signalFtpOptions.RequiresPpk)
            {
                if (string.IsNullOrWhiteSpace(signalFtpOptions.PpkLocation))
                {
                    validationErrors.Add("PPKLocation is required when RequiresPPK=true.");
                }
                else if (!File.Exists(signalFtpOptions.PpkLocation))
                {
                    validationErrors.Add("PPKLocation file was not found: " + signalFtpOptions.PpkLocation);
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(credentialsFilePath))
                {
                    validationErrors.Add("SFTP_CREDENTIALS_FILE_PATH is required when RequiresPPK=false.");
                }
                else if (!File.Exists(credentialsFilePath))
                {
                    validationErrors.Add("SFTP credentials file was not found: " + credentialsFilePath);
                }
                else
                {
                    try
                    {
                        var credentialsLines = File.ReadAllLines(credentialsFilePath);
                        if (credentialsLines.Length < 2 || string.IsNullOrWhiteSpace(credentialsLines[0]) || string.IsNullOrWhiteSpace(credentialsLines[1]))
                        {
                            validationErrors.Add("SFTP credentials file must contain username on line 1 and password on line 2: " + credentialsFilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        validationErrors.Add("Unable to read SFTP credentials file " + credentialsFilePath + ": " + ex.Message);
                    }
                }
            }
        }

        private static void EnsureSignalDirectoryExists(string localDirectory, string signalId)
        {
            Directory.CreateDirectory(Path.Combine(localDirectory, signalId));
        }

        private static void WriteStartupBanner()
        {
            Console.WriteLine("SFTPFromAllControllers starting...");
            Console.WriteLine(" - Machine: " + Environment.MachineName);
            Console.WriteLine(" - Current directory: " + Environment.CurrentDirectory);
            Console.WriteLine(" - Config file: " + AppDomain.CurrentDomain.SetupInformation.ConfigurationFile);
        }

        private static string GetOptionalSetting(string key)
        {
            return ConfigurationManager.AppSettings[key];
        }

        private static string GetRequiredStringSetting(string key, List<string> validationErrors)
        {
            var value = GetOptionalSetting(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                validationErrors.Add("Missing required appSetting '" + key + "'.");
            }

            return value;
        }

        private static int GetRequiredIntSetting(string key, List<string> validationErrors)
        {
            var value = GetOptionalSetting(key);
            if (!int.TryParse(value, out var parsedValue))
            {
                validationErrors.Add("appSetting '" + key + "' must be an integer. Current value: '" + value + "'.");
            }

            return parsedValue;
        }

        private static int GetOptionalIntSetting(string key, int defaultValue)
        {
            var value = GetOptionalSetting(key);
            return int.TryParse(value, out var parsedValue) ? parsedValue : defaultValue;
        }

        private static bool GetRequiredBoolSetting(string key, List<string> validationErrors)
        {
            var value = GetOptionalSetting(key);
            if (!bool.TryParse(value, out var parsedValue))
            {
                validationErrors.Add("appSetting '" + key + "' must be true or false. Current value: '" + value + "'.");
            }

            return parsedValue;
        }

        private static bool GetOptionalBoolSetting(string key, bool defaultValue)
        {
            var value = GetOptionalSetting(key);
            return bool.TryParse(value, out var parsedValue) ? parsedValue : defaultValue;
        }

        private static bool GetOptionalBoolSetting(IEnumerable<string> keys, bool defaultValue)
        {
            foreach (var key in keys)
            {
                var value = GetOptionalSetting(key);
                if (bool.TryParse(value, out var parsedValue))
                {
                    return parsedValue;
                }
            }

            return defaultValue;
        }

        public static bool CheckIfIPAddressIsValid(MOE.Common.Models.Signal signal, bool checkIpAddress = true)
        {
            if (!checkIpAddress)
            {
                return true;
            }

            if (signal.IPAddress == "0" || signal.IPAddress == "0.0.0.0")
                return false;

            if (!IPAddress.TryParse(signal.IPAddress, out _))
                return false;

            Ping pingSender = new Ping();
            PingOptions pingOptions = new PingOptions
            {
                DontFragment = true
            };

            string data = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            byte[] buffer = Encoding.ASCII.GetBytes(data);
            int timeout = 500; // Increase timeout for stability
            int successCount = 0;
            int attempts = 3; // Retry 3 times

            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    PingReply reply = pingSender.Send(signal.IPAddress, timeout, buffer, pingOptions);
                    if (reply.Status == IPStatus.Success)
                    {
                        successCount++;
                    }
                }
                catch
                {
                    // Ignore failed attempts
                }
                Thread.Sleep(100); // Short delay between retries
            }

            // Consider IP valid if at least one attempt succeeds
            return successCount > 0;
        }

        private class RunCounters
        {
            public int TotalSignals;
            public int SignalsQueuedForTransfer;
            public int SignalsSkippedByIpValidation;
            public int SignalFailures;
        }
    }
}
