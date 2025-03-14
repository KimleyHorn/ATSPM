﻿/* 
* https://stackoverflow.com/questions/22492383/throttling-asynchronous-tasks 
* 
 * adapted by ajt 6/4/2018 
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MOE.Common;
using System.Data;
using System.Configuration;
using MOE.Common.Models.Repositories;

namespace AsyncGetMaxTimeRecords
{
    // process data 
    public class Processing
    {
        static IApplicationEventRepository errorRepository = ApplicationEventRepositoryFactory.Create();

        static int MAX_PROCESSORS = Properties.Settings.Default.MaxProcessors;

        SemaphoreSlim _semaphore = new SemaphoreSlim(MAX_PROCESSORS);
        HashSet<Task> _pending = new HashSet<Task>();
        object _lock = new Object();

        
        // This is where the magic happens 
        //: void doStuff(String url, String data)
        void DoStuff(MOE.Common.Models.Signal signal, XmlDocument xml)
        {
            SaveToDB(xml, signal.SignalID);
        }

        private static void SaveToDB(XmlDocument xml, string SignalId)
        {
            System.Collections.Specialized.NameValueCollection appSettings = ConfigurationManager.AppSettings;
            string destTable = appSettings["DestinationTableName"];

            MOE.Common.Data.MOE.Controller_Event_LogDataTable elTable =
                new MOE.Common.Data.MOE.Controller_Event_LogDataTable();
            UniqueConstraint custUnique =
                new UniqueConstraint(new DataColumn[]
                {
                    elTable.Columns["SignalID"],
                    elTable.Columns["Timestamp"],
                    elTable.Columns["EventCode"],
                    elTable.Columns["EventParam"]
                });

            elTable.Constraints.Add(custUnique);

            XmlNodeList list = xml.SelectNodes("/EventResponses/EventResponse/Event");

            foreach (XmlNode node in list)
            {
                XmlAttributeCollection attrColl = node.Attributes;

                DateTime EventTime = new DateTime();
                int EventCode = 0;
                int EventParam = 0;
                DateTime.TryParse(attrColl.GetNamedItem("TimeStamp").Value, out EventTime);
                int.TryParse(attrColl.GetNamedItem("EventTypeID").Value, out EventCode);
                int.TryParse(attrColl.GetNamedItem("Parameter").Value, out EventParam);

                try
                {
                    MOE.Common.Data.MOE.Controller_Event_LogRow eventrow = elTable.NewController_Event_LogRow();


                    eventrow.Timestamp = EventTime;
                    eventrow.SignalID = SignalId;
                    eventrow.EventCode = EventCode;
                    eventrow.EventParam = EventParam;
                    if (eventrow.Timestamp > Properties.Settings.Default.EarliestAcceptableDate)
                    {
                        elTable.AddController_Event_LogRow(eventrow);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error in SaveToDB " + ex.Message);
                    //errorRepository.QuickAdd("AsyncGetMaxTimeRecordsProcessing", "Main", "SaveToDB",
                    //    MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                    //    "Error in SaveToDB");
                }
            }

            MOE.Common.Business.BulkCopyOptions Options = new MOE.Common.Business.BulkCopyOptions(
                ConfigurationManager.ConnectionStrings["SPM"].ConnectionString,
                Properties.Settings.Default.DestinationTableName,
                Properties.Settings.Default.WriteToConsole, Properties.Settings.Default.forceNonParallel,
                Properties.Settings.Default.MaxThreads, false,
                Properties.Settings.Default.EarliestAcceptableDate, Properties.Settings.Default.BulkCopyBatchSize,
                Properties.Settings.Default.BulkCopyTimeOut);

            MOE.Common.Business.SignalFtp.BulktoDb(elTable, Options, destTable);
        }




        async Task ProcessAsync(MOE.Common.Models.Signal signal, XmlDocument xml)
        {
            await _semaphore.WaitAsync();
            try
            {
                await Task.Run(() => { DoStuff(signal, xml); });
            }
            finally
            {
                _semaphore.Release();
            }
        }


        public async void QueueItemAsync(MOE.Common.Models.Signal signal, XmlDocument xml)
        {

            var task = ProcessAsync(signal, xml);
            lock (_lock)
                _pending.Add(task);
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                errorRepository.QuickAdd(
                    "AsyncGetMaxTimeRecordsProcessing",
                    "Main",
                    "QueueItemAsync",
                    MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                    $"Error in QueueItemAsync: {ex.Message}");
                if (!task.IsCanceled && !task.IsFaulted)
                    throw; // not the task's exception, rethrow 
                // don't remove faulted/cancelled tasks from the list 
                return;
            }

            // remove successfully completed tasks from the list 
            lock (_lock)
                _pending.Remove(task);
        }

        public async Task WaitForCompleteAsync()
        {
            Task[] tasks;
            lock (_lock)
                tasks = _pending.ToArray();
            await Task.WhenAll(tasks);
        }
    }

    class AsyncGetMaxTimeRecords
    {
        static IApplicationEventRepository errorRepository = ApplicationEventRepositoryFactory.Create();

        static async Task DownloadAsync(List<MOE.Common.Models.Signal> signalsDT)
        {
            int MAX_DOWNLOADS = Properties.Settings.Default.MaxDownloads;

            var processing = new Processing();

            using (var semaphore = new SemaphoreSlim(MAX_DOWNLOADS))
            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                var tasks = signalsDT.Select(async (signal) =>
                {
                    var url = "";
                    var mostRecentRecord = GetMostRecentRecordTime(signal.SignalID);
                    switch (signal.ControllerTypeID)
                    {
                        case 4 when mostRecentRecord == null:
                            url = $"http://{signal.IPAddress}/v1/asclog/xml/full";
                            break;
                        case 4:
                        {
                            var since = GetMostRecentRecordTime(signal.SignalID)?.ToString("MM-dd-yyyy HH:mm:ss.f");
                            url = $"http://{signal.IPAddress}/v1/asclog/xml/full?since={since}";
                            break;
                        }
                        case 10 when mostRecentRecord == null:
                            url = $"http://{signal.IPAddress}/maxtime-rampmeter/v1/rmclog/xml/full";
                            break;
                        case 10:
                        {
                            var since = GetMostRecentRecordTime(signal.SignalID)?.ToString("MM-dd-yyyy HH:mm:ss.f");
                            url = $"http://{signal.IPAddress}/maxtime-rampmeter/v1/rmclog/xml/full?since={since}";
                            break;
                        }
                    }

                    await semaphore.WaitAsync();
                    try
                    {
                        var data = await httpClient.GetStringAsync(url);
                        var xml = new XmlDocument();
                        xml.LoadXml(data);

                        var list = xml.SelectNodes("/EventResponses/EventResponse/Event");

                        // put the result on the processing pipeline 
                        processing.QueueItemAsync(signal, xml);
                    }
                    catch (Exception exception)
                    {
                        //: Console.WriteLine($"{url}.-1"); // output -1 records to indicate an error with the url 
                        Console.WriteLine($"{signal.SignalID},{signal.IPAddress},-1");
                        //errorRepository.QuickAdd("AsyncGetMaxTimeRecords", "Main", "DownloadAsync",
                        //    MOE.Common.Models.ApplicationEvent.SeverityLevels.Medium,
                        //    $"Error in DownloadAsync {signal.SignalID},{signal.IPAddress},-1: {exception.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks.ToArray());
                await processing.WaitForCompleteAsync();
            }
        }

        private static DateTime? GetMostRecentRecordTime(string signalId)
        {
            var CELRepo = MOE.Common.Models.Repositories.ControllerEventLogRepositoryFactory.Create();
            DateTime mostRecentEventTime = CELRepo.GetMostRecentRecordTimestamp(signalId);

            if (mostRecentEventTime == null || mostRecentEventTime == DateTime.MinValue)
            {
                return null;
            }
            return (mostRecentEventTime);
        }

        public static async Task MainAsync(string[] args)
        {
            List<MOE.Common.Models.Signal> signalsDT;
            var signals = MOE.Common.Models.Repositories.SignalsRepositoryFactory.Create();


            signalsDT = (from s in signals.GetLatestVersionOfAllSignalsAsQueryable()
                         where s.ControllerTypeID == 4 || s.ControllerTypeID == 10
                         select s).ToList();

            await DownloadAsync(signalsDT);
        }

        public static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }
    }
}