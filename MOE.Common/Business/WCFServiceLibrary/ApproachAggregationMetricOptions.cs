using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Web.UI.DataVisualization.Charting;
using MOE.Common.Business.Bins;
using MOE.Common.Models;
using MOE.Common.Models.Repositories;

namespace MOE.Common.Business.WCFServiceLibrary
{
    [DataContract]
    public abstract class ApproachAggregationMetricOptions : SignalAggregationMetricOptions
    {
        public override string YAxisTitle { get; }

        protected override void GetChartByXAxisAggregation()
        {
            switch (SelectedXAxisType)
            {
                case XAxisType.Time:
                    GetTimeCharts();
                    break;
                case XAxisType.TimeOfDay:
                    GetTimeOfDayCharts();
                    break;
                case XAxisType.Approach:
                    GetApproachCharts();
                    break;
                case XAxisType.Direction:
                    GetDirectionCharts();
                    break;
                case XAxisType.Signal:
                    GetSignalCharts();
                    break;
                default:
                    throw new Exception("Invalid X-Axis");
            }
        }


        protected void GetDirectionCharts()
        {
            Chart chart;
            switch (SelectedSeries)
            {
                case SeriesType.Direction:
                    foreach (var signal in Signals)
                    {
                        chart = ChartFactory.CreateStringXIntYChart(this);
                        GetDirectionXAxisDirectionSeriesChart(signal, chart);
                        if (ShowEventCount)
                        {
                            SetDirectionXAxisSignalSeriesForEventCount(chart, signal);
                        }
                        SaveChartImage(chart);
                    }
                    break;
                default:
                    throw new Exception("Invalid X-Axis Series Combination");
            }
        }

        private void GetDirectionXAxisDirectionSeriesChart(Models.ATSPM_Signals atspmSignals, Chart chart)
        {
            Series series = GetDirectionXAxisDirectionSeries(atspmSignals);
            chart.Series.Add(series);
        }

        public virtual Series GetDirectionXAxisDirectionSeries(Models.ATSPM_Signals atspmSignals)
        {
            var series = CreateSeries(0, atspmSignals.SignalDescription);
            var directionsList = GetFilteredDirections();
            var columnCounter = 1;
            var colorCount = 1;
            foreach (var direction in directionsList)
            {
                var dataPoint = new DataPoint();
                dataPoint.XValue = columnCounter;
                if (SelectedAggregationType == AggregationType.Sum)
                    dataPoint.SetValueY(GetSumByDirection(atspmSignals, direction));
                else
                    dataPoint.SetValueY(GetAverageByDirection(atspmSignals, direction));
                dataPoint.AxisLabel = direction.Description;
                dataPoint.Color = GetSeriesColorByNumber(colorCount);
                series.Points.Add(dataPoint);
                colorCount++;
                columnCounter++;
            }

            return series;
        }

        public List<DirectionType> GetFilteredDirections()
        {
            var direcitonRepository = DirectionTypeRepositoryFactory.Create();
            var includedDirections = FilterDirections.Where(f => f.Include).Select(f => f.DirectionTypeId).ToList();
            var directionsList = direcitonRepository.GetDirectionsByIDs(includedDirections);
            return directionsList;
        }

        protected void GetApproachCharts()
        {
            Chart chart;
            switch (SelectedSeries)
            {
                case SeriesType.PhaseNumber:
                    foreach (var signal in Signals)
                    {
                        chart = ChartFactory.CreateStringXIntYChart(this);
                        GetApproachXAxisChart(signal, chart);
                        if (ShowEventCount)
                        {
                            SetApproachXAxisSignalSeriesForEventCount(chart, signal);
                        }
                        SaveChartImage(chart);
                    }
                    break;
                default:
                    throw new Exception("Invalid X-Axis Series Combination");
            }
        }


        protected override void GetTimeCharts()
        {
            Chart chart = null;
            switch (SelectedSeries)
            {
                case SeriesType.PhaseNumber:
                    foreach (var signal in Signals)
                    {
                        chart = ChartFactory.CreateTimeXIntYChart(this, new List<Models.ATSPM_Signals> {signal});
                        GetTimeXAxisApproachSeriesChart(signal, chart);
                        if (ShowEventCount)
                        {
                            SetTimeXAxisSignalSeriesForEventCount(chart, signal);
                        }
                        SaveChartImage(chart);
                    }
                    break;
                case SeriesType.Direction:
                    foreach (var signal in Signals)
                    {
                        chart = ChartFactory.CreateTimeXIntYChart(this, new List<Models.ATSPM_Signals> {signal});
                        GetTimeXAxisDirectionSeriesChart(signal, chart);
                        if (ShowEventCount)
                        {
                            SetTimeXAxisSignalSeriesForEventCount(chart, signal);
                            //var eventCountOptions = (ApproachEventCountAggregationOptions)this;
                            //Series series = eventCountOptions.GetTimeXAxisSignalSeries(signal);
                            //chart.Series.Add(series);
                        }
                        SaveChartImage(chart);
                    }
                    ;
                    break;
                case SeriesType.Signal:
                    chart = ChartFactory.CreateTimeXIntYChart(this, Signals);
                    GetTimeXAxisSignalSeriesChart(Signals, chart);
                    if (ShowEventCount)
                    {
                        SetTimeXAxisRouteSeriesForEventCount(Signals, chart);

                    }
                    SaveChartImage(chart);
                    break;
                case SeriesType.Route:
                    chart = ChartFactory.CreateTimeXIntYChart(this, Signals);
                    SetTimeXAxisRouteSeriesChart(Signals, chart);
                    if (ShowEventCount)
                    {
                        SetTimeXAxisRouteSeriesForEventCount(Signals, chart);
                    }
                        SaveChartImage(chart);
                    break;
                default:
                    throw new Exception("Invalid X-Axis Series Combination");
            }
        }


        protected override void GetSignalCharts()
        {
            Chart chart;
            switch (SelectedSeries)
            {
                case SeriesType.PhaseNumber:
                    chart = ChartFactory.CreateStringXIntYChart(this);
                    GetSignalsXAxisPhaseNumberSeriesChart(Signals, chart);
                    break;
                case SeriesType.Direction:
                    chart = ChartFactory.CreateStringXIntYChart(this);
                    GetSignalsXAxisDirectionSeriesChart(Signals, chart);
                    break;
                case SeriesType.Signal:
                    chart = ChartFactory.CreateStringXIntYChart(this);
                    GetSignalsXAxisSignalSeriesChart(Signals, chart);
                    break;
                default:
                    throw new Exception("Invalid X-Axis Series Combination");
            }
            if (ShowEventCount)
            {
                SetSignalsXAxisSignalSeriesForEventCount(Signals, chart);
            }
            SaveChartImage(chart);
        }


        protected void GetSignalsXAxisDirectionSeriesChart(List<Models.ATSPM_Signals> signals, Chart chart)
        {
            var availableDirections = new List<DirectionType>();
            foreach (var signal in signals)
                availableDirections.AddRange(signal.GetAvailableDirections());
            availableDirections = availableDirections.Distinct().ToList();
            var colorCode = 1;
            foreach (var directionType in availableDirections)
            {
                var seriesName = directionType.Description;
                var series = CreateSeries(colorCode, seriesName);
                foreach (var signal in signals)
                {
                    var binsContainers = GetBinsContainersByDirection(directionType, signal);
                    var dataPoint = new DataPoint();
                    dataPoint.SetValueY(SelectedAggregationType == AggregationType.Sum
                        ? binsContainers.Sum(b => b.SumValue)
                        : Convert.ToInt32(Math.Round(binsContainers.Sum(b => b.SumValue) /
                                                     (double) availableDirections.Count)));
                    dataPoint.AxisLabel = signal.SignalDescription;
                    series.Points.Add(dataPoint);
                }
                colorCode++;
                chart.Series.Add(series);
            }
        }


        protected void GetTimeXAxisDirectionSeriesChart(Models.ATSPM_Signals atspmSignals, Chart chart)
        {
            var i = 1;
            foreach (var directionType in atspmSignals.GetAvailableDirections())
            {
                GetDirectionSeries(chart, i, directionType, atspmSignals);
                i++;
            }
        }

        private void GetDirectionSeries(Chart chart, int colorCode, DirectionType directionType, Models.ATSPM_Signals atspmSignals)
        {
            var series = CreateSeries(colorCode, directionType.Description);
            var binsContainers = GetBinsContainersByDirection(directionType, atspmSignals);
            foreach (var binsContainer in binsContainers)
            foreach (var bin in binsContainer.Bins)
            {
                var dataPoint = SelectedAggregationType == AggregationType.Sum
                    ? GetDataPointForSum(bin)
                    : GetDataPointForAverage(bin);
                series.Points.Add(dataPoint);
            }
            chart.Series.Add(series);
        }


        protected override void GetTimeOfDayCharts()
        {
            switch (SelectedSeries)
            {
                case SeriesType.PhaseNumber:
                    foreach (var signal in Signals)
                    {
                        Chart phaseChart = ChartFactory.CreateTimeXIntYChart(this, new List<Models.ATSPM_Signals> {signal});
                        GetTimeOfDayXAxisApproachSeriesChart(signal, phaseChart);
                        if (ShowEventCount)
                        {
                            SetTimeofDayAxisSignalSeriesForEventCount(signal, phaseChart);
                        }
                        SaveChartImage(phaseChart);
                    }
                    break;
                case SeriesType.Direction:
                    foreach (var signal in Signals)
                    {
                        var signals = new List<Models.ATSPM_Signals> {signal};
                        Chart directionChart = ChartFactory.CreateTimeXIntYChart(this, signals);
                        GetTimeOfDayXAxisDirectionSeriesChart(signal, directionChart);
                        if (ShowEventCount)
                        {
                            SetTimeofDayAxisSignalSeriesForEventCount(signal, directionChart);
                        }
                    SaveChartImage(directionChart);
                    }
                    break;
                case SeriesType.Signal:
                    Chart signalChart = ChartFactory.CreateTimeXIntYChart(this, Signals);
                    GetTimeOfDayXAxisSignalSeriesChart(Signals, signalChart);
                    if (ShowEventCount)
                    {
                        SetTimeOfDayAxisRouteSeriesForEventCount(Signals, signalChart);
                    }
                    SaveChartImage(signalChart);
                    break;
                case SeriesType.Route:
                    Chart RouteChart = ChartFactory.CreateTimeXIntYChart(this, Signals);
                    GetTimeOfDayXAxisRouteSeriesChart(Signals, RouteChart);
                    if (ShowEventCount)
                    {
                        SetTimeOfDayAxisRouteSeriesForEventCount(Signals, RouteChart);
                    }
                    SaveChartImage(RouteChart);
                    break;
                default:
                    throw new Exception("Invalid X-Axis Series Combination");
            }
        }
        

        private void SetTimeofDayAxisSignalSeriesForEventCount(Models.ATSPM_Signals atspmSignals, Chart chart)
        {
            //var eventCountOptions = new  ApproachEventCountAggregationOptions(this);
            //var binsContainers = eventCountOptions.GetBinsContainersByRoute(new List<Models.Signal> { signal });
            //Series series = CreateEventCountSeries();
            //eventCountOptions.SetTimeAggregateSeries(series, binsContainers);
            //chart.Series.Add(series);
        }


        protected void GetTimeOfDayXAxisDirectionSeriesChart(Models.ATSPM_Signals atspmSignals, Chart chart)
        {
            SetTimeXAxisAxisMinimum(chart);
            var availableDirections = atspmSignals.GetAvailableDirections();
            var seriesList = new ConcurrentBag<Series>();
            Parallel.For(0, availableDirections.Count, i => // foreach (var signal in signals)
            {
                var binsContainers = GetBinsContainersByDirection(availableDirections[i], atspmSignals);
                var series = CreateSeries(i, availableDirections[i].Description);
                try
                {
                    SetTimeAggregateSeries(series, binsContainers);
                    seriesList.Add(series);
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                    throw;
                }
            });
            foreach (var direction in availableDirections)
                chart.Series.Add(seriesList.FirstOrDefault(s => s.Name == direction.Description));
        }


        protected void GetTimeOfDayXAxisApproachSeriesChart(Models.ATSPM_Signals atspmSignals, Chart chart)
        {
            var seriesList = new ConcurrentBag<Series>();
            var approaches = atspmSignals.Approaches.ToList();
            try
            {

            
            Parallel.For(0, approaches.Count, i =>
            {
                var phaseDescription = GetPhaseDescription(approaches[i], true);
                var binsContainers = GetBinsContainersByApproach(approaches[i], true);
                var series = CreateSeries(i, approaches[i].Description + phaseDescription);
                SetTimeAggregateSeries(series, binsContainers);
                seriesList.Add(series);
                if (approaches[i].PermissivePhaseNumber != null)
                {
                    var permissivePhaseDescription = GetPhaseDescription(approaches[i], false);
                    var permissiveBinsContainers = GetBinsContainersByApproach(approaches[i], false);
                    var permissiveSeries = CreateSeries(i, approaches[i].Description + permissivePhaseDescription);
                    SetTimeAggregateSeries(permissiveSeries, permissiveBinsContainers);
                    seriesList.Add(permissiveSeries);
                    i++;
                }
            });
            }
            catch (Exception e)
            {
                throw;
            }
            var orderedSeries = seriesList.OrderBy(s => s.Name).ToList();
            foreach (var series in orderedSeries)
                chart.Series.Add(series);
        }


        protected void GetSignalsXAxisPhaseNumberSeriesChart(List<Models.ATSPM_Signals> signals, Chart chart)
        {
            var availablePhaseNumbers = new List<int>();
            foreach (var signal in signals)
                availablePhaseNumbers.AddRange(signal.GetPhasesForSignal());
            availablePhaseNumbers = availablePhaseNumbers.Distinct().ToList();
            var colorCode = 1;
            foreach (var phaseNumber in availablePhaseNumbers)
            {
                var seriesName = "Phase " + phaseNumber;
                Series series = GetSignalXAxisPhaseNumberSeries(signals, colorCode, phaseNumber, seriesName);
                colorCode++;
                chart.Series.Add(series);
            }
        }

        public Series GetSignalXAxisPhaseNumberSeries(List<Models.ATSPM_Signals> signals, int colorCode, int phaseNumber, string seriesName)
        {
            var series = CreateSeries(colorCode, seriesName);
            foreach (var signal in signals)
            {
                var binsContainers = GetBinsContainersByPhaseNumber(signal, phaseNumber);
                var dataPoint = new DataPoint();
                dataPoint.SetValueY(SelectedAggregationType == AggregationType.Sum
                    ? binsContainers.Sum(b => b.SumValue)
                    : binsContainers.Average(b => b.SumValue));
                dataPoint.AxisLabel = signal.SignalDescription;
                series.Points.Add(dataPoint);
            }

            return series;
        }

        protected void GetApproachXAxisChart(Models.ATSPM_Signals atspmSignals, Chart chart)
        {
            Series series = GetApproachXAxisApproachSeries(atspmSignals, 0);
            chart.Series.Add(series);
        }

        private Series GetApproachXAxisApproachSeries(Models.ATSPM_Signals atspmSignals, int colorCode)
        {
            var series = CreateSeries(0, atspmSignals.SignalDescription);
            var i = 1;
            foreach (var approach in atspmSignals.Approaches)
            {
                var binsContainers = GetBinsContainersByApproach(approach, true);
                var dataPoint = new DataPoint();
                dataPoint.XValue = i;
                dataPoint.Color = GetSeriesColorByNumber(i);
                if (SelectedAggregationType == AggregationType.Sum)
                    dataPoint.SetValueY(binsContainers.FirstOrDefault().SumValue);
                else
                    dataPoint.SetValueY(binsContainers.FirstOrDefault().AverageValue);
                dataPoint.AxisLabel = approach.Description;
                series.Points.Add(dataPoint);
                i++;
                if (approach.PermissivePhaseNumber != null)
                {
                    var binsContainers2 = GetBinsContainersByApproach(approach, false);
                    var dataPoint2 = new DataPoint();
                    dataPoint2.XValue = i;
                    dataPoint2.Color = GetSeriesColorByNumber(i);
                    if (SelectedAggregationType == AggregationType.Sum)
                        dataPoint2.SetValueY(binsContainers2.FirstOrDefault().SumValue);
                    else
                        dataPoint2.SetValueY(binsContainers2.FirstOrDefault().AverageValue);
                    dataPoint2.AxisLabel = approach.Description;
                    series.Points.Add(dataPoint2);
                    i++;
                }
            }

            return series;
        }

        protected void GetPhaseXAxisChart(Models.ATSPM_Signals atspmSignals, Chart chart)
        {
            Series series = GetPhaseXAxisPhaseSeries(atspmSignals, 0);
            chart.Series.Add(series);
        }

        public Series GetPhaseXAxisPhaseSeries(Models.ATSPM_Signals atspmSignals, int colorCode)
        {
            var series = CreateSeries(colorCode, atspmSignals.SignalDescription);
            var i = 1;
            var phaseNumbers = atspmSignals.GetPhasesForSignal();
            foreach (var phaseNumber in phaseNumbers)
            {
                var dataPoint = new DataPoint();
                dataPoint.XValue = i;
                if (SelectedAggregationType == AggregationType.Sum)
                    dataPoint.SetValueY(GetSumByPhaseNumber(atspmSignals, phaseNumber));
                else
                    dataPoint.SetValueY(GetAverageByPhaseNumber(atspmSignals, phaseNumber));
                dataPoint.AxisLabel = "Phase " + phaseNumber;
                dataPoint.Color = GetSeriesColorByNumber(i);
                series.Points.Add(dataPoint);
                i++;
            }
            return series;
        }

        protected void GetTimeXAxisApproachSeriesChart(Models.ATSPM_Signals atspmSignals, Chart chart)
        {
            var i = 1;
            foreach (var approach in atspmSignals.Approaches)
            {
                GetApproachTimeSeriesByProtectedPermissive(chart, i, approach, true);
                i++;
                if (approach.PermissivePhaseNumber != null)
                {
                    GetApproachTimeSeriesByProtectedPermissive(chart, i, approach, false);
                    i++;
                }
            }
        }

        private static string GetPhaseDescription(Approach approach, bool getProtectedPhase)
        {
            return getProtectedPhase
                ? " Phase " + approach.ProtectedPhaseNumber
                : " Phase " + approach.PermissivePhaseNumber;
        }

        private void GetApproachTimeSeriesByProtectedPermissive(Chart chart, int i, Approach approach,
            bool getProtectedPhase)
        {
            Series series = GetTimeXAxisPhaseSeries(i, approach, getProtectedPhase);
            chart.Series.Add(series);
        }

        private Series GetTimeXAxisPhaseSeries(int colorCode, Approach approach, bool getProtectedPhase)
        {
            var phaseDescription = GetPhaseDescription(approach, getProtectedPhase);
            var binsContainers = GetBinsContainersByApproach(approach, getProtectedPhase);
            var series = CreateSeries(colorCode,
                //approach.DirectionType.Abbreviation 
                approach.Description
                + " - PH " + (getProtectedPhase ? approach.ProtectedPhaseNumber.ToString(): approach.PermissivePhaseNumber.Value.ToString())); //approach.Description + phaseDescription);
            if ((TimeOptions.SelectedBinSize == BinFactoryOptions.BinSize.Month ||
                 TimeOptions.SelectedBinSize == BinFactoryOptions.BinSize.Year) &&
                TimeOptions.TimeOption == BinFactoryOptions.TimeOptions.TimePeriod)
                foreach (var binsContainer in binsContainers)
                {
                    var dataPoint = SelectedAggregationType == AggregationType.Sum
                        ? GetContainerDataPointForSum(binsContainer)
                        : GetContainerDataPointForAverage(binsContainer);
                    series.Points.Add(dataPoint);
                }
            else
                foreach (var bin in binsContainers.FirstOrDefault()?.Bins)
                {
                    var dataPoint = SelectedAggregationType == AggregationType.Sum
                        ? GetDataPointForSum(bin)
                        : GetDataPointForAverage(bin);
                    series.Points.Add(dataPoint);
                }

            return series;
        }


        public virtual void SetDirectionXAxisSignalSeriesForEventCount(Chart chart, Models.ATSPM_Signals atspmSignals)
        {
        //    var eventCountOptions = new ApproachEventCountAggregationOptions(this);
        //    Series series = eventCountOptions.GetDirectionXAxisDirectionSeries(signal);
        //    chart.Series.Add(SetEventCountSeries(series));
        }


        public virtual void SetApproachXAxisSignalSeriesForEventCount(Chart chart, Models.ATSPM_Signals atspmSignals)
        {
            //var eventCountOptions = new ApproachEventCountAggregationOptions(this);
            //Series series = eventCountOptions.GetApproachXAxisApproachSeries(signal, -1);
            
            //chart.Series.Add(SetEventCountSeries(series));
        }


        public virtual void SetTimeXAxisSignalSeriesForEventCount(Chart chart, Models.ATSPM_Signals atspmSignals)
        {
            //var eventCountOptions = new ApproachEventCountAggregationOptions(this);
            //Series series = eventCountOptions.GetTimeXAxisSignalSeries(signal);
            
            //chart.Series.Add(SetEventCountSeries(series));
        }

        public override void SetSignalsXAxisSignalSeriesForEventCount(List<Models.ATSPM_Signals> signals, Chart chart)
        {
            //var eventCountOptions = new ApproachEventCountAggregationOptions(this);
            //Series series = eventCountOptions.GetSignalsXAxisSignalSeries(signals, "Event Count");
            //chart.Series.Add(SetEventCountSeries(series));
        }

        public override void SetTimeXAxisRouteSeriesForEventCount(List<Models.ATSPM_Signals> signals, Chart chart)
        {
            //var eventCountOptions = new ApproachEventCountAggregationOptions(this);
            //Series series = eventCountOptions.GetTimeXAxisRouteSeries(signals);
            //chart.Series.Add(SetEventCountSeries(series));
        }

        public override void SetTimeOfDayAxisRouteSeriesForEventCount(List<Models.ATSPM_Signals> signals, Chart chart)
        {
            //var eventCountOptions = new ApproachEventCountAggregationOptions(this);
            //Series eventCountSeries = CreateEventCountSeries();
            //var eventBinsContainers = eventCountOptions.GetBinsContainersByRoute(signals);
            //eventCountOptions.SetTimeAggregateSeries(eventCountSeries, eventBinsContainers);
            //chart.Series.Add(eventCountSeries);
        }

        protected abstract List<BinsContainer> GetBinsContainersByApproach(Approach approach, bool getprotectedPhase);
        protected abstract int GetAverageByPhaseNumber(Models.ATSPM_Signals atspmSignals, int phaseNumber);
        protected abstract double GetSumByPhaseNumber(Models.ATSPM_Signals atspmSignals, int phaseNumber);
        protected abstract int GetAverageByDirection(Models.ATSPM_Signals atspmSignals, DirectionType direction);
        protected abstract double GetSumByDirection(Models.ATSPM_Signals atspmSignals, DirectionType direction);

        protected abstract List<BinsContainer> GetBinsContainersByDirection(DirectionType directionType,
            Models.ATSPM_Signals atspmSignals);

        protected abstract List<BinsContainer> GetBinsContainersByPhaseNumber(Models.ATSPM_Signals atspmSignals, int phaseNumber);

    }
}