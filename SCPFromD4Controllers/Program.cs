using MOE.Common.Business;
using MOE.Common.Models.Repositories;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MOE.Common.Business.CustomReport;
using Dapper;
using System;
using System.Data;
using Microsoft.Data.SqlClient;

namespace SCPFromD4Controllers
{
    class Program
    {

        public static IDbConnection db = new SqlConnection(ConfigurationManager.ConnectionStrings["SPM"].ConnectionString);
        static void Main(string[] args)
        {

            try
            {
                
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
                    Convert.ToBoolean(ConfigurationManager.AppSettings["IsGzip"])
                );
                var connection = new SqlConnection(ConfigurationManager.ConnectionStrings["SPM"].ConnectionString);
                var signalList = GetSignals(db);

                //get the
                var maxThreads = Convert.ToInt32(ConfigurationManager.AppSettings["MaxThreads"]);

                var options = new ParallelOptions { MaxDegreeOfParallelism = maxThreads };
                if (signalFtpOptions.RequiresPpk)
                {

                    //Parallel.ForEach(signals.AsEnumerable(), options, signal =>
                    foreach (var signal in signalList)
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
                            //todo if the sftp fails because it isnt listening, try ftp

                            signalFtp.GetCubicFilesAsync(ConfigurationManager.AppSettings["SFTP_CREDENTIALS_FILE_PATH"]);
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

        public static List<MOE.Common.Models.Signal> GetSignals(IDbConnection db)
        {
            var sql = "SELECT * FROM signals Where ControllerType = 30";
            var products = db.Query<MOE.Common.Models.Signal>(sql);
            return products.ToList();
        }
    }
}