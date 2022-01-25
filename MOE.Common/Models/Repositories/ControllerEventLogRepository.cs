using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity.Core;
using System.Data.Entity.Core.Objects;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Management;
using Microsoft.EntityFrameworkCore.Internal;
using Parquet;

namespace MOE.Common.Models.Repositories
{
    public class ParquetEventLog
    {
        public string SignalID { get; set; }
        public string Date { get; set; }
        public double TimestampMs { get; set; }
        public int EventCode { get; set; }
        public int EventParam { get; set; }

        public ParquetEventLog()
        { }
    }

    public class ControllerEventLogRepository : IControllerEventLogRepository
    {
        private readonly SPM _db = new SPM();
        private const string LocalArchiveDirectory = "LocalArchiveDirectory";
        private readonly string _localPath = GetSetting(LocalArchiveDirectory);

        public ControllerEventLogRepository()
        {
            _db.Database.CommandTimeout = 60;
            _db.Database.ExecuteSqlCommand("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;");
        }

        public int GetRecordCountByParameterAndEvent(string signalId, DateTime startTime, DateTime endTime,
            List<int> eventParameters,
            List<int> eventCodes)
        {
            var query = _db.Controller_Event_Log.Where(c =>
                c.SignalID == signalId && c.Timestamp >= startTime && c.Timestamp <= endTime);
            if (eventParameters != null && eventParameters.Count > 0)
                query = query.Where(c => eventParameters.Contains(c.EventParam));
            if (eventCodes != null && eventCodes.Count > 0)
                query = query.Where(c => eventCodes.Contains(c.EventCode));
            if (query.Any())
                return query.Count();

            //Check the archive if no data in DB
            var archivedData = GetDataFromArchive(signalId, startTime, endTime);

            if (eventParameters != null && eventParameters.Count > 0)
                archivedData = archivedData.Where(c => eventParameters.Contains(c.EventParam)).ToList();
            if (eventCodes != null && eventCodes.Count > 0)
                archivedData = archivedData.Where(c => eventCodes.Contains(c.EventCode)).ToList();

            return archivedData.Count;
        }

        public List<Controller_Event_Log> GetRecordsByParameterAndEvent(string signalId, DateTime startTime,
            DateTime endTime, List<int> eventParameters, List<int> eventCodes)
        {
            var query = _db.Controller_Event_Log.Where(c =>
                c.SignalID == signalId && c.Timestamp >= startTime && c.Timestamp <= endTime);
            if (eventParameters != null && eventParameters.Count > 0)
                query = query.Where(c => eventParameters.Contains(c.EventParam));
            if (eventCodes != null && eventCodes.Count > 0)
                query = query.Where(c => eventCodes.Contains(c.EventCode));

            if (query.Any())
                return query.ToList();

            var archivedData = GetDataFromArchive(signalId, startTime, endTime);

            if (eventParameters != null && eventParameters.Count > 0)
                archivedData = archivedData.Where(c => eventParameters.Contains(c.EventParam)).ToList();
            if (eventCodes != null && eventCodes.Count > 0)
                archivedData = archivedData.Where(c => eventCodes.Contains(c.EventCode)).ToList();

            return archivedData.ToList();
        }

        public List<Controller_Event_Log> GetAllAggregationCodes(string signalId, DateTime startTime, DateTime endTime)
        {
            var codes = new List<int> { 150, 114, 113, 112, 105, 102, 1 };
            var records = _db.Controller_Event_Log
                .Where(c => c.SignalID == signalId && c.Timestamp >= startTime && c.Timestamp <= endTime &&
                            codes.Contains(c.EventCode))
                .ToList();

            if (records.Any()) return records;

            var archivedData = GetDataFromArchive(signalId, startTime, endTime);
            records = archivedData.Where(x => codes.Contains(x.EventCode)).ToList();

            return records;
        }

        public int GetDetectorActivationCount(string signalId,
            DateTime startTime, DateTime endTime, int detectorChannel)
        {
            var count = (from cel in _db.Controller_Event_Log
                         where cel.Timestamp >= startTime
                               && cel.Timestamp < endTime
                               && cel.SignalID == signalId
                               && cel.EventParam == detectorChannel
                               && cel.EventCode == 82
                         select cel).Count();

            if (count > 0) return count;

            var archivedData = GetDataFromArchive(signalId, startTime, endTime);
            count = archivedData.Count(x => x.EventParam == detectorChannel && x.EventCode == 82);
            return count;
        }

        public double GetTmcVolume(DateTime startDate, DateTime endDate, string signalId, int phase)
        {
            var repository =
                SignalsRepositoryFactory.Create();
            var signal = repository.GetVersionOfSignalByDate(signalId, startDate);
            var graphDetectors = signal.GetDetectorsForSignalByPhaseNumber(phase);

            var tmcChannels = new List<int>();
            foreach (var gd in graphDetectors)
                foreach (var dt in gd.DetectionTypes)
                    if (dt.DetectionTypeID == 4)
                        tmcChannels.Add(gd.DetChannel);


            double count = (from cel in _db.Controller_Event_Log
                            where cel.Timestamp >= startDate
                                  && cel.Timestamp < endDate
                                  && cel.SignalID == signalId
                                  && tmcChannels.Contains(cel.EventParam)
                                  && cel.EventCode == 82
                            select cel).Count();

            if (!(count <= 0)) return count;

            var archivedData = GetDataFromArchive(signalId, startDate, endDate);
            count = archivedData.Count(x => x.EventCode == 82 && tmcChannels.Contains(x.EventParam));

            return count;
        }

        public List<Controller_Event_Log> GetSplitEvents(string signalId, DateTime startTime, DateTime endTime)
        {
            var results = (from r in _db.Controller_Event_Log
                           where r.SignalID == signalId && r.Timestamp > startTime && r.Timestamp < endTime
                                 && r.EventCode > 130 && r.EventCode < 150
                           select r).ToList();

            if (results.Any()) return results;

            var archivedData = GetDataFromArchive(signalId, startTime, endTime);
            results = archivedData.Where(x => x.EventCode > 130 && x.EventCode < 150).ToList();

            return results;
        }

        public List<Controller_Event_Log> GetSignalEventsBetweenDates(string signalId,
            DateTime startTime, DateTime endTime)
        {
            try
            {
                var events = (from r in _db.Controller_Event_Log
                        where r.SignalID == signalId
                              && r.Timestamp >= startTime
                              && r.Timestamp < endTime
                        select r).ToList();
                if (events.Any()) return events;

                return GetDataFromArchive(signalId, startTime, endTime);

            }
            catch (Exception ex)
            {
                var logRepository =
                    ApplicationEventRepositoryFactory.Create();
                var e = new ApplicationEvent();
                e.ApplicationName = "MOE.Common";
                e.Class = GetType().ToString();
                e.Function = "GetSignalEventsBetweenDates";
                e.SeverityLevel = ApplicationEvent.SeverityLevels.High;
                e.Timestamp = DateTime.Now;
                e.Description = signalId + " " + startTime.Date.ToShortDateString() + " - " + ex.Message;
                logRepository.Add(e);
                return new List<Controller_Event_Log>();
                //throw;
            }
        }

        public List<Controller_Event_Log> GetTopNumberOfSignalEventsBetweenDates(string signalId, int numberOfRecords,
            DateTime startTime, DateTime endTime)
        {
            try
            {
                var events =
                (from r in _db.Controller_Event_Log
                 where r.SignalID == signalId
                       && r.Timestamp >= startTime
                       && r.Timestamp < endTime
                 select r).Take(numberOfRecords).ToList();

                if (events.Any())
                    return events;

                var archivedData = GetDataFromArchive(signalId, startTime, endTime);
                events = archivedData.Take(numberOfRecords).ToList();

                return events;
            }
            catch (Exception ex)
            {
                var logRepository =
                    ApplicationEventRepositoryFactory.Create();
                var e = new ApplicationEvent();
                e.ApplicationName = "MOE.Common";
                e.Class = GetType().ToString();
                e.Function = "GetTopNumberOfSignalEventsBetweenDates";
                e.SeverityLevel = ApplicationEvent.SeverityLevels.High;
                e.Timestamp = DateTime.Now;
                e.Description = ex.Message;
                logRepository.Add(e);
                throw;
            }
        }

        public int GetRecordCount(string signalId, DateTime startTime, DateTime endTime)
        {
            try
            {
                var count =  _db.Controller_Event_Log.Count(r => r.SignalID == signalId
                                                           && r.Timestamp >= startTime
                                                           && r.Timestamp < endTime);

                if (count > 0)
                    return count;

                var archivedData = GetDataFromArchive(signalId, startTime, endTime);
                return archivedData.Count;
            }
            catch (Exception ex)
            {
                var logRepository =
                    ApplicationEventRepositoryFactory.Create();
                var e = new ApplicationEvent();
                e.ApplicationName = "MOE.Common";
                e.Class = GetType().ToString();
                e.Function = "GetRecordCount";
                e.SeverityLevel = ApplicationEvent.SeverityLevels.High;
                e.Timestamp = DateTime.Now;
                if (ex.InnerException != null)
                    e.Description = signalId + " - " + ex.Message + ex.InnerException.Message.Substring(0, 50);
                logRepository.Add(e);
                throw ex;
            }
        }

        public bool CheckForRecords(string signalId, DateTime startTime, DateTime endTime)
        {
            try
            {
                return _db.Controller_Event_Log.Any(r => r.SignalID == signalId
                                                         && r.Timestamp >= startTime
                                                         && r.Timestamp < endTime);

                //todo: ? mjw
            }
            catch (Exception ex)
            {
                var logRepository =
                    ApplicationEventRepositoryFactory.Create();
                var e = new ApplicationEvent();
                e.ApplicationName = "MOE.Common";
                e.Class = GetType().ToString();
                e.Function = "CheckForRecords";
                e.SeverityLevel = ApplicationEvent.SeverityLevels.High;
                e.Timestamp = DateTime.Now;
                e.Description = ex.Message;
                logRepository.Add(e);
                throw;
            }
        }


        public List<Controller_Event_Log> GetSignalEventsByEventCode(string signalId,
            DateTime startTime, DateTime endTime, int eventCode)
        {
            try
            {
                var events = (from r in _db.Controller_Event_Log
                        where r.SignalID == signalId
                              && r.Timestamp >= startTime
                              && r.Timestamp < endTime
                              && r.EventCode == eventCode
                        select r).ToList();

                if (!events.Any())
                {
                    var logs = GetDataFromArchive(signalId, startTime, endTime);
                    events = (from s in logs
                              where s.SignalID == signalId &&
                                    s.Timestamp >= startTime &&
                                    s.Timestamp < endTime &&
                                    s.EventCode == eventCode
                              select s).ToList();
                }

                return events;
            }
            catch (Exception ex)
            {
                var logRepository =
                    ApplicationEventRepositoryFactory.Create();
                var e = new ApplicationEvent();
                e.ApplicationName = "MOE.Common";
                e.Class = GetType().ToString();
                e.Function = "GetSignalEventsByEventCode";
                e.SeverityLevel = ApplicationEvent.SeverityLevels.High;
                e.Timestamp = DateTime.Now;
                e.Description = ex.Message;
                logRepository.Add(e);
                throw;
            }
        }

        public List<Controller_Event_Log> GetSignalEventsByEventCodes(string signalId,
            DateTime startTime, DateTime endTime, List<int> eventCodes)
        {
            try
            {
                var events = (from s in _db.Controller_Event_Log
                              where s.SignalID == signalId &&
                                    s.Timestamp >= startTime &&
                                    s.Timestamp <= endTime &&
                                    eventCodes.Contains(s.EventCode)
                              select s).ToList();

                if (!events.Any())
                {

                    var logs = GetDataFromArchive(signalId, startTime, endTime);
                    events = (from s in logs
                              where s.SignalID == signalId &&
                                    s.Timestamp >= startTime &&
                                    s.Timestamp <= endTime &&
                                    eventCodes.Contains(s.EventCode)
                              select s).ToList();
                }

                events.Sort((x, y) => DateTime.Compare(x.Timestamp, y.Timestamp));
                return events;
            }
            catch (Exception ex)
            {
                var logRepository =
                    ApplicationEventRepositoryFactory.Create();
                var e = new ApplicationEvent();
                e.ApplicationName = "MOE.Common";
                e.Class = GetType().ToString();
                e.Function = "GetSignalEventsByEventCodes";
                e.SeverityLevel = ApplicationEvent.SeverityLevels.High;
                e.Timestamp = DateTime.Now;
                e.Description = signalId + " - " + ex.Message;
                logRepository.Add(e);
                throw ex;
            }
        }

        public List<Controller_Event_Log> GetEventsByEventCodesParam(string signalId, DateTime startTime,
            DateTime endTime, List<int> eventCodes, int param)
        {
            try
            {
                var events = _db.Controller_Event_Log.Where(s => s.SignalID == signalId &&
                                   s.Timestamp >= startTime &&
                                   s.Timestamp <= endTime &&
                                   s.EventParam == param &&
                                   eventCodes.Contains(s.EventCode)).ToList();

                if (!events.Any())
                {
                    var logs = GetDataFromArchive(signalId, startTime, endTime);
                    events = logs.Where(x => x.EventParam == param && eventCodes.Contains(x.EventCode)).ToList();
                }

                events = events.OrderBy(e => e.Timestamp).ThenBy(e => e.EventParam).ToList();
                return events;
            }
            catch (Exception ex)
            {
                var logRepository =
                    ApplicationEventRepositoryFactory.Create();
                var e = new ApplicationEvent();
                e.ApplicationName = "MOE.Common";
                e.Class = GetType().ToString();
                e.Function = "GetEventsByEventCodesParam";
                e.SeverityLevel = ApplicationEvent.SeverityLevels.High;
                e.Timestamp = DateTime.Now;
                e.Description = ex.Message;
                logRepository.Add(e);
                throw;
            }
        }

        public List<Controller_Event_Log> GetTopEventsAfterDateByEventCodesParam(string signalId,
            DateTime timestamp, List<int> eventCodes, int param, int top)
        {
            try
            {
                var endDate = timestamp.AddHours(1);
                var events = _db.Controller_Event_Log.Where(c =>
                        c.SignalID == signalId &&
                        c.Timestamp > timestamp &&
                        c.Timestamp < endDate &&
                        c.EventParam == param &&
                        eventCodes.Contains(c.EventCode))
                    .OrderBy(s => s.Timestamp)
                    .Take(top).ToList();

                if (events.Any())
                    return events;

                var archivedData = GetDataFromArchive(signalId, timestamp, endDate);
                events = archivedData.Where(x => x.EventParam == param && eventCodes.Contains(x.EventCode))
                    .OrderBy(x => x.Timestamp).Take(top).ToList();

                return events;
            }
            catch (Exception e)
            {
                var errorLog = ApplicationEventRepositoryFactory.Create();
                errorLog.QuickAdd(Assembly.GetExecutingAssembly().FullName,
                    GetType().DisplayName(), e.TargetSite.ToString(), ApplicationEvent.SeverityLevels.Low, e.Message);
                return null;
            }
        }


        public int GetEventCountByEventCodesParamDateTimeRange(string signalId,
            DateTime startTime, DateTime endTime, int startHour, int startMinute, int endHour, int endMinute,
            List<int> eventCodes, int param)
        {
            try
            {
                var count =
                (from s in _db.Controller_Event_Log
                 where s.SignalID == signalId &&
                       s.Timestamp >= startTime &&
                       s.Timestamp <= endTime &&
                       (s.Timestamp.Hour > startHour && s.Timestamp.Hour < endHour ||
                        s.Timestamp.Hour == startHour && s.Timestamp.Hour == endHour &&
                        s.Timestamp.Minute >= startMinute && s.Timestamp.Minute <= endMinute ||
                        s.Timestamp.Hour == startHour && s.Timestamp.Hour < endHour &&
                        s.Timestamp.Minute >= startMinute ||
                        s.Timestamp.Hour < startHour && s.Timestamp.Hour == endHour &&
                        s.Timestamp.Minute <= endMinute)
                       &&
                       s.EventParam == param &&
                       eventCodes.Contains(s.EventCode)
                 select s).Count();

                if (count <= 0)
                {
                    var archivedData = GetDataFromArchive(signalId, startTime, endTime);
                    count = (from s in archivedData
                             where s.SignalID == signalId &&
                                   s.Timestamp >= startTime &&
                                   s.Timestamp <= endTime &&
                                   (s.Timestamp.Hour > startHour && s.Timestamp.Hour < endHour ||
                                    s.Timestamp.Hour == startHour && s.Timestamp.Hour == endHour &&
                                    s.Timestamp.Minute >= startMinute && s.Timestamp.Minute <= endMinute ||
                                    s.Timestamp.Hour == startHour && s.Timestamp.Hour < endHour &&
                                    s.Timestamp.Minute >= startMinute ||
                                    s.Timestamp.Hour < startHour && s.Timestamp.Hour == endHour &&
                                    s.Timestamp.Minute <= endMinute)
                                   &&
                                   s.EventParam == param &&
                                   eventCodes.Contains(s.EventCode)
                             select s).Count();
                }

                return count;
            }
            catch (Exception ex)
            {
                var logRepository =
                    ApplicationEventRepositoryFactory.Create();
                var e = new ApplicationEvent();
                e.ApplicationName = "MOE.Common";
                e.Class = GetType().ToString();
                e.Function = "GetEventCountByEventCodesParamDateTimeRange";
                e.SeverityLevel = ApplicationEvent.SeverityLevels.High;
                e.Timestamp = DateTime.Now;
                e.Description = ex.Message;
                logRepository.Add(e);
                throw;
            }
        }


        public List<Controller_Event_Log> GetEventsByEventCodesParamDateTimeRange(string signalId,
            DateTime startTime, DateTime endTime, int startHour, int startMinute, int endHour, int endMinute,
            List<int> eventCodes, int param)
        {
            try
            {
                var events = (from s in _db.Controller_Event_Log
                              where s.SignalID == signalId &&
                                    s.Timestamp >= startTime &&
                                    s.Timestamp <= endTime &&
                                    (s.Timestamp.Hour > startHour && s.Timestamp.Hour < endHour ||
                                     s.Timestamp.Hour == startHour && s.Timestamp.Hour == endHour &&
                                     s.Timestamp.Minute >= startMinute && s.Timestamp.Minute <= endMinute ||
                                     s.Timestamp.Hour == startHour && s.Timestamp.Hour < endHour &&
                                     s.Timestamp.Minute >= startMinute ||
                                     s.Timestamp.Hour < startHour && s.Timestamp.Hour == endHour &&
                                     s.Timestamp.Minute <= endMinute)
                                    &&
                                    s.EventParam == param &&
                                    eventCodes.Contains(s.EventCode)
                              select s).ToList();

                if (!events.Any())
                {
                    var archivedData = GetDataFromArchive(signalId, startTime, endTime);
                    events = (from s in archivedData
                              where s.SignalID == signalId &&
                                    s.Timestamp >= startTime &&
                                    s.Timestamp <= endTime &&
                                    (s.Timestamp.Hour > startHour && s.Timestamp.Hour < endHour ||
                                     s.Timestamp.Hour == startHour && s.Timestamp.Hour == endHour &&
                                     s.Timestamp.Minute >= startMinute && s.Timestamp.Minute <= endMinute ||
                                     s.Timestamp.Hour == startHour && s.Timestamp.Hour < endHour &&
                                     s.Timestamp.Minute >= startMinute ||
                                     s.Timestamp.Hour < startHour && s.Timestamp.Hour == endHour &&
                                     s.Timestamp.Minute <= endMinute)
                                    &&
                                    s.EventParam == param &&
                                    eventCodes.Contains(s.EventCode)
                              select s).ToList();
                }

                events.Sort((x, y) => DateTime.Compare(x.Timestamp, y.Timestamp));
                return events;
            }
            catch (Exception ex)
            {
                var logRepository =
                    ApplicationEventRepositoryFactory.Create();
                var e = new ApplicationEvent();
                e.ApplicationName = "MOE.Common";
                e.Class = GetType().ToString();
                e.Function = "GetSignalEventsByEventCodesParamDateTimeRange";
                e.SeverityLevel = ApplicationEvent.SeverityLevels.High;
                e.Timestamp = DateTime.Now;
                e.Description = ex.Message;
                logRepository.Add(e);
                throw;
            }
        }


        public List<Controller_Event_Log> GetEventsByEventCodesParamWithOffsetAndLatencyCorrection(string signalId,
            DateTime startTime, DateTime endTime, List<int> eventCodes, int param, double offset,
            double latencyCorrection)
        {
            try
            {
                var events = (from s in _db.Controller_Event_Log
                              where s.SignalID == signalId &&
                                    s.Timestamp >= startTime &&
                                    s.Timestamp <= endTime &&
                                    s.EventParam == param &&
                                    eventCodes.Contains(s.EventCode)
                              select s).ToList();
                if (!events.Any())
                {
                    var archivedData = GetDataFromArchive(signalId, startTime, endTime);
                    events = (from s in archivedData
                              where s.SignalID == signalId &&
                                    s.Timestamp >= startTime &&
                                    s.Timestamp <= endTime &&
                                    s.EventParam == param &&
                                    eventCodes.Contains(s.EventCode)
                              select s).ToList();
                }

                events.Sort((x, y) => DateTime.Compare(x.Timestamp, y.Timestamp));
                foreach (var cel in events)
                {
                    cel.Timestamp = cel.Timestamp.AddMilliseconds(offset);
                    cel.Timestamp = cel.Timestamp.AddSeconds(0 - latencyCorrection);
                }
                return events;
            }
            catch (Exception ex)
            {
                var logRepository =
                    ApplicationEventRepositoryFactory.Create();
                var e = new ApplicationEvent();
                e.ApplicationName = "MOE.Common";
                e.Class = GetType().ToString();
                e.Function = "GetEventsByEventCodesParamWithOffsetAndLatencyCorrection";
                e.SeverityLevel = ApplicationEvent.SeverityLevels.High;
                e.Timestamp = DateTime.Now;
                e.Description = ex.Message;
                logRepository.Add(e);
                throw;
            }
        }

        public List<Controller_Event_Log> GetEventsByEventCodesParamWithLatencyCorrection(string signalId,
            DateTime startTime, DateTime endTime, List<int> eventCodes, int param, double latencyCorrection)
        {
            try
            {
                var events = _db.Controller_Event_Log.Where(s => s.SignalID == signalId &&
                          s.Timestamp >= startTime &&
                          s.Timestamp <= endTime &&
                          s.EventParam == param &&
                          eventCodes.Contains(s.EventCode)).ToList();

                if (!events.Any())
                {
                    var archivedData = GetDataFromArchive(signalId, startTime, endTime);
                    events = archivedData.Where(s => s.SignalID == signalId &&
                                                     s.Timestamp >= startTime &&
                                                     s.Timestamp <= endTime &&
                                                     s.EventParam == param &&
                                                     eventCodes.Contains(s.EventCode)).ToList();
                }

                foreach (var cel in events)
                {
                    cel.Timestamp = cel.Timestamp.AddSeconds(0 - latencyCorrection);
                }
                return events.OrderBy(e => e.Timestamp).ThenBy(e => e.EventCode).ToList();
            }
            catch (Exception ex)
            {
                var logRepository =
                    ApplicationEventRepositoryFactory.Create();
                var e = new ApplicationEvent();
                e.ApplicationName = "MOE.Common";
                e.Class = GetType().ToString();
                e.Function = "GetEventsByEventCodesParamWithLatencyCorrection";
                e.SeverityLevel = ApplicationEvent.SeverityLevels.High;
                e.Timestamp = DateTime.Now;
                e.Description = ex.Message;
                logRepository.Add(e);
                throw;
            }
        }

        public Controller_Event_Log GetFirstEventBeforeDate(string signalId,
            int eventCode, DateTime date)
        {
            try
            {
                var tempDate = date.AddHours(-1);
                var lastEvent = _db.Controller_Event_Log.Where(c => c.SignalID == signalId &&
                                                                    c.Timestamp >= tempDate &&
                                                                    c.Timestamp < date &&
                                                                    c.EventCode == eventCode)
                    .OrderByDescending(c => c.Timestamp).FirstOrDefault();

                if (lastEvent == null)
                {
                    var archivedData = GetDataFromArchive(signalId, tempDate, date);
                    lastEvent = archivedData.Where(c => c.SignalID == signalId &&
                                                        c.Timestamp >= tempDate &&
                                                        c.Timestamp < date &&
                                                        c.EventCode == eventCode)
                        .OrderByDescending(c => c.Timestamp).FirstOrDefault();
                }

                return lastEvent;
            }
            catch (Exception ex)
            {
                var logRepository = ApplicationEventRepositoryFactory.Create();
                var e = new ApplicationEvent();
                e.ApplicationName = "MOE.Common";
                e.Class = GetType().ToString();
                e.Function = "GetEventsByEventCodesParamWithOffsetAndLatencyCorrection";
                e.SeverityLevel = ApplicationEvent.SeverityLevels.High;
                e.Description = ex.Message;
                e.Timestamp = DateTime.Now;
                logRepository.Add(e);
                return null;
            }
        }

        public Controller_Event_Log GetFirstEventBeforeDateByEventCodeAndParameter(string signalId, int eventCode,
            int eventParam, DateTime date)
        {
            try
            {
                var tempDate = date.AddDays(-1);
                var lastEvent = _db.Controller_Event_Log.Where(c => c.SignalID == signalId &&
                                                                    c.Timestamp >= tempDate &&
                                                                    c.Timestamp < date &&
                                                                    c.EventCode == eventCode &&
                                                                    c.EventParam == eventParam)
                    .OrderByDescending(c => c.Timestamp).FirstOrDefault();

                if (lastEvent == null)
                {
                    var archivedData = GetDataFromArchive(signalId, tempDate, date);
                    lastEvent = archivedData.Where(c => c.SignalID == signalId &&
                                                                    c.Timestamp >= tempDate &&
                                                                    c.Timestamp < date &&
                                                                    c.EventCode == eventCode &&
                                                                    c.EventParam == eventParam)
                    .OrderByDescending(c => c.Timestamp).FirstOrDefault();
                }

                return lastEvent;
            }
            catch (Exception ex)
            {
                var logRepository = ApplicationEventRepositoryFactory.Create();
                var e = new ApplicationEvent();
                e.ApplicationName = "MOE.Common";
                e.Class = GetType().ToString();
                e.Function = "GetEventsByEventCodesParamWithOffsetAndLatencyCorrection";
                e.SeverityLevel = ApplicationEvent.SeverityLevels.High;
                e.Description = ex.Message;
                e.Timestamp = DateTime.Now;
                logRepository.Add(e);
                return null;
            }
        }

        public int GetSignalEventsCountBetweenDates(string signalId, DateTime startTime, DateTime endTime)
        {
            var count = _db.Controller_Event_Log.Count(r => r.SignalID == signalId &&
                                                r.Timestamp >= startTime
                                                && r.Timestamp < endTime);

            if (count <= 0)
            {
                var archivedData = GetDataFromArchive(signalId, startTime, endTime);
                count = archivedData.Count(r =>
                    r.SignalID == signalId && r.Timestamp >= startTime && r.Timestamp < endTime);
            }

            return count;
        }

        public int GetApproachEventsCountBetweenDates(int approachId, DateTime startTime, DateTime endTime,
            int phaseNumber)
        {
            var approachCodes = new List<int> { 1, 8, 10 };
            var ar = ApproachRepositoryFactory.Create();
            Approach approach = ar.GetApproachByApproachID(approachId);

            var results = _db.Controller_Event_Log.Where(r =>
                r.SignalID == approach.SignalID && r.Timestamp > startTime && r.Timestamp < endTime
                && approachCodes.Contains(r.EventCode) && r.EventParam == phaseNumber).ToList();

            if (!results.Any())
            {
                var archivedData = GetDataFromArchive(approach.SignalID, startTime, endTime);
                results = archivedData.Where(r =>
                    r.SignalID == approach.SignalID && r.Timestamp > startTime && r.Timestamp < endTime
                    && approachCodes.Contains(r.EventCode) && r.EventParam == phaseNumber).ToList();
            }

            return results.Count;
        }

        public DateTime GetMostRecentRecordTimestamp(string signalID)
        {
            MOE.Common.Models.Controller_Event_Log row = (from r in _db.Controller_Event_Log
                where r.SignalID == signalID
                orderby r.Timestamp descending
                select r).Take(1).FirstOrDefault();
            if (row != null)
            {
                return row.Timestamp;
            }
            else
            {
                return new DateTime();
            }
        }

        #region Parquet Archive

        public List<Controller_Event_Log> GetDataFromArchive(string signalId, DateTime startTime, DateTime endTime)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_localPath)) return new List<Controller_Event_Log>();
                var dateRange = startTime.Date == endTime.Date ? new List<DateTime>() { startTime.Date } : GetDateRange(startTime, endTime);

                var events = new List<Controller_Event_Log>();
                foreach (var date in dateRange)
                {
                    if (File.Exists($"{_localPath}\\date={date.Date:yyyy-MM-dd}\\{signalId}_{date.Date:yyyy-MM-dd}.parquet"))
                    {
                        using (var stream = File.OpenRead($"{_localPath}\\date={date.Date:yyyy-MM-dd}\\{signalId}_{date.Date:yyyy-MM-dd}.parquet"))
                        {
                            var newEvents = ParquetConvert.Deserialize<ParquetEventLog>(stream);
                            foreach (var parquetEvent in newEvents)
                            {
                                events.Add(new Controller_Event_Log
                                {
                                    SignalID = parquetEvent.SignalID,
                                    Timestamp = date.AddMilliseconds(parquetEvent.TimestampMs),
                                    EventCode = parquetEvent.EventCode,
                                    EventParam = parquetEvent.EventParam
                                });
                            }
                        }
                    }
                    else
                    {
                        //var logRepository = ApplicationEventRepositoryFactory.Create();
                        //var e = new ApplicationEvent();
                        //e.ApplicationName = "MOE.Common";
                        //e.Class = GetType().ToString();
                        //e.Function = "GetDataFromArchive";
                        //e.SeverityLevel = ApplicationEvent.SeverityLevels.High;
                        //e.Description = $"File {_localPath}\\date={date.Date:yyyy-MM-dd}\\{signalId}_{date.Date:yyyy-MM-dd}.parquet does not exist";
                        //e.Timestamp = DateTime.Now;
                        //logRepository.Add(e);
                        return new List<Controller_Event_Log>();
                    }

                    return events.Where(x => x.Timestamp >= startTime && x.Timestamp < endTime).ToList();
                }

                return events.Where(x => x.Timestamp >= startTime && x.Timestamp < endTime).ToList();
            }
            catch (Exception ex)
            {
                var logRepository = ApplicationEventRepositoryFactory.Create();
                var e = new ApplicationEvent();
                e.ApplicationName = "MOE.Common";
                e.Class = GetType().ToString();
                e.Function = "GetDataFromArchive";
                e.SeverityLevel = ApplicationEvent.SeverityLevels.High;
                e.Description = ex.Message;
                e.Timestamp = DateTime.Now;
                logRepository.Add(e);
                return new List<Controller_Event_Log>();
            }
        }

        public static IEnumerable<DateTime> GetDateRange(DateTime startDate, DateTime endDate)
        {
            if (endDate < startDate)
                throw new ArgumentException("EndDate must be greater than or equal to StartDate");

            while (startDate <= endDate)
            {
                yield return startDate;
                startDate = startDate.AddDays(1);
            }
        }

        private static string GetSetting(string settingName)
        {
            return ConfigurationManager.AppSettings[settingName];
        }

        #endregion
    }
}