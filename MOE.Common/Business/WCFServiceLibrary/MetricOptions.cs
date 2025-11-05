using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Configuration;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Web.UI.DataVisualization.Charting;
using System.Xml;
using MOE.Common.Models;
using MOE.Common.Models.Repositories;

namespace MOE.Common.Business.WCFServiceLibrary
{
    [DataContract]
    [KnownType(typeof(PCDOptions))]
    [KnownType(typeof(TMCOptions))]
    [KnownType(typeof(AoROptions))]
    [KnownType(typeof(ApproachDelayOptions))]
    [KnownType(typeof(MetricOptions))]
    [KnownType(typeof(PhaseTerminationOptions))]
    [KnownType(typeof(PreemptDetailOptions))]
    [KnownType(typeof(PreemptServiceMetricOptions))]
    [KnownType(typeof(PreemptServiceRequestOptions))]
    [KnownType(typeof(YellowAndRedOptions))]
    [KnownType(typeof(ApproachSpeedOptions))]
    [KnownType(typeof(SplitFailOptions))]
    [KnownType(typeof(SplitMonitorOptions))]
    [KnownType(typeof(PedDelayOptions))]
    [KnownType(typeof(LeftTurnGapAnalysisOptions))]
    [KnownType(typeof(WaitTimeOptions))]
    [KnownType(typeof(ApproachVolumeOptions))]
    [KnownType(typeof(SignalAggregationMetricOptions))]
    [KnownType(typeof(ApproachAggregationMetricOptions))]
    [KnownType(typeof(ApproachSplitFailAggregationOptions))]
    [KnownType(typeof(PreemptionAggregationOptions))]
    [KnownType(typeof(PriorityAggregationOptions))]
    [KnownType(typeof(ApproachPcdAggregationOptions))]
    [KnownType(typeof(ApproachYellowRedActivationsAggregationOptions))]
    [KnownType(typeof(DetectorAggregationMetricOptions))]
    [KnownType(typeof(DetectorVolumeAggregationOptions))]
    [KnownType(typeof(ApproachSpeedAggregationOptions))]
    [KnownType(typeof(PhaseCycleAggregationOptions))]
    [KnownType(typeof(SignalEventCountAggregationOptions))]
    [KnownType(typeof(PhaseTerminationAggregationOptions))]
    [KnownType(typeof(PhasePedAggregationOptions))]
    [KnownType(typeof(TimingAndActuationsOptions))]
    [KnownType(typeof(PhaseLeftTurnGapAggregationOptions))]
    [KnownType(typeof(PhaseSplitMonitorAggregationOptions))]
    [KnownType(typeof(string[]))]
    public class MetricOptions
    {
        //IMeasuresDefaultsRepository measuresDefaultsRepository = MeasuresDefaultsRepositoryFactory.Create();
        public MetricOptions()
        {
            //var applicationSettingRepository = ApplicationSettingsRepositoryFactory.Create();

            //GeneralSettings generalSettings = applicationSettingRepository.GetGeneralSettings();
            SignalID = string.Empty;
            YAxisMin = 0;
            Y2AxisMax = 0;
            Y2AxisMin = 0;
            MetricTypeID = 0;
            //MetricFileLocation = generalSettings.ImagePath;
            //MetricWebPath = generalSettings.ImageUrl;
            ReturnList = new List<string>();
        }

        [Key]
        [Required]
        [DataMember]
        public string SignalID { get; set; }

        [DataMember]
        public int MetricTypeID { get; set; }

        [DataMember]
        public DateTime StartDate { get; set; }

        [DataMember]
        public DateTime EndDate { get; set; }

        [DataMember]
        [Display(Name = "Y-axis Max")]
        public double? YAxisMax { get; set; }

        [DataMember]
        [Display(Name = "Y-axis Min")]
        public double YAxisMin { get; set; }

        [DataMember]
        [Display(Name = "Secondary Y-axis Max")]
        public double? Y2AxisMax { get; set; }

        [DataMember]
        [Display(Name = "Secondary Y-axis Min")]
        public double? Y2AxisMin { get; set; }

        [DataMember]
        public string MetricFileLocation { get; set; }

        [DataMember]
        public string MetricWebPath { get; set; }

        public List<XmlDocument> XMLMetricData { get; protected set; } = new List<XmlDocument>();

        public MetricType MetricType { get; set; }

        [DataMember]
        public List<string> ReturnList { get; set; }

        public string Y2AxisTitle { get; set; }

        public List<Tuple<string, string>> ResultChartAndXmlLocations { get; set; } = new List<Tuple<string, string>>();

        public virtual List<string> CreateMetric()
        {
            EndDate = EndDate.AddMinutes(1);
            var metricTypeRepository = MetricTypeRepositoryFactory.Create();
            MetricType = metricTypeRepository.GetMetricsByID(MetricTypeID);
            var settingsRepository = Models.Repositories.ApplicationSettingsRepositoryFactory.Create();
            var settings = settingsRepository.GetGeneralSettings();
            MetricFileLocation = settings.ImagePath;
            MetricWebPath = settings.ImageUrl;
            LogMetricRun();
            return new List<string>();
        }

        public void SetDefaults()
        {
            //var measure = GetType().Name.Replace("Options", "");
            //var defaults = measuresDefaultsRepository.GetMeasureDefaultsAsDictionary(measure);
            //foreach (var option in defaults)
            //{
            //    var type = GetType().GetProperty(option.Key)?.PropertyType;

            //    if (option.Value == null || option.Value.ToLower() == "null") continue;

            //    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            //    {
            //        type = Nullable.GetUnderlyingType(type);
            //    }

            //    var converted = Convert.ChangeType(option.Value, type);
            //    GetType().GetProperty(option.Key).SetValue(this, converted);
            //}
        }

        protected void LogMetricRun()
        {
            var appEventRepository = ApplicationEventRepositoryFactory.Create();
            var applicationEvent = new ApplicationEvent();
            applicationEvent.ApplicationName = "SPM Website";
            applicationEvent.Description = MetricType.ChartName + " Executed";
            applicationEvent.SeverityLevel = ApplicationEvent.SeverityLevels.Low;
            applicationEvent.Timestamp = DateTime.Now;
            appEventRepository.Add(applicationEvent);
        }

        public static string RgbaFromColor(Color c, double? opacity = null)
        {
            var a = opacity ?? (c.A / 255.0);
            return $"rgba({c.R},{c.G},{c.B},{a:0.###})";
        }


        internal void ConfigureGraph(PlotlyObject obj, DateTime start, DateTime end, int xTick, int yTick, TimeZoneInfo zone)
        {
            obj.Layout.Title.Text = "Purdue Phase Termination";
            obj.Layout.XAxis.Title.Text = "Time (Hours:Minutes of Day)";
            obj.Layout.YAxis.Title.Text = "Phase";
            obj.Layout.XAxis.Type = "date";
            obj.Layout.Title.Subtitle.Text = $"SIG{SignalID} between {StartDate} and {EndDate}";
            obj.Layout.XAxis.DTick = 3600000;
            obj.Layout.YAxis.DTick = 1;
            var startLocal = TimeZoneInfo.ConvertTime(StartDate, zone);
            var endLocal = TimeZoneInfo.ConvertTime(EndDate, zone);

            obj.Layout.XAxis.Range.Add(startLocal.ToString("o", CultureInfo.InvariantCulture));
            obj.Layout.XAxis.Range.Add(endLocal.ToString("o", CultureInfo.InvariantCulture));
        }
        public string GetSignalLocation()
        {
            var signalRepository = SignalsRepositoryFactory.Create();
            return signalRepository.GetSignalLocation(SignalID);
        }

        public string CreateFileName()
        {
            if (MetricType == null)
            {
                var metricTypeRepository = MetricTypeRepositoryFactory.Create();
                MetricType = metricTypeRepository.GetMetricsByID(MetricTypeID);
            }

            var fileName = MetricType.Abbreviation +
                           SignalID +
                           "-" +
                           StartDate.Year +
                           StartDate.ToString("MM") +
                           StartDate.ToString("dd") +
                           StartDate.ToString("HH") +
                           StartDate.ToString("mm") +
                           "-" +
                           EndDate.Year +
                           EndDate.ToString("MM") +
                           EndDate.ToString("dd") +
                           EndDate.ToString("HH") +
                           EndDate.ToString("mm-");
            var r = new Random();
            fileName += r.Next().ToString();
            fileName += ".jpg";
            try
            {
                if (DriveAvailable())
                    return fileName;
                return null;
            }
            catch
            {
                throw new Exception("Path not found");
            }
        }
        

        public void SerializeMetricData(Chart chart)
        {
            XMLMetricData.Add(Export.ChartToCSVExporter.GetXMLFromChart(chart));
        }

        public XmlDocument GetXmlForChart(Chart chart)
        {
            return Export.ChartToCSVExporter.GetXMLFromChart(chart);
        }


        public bool DriveAvailable()
        {
            var di = new DirectoryInfo(MetricFileLocation);
            di.Refresh();
            if (di.Exists)
                return true;
            Directory.CreateDirectory(MetricFileLocation);
            di.Refresh();
            if (di.Exists)
                return true;
            return false;
        }
    }
}