using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Web.UI.DataVisualization.Charting;
using System.Linq;
using MOE.Common.Business.WCFServiceLibrary;
using MOE.Common.Models;

namespace MOE.Common.Business.PEDDelay
{
    public class PEDDelayChart
    {
        public Chart Chart;
        private readonly PedPhase PedPhase;
        private readonly List<RedToRedCycle> RedToRedCycles;
        private Dictionary<DateTime, double> DelayByCycleLengthStepChart;
        private readonly PedDelayOptions Options;

        public PEDDelayChart(PedDelayOptions options,
            PedPhase pedPhase, List<RedToRedCycle> redToRedCycles)
        {
            if (options.ShowPercentDelay)
            {
                Chart = ChartFactory.CreateDefaultChartNoX2Axis(options);
            }
            else
            {
                Chart = ChartFactory.CreateDefaultChartNoX2AxisNoY2Axis(options);
            }
            PedPhase = pedPhase;
            Options = options;
            RedToRedCycles = redToRedCycles;

            //Set the chart properties
            ChartFactory.SetImageProperties(Chart);


            SetChartTitle(Chart, pedPhase, options);

            //Create the chart legend
            var chartLegend = new Legend();
            chartLegend.Name = "MainLegend";
            chartLegend.Docking = Docking.Left;
            Chart.Legends.Add(chartLegend);


            //Create the chart area
            //var chartArea = new ChartArea();
            Chart.ChartAreas[0].AxisY.Title = "Pedestrian Delay per Ped Requests(seconds)";
            Chart.ChartAreas[0].AxisY.IntervalType = (int)IntervalType.Number;
            Chart.ChartAreas[0].AxisY.Minimum = 0;
            Chart.ChartAreas[0].AxisY.Interval = 30;
            Chart.ChartAreas[0].AxisX.MajorGrid.LineColor = Color.LightGray;
            Chart.ChartAreas[0].AxisY.MajorGrid.LineColor = Color.LightGray;
            if (options.YAxisMax != null)
                Chart.ChartAreas[0].AxisY.Maximum = options.YAxisMax.Value;

            Chart.ChartAreas[0].AxisY2.Title = "% Delay by Cycle Length";
            Chart.ChartAreas[0].AxisY2.IntervalType = (int)IntervalType.Number;
            Chart.ChartAreas[0].AxisY2.Interval = 20;
            Chart.ChartAreas[0].AxisY2.Maximum = 100;

            //Add the point series
            var CycleLength = new Series();
            CycleLength.ChartType = SeriesChartType.Line;
            CycleLength.Color = Color.Red;
            CycleLength.Name = "Cycle Length";
            CycleLength.XValueType = ChartValueType.DateTime;
            Chart.Series.Add(CycleLength);

            var PedestrianDelay = new Series();
            PedestrianDelay.ChartType = SeriesChartType.Column;
            PedestrianDelay.Color = Color.Blue;
            PedestrianDelay.Name = "Pedestrian Delay per Ped Requests";
            PedestrianDelay.XValueType = ChartValueType.DateTime;
            Chart.Series.Add(PedestrianDelay);
            Chart.Series["Pedestrian Delay per Ped Requests"]["PixelPointWidth"] = "2";

            var PedWalk = new Series();
            PedWalk.ChartType = SeriesChartType.Point;
            PedWalk.MarkerStyle = MarkerStyle.Circle;
            PedWalk.MarkerColor = Color.Orange;
            PedWalk.Name = "Start of Begin Walk";
            PedWalk.XValueType = ChartValueType.DateTime;
            PedWalk.MarkerSize = 5;
            Chart.Series.Add(PedWalk);

            var DelayByCycleLength = new Series();
            DelayByCycleLength.ChartType = SeriesChartType.StepLine;
            DelayByCycleLength.BorderDashStyle = ChartDashStyle.Dash;
            DelayByCycleLength.Color = Color.FromArgb(51, 153, 255);
            DelayByCycleLength.BorderWidth = 1;
            DelayByCycleLength.Name = "% Delay By Cycle Length";
            DelayByCycleLength.YAxisType = AxisType.Secondary;
            Chart.Series.Add(DelayByCycleLength);

            AddDataToChart();
            SetPlanStrips();
        }

        public PEDDelayChart(PedDelayOptions options, PlotlyObject pedDelayChart, PedPhase pedPhase, List<RedToRedCycle> redCycles, TimeZoneInfo tz)
        {
            // ----- Layout: title, legend, margins -----
            var layout = pedDelayChart.Layout;

            layout.Title.Text = $"Pedestrian Delay — Phase {pedPhase?.PhaseNumber}";
            layout.Title.Pad.T = Math.Max(layout.Title.Pad.T, 10);
            layout.Title.Pad.B = Math.Max(layout.Title.Pad.B, 10);

            layout.Legend.Orientation = "v";
            layout.Legend.X = 0;
            layout.Legend.XAnchor = "left";
            layout.Legend.Y = 1;

            layout.Margin.L = Math.Max(layout.Margin.L, 90);
            layout.Margin.T = Math.Max(layout.Margin.T, 80);

            // ----- Axes -----
            // X (time)
            layout.XAxis.Type = "date";
            layout.XAxis.TickFormat = "%-m/%-d %-I:%M %p";
            layout.XAxis.AutoMargin = true;

            // Y (seconds)
            layout.YAxis.Title.Text = "Pedestrian Delay per Ped Requests (seconds)";
            layout.YAxis.Title.Standoff = Math.Max(layout.YAxis.Title.Standoff, 50);
            layout.YAxis.AutoMargin = true;
            layout.YAxis.DTick = 30;
            layout.YAxis.Range.Clear();
            layout.YAxis.Range.Add("0");
            if (options?.YAxisMax != null)
            {
                layout.YAxis.Range.Add(options.YAxisMax.Value.ToString(CultureInfo.InvariantCulture));
            }

            // Y2 (%)
            layout.YAxis2.Title.Text = "% Delay by Cycle Length";
            layout.YAxis2.AutoMargin = true;
            layout.YAxis2.DTick = 20;
            // If your Axis supports these (recommended for y2 on the right):
            layout.YAxis2.Overlaying = "y";
            layout.YAxis2.Side = "right";

            // (Optional) light gray grid like your WinForms chart (add GridColor to Axis if needed)
            // layout.XAxis.GridColor = "#D3D3D3";
            // layout.YAxis.GridColor = "#D3D3D3";

            // ----- Traces -----

            pedDelayChart.Data.AddRange(AddData()); 
            // ----- Populate data -----

            // If you previously had plan strips/annotations, add them here:
            // pedDelayChart.Layout.Shapes.Add(...);
            // pedDelayChart.Layout.Annotations.Add(...);
        }
        private void SetChartTitle(Chart chart, PedPhase pedPhase, PedDelayOptions options)
        {
            chart.Titles.Add(ChartTitleFactory.GetChartName(options.MetricTypeID));
            chart.Titles.Add(ChartTitleFactory.GetSignalLocationAndDateRange(
                options.SignalID, options.StartDate, options.EndDate));
            chart.Titles.Add(ChartTitleFactory.GetPhase(pedPhase.PhaseNumber));
            var statistics = new Dictionary<string, string>();
            statistics.Add("Ped Presses(PP)", pedPhase.PedPresses.ToString());
            statistics.Add("Cycles With Ped Requests(CPR)", pedPhase.Plans.Sum(p => p.CyclesWithPedRequests).ToString());
            statistics.Add("Time Buffered " + pedPhase.TimeBuffer + "s Presses(TBP)", pedPhase.UniquePedDetections.ToString());
            statistics.Add("Min Delay", Math.Round(pedPhase.MinDelay) + "s");
            statistics.Add("Max Delay", Math.Round(pedPhase.MaxDelay) + "s");
            statistics.Add("Average Delay(AD)", Math.Round(pedPhase.AverageDelay) + "s");
            chart.Titles.Add(ChartTitleFactory.GetStatistics(statistics));
        }


        protected void AddDataToChart()
        {
            int currentRedToRedCycle = 0;
            var delayByCycleLengthDataPoints = new Dictionary<PedCycle, double>();

            foreach (var pedPlan in PedPhase.Plans)
            {
                foreach (var pedCycle in pedPlan.Cycles)
                {
                    Chart.Series["Pedestrian Delay per Ped Requests"].Points
                    .AddXY(pedCycle.BeginWalk, pedCycle.Delay);

                    if (Options.ShowPedBeginWalk)
                    {
                        Chart.Series["Start of Begin Walk"].Points
                            .AddXY(pedCycle.BeginWalk, pedCycle.Delay); //add ped walk to top of delay
                    }

                    if (Options.ShowPercentDelay)
                    {
                        AddDelayByCycleLengthDataPoint(pedCycle, ref currentRedToRedCycle, delayByCycleLengthDataPoints);
                    }
                }
            }

            if (Options.ShowCycleLength)
            {
                foreach (var cycle in RedToRedCycles)
                {
                    Chart.Series["Cycle Length"].Points.AddXY(cycle.EndTime, cycle.RedLineY);
                }
            }

            if (Options.ShowPedBeginWalk)
            {
                foreach (var e in PedPhase.PedBeginWalkEvents)
                {
                    Chart.Series["Start of Begin Walk"].Points
                            .AddXY(e.Timestamp, 0);
                }
            }

            if (Options.ShowPercentDelay)
            {
                CreateDelayByCycleLengthStepChart(delayByCycleLengthDataPoints);
                foreach (var cycle in DelayByCycleLengthStepChart)
                {
                    Chart.Series["% Delay By Cycle Length"].Points
                                    .AddXY(cycle.Key, cycle.Value);
                }
            }
        }

        public List<Trace> AddData()
        {
            var allTraces = new List<Trace>();
            int currentRedToRedCycle = 0;
            var delayByCycleLengthDataPoints = new Dictionary<PedCycle, double>();
            var cycleLength = new Trace("Cycle Length")
            {
                Type = "scatter",
                Mode = "lines"
            };
            cycleLength.Line.Color = "red";

            var pedDelay = new Trace("Pedestrian Delay per Ped Requests")
            {
                Type = "bar",
                Mode = "markers" // ignored for bars
            };
            pedDelay.Marker.Color = "blue";

            var pedWalk = new Trace("Start of Begin Walk")
            {
                Type = "scatter",
                Mode = "markers"
            };
            pedWalk.Marker.Color = "orange";
            pedWalk.Marker.Size = 5;

            var pctDelay = new Trace("% Delay By Cycle Length")
            {
                Type = "scatter",
                Mode = "lines",
                YAxisRef = "y2" // <- uses the secondary axis
            };
            pctDelay.Line.Color = "rgba(51,153,255,1)"; // #3399FF
            pctDelay.Line.Width = 1;
            pctDelay.Line.Dash = "dash";
            pctDelay.Line.Shape = "hv"; // step-like

            foreach (var pedPlan in PedPhase.Plans)
            {
                foreach (var pedCycle in pedPlan.Cycles)
                {
                    pedDelay.AddXY(pedCycle.BeginWalk, pedCycle.Delay);

                    if (Options.ShowPedBeginWalk)
                    {
                        pedWalk.AddXY(pedCycle.BeginWalk, pedCycle.Delay); //add ped walk to top of delay
                    }

                    if (Options.ShowPercentDelay)
                    {
                        AddDelayByCycleLengthDataPoint(pedCycle, ref currentRedToRedCycle, delayByCycleLengthDataPoints);
                    }
                }
            }

            if (Options.ShowCycleLength)
            {
                foreach (var cycle in RedToRedCycles)
                {
                    cycleLength.AddXY(cycle.EndTime, cycle.RedLineY);
                }
            }

            if (Options.ShowPedBeginWalk)
            {
                foreach (var e in PedPhase.PedBeginWalkEvents)
                {
                    pedWalk.AddXY(e.Timestamp, 0);
                }
            }

            if (Options.ShowPercentDelay)
            {
                CreateDelayByCycleLengthStepChart(delayByCycleLengthDataPoints);
                foreach (var cycle in DelayByCycleLengthStepChart)
                {
                    pctDelay.AddXY(cycle.Key, cycle.Value);
                }
            }
            allTraces.Add(cycleLength);
            allTraces.Add(pedDelay);
            allTraces.Add(pedWalk);
            allTraces.Add(pctDelay);
            allTraces = allTraces.Where(t => t.X.Any() && t.Y.Any()).ToList();
            return allTraces;
        }

        protected void AddDelayByCycleLengthDataPoint(PedCycle pc, ref int currentRedToRedCycle, 
            Dictionary<PedCycle, double> delayByCycleLengthDataPoints)
        {
            while (currentRedToRedCycle < RedToRedCycles.Count)
            {
                if (RedToRedCycles[currentRedToRedCycle].EndTime > pc.BeginWalk)
                {
                    double cycle1;
                    if (currentRedToRedCycle > 0)
                    {
                        cycle1 = RedToRedCycles[currentRedToRedCycle - 1].RedLineY;
                    }
                    else
                    {
                        cycle1 = RedToRedCycles[currentRedToRedCycle].RedLineY;
                    }

                    var cycle2 = RedToRedCycles[currentRedToRedCycle].RedLineY;
                    var average = (cycle1 + cycle2) / 2;
                    delayByCycleLengthDataPoints.Add(pc, pc.Delay / average * 100);
                    break;
                }
                currentRedToRedCycle++;
            }
        }

        protected void CreateDelayByCycleLengthStepChart(Dictionary<PedCycle, double> delayByCycleLengthDataPoints)
        {
            DelayByCycleLengthStepChart = new Dictionary<DateTime, double>();
            var startTime = PedPhase.StartDate;
            while (startTime <= PedPhase.EndDate)
            {
                var endTime = startTime.AddMinutes(30);
                var cycles = delayByCycleLengthDataPoints.Where(c => c.Key.BeginWalk >= startTime && c.Key.BeginWalk < endTime).ToList();
                double average = 0;
                if (cycles.Count > 0)
                {
                    average = cycles.Average(c => c.Value);
                }
                DelayByCycleLengthStepChart.Add(startTime, average);
                startTime = startTime.AddMinutes(30);
            }
        }


        protected void SetPlanStrips()
        {
            var backGroundColor = 1;
            foreach (var plan in PedPhase.Plans)
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
                stripline.IntervalOffset = (plan.StartDate - PedPhase.StartDate).TotalHours;
                stripline.StripWidth = (plan.EndDate - plan.StartDate).TotalHours;
                stripline.StripWidthType = DateTimeIntervalType.Hours;

                Chart.ChartAreas["ChartArea1"].AxisX.StripLines.Add(stripline);
                Chart.ChartAreas["ChartArea1"].AxisX2.LabelAutoFitStyle = LabelAutoFitStyles.DecreaseFont;

                //Add a corresponding custom label for each strip

                if (Options.ShowPedRecall)
                {
                    var vehicleCycles = RedToRedCycles.Where(r => r.StartTime >= plan.StartDate && r.EndTime < plan.EndDate).ToList();
                    if (vehicleCycles.Count > 0 && ((double)plan.PedBeginWalkCount / (double)vehicleCycles.Count * 100 >= Options.PedRecallThreshold))
                    {
                        var pedRecallLabel = new CustomLabel();
                        pedRecallLabel.FromPosition = plan.StartDate.ToOADate();
                        pedRecallLabel.ToPosition = plan.EndDate.ToOADate();
                        pedRecallLabel.LabelMark = LabelMarkStyle.LineSideMark;
                        pedRecallLabel.Text = "Ped Recall On";
                        pedRecallLabel.RowIndex = 6;
                        Chart.ChartAreas["ChartArea1"].AxisX2.CustomLabels.Add(pedRecallLabel);
                    }
                }

                var Plannumberlabel = new CustomLabel();
                Plannumberlabel.FromPosition = plan.StartDate.ToOADate();
                Plannumberlabel.ToPosition = plan.EndDate.ToOADate();
                Plannumberlabel.LabelMark = LabelMarkStyle.LineSideMark;
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
                Plannumberlabel.RowIndex = 5;
                Chart.ChartAreas["ChartArea1"].AxisX2.CustomLabels.Add(Plannumberlabel);

                var pedPressesLabel = new CustomLabel();
                pedPressesLabel.FromPosition = plan.StartDate.ToOADate();
                pedPressesLabel.ToPosition = plan.EndDate.ToOADate();
                pedPressesLabel.Text = plan.CyclesWithPedRequests + " CPR";
                pedPressesLabel.LabelMark = LabelMarkStyle.LineSideMark;
                pedPressesLabel.RowIndex = 4;
                Chart.ChartAreas["ChartArea1"].AxisX2.CustomLabels.Add(pedPressesLabel);

                var timeBufferedPressesLabel = new CustomLabel();
                timeBufferedPressesLabel.FromPosition = plan.StartDate.ToOADate();
                timeBufferedPressesLabel.ToPosition = plan.EndDate.ToOADate();
                timeBufferedPressesLabel.Text = plan.UniquePedDetections + " TBP";
                timeBufferedPressesLabel.RowIndex = 3;
                timeBufferedPressesLabel.LabelMark = LabelMarkStyle.LineSideMark;
                Chart.ChartAreas["ChartArea1"].AxisX2.CustomLabels.Add(timeBufferedPressesLabel);

                var avgDelayLabel = new CustomLabel();
                avgDelayLabel.FromPosition = plan.StartDate.ToOADate();
                avgDelayLabel.ToPosition = plan.EndDate.ToOADate();
                avgDelayLabel.Text = Math.Round(plan.AvgDelay, 2) + " AD";
                avgDelayLabel.RowIndex = 2;
                avgDelayLabel.LabelMark = LabelMarkStyle.LineSideMark;
                Chart.ChartAreas["ChartArea1"].AxisX2.CustomLabels.Add(avgDelayLabel);

                if (Options.ShowCycleLength)
                {
                    var cycleLengthLabel = new CustomLabel();
                    cycleLengthLabel.FromPosition = plan.StartDate.ToOADate();
                    cycleLengthLabel.ToPosition = plan.EndDate.ToOADate();
                    cycleLengthLabel.LabelMark = LabelMarkStyle.LineSideMark;
                    cycleLengthLabel.RowIndex = 1;
                    if (RedToRedCycles.Count > 0)
                    {
                        var cycles = RedToRedCycles.Where(r => r.StartTime >= plan.StartDate && r.EndTime < plan.EndDate).ToList();
                        cycleLengthLabel.Text = "avg CL: " + (cycles.Count > 0 ? Math.Round(cycles.Average(r => r.RedLineY)) + "s" : "");                      
                    }
                    else
                    {
                        cycleLengthLabel.Text = "No Cycles Found";
                    }
                    Chart.ChartAreas["ChartArea1"].AxisX2.CustomLabels.Add(cycleLengthLabel);
                }

                //Change the background color counter for alternating color
                backGroundColor++;
            }
        }


    }
}