using MOE.Common.Business.PEDDelay;
using MOE.Common.Models;
using MOE.Common.Models.Repositories;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Policy;
using System.Text.Json;

namespace MOE.Common.Business.WCFServiceLibrary
{
    [DataContract]
    public class PedDelayOptions : MetricOptions
    {
        public PedDelayOptions(string signalId, double? yAxisMax, DateTime startDate, DateTime endDate, int timeBuffer,
            bool showPedBeginWalk, bool showCycleLength, bool showPercentDelay, bool showPedRecall,
            int pedRecallThreshold, string zone, int storageLoc = 0, string connString = "")
        {
            SignalID = signalId;
            StartDate = startDate;
            EndDate = endDate;
            Zone = TimeZoneInfo.FindSystemTimeZoneById(zone);
            TimeBuffer = timeBuffer;
            ShowPedBeginWalk = showPedBeginWalk;
            ShowCycleLength = showCycleLength;
            ShowPercentDelay = showPercentDelay;
            ShowPedRecall = showPedRecall;
            PedRecallThreshold = pedRecallThreshold;
            YAxisMax = yAxisMax;
            Settings = new MOEService(storageLoc, connString);
        }

        public PedDelayOptions()
        {
            Y2AxisMax = 10;
            SetDefaults();
        }

        private readonly PedPhase PedPhase;
        private readonly List<RedToRedCycle> RedToRedCycles;
        private Dictionary<DateTime, double> DelayByCycleLengthStepChart;
        private readonly PedDelayOptions Options;

        public TimeZoneInfo Zone { get; set; } // set this from ctor/options

        [DataMember]
        [Display(Name = "Time Buffer Between Unique Ped Detections")]
        public int TimeBuffer { get; set; }



        [DataMember]
        [Display(Name = "Show Ped Begin Walk")]
        public bool ShowPedBeginWalk { get; set; }

        [DataMember]
        [Display(Name = "Show Cycle Length")]
        public bool ShowCycleLength { get; set; }

        [DataMember]
        [Display(Name = "Show Percent Delay")]
        public bool ShowPercentDelay { get; set; }

        [DataMember]
        [Display(Name = "Show Ped Recall")]
        public bool ShowPedRecall { get; set; }

        [DataMember]
        [Display(Name = "Ped Recall Threshold (Percent)")]
        public int PedRecallThreshold { get; set; }

        public PlotlyObject PedDelayPlotlyObject { get; set; }

        public MOEService Settings { get; set; }
        public List<PlotlyObject> PedDelayCharts { get; set; }


        public override List<string> CreateMetric()
        {
            base.CreateMetric();
            var signalRepository = SignalsRepositoryFactory.Create();
            ATSPM_Signals atspmSignals = signalRepository.GetVersionOfSignalByDate(SignalID, StartDate);

            var pedDelaySignal = new PedDelaySignal(atspmSignals, TimeBuffer, StartDate, EndDate);

            foreach (var pedPhase in pedDelaySignal.PedPhases)
                if (pedPhase.Cycles.Count > 0)
                {
                    var cycleLength = CycleFactory.GetRedToRedCycles(pedPhase.Approach, StartDate, EndDate);
                    var pdc = new PEDDelayChart(this, pedPhase, cycleLength);
                    var chart = pdc.Chart;
                    var chartName = CreateFileName();
                    chart.SaveImage(MetricFileLocation + chartName);
                    ReturnList.Add(MetricWebPath + chartName);
                }

            return ReturnList;
        }

        public string CreateTractionMetric()
        {
            var signalRepository = SignalsRepositoryFactory.Create();
            ATSPM_Signals atspmSignals = signalRepository.GetVersionOfSignalByDate(SignalID, StartDate);
            PedDelayCharts = new List<PlotlyObject>();
            var pedDelaySignal = new PedDelaySignal(atspmSignals, TimeBuffer, StartDate, EndDate);

            foreach (var pedPhase in pedDelaySignal.PedPhases)
            {
                if (pedPhase.Cycles.Count > 0)
                {
                    PedDelayPlotlyObject = new PlotlyObject();
                    ConfigureGraph(PedDelayPlotlyObject, StartDate, EndDate, 3600000, 1, Zone);
                    var layout = PedDelayPlotlyObject.Layout;

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
                    if (YAxisMax != null)
                    {
                        layout.YAxis.Range.Add(YAxisMax.Value.ToString(CultureInfo.InvariantCulture));
                    }

                    // Y2 (%)
                    layout.YAxis2.Title.Text = "% Delay by Cycle Length";
                    layout.YAxis2.AutoMargin = true;
                    layout.YAxis2.DTick = 20;
                    // If your Axis supports these (recommended for y2 on the right):
                    layout.YAxis2.Overlaying = "y";
                    layout.YAxis2.Side = "right";
                    PedDelayPlotlyObject.Data.AddRange(AddData());
                    PedDelayCharts.Add(PedDelayPlotlyObject);
                    SetPlanStripes();
                }
            }
            return JsonSerializer.Serialize(PedDelayCharts,
                new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = true
                });
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
                        AddDelayByCycleLengthDataPoint(pedCycle, ref currentRedToRedCycle,
                            delayByCycleLengthDataPoints);
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
                var cycles = delayByCycleLengthDataPoints
                    .Where(c => c.Key.BeginWalk >= startTime && c.Key.BeginWalk < endTime).ToList();
                double average = 0;
                if (cycles.Count > 0)
                {
                    average = cycles.Average(c => c.Value);
                }

                DelayByCycleLengthStepChart.Add(startTime, average);
                startTime = startTime.AddMinutes(30);
            }
        }

        public void SetPlanStripes()
        {
            var backGroundColor = 1;
            foreach (var plan in PedPhase.Plans)
            {
                var heightAdd = .03;
                var heightMult = 1;
                var annoFontSize = 12;
                var defaultY = new Annotation().Y;
                var startLocal = TimeZoneInfo.ConvertTime(plan.StartDate, Zone);
                var endLocal = TimeZoneInfo.ConvertTime(plan.EndDate, Zone);
                var stripLine = new Shape();
                //Creates alternating backcolor to distinguish the plans
                if (backGroundColor % 2 == 0)

                    stripLine.FillColor = RgbaFromColor(Color.LightSeaGreen);
                else
                    stripLine.FillColor = RgbaFromColor(Color.LightSteelBlue);

                stripLine.X0 = startLocal.ToString("o");
                stripLine.X1 = endLocal.ToString("o");

                var midpoint = new DateTime((startLocal.Ticks + endLocal.Ticks) / 2, startLocal.Kind);
                var plannotation = new Annotation
                {
                    Font = { Size = 12 },
                    X = midpoint.ToString("yyyy-MM-ddTHH:mm"),
                    ShowArrow = false
                };
                switch (plan.PlanNumber)
                {
                    case 254:
                        plannotation.Text = "Free";
                        break;
                    case 255:
                        plannotation.Text = "Flash";
                        break;
                    case 0:
                        plannotation.Text = "Unknown";
                        break;
                    default:
                        plannotation.Text = "Plan " + plan.PlanNumber;
                        break;
                }
                PedDelayPlotlyObject.Layout.Annotations.Add(plannotation);

                if (Options.ShowPedRecall)
                {
                    var vehicleCycles = RedToRedCycles.Where(r => r.StartTime >= plan.StartDate && r.EndTime < plan.EndDate).ToList();
                    if (vehicleCycles.Count > 0 && ((double)plan.PedBeginWalkCount / (double)vehicleCycles.Count * 100 >= Options.PedRecallThreshold))
                    {
                        var pedRecallLabel = new Annotation
                        {
                            X = midpoint.ToString("yyyy-MM-ddTHH:mm"),
                        };
                        pedRecallLabel.Text = "Ped Recall On";
                        PedDelayPlotlyObject.Layout.Annotations.Add(pedRecallLabel);
                    }
                }

                PedDelayPlotlyObject.Layout.Annotations.Add(new Annotation
                {
                    Text = plan.CyclesWithPedRequests + " CPR",
                    Font =
                        {
                            Size = annoFontSize,
                            Color = "white"
                        },
                    Y = defaultY + (heightMult * heightAdd),
                    ShowArrow = false,
                    X = midpoint.ToString("o")
                });
                heightMult++;

                PedDelayPlotlyObject.Layout.Annotations.Add(new Annotation
                {
                    Text = plan.UniquePedDetections + " TBP",
                    Font =
                    {
                        Size = annoFontSize,
                        Color = "white"
                    },
                    Y = defaultY + (heightMult * heightAdd),
                    ShowArrow = false,
                    X = midpoint.ToString("o")
                });
                heightMult++;
                PedDelayPlotlyObject.Layout.Annotations.Add(new Annotation
                {
                    Text = Math.Round(plan.AvgDelay, 2) + " AD",
                    Font =
                    {
                        Size = annoFontSize,
                        Color = "white"
                    },
                    Y = defaultY + (heightMult * heightAdd),
                    ShowArrow = false,
                    X = midpoint.ToString("o")
                });
                heightMult++;


                if (Options.ShowCycleLength)
                {
                    var cycleLengthAnnotation = new Annotation
                    {
                        Font =
                        {
                            Size = annoFontSize,
                            Color = "white"
                        },
                        Y = defaultY + (heightMult * heightAdd),
                        ShowArrow = false,
                        X = midpoint.ToString("o")
                    };
                    
                    if (RedToRedCycles.Count > 0)
                    {
                        var cycles = RedToRedCycles.Where(r => r.StartTime >= plan.StartDate && r.EndTime < plan.EndDate).ToList();
                        cycleLengthAnnotation.Text = "avg CL: " + (cycles.Count > 0 ? Math.Round(cycles.Average(r => r.RedLineY)) + "s" : "");
                    }
                    else
                    {
                        cycleLengthAnnotation.Text = "No Cycles Found";
                    }
                    PedDelayPlotlyObject.Layout.Annotations.Add(cycleLengthAnnotation);
                }

                PedDelayPlotlyObject.Layout.Shapes.Add(stripLine);
                PedDelayPlotlyObject.Layout.Annotations.Add(plannotation);
                backGroundColor++;
            }
        }
    }
}