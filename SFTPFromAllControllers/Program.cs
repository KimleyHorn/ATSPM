using MOE.Common.Business;
using MOE.Common.Models.Repositories;
using Serilog;
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
        private const int DefaultSftpControllerType = 20;

        static void Main(string[] args)
        {
            try
            {
                var errorRepository = ApplicationEventRepositoryFactory.Create();
                var signalFtpOptions = new SignalFtpOptions(
                    Convert.ToInt32(ConfigurationManager.AppSettings["SNMPTimeout"]),
                    Convert.ToInt32(ConfigurationManager.AppSettings["SNMPRetry"]),
                    Convert.ToInt32(ConfigurationManager.AppSettings["SNMPPort"]),
                    Convert.ToBoolean(ConfigurationManager.AppSettings["DeleteFilesAfterFTP"]),
                    ConfigurationManager.AppSettings["LocalDirectory"],
                    Convert.ToInt32(ConfigurationManager.AppSettings["FTPConnectionTimeoutInSeconds"]),
                    Convert.ToInt32(ConfigurationManager.AppSettings["FTPReadTimeoutInSeconds"]),
                    Convert.ToBoolean(ConfigurationManager.AppSettings["skipCurrentLog"]),
                    Convert.ToBoolean(ConfigurationManager.AppSettings["RenameDuplicateFiles"]),
                    Convert.ToInt32(ConfigurationManager.AppSettings["waitBetweenFileDownloadMilliseconds"]),
                    Convert.ToInt32(ConfigurationManager.AppSettings["MaximumNumberOfFilesTransferAtOneTime"]),
                    Convert.ToBoolean(ConfigurationManager.AppSettings["RequiresPPK"]),
                    ConfigurationManager.AppSettings["PPKLocation"],
                    Convert.ToInt32(ConfigurationManager.AppSettings["RegionalControllerType"]),
                    ConfigurationManager.AppSettings["SshFingerprint"],
                    Convert.ToBoolean(ConfigurationManager.AppSettings["IsGzip"]),
                    Convert.ToBoolean(ConfigurationManager.AppSettings["UsePhysicalLocation"]),
                    GetBooleanAppSetting("UseLegacySshAlgorithms", false)
                );
                var maxThreads = Convert.ToInt32(ConfigurationManager.AppSettings["MaxThreads"]);


                var db = new MOE.Common.Models.SPM();
                var signalsRepository = SignalsRepositoryFactory.Create(db);
                var signals = signalsRepository.GetLatestVersionOfAllSignalsForSftp(DefaultSftpControllerType);
                if (GetBooleanAppSetting("UseRegionalControllerType", false)
                    && signalFtpOptions.RegionControllerType != DefaultSftpControllerType)
                {
                    var existingSignalIds = new HashSet<string>(signals.Select(signal => signal.SignalID));
                    var regionalSignals = signalsRepository.GetLatestVersionOfAllSignalsForSftp(signalFtpOptions.RegionControllerType);
                    foreach (var regionalSignal in regionalSignals)
                    {
                        if (existingSignalIds.Add(regionalSignal.SignalID))
                        {
                            signals.Add(regionalSignal);
                        }
                    }
                }

                var options = new ParallelOptions { MaxDegreeOfParallelism = maxThreads };
                if (signalFtpOptions.RequiresPpk)
                {
                    //Parallel.ForEach(signals.AsEnumerable(), options, signal =>
                    foreach (var signal in signals)
                    {
                        try
                        {
                            var signalFtp =
                                new SignalFtp(signal, signalFtpOptions);

                            if (!Directory.Exists(signalFtpOptions.LocalDirectory + signal.SignalID))
                            {
                                Directory.CreateDirectory(signalFtpOptions.LocalDirectory + signal.SignalID);
                            }

                            //Get the records over FTP
                            if (CheckIfIPAddressIsValid(signal))
                            {
                                try
                                {
                                    signalFtp.GetCubicFilesAsyncPpk(signalFtpOptions.PpkLocation,
                                        true);
                                }
                                catch (Exception ex)
                                {
                                    LogHighestLevelError(errorRepository, "FTPFromAllControllers", signal.SignalID, ex);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHighestLevelError(errorRepository, "FTPFromAllControllers", signal.SignalID, ex);

                        }

                        //}
                    }
                }
                else
                {
                    Parallel.ForEach(signals.AsEnumerable(), options, signal =>
                    {
                        try
                        {
                            var signalFtp =
                                new SignalFtp(signal, signalFtpOptions);

                            if (!Directory.Exists(signalFtpOptions.LocalDirectory + signal.SignalID))
                            {
                                Directory.CreateDirectory(signalFtpOptions.LocalDirectory + signal.SignalID);
                            }

                            //Get the records over FTP
                            if (CheckIfIPAddressIsValid(signal))
                            {
                                try
                                {
                                    signalFtp.GetCubicFilesAsync();
                                }
                                catch (Exception ex)
                                {
                                    LogHighestLevelError(errorRepository, "FTPFromAllControllers", signal.SignalID, ex);
                                }

                            }
                            else
                            {
                                Console.WriteLine("Signal " + signal.SignalID + "has failed IP validation. Check IP config and if the signal is pingable");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHighestLevelError(errorRepository, "SFTPFromAllControllers", signal.SignalID, ex);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error At Highest Level:" + Environment.NewLine + FormatExceptionMessages(ex));
            }
        }

        private static bool GetBooleanAppSetting(string key, bool defaultValue)
        {
            var value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            bool parsedValue;
            return bool.TryParse(value, out parsedValue) ? parsedValue : defaultValue;
        }

        private static void LogHighestLevelError(
            IApplicationEventRepository errorRepository,
            string applicationName,
            string signalId,
            Exception ex)
        {
            var errorMessage = "Error At Highest Level for signal " + signalId + ":" +
                               Environment.NewLine + FormatExceptionMessages(ex);
            Console.WriteLine(errorMessage);
            errorRepository.QuickAdd(applicationName, "Main", "Main Loop",
                MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                errorMessage);
        }

        private static string FormatExceptionMessages(Exception ex)
        {
            var message = new StringBuilder();
            AppendExceptionDetails(message, ex, 0);
            return message.ToString();
        }

        private static void AppendExceptionDetails(StringBuilder message, Exception ex, int depth)
        {
            if (ex == null)
            {
                return;
            }

            message.AppendLine(depth == 0
                ? "Exception:"
                : "Inner Exception " + depth + ":");
            message.AppendLine("Type: " + ex.GetType().FullName);
            message.AppendLine("Message: " + ex.Message);

            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            {
                message.AppendLine("Stack Trace:");
                message.AppendLine(ex.StackTrace);
            }

            var aggregateException = ex as AggregateException;
            if (aggregateException != null)
            {
                var innerDepth = depth + 1;
                foreach (var innerException in aggregateException.Flatten().InnerExceptions)
                {
                    AppendExceptionDetails(message, innerException, innerDepth);
                    innerDepth++;
                }

                return;
            }

            AppendExceptionDetails(message, ex.InnerException, depth + 1);
        }

        public static bool CheckIfIPAddressIsValid(MOE.Common.Models.Signal signal)
        {
            var checkIp = ConfigurationManager.AppSettings["CheckIPAddress"];
            if (checkIp != null && checkIp.ToLower() == "false")
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
    }
}
