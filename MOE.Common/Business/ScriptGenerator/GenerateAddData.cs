﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.IO;
using System.Threading.Tasks;
using MOE.Common.Models;
using MOE.Common.Models.Repositories;

using Microsoft.EntityFrameworkCore.Internal;

using System.Linq;

namespace MOE.Common.Business.ScriptGenerator
{
    public class GenerateAddData
    {
        //private static List<Pin> GetPins()
        //{
        //    var repository = SignalsRepositoryFactory.Create();
        //    return repository.GetPinInfo();
        //}

        private static bool ValidateCoordinates(string latitudeStr, string longitudeStr)
        {
            if (!double.TryParse(latitudeStr, out var latitude) || !double.TryParse(longitudeStr, out var longitude))
            {
                // Unable to parse latitude or longitude from string
                return false;
            }

            // Latitude ranges from -90 to 90
            // Longitude ranges from -180 to 180
            return (latitude >= -90 && latitude <= 90) && (longitude >= -180 && longitude <= 180);
        }

        public static List<Pin> GetPinInfo()
        {
            var pins = new ConcurrentBag<Pin>();
            List<Signal> signals = GetSignalVersionByDate(DateTime.Now);
            //foreach (var signal in signals)
            Parallel.ForEach(signals, signal =>
            {
                if (!ValidateCoordinates(signal.Latitude, signal.Longitude))
                {
                    var repo = new ApplicationEventRepository();
                    repo.QuickAdd("GenerateAddData", "Invalid Coordinates", "GetPinInfo", ApplicationEvent.SeverityLevels.Medium, "Invalid coordinates for signal " + signal.SignalID);
                    return;
                }
                    
                var pin = new Pin(signal.SignalID, signal.Latitude,
                    signal.Longitude,
                    signal.PrimaryName + " " + signal.SecondaryName,
                    signal.RegionID.ToString(),
                    signal.Jurisdiction.Id.ToString()
                );
                pin.MetricTypes = signal.GetMetricTypesString();
                pin.Areas = signal.GetAreasString();
                pins.Add(pin);
                //Console.WriteLine(pin.SignalID);
                });
            return pins.ToList();
        }


        private static List<Signal> GetSignalVersionByDate(DateTime dt)
        {
            using (var db = new SPM())
            {
                db.Configuration.LazyLoadingEnabled = false;

                List<int> versionIds = new List<int>();
                    List<string> restrictedSignalList = db.SignalsToAggregate.Select(s => s.SignalID).ToList();
                    versionIds = db.Signals.Where(
                            r => r.VersionActionId != 3 && r.Start < dt //&&(r.SignalID != "8204" || r.SignalID != "8215"|| r.SignalID != "8206")
                        ).GroupBy(r => r.SignalID).Select(g => g.OrderByDescending(r => r.Start).FirstOrDefault())
                        .Select(s => s.VersionID).ToList();

                var signals = db.Signals.Where(signal => versionIds.Contains(signal.VersionID))
                    .Include(signal => signal.Jurisdiction)
                    .Include(signal => signal.Areas)
                    .Include(signal => signal.Approaches.Select(a => a.Detectors.Select(d => d.DetectionTypes)))
                    .Include(signal => signal.Approaches.Select(a =>
                        a.Detectors.Select(d => d.DetectionTypes.Select(det => det.MetricTypes))))
                    .Include(signal => signal.Approaches.Select(a => a.Detectors.Select(d => d.DetectionHardware)))
                    .Include(signal => signal.Approaches.Select(a => a.DirectionType))
                    .Where(s => s.Enabled)
                    .OrderBy(signal => signal.SignalID).ToList();

                return signals;
            }
        }

        public static void CreateScript()
        {
            //string Locations = string.Empty;
            var script = @"function AddData() {var pinColor =new Microsoft.Maps.Color(255, 238, 118, 35);
            var iconURL = './images/orangePin.png';
            var regionDdl = $('#Regions')[0];
            var jurisdictionDdl = $('#Jurisdictions')[0];
            var areaDdl = $('#Areas')[0];
            var pins = [];
            var regionFilter = -1; if (regionDdl.options[regionDdl.selectedIndex].value != '') 
                {regionFilter = regionDdl.options[regionDdl.selectedIndex].value;}
            var jurisdictionFilter = -1; if (jurisdictionDdl.options[jurisdictionDdl.selectedIndex].value != '') 
                {jurisdictionFilter = jurisdictionDdl.options[jurisdictionDdl.selectedIndex].value;}
            var areaFilter = -1; if (areaDdl.options[areaDdl.selectedIndex].value != '') 
                {areaFilter = areaDdl.options[areaDdl.selectedIndex].value;}
            var reportType = $('#MetricTypes')[0]; 
            var reportTypeFilter = -1; if (reportType.options[reportType.selectedIndex].value != '')
                { reportTypeFilter = ','+reportType.options[reportType.selectedIndex].value;}";

            var pins = GetPinInfo();


            foreach (var pin in pins)
            {
                if (pin != null)
                {
                    Console.WriteLine(pin.SignalID);
                    var PinName = "pin" + pin.SignalID;

                    //The script string is appended for every pin in the collection.
                    script +=
                        " if(PinFilterCheck(regionFilter, reportTypeFilter, jurisdictionFilter, areaFilter," + pin.Region + ", " + pin.Jurisdiction + ", '" + pin.Areas + "', '" + pin.MetricTypes +"')) " +
                        "{var " + PinName + " = new Microsoft.Maps.Pushpin(new Microsoft.Maps.Location(" +
                        pin.Latitude + ", " + pin.Longitude +
                        "));" + PinName +
                        ".SignalID = '" + pin.SignalID + "';" + PinName + ".Region = '" + pin.Region + "';" + PinName +
                        ".Actions = '" + pin.MetricTypes + "';" +
                        "Microsoft.Maps.Events.addHandler(" + PinName + ", 'mouseup', ZoomIn);" +
                        "Microsoft.Maps.Events.addHandler(" + PinName + ", 'click', displayInfobox);pins.push(" +
                        PinName + ");}";
                }
            }
            script += @" 
return pins;}";

            var appSettings = ConfigurationManager.AppSettings;
            for (var i = 0; i < appSettings.Count; i++)
                using (var sw = File.CreateText(appSettings[i] + "AddData.js"))
                {
                    sw.Write(script);
                }
        }

        public static void CreateRouteScript()
        {
            //string Locations = string.Empty;
            var script = @"function AddData() {
            var regionDdl = $('#Regions')[0];
            var pins = [];
            var regionFilter = 0; if (regionDdl.options[regionDdl.selectedIndex].value != '') 
                {regionFilter = regionDdl.options[regionDdl.selectedIndex].value;}
            var reportType = $('#MetricTypes')[0]; 
            var reportTypeFilter = 0; if (reportType.options[reportType.selectedIndex].value != '')
                { reportTypeFilter = ','+reportType.options[reportType.selectedIndex].value;}";
            var pins = GetPinInfo();
            foreach (var pin in pins)
            {
                if (pin != null)
                {
                    var PinName = "pin" + pin.SignalID;
                    //The script string is appended for every pin in the collection.
                    script +=
                        " if((regionFilter == 0 && reportTypeFilter == 0) || (reportTypeFilter == 0 && regionFilter == " +
                        pin.Region + ") || (regionFilter == 0 && '" + pin.MetricTypes +
                        "'.indexOf(reportTypeFilter) > -1) || ('" + pin.MetricTypes +
                        "'.indexOf(reportTypeFilter) > -1 && regionFilter == " + pin.Region + ") ){var " + PinName +
                        " = new Microsoft.Maps.Pushpin(new Microsoft.Maps.Location(" +
                        pin.Latitude + ", " + pin.Longitude +
                        "));" +
                        PinName + ".SignalID = '" + pin.SignalID + "';" + PinName + ".Region = '" + pin.Region + "';" +
                        PinName + ".Actions = '" + pin.MetricTypes + "';Microsoft.Maps.Events.addHandler(" + PinName +
                        ", 'mouseup', ZoomIn);Microsoft.Maps.Events.addHandler(" + PinName +
                        ", 'mouseover', displayRouteInfobox);Microsoft.Maps.Events.addHandler(" + PinName +
                        ", 'mouseout', closeInfobox);Microsoft.Maps.Events.addHandler(" + PinName +
                        ", 'click', AddSignalFromPin);pins.push(" + PinName + ");}";
                }
            }

            //The Locaitons string will be used ot create a literal that is inserted into the default.html

            script += @" 
return pins;}";

            var appSettings = ConfigurationManager.AppSettings;
            for (var i = 0; i < appSettings.Count; i++)
                using (var sw = File.CreateText(appSettings[i] + "AddRouteData.js"))
                {
                    sw.Write(script);
                }
        }
    }
}