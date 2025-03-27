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

namespace STFPFromAllControllers
{
    class Program
    {
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
                    ConfigurationManager.AppSettings["SshFingerprint"]
                );
                var maxThreads = Convert.ToInt32(ConfigurationManager.AppSettings["MaxThreads"]);


                var db = new MOE.Common.Models.SPM();
                var signalsRepository = SignalsRepositoryFactory.Create(db);

                var options = new ParallelOptions { MaxDegreeOfParallelism = maxThreads };
                if (signalFtpOptions.RequiresPpk)
                {
                    var signals =
                        signalsRepository.GetLatestVersionOfAllSignalsForSftp(signalFtpOptions.RegionControllerType);
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
                                        signalFtpOptions.SshFingerprint);
                                }
                                catch (AggregateException ex)
                                {
                                    Console.WriteLine("Error At Highest Level for signal " + ex.Message);
                                    errorRepository.QuickAdd("FTPFromAllControllers", "Main", "Main Loop",
                                        MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                                        "Error At Highest Level for signal " + signal.SignalID);
                                }
                            }
                        }
                        catch (AggregateException ex)
                        {
                            Console.WriteLine("Error At Highest Level for signal " + ex.Message);
                            errorRepository.QuickAdd("FTPFromAllControllers", "Main", "Main Loop",
                                MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                                "Error At Highest Level for signal " + signal.SignalID);

                        }

                        //}
                    }
                }
                else
                {
                    var signals = signalsRepository.GetLatestVersionOfAllSignalsForSftp();
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
                                    signalFtp.GetCubicFilesAsync(
                                        ConfigurationManager.AppSettings["SFTP_CREDENTIALS_FILE_PATH"]);
                                }
                                catch (AggregateException ex)
                                {
                                    Console.WriteLine("Error At Highest Level for signal " + ex.Message);
                                    errorRepository.QuickAdd("FTPFromAllControllers", "Main", "Main Loop",
                                        MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                                        "Error At Highest Level for signal " + signal.SignalID);
                                }

                            }
                            else
                            {
                                Console.WriteLine("Signal " + signal.SignalID + "has failed IP validation. Check IP config and if the signal is pingable");
                            }
                        }
                        catch (AggregateException ex)
                        {
                            Console.WriteLine("Error At Highest Level for signal " + ex.Message);
                            errorRepository.QuickAdd("SFTPFromAllControllers", "Main", "Main Loop",
                                MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                                "Error At Highest Level for signal " + signal.SignalID);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error At Highest Level for signal " + ex.Message);
            }
        }

        public static bool CheckIfIPAddressIsValid(MOE.Common.Models.Signal signal)
        {
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
