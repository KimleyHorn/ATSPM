using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Configuration;
using System.Net.Mail;
using MOE.Common;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.Data.SqlClient;
using System.Timers;
using Serilog;
using Serilog.Core;


namespace DecodeSiemensLogs
{
    public class EventDetail
    {
        public int EventCode;
        public string EventDescriptor;
        public string Parameter;
        public string Description;

        public override string ToString()
        {
            return "Event Code: " + EventCode + "\t" +
                   "Event Descriptor: " + EventDescriptor + "\t" +
                   "Parameter: " + Parameter + "\t" +
                   "Description: " + Description + "\t";
        }
    }

    class Program
    {
        //create a new const integer list named YunexIntList

        public static readonly List<EventDetail> YunexUniqueEventCodes = new List<EventDetail>
        {
            //Utility Events
            new EventDetail
            {
                EventCode = 1000, EventDescriptor = "Data Gap Alarm", Parameter = "<none>",
                Description = "Buffer overrun has caused loss of data"
            },
            new EventDetail
            {
                EventCode = 1001, EventDescriptor = "IPv4 Address", Parameter = "IP addr as 32 bits",
                Description = "Per InDOT for filename"
            },
            new EventDetail
            {
                EventCode = 1003, EventDescriptor = "Timestamp", Parameter = "Date/Time",
                Description = "Per InDOT standard; Local and UTC available; UTC default"
            },
            new EventDetail
            {
                EventCode = 1006, EventDescriptor = "PerfLog Revision", Parameter = "Rev #",
                Description = "Omitted implies Rev 0 = InDOT 2012; Rev 1 = InDOT 2020"
            },

            //Phase Events
            new EventDetail
            {
                EventCode = 1010, EventDescriptor = "Coord Oversized Ped", Parameter = "Phase # (1-255)",
                Description =
                    "Forces a phase to run longer than its normal coord split. Logged when the split exceeds normal time."
            },

            // Detector Events
            new EventDetail
            {
                EventCode = 1020, EventDescriptor = "Special Detector Off", Parameter = "Spec. Det # (1-8)",
                Description =
                    "Internal detectors 65-72; detector off events triggered post any detector extension processing."
            },
            new EventDetail
            {
                EventCode = 1021, EventDescriptor = "Special Detector On", Parameter = "Spec. Det # (1-8)",
                Description =
                    "Internal detectors 65-72; detector on events triggered post any detector delay processing."
            },

            // Priority Events
            new EventDetail
            {
                EventCode = 1080, EventDescriptor = "TSP Check-In Vehicle", Parameter = "Vehicle # (1-9999)",
                Description = "Vehicle number is in TSP queue."
            },
            new EventDetail
            {
                EventCode = 1081, EventDescriptor = "TSP Check-In Detector", Parameter = "Detector # (1-8)",
                Description =
                    "Advance detectors triggered by priority vehicle. 1 = Preempt as priority, 2 = 1A, 3 = 2A, 4 = 3A, 5 = 4A, 6 = 5A, 7 = 6A, 8 = Backup/NTCIP 1211."
            },
            new EventDetail
            {
                EventCode = 1082, EventDescriptor = "TSP Service Level", Parameter = "Level # (0-2)",
                Description = "Priority service level: 0 = Minimal, 1 = Partial, 2 = Full."
            },
            new EventDetail
            {
                EventCode = 1083, EventDescriptor = "TSP Service Type", Parameter = "Type # (0-1)",
                Description = "Priority service type: 0 = Primary, 1 = Secondary."
            },
            new EventDetail
            {
                EventCode = 1088, EventDescriptor = "TSP Recovery Action", Parameter = "Action Code (0-15)",
                Description =
                    "Bit 0-1: Recovery Method: 0 = Normal, 1 = Wait, 2 = Recovery, 3 = reserved. Bit 2: Recovery Return: 0 = Cycle, 1 = Jump."
            },

            //Vehicle Detector Diagnostic Events
            new EventDetail
            {
                EventCode = 1110, EventDescriptor = "Faults Cleared", Parameter = "Veh Det # (1-64)",
                Description = "Detector no longer has a fault of any kind; logged with Event 83"
            },
            new EventDetail
            {
                EventCode = 1111, EventDescriptor = "Max Presence Fault", Parameter = "Veh Det # (1-64)",
                Description = "Detector On for longer than Max Presence time; logged with Event 84 to disambiguate"
            },
            new EventDetail
            {
                EventCode = 1112, EventDescriptor = "No Activity Fault", Parameter = "Veh Det # (1-64)",
                Description = "Detector Off for longer than No Activity time; logged with Event 84 to disambiguate"
            },
            new EventDetail
            {
                EventCode = 1113, EventDescriptor = "Erratic Count Fault", Parameter = "Veh Det # (1-64)",
                Description = "Detector counts exceed Erratic Count limit; logged with Event 84 to disambiguate"
            },
            new EventDetail
            {
                EventCode = 1114, EventDescriptor = "Detector Not Supported Fault", Parameter = "Veh Det # (1-64)",
                Description = "Detector assigned but no hardware to support it; logged with Event 84 to disambiguate"
            },
            new EventDetail
            {
                EventCode = 1115, EventDescriptor = "BIU Fault", Parameter = "Veh Det # (1-64)",
                Description = "Detector BIU has failed for this detector; logged with Event 84 to disambiguate"
            },
            new EventDetail
            {
                EventCode = 1116, EventDescriptor = "Watchdog Fault", Parameter = "Veh Det # (1-64)",
                Description = "Detector card has failed; logged with Event 85"
            },
            new EventDetail
            {
                EventCode = 1117, EventDescriptor = "Open Loop Fault", Parameter = "Veh Det # (1-64)",
                Description = "Detector loop is broken open; logged with Event 86"
            },
            new EventDetail
            {
                EventCode = 1118, EventDescriptor = "Shorted Loop Fault", Parameter = "Veh Det # (1-64)",
                Description = "Detector loop is shorted out; logged with Event 87"
            },
            new EventDetail
            {
                EventCode = 1119, EventDescriptor = "Excessive Change Fault", Parameter = "Veh Det # (1-64)",
                Description = "Detector loop is changing impedance/reluctance too much; logged with Event 88"
            },
            // Pedestrian Detector Diagnostic Events
            new EventDetail
            {
                EventCode = 1120, EventDescriptor = "Faults Cleared", Parameter = "Ped Det # (1-8)",
                Description = "Detector no longer has a fault of any kind; logged with Event 92"
            },
            new EventDetail
            {
                EventCode = 1121, EventDescriptor = "Max Presence Fault", Parameter = "Ped Det # (1-8)",
                Description = "Detector On for longer than Max Presence time; logged with Event 91 to disambiguate"
            },
            new EventDetail
            {
                EventCode = 1122, EventDescriptor = "No Activity Fault", Parameter = "Ped Det # (1-8)",
                Description = "Detector Off for longer than No Activity time; logged with Event 91 to disambiguate"
            },
            new EventDetail
            {
                EventCode = 1123, EventDescriptor = "Erratic Count Fault", Parameter = "Ped Det # (1-8)",
                Description = "Detector counts exceed Erratic Count limit; logged with Event 91 to disambiguate"
            },
            new EventDetail
            {
                EventCode = 1124, EventDescriptor = "Detector Not Supported Fault", Parameter = "Ped Det # (1-8)",
                Description = "Detector assigned but no hardware to support it; logged with Event 91 to disambiguate"
            },

// Overlap Events
            new EventDetail
            {
                EventCode = 1130, EventDescriptor = "Pre-Green Overlap", Parameter = "A-P (A=1, B=2, etc)",
                Description =
                    "Set when a TSP overlap is in the pre-green state (flashing red or flashing horizontal bar)."
            },

// Special Detector Diagnostic Events
            new EventDetail
            {
                EventCode = 1140, EventDescriptor = "Faults Cleared", Parameter = "Spec Det # (1-64)",
                Description = "Detector no longer has a fault of any kind"
            },
            new EventDetail
            {
                EventCode = 1141, EventDescriptor = "Max Presence Fault", Parameter = "Spec Det # (1-64)",
                Description = "Detector On for longer than Max Presence time"
            },
            new EventDetail
            {
                EventCode = 1142, EventDescriptor = "No Activity Fault", Parameter = "Spec Det # (1-64)",
                Description = "Detector Off for longer than No Activity time"
            },
            new EventDetail
            {
                EventCode = 1143, EventDescriptor = "Erratic Count Fault", Parameter = "Spec Det # (1-64)",
                Description = "Detector counts exceed Erratic Count limit"
            },
            new EventDetail
            {
                EventCode = 1144, EventDescriptor = "Detector Not Supported Fault", Parameter = "Spec Det # (1-64)",
                Description = "Detector assigned but no hardware to support it"
            },

// Load Switch Events (1201 to 1232)
            new EventDetail
            {
                EventCode = 1200, EventDescriptor = "All Dark", Parameter = "Always 0",
                Description =
                    "All outputs dark; if used, must immediately be followed by 1201-1232 unless truly all dark."
            },
            new EventDetail
            {
                EventCode = 1201, EventDescriptor = "Load Switch 1 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },

// Load Switch Events continued for 1202-1232
            new EventDetail
            {
                EventCode = 1202, EventDescriptor = "Load Switch 2 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1203, EventDescriptor = "Load Switch 3 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1204, EventDescriptor = "Load Switch 4 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1205, EventDescriptor = "Load Switch 5 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1206, EventDescriptor = "Load Switch 6 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1207, EventDescriptor = "Load Switch 7 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1208, EventDescriptor = "Load Switch 8 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1209, EventDescriptor = "Load Switch 9 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
                {
                EventCode = 1210, EventDescriptor = "Load Switch 10 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1211, EventDescriptor = "Load Switch 11 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1212, EventDescriptor = "Load Switch 12 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1213, EventDescriptor = "Load Switch 13 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1214, EventDescriptor = "Load Switch 14 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1215, EventDescriptor = "Load Switch 15 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1216, EventDescriptor = "Load Switch 16 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1217, EventDescriptor = "Load Switch 17 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1218, EventDescriptor = "Load Switch 18 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1219, EventDescriptor = "Load Switch 19 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1220, EventDescriptor = "Load Switch 20 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1221, EventDescriptor = "Load Switch 21 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1222, EventDescriptor = "Load Switch 22 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1223, EventDescriptor = "Load Switch 23 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1224, EventDescriptor = "Load Switch 24 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1225, EventDescriptor = "Load Switch 25 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1226, EventDescriptor = "Load Switch 26 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1227, EventDescriptor = "Load Switch 27 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1228, EventDescriptor = "Load Switch 28 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1229, EventDescriptor = "Load Switch 29 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1230, EventDescriptor = "Load Switch 30 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1231, EventDescriptor = "Load Switch 31 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            },
            new EventDetail
            {
                EventCode = 1232, EventDescriptor = "Load Switch 32 Output", Parameter = "Color # (0-7)",
                Description =
                    "Final color(s) being sent to the load switches, where: 0 = Dark; 1 = Green, 2 = Yellow, 3 = Green+Yellow, 4 = Red, 5 = Green+Red, 6 = Yellow+Red, 7 = All."
            }
        };

        bool fileDecoded = true;

        static void Main(string[] args)
        {
            string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "log.txt");

            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day) // Log to a file (optional)
                .CreateLogger();
            new Program().FindFiles();
            var offset = Properties.Settings.Default.PerfLogOffset;
            //wait 30 seconds here and coutndown
            for (int i = offset; i > 0; i--)
            {
                Console.WriteLine($"Waiting for {offset} seconds before saving events. " + i + " seconds remaining.");
                Thread.Sleep(1000);
            }
            Thread.Sleep(15000);
            new Program().SaveEvents();
        }

        private void FindFiles()
        {
            string CWD = Properties.Settings.Default.LogPath;
            string CSV = Properties.Settings.Default.CSVOutPAth;
            List<string> dirList = new List<string>();
            List<string> fileList = new List<string>();

            // MOEDataSetTableAdapters.QueriesTableAdapter MoeTA = new MOEDataSetTableAdapters.QueriesTableAdapter();



            foreach (string s in Directory.GetDirectories(CWD))
            {
                dirList.Add(s);
            }

            if (dirList.Count > 0)
            {
                foreach (string dir in dirList)

                    //DO NOT MAKE THIS A PARALLEL OPPERATION!
                    //Doing so results in the files beign put in the wrong directory, were they are read as being part of the 
                    //Wrong signal
                    //var options = new ParallelOptions { MaxDegreeOfParallelism = Convert.ToInt32(4) };
                    //Parallel.ForEach(dirList.AsEnumerable(), options , dir =>
                {

                    //foreach (string file in Directory.GetFiles(dir, "*.dat"))
                    //{
                    DecodeSiemens(dir);
                    if (Properties.Settings.Default.WriteToConsole)
                    {
                        Log.Information("Done decoding Directory {0}", dir);
                    }
                    //}


                    //foreach (string file in Directory.GetFiles(dir, "*.csv"))
                    //{
                    //    WritetoDB(dir, file, MoeTA);
                    //    Console.WriteLine("Done writing file {0} to database", file);
                    //}


                } //);


            }
        }

        // returns true if the filename indicates new data
        // standard Sepac filename format such as SIEM_50.73.234.81_2017_09_11_1700
        // and 60 minutes per file
        private bool NewDataFile(string file, DateTime lastrecord)
        {
            string[] filename = file.Split('_');
            var filetime = new DateTime(Int32.Parse(filename[2]),
                Int32.Parse(filename[3]),
                Int32.Parse(filename[4]),
                Int32.Parse(filename[5].Substring(0, 2)),
                59,
                59,
                999);
            var newer = (lastrecord < filetime);
            return (newer);
        }

        private int ExistingRecords(string signal, string file,
            MOE.Common.Models.Repositories.IControllerEventLogRepository celRepository)
        {
            string[] filename = file.Split('_');
            var start = new DateTime(Int32.Parse(filename[2]),
                Int32.Parse(filename[3]),
                Int32.Parse(filename[4]),
                Int32.Parse(filename[5].Substring(0, 2)),
                0,
                0,
                0);
            var end = start + new TimeSpan(0, 1, 59, 59, 999);
            return celRepository.GetRecordCount(signal, start, end);
        }

        private void DecodeSiemens(string dir)
        {

            //path to the decoder program
            string decoder = Properties.Settings.Default.DecoderPath;
            //time in MS to wait for the decoder ot fail
            int timeOut = Properties.Settings.Default.TimeOut;


            try
            {
                //Set the current directory.  One of the quirks of the decoder is that
                //it requires the target file to be in the current workig directory
                Directory.SetCurrentDirectory(dir);
            }
            catch (DirectoryNotFoundException ex)
            {
                if (Properties.Settings.Default.WriteToConsole)
                {
                    Log.Error("The specified directory does not exist. {0}", ex);
                }
            }

            try
            {
                //string csvfile = file.Replace(".dat", ".csv");
                //string arguments = file + " " + csvfile;
                string arguments = "-i *.dat";
                Process p = Process.Start(decoder, arguments);

                //Wait for window to finish loading.

                //Wait for the process to exit or time out.
                p.WaitForExit(timeOut);
                //Check to see if the process is still running.
                if (p.HasExited == false)
                {
                    //Process is still running.
                    //Test to see if the process is hung up.
                    if (p.Responding)
                    {
                        //Process was responding; close the main window.
                        p.CloseMainWindow();
                    }
                    else
                    {
                        //Process was not responding; force the process to close.
                        p.Kill();
                    }
                }

                p.Dispose();
            }
            catch (Exception ex)
            {
                if (Properties.Settings.Default.WriteToConsole)
                {
                    Log.Error("Exception {0} while decoding directory {1}", ex, dir);
                }

                fileDecoded = false;
            }

            //If the Delete flag is checking in settings, and the file has been decoded, then delte the file.
            if (Properties.Settings.Default.DeleteFiles && fileDecoded)
            {
                DeleteFiles(dir);
            }
        }

        private void DeleteFiles(string dir)
        {
            foreach (string file in Directory.GetFiles(dir, "*.dat"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    if (Properties.Settings.Default.WriteToConsole)
                    {
                        Log.Error("Exception {0} while deleting file {1}", ex, file);
                    }

                }
            }
        }

        private void WriteToConsole(string msg)
        {
            if (!Properties.Settings.Default.WriteToConsole)
                return;
            Log.Information("{0}: {1}", DateTime.Now, msg);
        }
        //subroutine to write the decoded log to the database.
        //this is where most of the work is done.

        //The only way we match signalid to the collected logs is by the directory name.
        //static void WritetoDB(string dir, string file, MOEDataSetTableAdapters.QueriesTableAdapter MoeTA)
        private void SaveEvents()
        {
            int insertErrorCount = 0;
            int insertedLinecount = 0;
            double errorRatio = 0;
            DateTime startTime = new DateTime();
            DateTime endTime = new DateTime();
            TimeSpan elapsedTime = new TimeSpan();
            string CWD = Properties.Settings.Default.LogPath;
            List<string> dirList = new List<string>();
            List<string> fileList = new List<string>();
            ConcurrentQueue<string> FilesToDelete = new ConcurrentQueue<string>();
            var lastrecords = new Dictionary<string, DateTime>();
            var countrecords = new Dictionary<string, int>();
            MOE.Common.Models.Repositories.IControllerEventLogRepository celRepository =
                MOE.Common.Models.Repositories.ControllerEventLogRepositoryFactory.Create();

            foreach (string s in Directory.GetDirectories(CWD))
            {
                dirList.Add(s);
                var signalID = s.Split(new char[] { '\\' }).Last();
                lastrecords.Add(signalID, celRepository.GetMostRecentRecordTimestamp(signalID));
                foreach (var file in Directory.GetFiles(s))
                {
                    countrecords.Add(file, ExistingRecords(signalID, file, celRepository));
                }
            }

            var options = new ParallelOptions
                { MaxDegreeOfParallelism = Convert.ToInt32(Properties.Settings.Default.MaxThreads) };
            Parallel.ForEach(dirList.AsEnumerable(), options, dir =>
                    //foreach (var dir in dirList.AsEnumerable())
                {
                    //get the name of the directory and casting it to an int
                    //This is the only way the program knows the signal number of the controller.
                    string[] strsplit = dir.Split(new char[] { '\\' });
                    string dirname = strsplit.Last();
                    string sigid = dirname;
                    var localZone = TimeZone.CurrentTimeZone;
                    DateTime currentDate = DateTime.Now;
                    const string dataFmt = "{0,-30}{1}";
                    bool isDayligtSavings = localZone.IsDaylightSavingTime(currentDate);
                    bool isArizona = TimeZoneInfo.Local.Id == "US Mountain Standard Time" || TimeZoneInfo.Local.DisplayName.Contains("Arizona");
                    Console.WriteLine(dataFmt, "Daylight saving time?", isDayligtSavings);

                    //var dstOffset = Math.Abs(DateTimeOffset.Now.Offset.Hours);
                    WriteToConsole("Starting signal " + dirname);
                    // Get Eastern Time Zone
                    //TimeZoneInfo easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                    //DateTime currentDate = DateTime.Now;
                    //const string dataFmt = "{0,-30}{1}";

                    //// Check if Eastern timezone is in Daylight Saving Time
                    //Console.WriteLine(dataFmt, "Daylight saving time?", easternZone.IsDaylightSavingTime(currentDate));

                    //// Convert current time to Eastern time
                    //DateTime easternTime = TimeZoneInfo.ConvertTime(currentDate, easternZone);
                    //Console.WriteLine(dataFmt, "Eastern time:", easternTime.ToString());

                    WriteToConsole("Starting signal " + dirname);
                    var options1 = new ParallelOptions
                        { MaxDegreeOfParallelism = Convert.ToInt32(Properties.Settings.Default.MaxThreads) };
                    //Parallel.ForEach(Directory.GetFiles(dir, "*.csv").OrderBy(f => f), options1, file =>
                    foreach (var file in Directory.GetFiles(dir, "*.csv").OrderBy(f => f))
                    {
                        if (countrecords[file] >= File.ReadAllLines(file).Length - 1)
                        {
                            var delete = Properties.Settings.Default.DeleteFiles;
                            WriteToConsole(String.Format("Skipping {0} {1}, we already imported this.",
                                (delete ? "and deleting" : ""), file));
                            if (delete)
                            {
                                try
                                {
                                    File.Delete(file);
                                }
                                catch (Exception e)
                                {
                                    WriteToConsole(String.Format("Unable to delete {0}: {1}", file, e.Message));
                                }
                            }

                            continue;
                            //return;
                        }

                        int skippedrecords = 0;
                        DataTable elTable = new DataTable();
                        elTable.Columns.Add("sigid", System.Type.GetType("System.String"));
                        elTable.Columns.Add("timeStamp", System.Type.GetType("System.DateTime"));
                        elTable.Columns.Add("eventCode", System.Type.GetType("System.Int32"));
                        elTable.Columns.Add("eventParam", System.Type.GetType("System.Int32"));
                        UniqueConstraint custUnique = new UniqueConstraint(new DataColumn[]
                        {
                            elTable.Columns[0],
                            elTable.Columns[1],
                            elTable.Columns[2],
                            elTable.Columns[3]
                        });
                        elTable.Constraints.Add(custUnique);
                        startTime = DateTime.Now;
                        //Siemens decoder makes the first line the IP address, so skip it.
                        foreach (string line in File.ReadAllLines(file).Skip(1))
                        {
                            //Every other line is blank.  We only care about the lines that have data, and 
                            //every data line has a comma
                            if (line.Contains(','))
                            {
                                //split the line on commas and assign each split to a var
                                string[] lineSplit = line.Split(new char[] { ',' });
                                DateTime timeStamp = new DateTime();
                                int eventCode = 0;
                                int eventParam = 0;
                                //it might happen that the character on the line are not quite right.
                                //the Try/catch stuff is an attempt to deal with that.
                                try
                                {
                                    timeStamp = Convert.ToDateTime(lineSplit[0]);
                                    //Siemens decoder is converting to local time from UTC, so convert back to UTC time
                                    //Not perfect during DST transitions (at 2:00 AM twice per year)
                                    //timeStamp = timeStamp + TimeSpan.FromHours(dstOffset);
                                    if (isDayligtSavings || isArizona)
                                    {
                                        timeStamp = timeStamp.AddHours(-1);
                                    }
                                    else
                                    {
                                        timeStamp = timeStamp.AddHours(-2);
                                    }
                                    if (timeStamp < lastrecords[sigid])
                                    {
                                        skippedrecords++;
                                        continue;
                                    }

                                    //if the event code is in the YunexUniqueEventCodes, then we need to log it and skip it 
                                    //because it is not a valid event code.
                                    var uniqueCode = YunexUniqueEventCodes.Find(x =>
                                        x.EventCode == Convert.ToInt32(lineSplit[1]));
                                    if (uniqueCode != null)
                                    {
                                        Log.Information("Found a Yunex Unique Event Code: " + lineSplit[1] + " Description: " + uniqueCode.ToString() );
                                        continue;
                                    }
                                    
                                    eventCode = Convert.ToInt32(lineSplit[1]);
                                    eventParam = Convert.ToInt32(lineSplit[2]);
                                }
                                catch (Exception ex)
                                {
                                    WriteToConsole(String.Format("{0} while converting {1} to event.  Skipping line",
                                        ex, lineSplit[0]));
                                    continue;
                                }

                                try
                                {
                                    elTable.Rows.Add(sigid, timeStamp, eventCode, eventParam);
                                }
                                catch (Exception ex)
                                {
                                    WriteToConsole(String.Format("{0} while adding event to data table",
                                        ex.ToString()));
                                }

                            }
                        }

                        WriteToConsole(String.Format("{0} has been parsed. Skipped {1} old records", file,
                            skippedrecords));

                        //Do the Math to find out if the error ratio is intolerably high before deleting the file
                        if (insertErrorCount > 0)
                        {
                            errorRatio = Convert.ToDouble(insertErrorCount) /
                                         Convert.ToDouble((insertedLinecount + insertErrorCount));
                        }
                        else
                        {
                            errorRatio = 0;
                        }
                        //get the connection string here from the appsettings

                        string connectionString = ConfigurationManager.ConnectionStrings["SPM"].ConnectionString;
                        MOE.Common.Business.BulkCopyOptions bulkOptions = new MOE.Common.Business.BulkCopyOptions(
                            connectionString, Properties.Settings.Default.DestinationTableName,
                            Properties.Settings.Default.WriteToConsole, Properties.Settings.Default.forceNonParallel,
                            Properties.Settings.Default.MaxThreads, Properties.Settings.Default.DeleteFile,
                            Properties.Settings.Default.EarliestAcceptableDate,
                            Properties.Settings.Default.BulkCopyBatchSize, Properties.Settings.Default.BulkCopyTimeOut);

                        endTime = DateTime.Now;

                        //the Signal class has a static methods to insert the table into the DB.  We are using that.
                        MOE.Common.Business.SignalFtp.BulktoDb(elTable, bulkOptions,
                            Properties.Settings.Default.DestinationTableName);
                        elapsedTime = endTime - startTime;

                        if (Properties.Settings.Default.DeleteFiles)
                        {
                            try
                            {
                                File.Delete(file);
                                WriteToConsole(String.Format("{0} Deleted", file));
                            }
                            catch (SystemException sysex)
                            {
                                WriteToConsole(String.Format(
                                    "{0} while Deleting {1}, waiting 100 ms before trying again", sysex, file));
                                Thread.Sleep(100);
                                try
                                {
                                    File.Delete(file);
                                }
                                catch (SystemException sysex2)
                                {
                                    WriteToConsole(String.Format("{0} while Deleting {1}, giving up", sysex2, file));
                                }
                            }

                            catch (Exception ex)
                            {
                                WriteToConsole(String.Format("{0} while deleting file {1}", ex, file));
                                FilesToDelete.Enqueue(file);
                            }
                        }
                    }
                    //);  
                }
            );
        }
    }
}