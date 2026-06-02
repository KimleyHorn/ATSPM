using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.NetworkInformation;
using Lextm.SharpSnmpLib.Messaging;
using System.Threading.Tasks;
using System.Configuration;
using System.Security;
using MOE.Common.Business;
using MOE.Common.Data;
using MOE.Common.Models;
using MOE.Common.Models.Repositories;
using Serilog;

namespace FTPfromAllControllers
{
    public class FTPfromAllControllers
    {
        static void Main(string[] args)
        {
            IApplicationEventRepository errorRepository = ApplicationEventRepositoryFactory.Create();
            ConfigureLogging();
            //while (true) 
            //{ 
            try
            {
                Log.Information("FTPfromAllControllers starting");

                SignalFtpOptions signalFtpOptions = new SignalFtpOptions(
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
                    Convert.ToBoolean(ConfigurationManager.AppSettings["IsGzipAgency"]),
                    Convert.ToBoolean(ConfigurationManager.AppSettings["UsePhysicalPath"])
                );
                Log.Debug("Signal FTP options loaded. LocalDirectory={LocalDirectory}", signalFtpOptions.LocalDirectory);

                SPM db = new MOE.Common.Models.SPM();
                ISignalsRepository signalsRepository = SignalsRepositoryFactory.Create(db);
                List<Signal> signals = signalsRepository.GetLatestVersionOfAllSignalsForFtp().OrderBy(x=>x.SignalID).ToList();
                Log.Information("Loaded {SignalCount} signals", signals.Count);

                foreach (var signal in signals)
                {
                    Log.Debug("Signal discovered. SignalID={SignalId}, IPAddress={IpAddress}, ControllerTypeID={ControllerTypeId}", signal.SignalID, signal.IPAddress, signal.ControllerTypeID);
                }

                int maxThreads = Convert.ToInt32(ConfigurationManager.AppSettings["MaxThreads"]);
                int minutesToWait = Convert.ToInt32(ConfigurationManager.AppSettings["MinutesToWait"]);
                var options = new ParallelOptions { MaxDegreeOfParallelism = maxThreads };
                Log.Debug("Run settings loaded. MaxThreads={MaxThreads}, MinutesToWait={MinutesToWait}", maxThreads, minutesToWait);
                Parallel.ForEach(signals, options, signal =>
                {
                    ProcessSignal(signal, signalFtpOptions, errorRepository);
                });
                Log.Information("FTPfromAllControllers finished successfully");
                //});
                //string timeNow = DateTime.Now.ToString("t"); 
                //Console.WriteLine("At {0}, it is time to take a nap. Program will wait for {1} minutes.", timeNow, minutesToWait); 
                //System.Threading.Thread.Sleep(minutesToWait * 60 * 1000); 
            }
            catch (AggregateException ex)
            {
                Log.Fatal(ex, "Aggregate error at highest level for FTPfromAllControllers");
                errorRepository.QuickAdd("FTPFromAllControllers", "Main", "Main Loop",
                    MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                    "Error At Highest Level for Main (FTPfromAllControllers) at " + DateTime.Now.ToString("g"));
            }
            finally
            {
                Log.CloseAndFlush();
            }
            //} 
        }

        private static void ConfigureLogging()
        {
            var logPath = Path.Combine(GetDefaultLogDirectory(), "log-.txt");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console()
                .WriteTo.File(
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }

        private static string GetDefaultLogDirectory()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var directory = new DirectoryInfo(baseDirectory);

            while (directory != null && !directory.GetFiles("*.csproj").Any())
            {
                directory = directory.Parent;
            }

            var projectDirectory = directory != null ? directory.FullName : baseDirectory;
            var datedLogDirectory = Path.Combine(projectDirectory, "logs", DateTime.Now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(datedLogDirectory);
            return datedLogDirectory;
        }

        private static void ProcessSignal(Signal signal, SignalFtpOptions signalFtpOptions, IApplicationEventRepository errorRepository)
        {
            if (signal.ControllerTypeID != 2)
            {
                Log.Debug("Skipping signal {SignalId}. Unsupported ControllerTypeID={ControllerTypeId} (SFTP)", signal.SignalID, signal.ControllerTypeID);
                return;
            }

            try
            {
                Log.Information("Starting FTP for signal {SignalId}", signal.SignalID);
                MOE.Common.Business.SignalFtp signalFtp =
                    new MOE.Common.Business.SignalFtp(signal, signalFtpOptions);
                var signalDirectory = Path.Combine(signalFtpOptions.LocalDirectory, signal.SignalID.ToString());
                if (!Directory.Exists(signalDirectory))
                {
                    Directory.CreateDirectory(signalDirectory);
                    Log.Debug("Created signal directory {SignalDirectory}", signalDirectory);
                }

                try
                {
                    signalFtp.GetCurrentRecords();
                    Log.Information("Completed FTP for signal {SignalId}", signal.SignalID);
                }
                catch (AggregateException ex)
                {
                    Log.Error(ex, "Aggregate error while processing signal {SignalId}", signal.SignalID);
                    errorRepository.QuickAdd("FTPFromAllControllers", "Main", "Main Loop",
                        MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                        "Error At Highest Level for signal " + signal.SignalID);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Unexpected error while processing signal {SignalId}", signal.SignalID);
                    errorRepository.QuickAdd("FTPFromAllControllers", "Main", "Main Loop",
                        MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                        "Unexpected error while processing signal " + signal.SignalID);
                }
            }
            catch (AggregateException ex)
            {
                Log.Error(ex, "Aggregate error creating FTP processor for signal {SignalId}", signal.SignalID);
                errorRepository.QuickAdd("FTPFromAllControllers", "Main", "Main Loop",
                    MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                    "Error At Highest Level for signal " + signal.SignalID);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error creating FTP processor for signal {SignalId}", signal.SignalID);
                errorRepository.QuickAdd("FTPFromAllControllers", "Main", "Main Loop",
                    MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                    "Unexpected error creating FTP processor for signal " + signal.SignalID);
            }
        }

        //public static bool CheckIfIPAddressIsValid(MOE.Common.Models.Signal signal)
        //{
        //    bool hasValidIP = false;
        //    IPAddress ip;
        //    if (signal.IPAddress == "0")
        //    {
        //        return false;
        //    }
        //    if (signal.IPAddress == "0.0.0.0")
        //    {
        //        return false;
        //    }

        //    //test to see if the address is reachable 
        //    if (IPAddress.TryParse(signal.IPAddress, out ip))
        //    {
        //        Ping pingSender = new Ping();
        //        PingOptions pingOptions = new PingOptions();

        //        // Use the default Ttl value which is 128,  
        //        // but change the fragmentation behavior. 
        //        pingOptions.DontFragment = true;

        //        // Create a buffer of 32 bytes of data to be transmitted.  
        //        string data = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        //        byte[] buffer = Encoding.ASCII.GetBytes(data);
        //        int timeout = 120;
        //        try
        //        {
        //            PingReply reply = pingSender.Send(signal.IPAddress, timeout, buffer, pingOptions);
        //            if (reply != null && reply.Status == IPStatus.Success)
        //            {
        //                hasValidIP = true;
        //            }
        //        }
        //        catch
        //        {
        //            hasValidIP = false;
        //        }
        //    }
        //    return hasValidIP;
        //}
    }
}
