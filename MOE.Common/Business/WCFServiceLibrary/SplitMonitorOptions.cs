using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Web.Mvc;
using System.Web.UI.DataVisualization.Charting;
using Amazon.S3.Model;
using MOE.Common.Models;
using MOE.Common.Models.Custom;
using MOE.Common.Models.Repositories;
using Annotation = MOE.Common.Models.Annotation;

namespace MOE.Common.Business.WCFServiceLibrary
{
    [DataContract]
    public class SplitMonitorOptions : MetricOptions
    {
        public SplitMonitorOptions(string signalID, double? yAxisMax,
            DateTime startDate, DateTime endDate, string zone,
            int percentileSplit, bool showPlanStripes, bool showPedActivity,
            bool showAverageSplit, bool showPercentMaxOutForceOff, bool showPercentGapOuts, bool showPercentSkip, int storageLoc, string connString)
        {
            SignalID = signalID;
            YAxisMax = yAxisMax;
            //MetricTypeID = metricTypeID;
            StartDate = startDate;
            EndDate = endDate;
            Zone = TimeZoneInfo.FindSystemTimeZoneById(zone);
            SelectedPercentileSplit = percentileSplit;
            ShowPlanStripes = showPlanStripes;
            ShowPedActivity = showPedActivity;
            ShowAverageSplit = showAverageSplit;
            ShowPercentMaxOutForceOff = showPercentMaxOutForceOff;
            ShowPercentGapOuts = showPercentGapOuts;
            ShowPercentSkip = showPercentSkip;
            //MetricType.ChartName = "SplitMonitor";
            Settings = new MOEService(storageLoc, connString);
        }

        public SplitMonitorOptions()
        {
            SetPercentSplitsList();
            SetDefaults();
        }
        public TimeZoneInfo Zone { get; set; } // set this from ctor/options
        [DataMember]
        [Display(Name = "Percentile Split")]
        public int? SelectedPercentileSplit { get; set; }

        [DataMember]
        [Display(Name = "Show Plans")]
        public bool ShowPlanStripes { get; set; }

        [DataMember]
        [Display(Name = "Show Ped Activity")]
        public bool ShowPedActivity { get; set; }

        [DataMember]
        [Display(Name = "Show Average Split")]
        public bool ShowAverageSplit { get; set; }

        [DataMember]
        [Display(Name = "Show % Max Out/ForceOff")]
        public bool ShowPercentMaxOutForceOff { get; set; }

        [DataMember]
        [Display(Name = "Show Percent GapOuts")]
        public bool ShowPercentGapOuts { get; set; }

        [DataMember]
        [Display(Name = "Show Percent Skip")]
        public bool ShowPercentSkip { get; set; }

        public List<SelectListItem> PercentSplitsSelectList { get; set; }

        [Display(Name = "Adjust Y Axis")]
        public bool AdjustYAxis { get; set; }

        public PlotlyObject SplitMonitorPlotlyObject { get; set; }

        public List<PlotlyObject> SplitMonitorCharts { get; set; }

        public MOEService Settings { get; set; }

        public string CreateTractionMetric()
        {
            
            var analysisPhaseCollection = new AnalysisPhaseCollection(SignalID, StartDate, EndDate, Settings);
            SplitMonitorCharts = new List<PlotlyObject>();
            if (analysisPhaseCollection.Items.Count > 0)
            {
                foreach (var plan in analysisPhaseCollection.Plans)
                {
                    plan.SetProgrammedSplits(SignalID);
                    plan.SetHighCycleCount(analysisPhaseCollection);
                }
                var phasesInOrder = (analysisPhaseCollection.Items.Select(r => r)).OrderBy(r => r.PhaseNumber);
                
                foreach (var phase in phasesInOrder)
                {
                    SplitMonitorPlotlyObject = new PlotlyObject();
                    ConfigureGraph(SplitMonitorPlotlyObject, StartDate, EndDate, 3600000, 5, Zone);

                    SplitMonitorPlotlyObject.Data.AddRange(AddSplitMonitorEventData(analysisPhaseCollection, phase, Zone));
                    SplitMonitorCharts.Add(SplitMonitorPlotlyObject);
                }
                return JsonSerializer.Serialize(SplitMonitorCharts, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true });
            }

            return "No Data Available";

        }



        public List<Trace> AddSplitMonitorEventData(AnalysisPhaseCollection analysisPhaseCollection, AnalysisPhase currentPhase, TimeZoneInfo tz)
        {
            var allTraces = new List<Trace>();
            var termEventList = new List<int>[4, 5, 6, 0];
            
            var programmedSplit = new Trace("Programmed Split");

            programmedSplit.Marker.Color = "orangered";

            if (ShowPlanStripes)
            {
                SetSimplePlanStripes(analysisPhaseCollection.Plans, StartDate, EndDate, tz);
                SetSplitMonitorStats(analysisPhaseCollection.Plans, currentPhase, SplitMonitorPlotlyObject);
            }

            //Table 
            if (currentPhase.Cycles.Items.Count > 0)
            {
                var maxSplitLength = 0;
                foreach (var plan in analysisPhaseCollection.Plans)
                {
                    var highestSplit = plan.FindHighestRecordedSplitPhase();
                    plan.FillMissingSplits(highestSplit);
                    try
                    {
                        programmedSplit.AddXY(plan.StartTime, plan.Splits[currentPhase.PhaseNumber], tz);
                        programmedSplit.AddXY(plan.EndTime, plan.Splits[currentPhase.PhaseNumber], tz);
                        if (plan.Splits[currentPhase.PhaseNumber] > maxSplitLength)
                            maxSplitLength = plan.Splits[currentPhase.PhaseNumber];
                    }
                    catch
                    {
                        //System.Windows.MessageBox.Show(ex.ToString());
                    }
                }

                var maxOut = new Trace("Max Out")
                {
                    Marker = {Color = "red"}
                };
                var forceOff = new Trace("Force Off")
                {
                    Marker = {Color = "mediumblue"}
                };
                var unknown = new Trace("Unknown Termination Cause")
                {
                    Marker = {Color="black"}
                };
                var pedAct = new Trace("Pedestrian Activity")
                {
                    Mode = "line",
                    Line = {Color = "darkgoldenrod"}
                };


                var gapOut = new Trace("Gap Out")
                {

                    Marker = { Color = "olivedrab" }
                };

                foreach (var Cycle in currentPhase.Cycles.Items)
                {

                    if(IsDataInPhaseCycle(currentPhase, 4))
                    {
                        gapOut.AddXY(Cycle.StartTime, Cycle.Duration.TotalSeconds, tz);
                    };
                       
                    if (IsDataInPhaseCycle(currentPhase, 5))
                    {
                        
                        maxOut.AddXY(Cycle.StartTime, Cycle.Duration.TotalSeconds, tz);
                    }

                    if (IsDataInPhaseCycle(currentPhase, 6))
                    {
                        forceOff.AddXY(Cycle.StartTime, Cycle.Duration.TotalSeconds, tz);
                    }

                    if (IsDataInPhaseCycle(currentPhase, 0))
                    {
                        unknown.AddXY(Cycle.StartTime, Cycle.Duration.TotalSeconds, tz);
                    }
                        
                    if (Cycle.HasPed && ShowPedActivity)
                    {
                        if (Cycle.PedDuration == 0)
                        {
                            if (Cycle.PedStartTime == DateTime.MinValue)
                                Cycle.SetPedStart(Cycle.StartTime);
                            if (Cycle.PedEndTime == DateTime.MinValue)
                                Cycle.SetPedEnd(Cycle.YellowEvent);
                        }
                        pedAct.AddXY(Cycle.PedStartTime, Cycle.PedDuration, tz);
                    }
                }
                allTraces.Add(gapOut);
                allTraces.Add(maxOut);
                allTraces.Add(forceOff);
                allTraces.Add(programmedSplit);
                allTraces.Add(pedAct);
                allTraces.Add(unknown);
                allTraces = allTraces.Where(t => t.X.Any() && t.Y.Any()).ToList();
                SetYAxisMaxAndInterval(SplitMonitorPlotlyObject, currentPhase, maxSplitLength);
            }
            
            
            

            return allTraces;
        }

        bool IsDataInPhaseCycle(AnalysisPhase currentPhase, int termEvent)
        {
            return currentPhase.Cycles.Items.Any(x => x.TerminationEvent == termEvent && x.Duration.TotalSeconds > 0);
        }

        public void SetSimplePlanStripes(List<PlanSplitMonitor> plans, DateTime graphStartDate, DateTime graphEndDate, TimeZoneInfo tz)
        {
            var backGroundColor = 1;
            foreach (PlanSplitMonitor plan in plans)
            {
                var startLocal = TimeZoneInfo.ConvertTime(plan.StartTime, Zone);
                var endLocal = TimeZoneInfo.ConvertTime(plan.EndTime, Zone);
                var stripLine = new Shape();
                //Creates alternating backcolor to distinguish the plans
                if (backGroundColor % 2 == 0)

                    stripLine.FillColor = RgbaFromColor(Color.LightSeaGreen);
                else
                    stripLine.FillColor = RgbaFromColor(Color.LightSteelBlue);

                stripLine.X0 = startLocal.ToString("o");
                stripLine.X1 = endLocal.ToString("o");

                var midpoint = new DateTime((startLocal.Ticks + endLocal.Ticks) / 2, startLocal.Kind);
                var annotation = new Annotation
                {
                    Font = {Size = 12},
                    X = midpoint.ToString("yyyy-MM-ddTHH:mm"),
                    ShowArrow = false
                };
                switch (plan.PlanNumber)
                {
                    case 254:
                        annotation.Text = "Free";
                        break;
                    case 255:
                        annotation.Text = "Flash";
                        break;
                    case 0:
                        annotation.Text = "Unknown";
                        break;
                    default:
                        annotation.Text = "Plan " + plan.PlanNumber;
                        break;
                }
                SplitMonitorPlotlyObject.Layout.Shapes.Add(stripLine);
                SplitMonitorPlotlyObject.Layout.Annotations.Add(annotation);
                backGroundColor++;
            }
        }



        private void SetPercentSplitsList()
        {
            PercentSplitsSelectList = new List<SelectListItem>();
            PercentSplitsSelectList.Add(new SelectListItem { Value = "", Text = "No Percentile Split" });
            PercentSplitsSelectList.Add(new SelectListItem { Value = "50", Text = "50" });
            PercentSplitsSelectList.Add(new SelectListItem { Value = "75", Text = "75" });
            PercentSplitsSelectList.Add(new SelectListItem { Value = "85", Text = "85" });
            PercentSplitsSelectList.Add(new SelectListItem { Value = "90", Text = "90" });
            PercentSplitsSelectList.Add(new SelectListItem { Value = "95", Text = "95" });
        }

        public override List<string> CreateMetric()
        {
            base.CreateMetric();
            var analysisPhaseCollection = new AnalysisPhaseCollection(SignalID, StartDate, EndDate);
            //If there are phases in the collection add the charts
            if (analysisPhaseCollection.Items.Count > 0)
            {
                foreach (var plan in analysisPhaseCollection.Plans)
                {
                    plan.SetProgrammedSplits(SignalID);
                    plan.SetHighCycleCount(analysisPhaseCollection);
                }

                //If there are phases in the collection add the charts


                //dummy chart to create a legend for the entire split monitor page.
                var dummychart = new Chart();
                var chartarea1 = new ChartArea();
                ChartFactory.SetImageProperties(dummychart);
                dummychart.BorderlineDashStyle = ChartDashStyle.Dot;

                dummychart.Height = 100;

                var PedActivity = new Series();
                var GapoutSeries = new Series();
                var MaxOutSeries = new Series();
                var ForceOffSeries = new Series();
                var ProgramedSplit = new Series();
                var UnknownSeries = new Series();

                PedActivity.Name = "Ped Activity";
                GapoutSeries.Name = "Gap Out";
                MaxOutSeries.Name = "Max Out";
                ForceOffSeries.Name = "Force Off";
                ProgramedSplit.Name = "Programmed Split";
                UnknownSeries.Name = "Unknown Termination Cause";


                PedActivity.MarkerStyle = MarkerStyle.Cross;
                GapoutSeries.MarkerStyle = MarkerStyle.Circle;
                MaxOutSeries.MarkerStyle = MarkerStyle.Circle;
                ForceOffSeries.MarkerStyle = MarkerStyle.Circle;
                ProgramedSplit.BorderDashStyle = ChartDashStyle.Solid;
                UnknownSeries.MarkerStyle = MarkerStyle.Circle;

                GapoutSeries.Color = Color.OliveDrab;
                PedActivity.Color = Color.DarkGoldenrod;
                MaxOutSeries.Color = Color.Red;
                ForceOffSeries.Color = Color.MediumBlue;
                ProgramedSplit.Color = Color.OrangeRed;
                UnknownSeries.Color = Color.Black;

                dummychart.Series.Add(ProgramedSplit);
                dummychart.Series.Add(GapoutSeries);
                dummychart.Series.Add(MaxOutSeries);
                dummychart.Series.Add(ForceOffSeries);
                dummychart.Series.Add(UnknownSeries);
                dummychart.Series.Add(PedActivity);

                dummychart.ChartAreas.Add(chartarea1);

                var dummychartLegend = new Legend();
                dummychartLegend.Name = "DummyLegend";

                dummychartLegend.IsDockedInsideChartArea = true;

                dummychartLegend.Title = "Chart Legend";
                dummychartLegend.Docking = Docking.Bottom;
                dummychartLegend.Alignment = StringAlignment.Center;
                dummychart.Legends.Add(dummychartLegend);
                var removethese = new List<Title>();

                foreach (var t in dummychart.Titles)
                    if (string.IsNullOrEmpty(t.Text))
                        removethese.Add(t);
                foreach (var t in removethese)
                    dummychart.Titles.Remove(t);


                var dummyChartFileName = CreateFileName();
                dummychart.SaveImage(MetricFileLocation + dummyChartFileName);
                ReturnList.Add(MetricWebPath + dummyChartFileName);

                if (analysisPhaseCollection.Items.Count > 0)
                {
                    var phasesInOrder = (analysisPhaseCollection.Items.Select(r => r)).OrderBy(r => r.PhaseNumber);
                    foreach (var phase in phasesInOrder)
                    {
                        var chart = GetNewSplitMonitorChart(StartDate, EndDate,
                            phase.PhaseNumber);
                        AddSplitMonitorDataToChart(chart, phase, analysisPhaseCollection.Plans);
                        if (ShowPlanStripes)
                        {
                            SetSimplePlanStrips(analysisPhaseCollection.Plans, chart, StartDate);
                            SetSplitMonitorStatistics(analysisPhaseCollection.Plans, phase, chart);
                        }
                        var chartFileName = CreateFileName();
                        removethese = new List<Title>();

                        foreach (var t in chart.Titles)
                            if (string.IsNullOrEmpty(t.Text))
                                removethese.Add(t);
                        foreach (var t in removethese)
                            chart.Titles.Remove(t);
                        chart.SaveImage(MetricFileLocation + chartFileName);
                        ReturnList.Add(MetricWebPath + chartFileName);
                    }
                }
            }
            return ReturnList;
        }

        public List<SplitMonitorSummary> CreateMeticDataWithoutGraph()
        {
            List<SplitMonitorSummary> splitMonitorSummaries = new List<SplitMonitorSummary>();
            base.CreateMetric();
            var analysisPhaseCollection = new AnalysisPhaseCollection(SignalID, StartDate, EndDate);
            //If there are phases in the collection add the charts
            if (analysisPhaseCollection.Items.Count > 0)
            {
                foreach (var plan in analysisPhaseCollection.Plans)
                {
                    plan.SetProgrammedSplits(SignalID);
                    plan.SetHighCycleCount(analysisPhaseCollection);
                }
                if (analysisPhaseCollection.Items.Count > 0)
                {
                    var phasesInOrder = (analysisPhaseCollection.Items.Select(r => r)).OrderBy(r => r.PhaseNumber);
                    foreach (var phase in phasesInOrder)
                    {
                        splitMonitorSummaries.AddRange(GetSplitMonitorStatisticsWithoutGraph(analysisPhaseCollection.Plans, phase));
                    }
                }
            }
            return splitMonitorSummaries;
        }

        private List<SplitMonitorSummary> GetSplitMonitorStatisticsWithoutGraph(List<PlanSplitMonitor> plans, AnalysisPhase phase)
        {
            List<SplitMonitorSummary> splitMonitorSummaries = new List<SplitMonitorSummary>();
            //find the phase Cycles that occure during the plan.
            foreach (var plan in plans)
            {
                SplitMonitorSummary splitMonitorSummary = new SplitMonitorSummary();
                splitMonitorSummary.SignalId = phase.SignalID;
                splitMonitorSummary.Plan = plan.PlanNumber;
                splitMonitorSummary.Phase = phase.PhaseNumber;
                splitMonitorSummary.Date = plan.StartTime.ToString("MM/dd/yyyy hh:mm tt");

                var Cycles = from cycle in phase.Cycles.Items
                             where cycle.StartTime >= plan.StartTime && cycle.EndTime < plan.EndTime
                             orderby cycle.Duration
                             select cycle;

                // find % Skips
                if (ShowPercentSkip)
                    if (plan.CycleCount > 0)
                    {
                        double CycleCount = plan.CycleCount;
                        double SkippedPhases = plan.CycleCount - Cycles.Count();
                        double SkipPercent = 0;
                        if (CycleCount > 0)
                            SkipPercent = SkippedPhases / CycleCount;

                        splitMonitorSummary.Skips = Math.Round(SkipPercent, 2);
                    }

                // find % GapOuts
                if (ShowPercentGapOuts)
                {
                    var GapOuts = from cycle in Cycles
                                  where cycle.TerminationEvent == 4
                                  select cycle;

                    double CycleCount = plan.CycleCount;
                    double gapouts = GapOuts.Count();
                    double GapPercent = 0;
                    if (CycleCount > 0)
                        GapPercent = gapouts / CycleCount;

                    splitMonitorSummary.GapOuts = Math.Round(GapPercent, 2);
                }

                // Set Force Off
                if (ShowPercentMaxOutForceOff && plan.PlanNumber != 254
                )
                {
                    var ForceOffs = from cycle in Cycles
                                    where cycle.TerminationEvent == 6
                                    select cycle;

                    double CycleCount = plan.CycleCount;
                    double forceoffs = ForceOffs.Count();
                    double ForcePercent = 0;
                    if (CycleCount > 0)
                        ForcePercent = forceoffs / CycleCount;

                    splitMonitorSummary.ForceOffs = Math.Round(ForcePercent, 2);
                }

                //Average Split
                if (ShowAverageSplit)
                {
                    double runningTotal = 0;
                    double averageSplits = 0;
                    foreach (var Cycle in Cycles)
                        runningTotal = runningTotal + Cycle.Duration.TotalSeconds;

                    if (Cycles.Count() > 0)
                        averageSplits = runningTotal / Cycles.Count();

                    splitMonitorSummary.AverageSplit = Math.Round(averageSplits, 2);

                    //Percentile Split
                    if (SelectedPercentileSplit != null && Cycles.Count() > 2)
                    {
                        double percentileResult = 0;
                        var Percentile = Convert.ToDouble(SelectedPercentileSplit) / 100;
                        var setCount = Cycles.Count();


                        var PercentilIndex = Percentile * setCount;
                        if (PercentilIndex % 1 == 0)
                        {
                            percentileResult = Cycles.ElementAt(Convert.ToInt16(PercentilIndex) - 1).Duration
                                .TotalSeconds;
                        }
                        else
                        {
                            var indexMod = PercentilIndex % 1;
                            //subtracting .5 leaves just the integer after the convert.
                            //There was probably another way to do that, but this is easy.
                            int indexInt = Convert.ToInt16(PercentilIndex - .5);

                            var step1 = Cycles.ElementAt(Convert.ToInt16(indexInt) - 1).Duration.TotalSeconds;
                            var step2 = Cycles.ElementAt(Convert.ToInt16(indexInt)).Duration.TotalSeconds;
                            var stepDiff = step2 - step1;
                            var step3 = stepDiff * indexMod;
                            percentileResult = step1 + step3;
                        }
                        splitMonitorSummary.PercentileSplit = Math.Round(percentileResult, 2);
                    }
                }
                splitMonitorSummaries.Add(splitMonitorSummary);
            }
            return splitMonitorSummaries;
        }

        private void SetSplitMonitorStatistics(List<PlanSplitMonitor> plans, AnalysisPhase phase, Chart chart)
        {
            //find the phase Cycles that occur during the plan.
            foreach (var plan in plans)
            {
                var Cycles = from cycle in phase.Cycles.Items
                             where cycle.StartTime >= plan.StartTime && cycle.EndTime < plan.EndTime
                             orderby cycle.Duration
                             select cycle;

                // find % Skips
                if (ShowPercentSkip)
                    if (plan.CycleCount > 0)
                    {
                        double CycleCount = plan.CycleCount;
                        double SkippedPhases = plan.CycleCount - Cycles.Count();
                        double SkipPercent = 0;
                        if (CycleCount > 0)
                            SkipPercent = SkippedPhases / CycleCount;


                        var skipLabel = ChartTitleFactory.GetCustomLabelForTitle(
                            $"{SkipPercent:0.0%} Skips", plan.StartTime.ToOADate(),
                            plan.EndTime.ToOADate(), 1, Color.Black);

                        //new CustomLabel();
                        //skipLabel.FromPosition = plan.StartTime.ToOADate();
                        //skipLabel.ToPosition = plan.EndTime.ToOADate();
                        //skipLabel.Text = string.Format("{0:0.0%} Skips", SkipPercent);
                        //skipLabel.LabelMark = LabelMarkStyle.LineSideMark;
                        //skipLabel.ForeColor = Color.Black;
                        //skipLabel.RowIndex = 1;
                        chart.ChartAreas[0].AxisX2.CustomLabels.Add(skipLabel);
                    }

                // find % GapOuts
                if (ShowPercentGapOuts)
                {
                    var GapOuts = from cycle in Cycles
                                  where cycle.TerminationEvent == 4
                                  select cycle;

                    double CycleCount = plan.CycleCount;
                    double gapouts = GapOuts.Count();
                    double GapPercent = 0;
                    if (CycleCount > 0)
                        GapPercent = gapouts / CycleCount;


                    var gapLabel = new CustomLabel();
                    gapLabel.FromPosition = plan.StartTime.ToOADate();
                    gapLabel.ToPosition = plan.EndTime.ToOADate();
                    gapLabel.Text = string.Format("{0:0.0%} GapOuts", GapPercent);
                    gapLabel.LabelMark = LabelMarkStyle.LineSideMark;
                    gapLabel.ForeColor = Color.OliveDrab;
                    gapLabel.RowIndex = 2;
                    chart.ChartAreas[0].AxisX2.CustomLabels.Add(gapLabel);
                }

                //Set Max Out
                if (ShowPercentMaxOutForceOff && plan.PlanNumber == 254)
                {
                    var MaxOuts = from cycle in Cycles
                                  where cycle.TerminationEvent == 5
                                  select cycle;

                    double CycleCount = plan.CycleCount;
                    double maxouts = MaxOuts.Count();
                    double MaxPercent = 0;
                    if (CycleCount > 0)
                        MaxPercent = maxouts / CycleCount;


                    var maxLabel = new CustomLabel();
                    maxLabel.FromPosition = plan.StartTime.ToOADate();
                    maxLabel.ToPosition = plan.EndTime.ToOADate();
                    maxLabel.Text = string.Format("{0:0.0%} MaxOuts", MaxPercent);
                    maxLabel.LabelMark = LabelMarkStyle.LineSideMark;
                    maxLabel.ForeColor = Color.Red;
                    maxLabel.RowIndex = 3;
                    chart.ChartAreas[0].AxisX2.CustomLabels.Add(maxLabel);
                }

                // Set Force Off
                if (ShowPercentMaxOutForceOff && plan.PlanNumber != 254
                )
                {
                    var ForceOffs = from cycle in Cycles
                                    where cycle.TerminationEvent == 6
                                    select cycle;

                    double CycleCount = plan.CycleCount;
                    double forceoffs = ForceOffs.Count();
                    double ForcePercent = 0;
                    if (CycleCount > 0)
                        ForcePercent = forceoffs / CycleCount;


                    var forceLabel = new CustomLabel();
                    forceLabel.FromPosition = plan.StartTime.ToOADate();
                    forceLabel.ToPosition = plan.EndTime.ToOADate();
                    forceLabel.Text = string.Format("{0:0.0%} ForceOffs", ForcePercent);
                    forceLabel.LabelMark = LabelMarkStyle.LineSideMark;
                    forceLabel.ForeColor = Color.MediumBlue;
                    forceLabel.RowIndex = 3;
                    chart.ChartAreas[0].AxisX2.CustomLabels.Add(forceLabel);
                }

                //Average Split
                if (ShowAverageSplit)
                {
                    double runningTotal = 0;
                    double averageSplits = 0;
                    foreach (var Cycle in Cycles)
                        runningTotal = runningTotal + Cycle.Duration.TotalSeconds;

                    if (Cycles.Count() > 0)
                        averageSplits = runningTotal / Cycles.Count();


                    var avgLabel = new CustomLabel();
                    avgLabel.FromPosition = plan.StartTime.ToOADate();
                    avgLabel.ToPosition = plan.EndTime.ToOADate();
                    avgLabel.Text = string.Format("{0: 0.0} Avg. Split", averageSplits);
                    avgLabel.LabelMark = LabelMarkStyle.LineSideMark;
                    avgLabel.ForeColor = Color.Black;
                    avgLabel.RowIndex = 4;
                    chart.ChartAreas[0].AxisX2.CustomLabels.Add(avgLabel);

                    //Percentile Split
                    if (SelectedPercentileSplit != null && Cycles.Count() > 2)
                    {
                        double percentileResult = 0;
                        var Percentile = Convert.ToDouble(SelectedPercentileSplit) / 100;
                        var setCount = Cycles.Count();


                        var PercentilIndex = Percentile * setCount;
                        if (PercentilIndex % 1 == 0)
                        {
                            percentileResult = Cycles.ElementAt(Convert.ToInt16(PercentilIndex) - 1).Duration
                                .TotalSeconds;
                        }
                        else
                        {
                            var indexMod = PercentilIndex % 1;
                            //subtracting .5 leaves just the integer after the convert.
                            //There was probably another way to do that, but this is easy.
                            int indexInt = Convert.ToInt16(PercentilIndex - .5);

                            var step1 = Cycles.ElementAt(Convert.ToInt16(indexInt) - 1).Duration.TotalSeconds;
                            var step2 = Cycles.ElementAt(Convert.ToInt16(indexInt)).Duration.TotalSeconds;
                            var stepDiff = step2 - step1;
                            var step3 = stepDiff * indexMod;
                            percentileResult = step1 + step3;
                        }

                        var percentileLabel = new CustomLabel();
                        percentileLabel.FromPosition = plan.StartTime.ToOADate();
                        percentileLabel.ToPosition = plan.EndTime.ToOADate();
                        percentileLabel.Text = string.Format("{0: 0.0} - {1} Percentile Split", percentileResult,
                            Convert.ToDouble(SelectedPercentileSplit));
                        percentileLabel.LabelMark = LabelMarkStyle.LineSideMark;
                        percentileLabel.ForeColor = Color.Purple;
                        percentileLabel.RowIndex = 5;
                        chart.ChartAreas[0].AxisX2.CustomLabels.Add(percentileLabel);
                    }
                }
            }
        }
        
        private void SetSplitMonitorStats(List<PlanSplitMonitor> plans, AnalysisPhase phase, PlotlyObject chart)
        {
            //find the phase Cycles that occur during the plan.
            foreach (var plan in plans)
            {
                var startLocal = TimeZoneInfo.ConvertTime(plan.StartTime, Zone);
                var endLocal = TimeZoneInfo.ConvertTime(plan.EndTime, Zone);
                var heightAdd = .03;
                var heightMult = 1;
                var annoFontSize = 12;
                var defaultY = new Annotation().Y;
                var Cycles = from cycle in phase.Cycles.Items
                             where cycle.StartTime >= plan.StartTime && cycle.EndTime < plan.EndTime
                             orderby cycle.Duration
                             select cycle;
                var midpoint = new DateTime((startLocal.Ticks + endLocal.Ticks) / 2, plan.StartTime.Kind);

                // find % Skips
                if (ShowPercentSkip)
                    if (plan.CycleCount > 0)
                    {
                        double CycleCount = plan.CycleCount;
                        double SkippedPhases = plan.CycleCount - Cycles.Count();
                        double SkipPercent = 0;
                        if (CycleCount > 0)
                            SkipPercent = SkippedPhases / CycleCount;
                        
                        var skipText = $"{SkipPercent:0.0%} Skips";
                        
                        chart.Layout.Annotations.Add(new Annotation
                        {
                            Text = skipText,
                            Font =
                            {
                                Size = annoFontSize,
                                Color = "gainsboro"
                            },
                            Y = defaultY + (heightMult * heightAdd),
                            ShowArrow = false,
                            X = midpoint.ToString("o")

                        });
                        heightMult++;
                    }

                // find % GapOuts
                if (ShowPercentGapOuts)
                {
                    var GapOuts = from cycle in Cycles
                                  where cycle.TerminationEvent == 4
                                  select cycle;

                    double CycleCount = plan.CycleCount;
                    double gapouts = GapOuts.Count();
                    double GapPercent = 0;
                    if (CycleCount > 0)
                        GapPercent = gapouts / CycleCount;

                    var gapText = string.Format("{0:0.0%} GapOuts", GapPercent);

                    chart.Layout.Annotations.Add(new Annotation
                    {
                        Text = gapText,
                        Font =
                        {
                            Size = annoFontSize,
                            Color = "olivedrab"
                        },
                        Y = defaultY + (heightMult * heightAdd),
                        ShowArrow = false,
                        X = midpoint.ToString("o")
                    });
                    heightMult++;

                }

                //Set Max Out
                if (ShowPercentMaxOutForceOff && plan.PlanNumber == 254)
                {
                    var MaxOuts = from cycle in Cycles
                                  where cycle.TerminationEvent == 5
                                  select cycle;

                    double CycleCount = plan.CycleCount;
                    double maxouts = MaxOuts.Count();
                    double MaxPercent = 0;
                    if (CycleCount > 0)
                        MaxPercent = maxouts / CycleCount;

                    var maxText = string.Format("{0:0.0%} MaxOuts", MaxPercent);


                    chart.Layout.Annotations.Add(new Annotation
                    {
                        Font =
                        {
                            Size = annoFontSize,
                            Color = "red"
                        },
                        Y = defaultY + (heightMult * heightAdd),
                        Text = maxText,
                        ShowArrow = false,
                        X = midpoint.ToString("o")
                    });
                    heightMult++;
                }

                // Set Force Off
                if (ShowPercentMaxOutForceOff && plan.PlanNumber != 254
                )
                {
                    var ForceOffs = from cycle in Cycles
                                    where cycle.TerminationEvent == 6
                                    select cycle;

                    double CycleCount = plan.CycleCount;
                    double forceoffs = ForceOffs.Count();
                    double ForcePercent = 0;
                    if (CycleCount > 0)
                        ForcePercent = forceoffs / CycleCount;


                    var forceText = string.Format("{0:0.0%} ForceOffs", ForcePercent);

                    chart.Layout.Annotations.Add(new Annotation
                    {
                        Font =
                        {
                            Size = annoFontSize,
                            Color = "lightblue"
                        },
                        Y = defaultY + (heightMult * heightAdd),
                        Text = forceText,
                        ShowArrow = false,
                        X = midpoint.ToString("o")
                    });
                    heightMult++;
                }

                //Average Split
                if (ShowAverageSplit)
                {
                    double runningTotal = 0;
                    double averageSplits = 0;
                    foreach (var Cycle in Cycles)
                        runningTotal = runningTotal + Cycle.Duration.TotalSeconds;

                    if (Cycles.Count() > 0)
                        averageSplits = runningTotal / Cycles.Count();

                    var avgText = string.Format("{0: 0.0} Avg. Split", averageSplits);
                    chart.Layout.Annotations.Add(new Annotation
                    {
                        Text = avgText,
                        Font =
                        {
                            Size = annoFontSize,
                            Color = "gainsboro"
                        },
                        ShowArrow = false,
                        Y = defaultY + (heightMult * heightAdd),
                        X = midpoint.ToString("o")
                    });
                    heightMult++;
                    //chart.ChartAreas[0].AxisX2.CustomLabels.Add(avgLabel);

                    //Percentile Split
                    if (SelectedPercentileSplit != null && Cycles.Count() > 2)
                    {
                        double percentileResult = 0;
                        var Percentile = Convert.ToDouble(SelectedPercentileSplit) / 100;
                        var setCount = Cycles.Count();


                        var PercentilIndex = Percentile * setCount;
                        if (PercentilIndex % 1 == 0)
                        {
                            percentileResult = Cycles.ElementAt(Convert.ToInt16(PercentilIndex) - 1).Duration
                                .TotalSeconds;
                        }
                        else
                        {
                            var indexMod = PercentilIndex % 1;
                            //subtracting .5 leaves just the integer after the convert.
                            //There was probably another way to do that, but this is easy.
                            int indexInt = Convert.ToInt16(PercentilIndex - .5);

                            var step1 = Cycles.ElementAt(Convert.ToInt16(indexInt) - 1).Duration.TotalSeconds;
                            var step2 = Cycles.ElementAt(Convert.ToInt16(indexInt)).Duration.TotalSeconds;
                            var stepDiff = step2 - step1;
                            var step3 = stepDiff * indexMod;
                            percentileResult = step1 + step3;
                        }


                        var percText = string.Format("{0: 0.0} - {1} Percentile Split", percentileResult,
                            Convert.ToDouble(SelectedPercentileSplit));

                        chart.Layout.Annotations.Add(new Annotation
                        {
                            Font = { Size = annoFontSize,Color = "plum" },
                            Text = percText,
                            ShowArrow = false,
                            X = midpoint.ToString("o"),
                            Y = defaultY + (heightMult * heightAdd),
                        });
                        heightMult++;
                        //chart.ChartAreas[0].AxisX2.CustomLabels.Add(percentileLabel);
                    }
                }
            }
        }


        private void SetSimplePlanStrips(List<PlanSplitMonitor> plans, Chart chart, DateTime graphStartDate)
        {
            var backGroundColor = 1;
            foreach (var plan in plans)
            {
                var stripline = new StripLine();
                //Creates alternating backcolor to distinguish the plans
                if (backGroundColor % 2 == 0)
                    stripline.BackColor = Color.FromArgb(120, Color.LightGray);
                else
                    stripline.BackColor = Color.FromArgb(120, Color.LightBlue);

                //Set the stripline properties
                stripline.IntervalOffsetType = DateTimeIntervalType.Hours;
                stripline.Interval = 1;
                stripline.IntervalOffset = (plan.StartTime - graphStartDate).TotalHours;
                stripline.StripWidth = (plan.EndTime - plan.StartTime).TotalHours;
                stripline.StripWidthType = DateTimeIntervalType.Hours;

                chart.ChartAreas["ChartArea1"].AxisX.StripLines.Add(stripline);

                //Add a corrisponding custom label for each strip
                var Plannumberlabel = new CustomLabel();
                Plannumberlabel.FromPosition = plan.StartTime.ToOADate();
                Plannumberlabel.ToPosition = plan.EndTime.ToOADate();
                switch (plan.PlanNumber)
                {
                    case 254:
                        Plannumberlabel.Text = "Free";
                        break;
                    case 255:
                        Plannumberlabel.Text = "Flash";
                        break;
                    case 0:
                        Plannumberlabel.Text = "Unknown";
                        break;
                    default:
                        Plannumberlabel.Text = "Plan " + plan.PlanNumber;

                        break;
                }
                Plannumberlabel.LabelMark = LabelMarkStyle.LineSideMark;
                Plannumberlabel.ForeColor = Color.Black;
                Plannumberlabel.RowIndex = 6;


                chart.ChartAreas["ChartArea1"].AxisX2.CustomLabels.Add(Plannumberlabel);


                backGroundColor++;
            }
        }

        private Chart GetNewSplitMonitorChart(DateTime graphStartDate, DateTime graphEndDate, int phase)
        {
            var chart = ChartFactory.CreateDefaultChartNoX2Axis(this);
            //chart.ChartAreas[0].AxisY.Interval = 5;

            //Set the chart properties
            ChartFactory.SetImageProperties(chart);
            chart.BorderlineDashStyle = ChartDashStyle.Dot;
            SetChartTitle(chart, phase);

            chart.ChartAreas[0].AxisY.Title = "Phase Duration (Seconds)";
            chart.ChartAreas[0].AxisY.MajorGrid.LineDashStyle = ChartDashStyle.Dot;

            var PedActivity = new Series();
            PedActivity.ChartType = SeriesChartType.Point;
            PedActivity.Color = Color.Transparent;
            PedActivity.Name = "PedActivity";
            PedActivity.MarkerStyle = MarkerStyle.Circle;
            PedActivity.MarkerBorderColor = Color.Orange;
            PedActivity.MarkerBorderWidth = 1;
            PedActivity.MarkerSize = 3;

            var GapoutSeries = new Series();
            GapoutSeries.ChartType = SeriesChartType.Point;
            GapoutSeries.Color = Color.OliveDrab;
            GapoutSeries.Name = "GapOut";
            GapoutSeries.XValueType = ChartValueType.DateTime;
            GapoutSeries.MarkerStyle = MarkerStyle.Circle;
            GapoutSeries.MarkerSize = 3;

            var MaxOutSeries = new Series();
            MaxOutSeries.ChartType = SeriesChartType.Point;
            MaxOutSeries.Color = Color.Red;
            MaxOutSeries.Name = "MaxOut";
            MaxOutSeries.XValueType = ChartValueType.DateTime;
            MaxOutSeries.MarkerStyle = MarkerStyle.Circle;
            MaxOutSeries.MarkerSize = 3;

            var ForceOffSeries = new Series();
            ForceOffSeries.ChartType = SeriesChartType.Point;
            ForceOffSeries.Color = Color.MediumBlue;
            ForceOffSeries.Name = "ForceOff";
            ForceOffSeries.MarkerStyle = MarkerStyle.Circle;
            ForceOffSeries.MarkerSize = 3;

            var UnknownSeries = new Series();
            UnknownSeries.ChartType = SeriesChartType.Point;
            UnknownSeries.Color = Color.Black;
            UnknownSeries.Name = "Unknown";
            UnknownSeries.MarkerStyle = MarkerStyle.Circle;
            UnknownSeries.MarkerSize = 3;

            var ProgramedSplit = new Series();
            ProgramedSplit.ChartType = SeriesChartType.StepLine;
            ProgramedSplit.Color = Color.OrangeRed;
            ProgramedSplit.Name = "Programed Split";
            ProgramedSplit.BorderWidth = 1;


            //Add the Posts series to ensure the chart is the size of the selected timespan
            //var testSeries = new Series();
            //testSeries.IsVisibleInLegend = false;
            //testSeries.ChartType = SeriesChartType.Point;
            //testSeries.Color = Color.gainsboro;
            //testSeries.Name = "Posts";
            //testSeries.XValueType = ChartValueType.DateTime;

            chart.Series.Add(ProgramedSplit);
            chart.Series.Add(GapoutSeries);
            chart.Series.Add(MaxOutSeries);
            chart.Series.Add(ForceOffSeries);
            chart.Series.Add(UnknownSeries);
            chart.Series.Add(PedActivity);
            //chart.Series.Add(testSeries);

            //Add points at the start and and of the x axis to ensure
            //the graph covers the entire period selected by the user
            //whether there is data or not
            //chart.Series["Posts"].Points.AddXY(graphStartDate, 0);
            //chart.Series["Posts"].Points.AddXY(graphEndDate.AddMinutes(5), 0);


            return chart;
        }

        private void SetChartTitle(Chart chart, int phase)
        {
            chart.Titles.Add(ChartTitleFactory.GetChartName(MetricTypeID));
            chart.Titles.Add(ChartTitleFactory.GetSignalLocationAndDateRange(SignalID, StartDate, EndDate));
            chart.Titles.Add(ChartTitleFactory.GetPhase(phase));
        }

        private void AddSplitMonitorDataToChart(Chart chart, AnalysisPhase phase, List<PlanSplitMonitor> plans)
        {
            //Table 
            if (phase.Cycles.Items.Count > 0)
            {
                var maxSplitLength = 0;
                foreach (var plan in plans)
                {
                    var highestSplit = plan.FindHighestRecordedSplitPhase();
                    plan.FillMissingSplits(highestSplit);
                    try
                    {
                        chart.Series["Programed Split"].Points.AddXY(plan.StartTime, plan.Splits[phase.PhaseNumber]);
                        chart.Series["Programed Split"].Points.AddXY(plan.EndTime, plan.Splits[phase.PhaseNumber]);
                        if (plan.Splits[phase.PhaseNumber] > maxSplitLength)
                            maxSplitLength = plan.Splits[phase.PhaseNumber];
                    }
                    catch
                    {
                        //System.Windows.MessageBox.Show(ex.ToString());
                    }
                }
                foreach (var Cycle in phase.Cycles.Items)
                {
                    if (Cycle.TerminationEvent == 4)
                        chart.Series["GapOut"].Points.AddXY(Cycle.StartTime, Cycle.Duration.TotalSeconds);
                    if (Cycle.TerminationEvent == 5)
                        chart.Series["MaxOut"].Points.AddXY(Cycle.StartTime, Cycle.Duration.TotalSeconds);
                    if (Cycle.TerminationEvent == 6)
                        chart.Series["ForceOff"].Points.AddXY(Cycle.StartTime, Cycle.Duration.TotalSeconds);
                    if (Cycle.TerminationEvent == 0)
                        chart.Series["Unknown"].Points.AddXY(Cycle.StartTime, Cycle.Duration.TotalSeconds);
                    if (Cycle.HasPed && ShowPedActivity)
                    {
                        if (Cycle.PedDuration == 0)
                        {
                            if (Cycle.PedStartTime == DateTime.MinValue)
                                Cycle.SetPedStart(Cycle.StartTime);
                            if (Cycle.PedEndTime == DateTime.MinValue)
                                Cycle.SetPedEnd(Cycle.YellowEvent);
                        }
                        chart.Series["PedActivity"].Points.AddXY(Cycle.PedStartTime, Cycle.PedDuration);
                    }
                }
                //SetYAxisMaxAndInterval(chart, phase, maxSplitLength);
            }
        }

        private void SetYAxisMaxAndInterval(PlotlyObject chart, AnalysisPhase phase, int maxSplitLength)
        {
            
            if (YAxisMax.Value == 0)
            {
                chart.Layout.YAxis.Range.Add( null);
                chart.Layout.YAxis.Range.Add( null);
                return;
            }
            chart.Layout.YAxis.Range.Add( "0");
            if (YAxisMax != null)
            {
                chart.Layout.YAxis.Range.Add(YAxisMax.Value.ToString("N0"));
            }
            else if (maxSplitLength > 0)
            {
                if (maxSplitLength >= 50)
                    chart.Layout.YAxis.Range.Add((1.5 * maxSplitLength).ToString("N0"));
                else
                    chart.Layout.YAxis.Range.Add((2.5 * maxSplitLength).ToString("N0"));
            }
            else
            {
                chart.Layout.YAxis.Range.Add(phase.Cycles.Items.Max(c => c.Duration).Seconds.ToString());
            }
            if (int.Parse(chart.Layout.YAxis.Range[1]) <= 50)
            {
                chart.Layout.YAxis.DTick = 10;
            }
            else if (int.Parse(chart.Layout.YAxis.Range[1]) > 50 && int.Parse(chart.Layout.YAxis.Range[1]) <= 200)
            {
                chart.Layout.YAxis.DTick = 20;
            }
            else
            {
                chart.Layout.YAxis.DTick = 50;
            }
        }
    }
}