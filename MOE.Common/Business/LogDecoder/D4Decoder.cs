using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration.Attributes;

namespace MOE.Common.Business.LogDecoder
{
    public class D4Decoder
    {
        public static void DecodeD4File(string fileName, string signalId,
            BlockingCollection<Data.MOE.Controller_Event_LogRow> rowBag, DateTime earliestAcceptableDate)
        {
            var table = new Data.MOE.Controller_Event_LogDataTable();
            using (var reader = new StreamReader(fileName))
            using (var csv = new CsvReader(reader))
            {
                var records = new List<D4Record>();
                try
                {
                    records = csv.GetRecords<D4Record>().ToList();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error reading file {fileName}, trying line-by-line");
                    while (csv.Read())
                    {
                        try
                        {
                            var line = new D4Record
                            {
                                EventCode = csv.GetField<int>(0),
                                Param = csv.GetField<int>(1),
                                DateTime = csv.GetField<DateTime>(2),
                                MsgIdx = csv.GetField<int>(3)
                            };
                            records.Add(line);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                        }
                    }
                }

                foreach (var record in records)
                {
                    if (record.DateTime > earliestAcceptableDate)
                    {
                        try
                        {
                            var row = table.NewController_Event_LogRow();
                            row.Timestamp = record.DateTime;
                            row.EventCode = record.EventCode;
                            row.EventParam = record.Param;
                            row.SignalID = signalId;
                            rowBag.Add(row);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                        }
                    }
                }
            }
        }

        public static List<string> DecodeD4GzipFile(string fileName, string signalId,
     BlockingCollection<Data.MOE.Controller_Event_LogRow> rowBag, DateTime earliestAcceptableDate, string cwd)
        {
            var fileList = new List<string> { fileName }; // Add original filename
            var table = new Data.MOE.Controller_Event_LogDataTable();

            var fileStream = Asc3Decoder.DecompressFile(fileName, cwd + signalId + "\\");
            string csvFilePath = Path.Combine(cwd + signalId + "\\", Path.GetFileName(fileName).Replace(".gz", ""));

            // Read all content to memory first
            string csvContent;
            using (var reader = new StreamReader(fileStream))
            {
                csvContent = reader.ReadToEnd();
            }

            // Save content as CSV file
            File.WriteAllText(csvFilePath, csvContent);
            fileList.Add(csvFilePath); // Add CSV file to list

            // Create new stream from saved content for CSV reading
            using (var stringReader = new StringReader(csvContent))
            using (var csv = new CsvReader(stringReader))
            {
                var records = new List<D4Record>();
                try
                {
                    records = csv.GetRecords<D4Record>().ToList();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error reading file {fileName}, trying line-by-line");
                    while (csv.Read())
                    {
                        try
                        {
                            var line = new D4Record
                            {
                                EventCode = csv.GetField<int>(0),
                                Param = csv.GetField<int>(1),
                                DateTime = csv.GetField<DateTime>(2),
                                MsgIdx = csv.GetField<int>(3)
                            };
                            records.Add(line);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                        }
                    }
                }

                foreach (var record in records)
                {
                    if (record.DateTime > earliestAcceptableDate)
                    {
                        try
                        {
                            var row = table.NewController_Event_LogRow();
                            row.Timestamp = record.DateTime;
                            row.EventCode = record.EventCode;
                            row.EventParam = record.Param;
                            row.SignalID = signalId;
                            rowBag.Add(row);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                        }
                    }
                }
            }

            return fileList;
        }
    }

    public class D4Record
    {
        [Index(0)]
        public int EventCode { get; set; }
        [Index(1)]
        public int Param { get; set; }
        [Index(2)]
        public DateTime DateTime { get; set; }
        [Index(3)]
        public int MsgIdx { get; set; }
    }

    public class D4RecordStreamed
    {
        [Index(0)]
        public int EventCode { get; set; }
        [Index(1)]
        public string EventName { get; set; }
        [Index(2)]
        public int Param { get; set; }
        [Index(3)]
        public DateTime DateTime { get; set; }
        [Index(4)]
        public int MsgIdx { get; set; }
    }
}
