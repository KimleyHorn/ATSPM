using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
// ReSharper disable StringLiteralTypo

namespace MOE.Common.Models
{


    public class PlotlyObject
    {
        [JsonPropertyName("data")]
        public List<Trace> Data { get; set; } = new List<Trace>();

        [JsonPropertyName("layout")]
        public Layout Layout { get; set; } = new Layout();
    }

    public class Trace
    {
        public Trace(string name)
        {
            Name = name;
        }
        [JsonPropertyName("x")]
        public List<string> X { get; set; } = new List<string>();

        [JsonPropertyName("y")]
        public List<double> Y { get; set; } = new List<double>();

        [JsonPropertyName("type")]
        public string Type { get; set; } = "scatter";

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "markers";

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("line")]
        public Line Line { get; set; } = new Line();

        [JsonPropertyName("marker")]
        public Marker Marker { get; set; } = new Marker();

        [JsonPropertyName("yaxis")]
        public string YAxisRef { get; set; } = "y"; // set to "y2" for secondary axis

        public void AddXY(string x, double y)
        {
            X.Add(x);
            Y.Add(y);
        }

        public void AddXY(DateTime x, double y)
        {
            X.Add(x.ToString("yyyy-MM-ddTHH:mm"));
            Y.Add(y);
        }
        public void AddXY(DateTime x, double y, TimeZoneInfo tz)
        {
            X.Add(ToLocalOffset(x, tz).ToString("yyyy-MM-ddTHH:mm"));
            Y.Add(y);
        }
        private static DateTimeOffset ToLocalOffset(DateTime utc, TimeZoneInfo tz)
        {
            var u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            var local = TimeZoneInfo.ConvertTimeFromUtc(u, tz);
            return new DateTimeOffset(local, tz.GetUtcOffset(local));
        }

    }

    public class Line
    {
        [JsonPropertyName("color")]
        public string Color { get; set; } = "blue";
        [JsonPropertyName("width")] public int Width { get; set; } = 2;
        [JsonPropertyName("dash")] public string Dash { get; set; } = "solid"; // "dash", "dot", "dashdot"
        [JsonPropertyName("shape")] public string Shape { get; set; } = "linear"; // "hv" for step
    }

    public class Marker
    {
        [JsonPropertyName("color")]
        public string Color { get; set; } = "blue";
        [JsonPropertyName("size")]
        public int Size { get; set; } = 4;
    }

    public class Layout
    {
        [JsonPropertyName("title")]
        public PlotlyTitle Title { get; set; } = new PlotlyTitle
        {
            Font =
            {
                Size = 30,
                Weight = 200
            },
            Pad =
            {
                T = 10,
                B = 10
            }
        };
        [JsonPropertyName("legend")]
        public PlotlyLegend Legend { get; set; } = new PlotlyLegend();
        [JsonPropertyName("margin")]
        public PlotlyMargin Margin { get; set; } = new PlotlyMargin();
        [JsonPropertyName("xaxis")]
        public Axis XAxis { get; set; } = new Axis();

        [JsonPropertyName("yaxis")]
        public Axis YAxis { get; set; } = new Axis
        {
            Title =
            {
                Standoff = 50
            }
        };
        [JsonPropertyName("yaxis2")]
        public Axis YAxis2 { get; set; } = new Axis();
        [JsonPropertyName("shapes")]
        public List<Shape> Shapes { get; set; } = new List<Shape>();
        [JsonPropertyName("annotations")]
        public List<Annotation> Annotations { get; set; } = new List<Annotation>();
    }

    public class PlotlyTitle
    {
        [JsonPropertyName("automargin")] public bool AutoMargin { get; set; } = true;

        [JsonPropertyName("text")]
        public string Text { get; set; } = "Main Title";

        [JsonPropertyName("font")]
        public PlotlyFont Font { get; set; } = new PlotlyFont();

        [JsonPropertyName("pad")]
        public PlotlyPad Pad { get; set; } = new PlotlyPad();

        [JsonPropertyName("subtitle")] public PlotlySubtitle Subtitle = new PlotlySubtitle();

        public PlotlyMargin Margin { get; set; } = new PlotlyMargin
        {
            B = 0
        };

    }

    public class PlotlyFont
    {
        [JsonPropertyName("size")]
        public int Size { get; set; } = 14;

        [JsonPropertyName("color")]
        public string Color { get; set; } = "";

        [JsonPropertyName("family")] public string Family { get; set; } = "";

        [JsonPropertyName("lineposition")] public string LinePosition { get; set; } = "";

        [JsonPropertyName("style")] public string Style { get; set; } = "normal";

        [JsonPropertyName("textcase")] public string TextCase { get; set; } = "normal";
        [JsonPropertyName("weight")] public int Weight { get; set; } = 10;
    }

    public class PlotlyLegend
    {
        [JsonPropertyName("title")]
        public LegendTitle Title { get; set; } = new LegendTitle();

        [JsonPropertyName("orientation")]
        public string Orientation { get; set; } = "h";

        [JsonPropertyName("x")]
        public double X { get; set; } = 0.5;

        [JsonPropertyName("xanchor")]
        public string XAnchor { get; set; } = "center";

        [JsonPropertyName("y")]
        public double Y { get; set; } = -0.1;
    }

    public class LegendTitle
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = "Legend";

        [JsonPropertyName("font")] public PlotlyFont Font { get; set; } = new PlotlyFont();
        [JsonPropertyName("standoff")] public int Standoff { get; set; } = 0;
    }

    public class Axis
    {
        [JsonPropertyName("type")] public string Type { get; set; }
        [JsonPropertyName("title")] public LegendTitle Title { get; set; } = new LegendTitle();
        [JsonPropertyName("range")] public List<string> Range { get; set; } = new List<string>(2);  // ISO strings
        [JsonPropertyName("tickformat")] public string TickFormat { get; set; } // e.g., "%-I:%M %p"
        [JsonPropertyName("dtick")] public int DTick { get; set; } = 1; // e.g., "M1" for monthly ticks
        [JsonPropertyName("automargin")] public bool AutoMargin { get; set; } = true;
        [JsonPropertyName("overlaying")] public string Overlaying { get; set; } = "";
        [JsonPropertyName("side")] public string Side { get; set; } = "";


    }
    public class Shape
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "rect";
        [JsonPropertyName("xref")] public string XRef { get; set; } = "x";
        [JsonPropertyName("yref")] public string YRef { get; set; } = "paper"; // full height
        [JsonPropertyName("x0")] public string X0 { get; set; } = "0";
        [JsonPropertyName("x1")] public string X1 { get; set; } = "1";
        [JsonPropertyName("y0")] public double Y0 { get; set; } = 0;
        [JsonPropertyName("y1")] public double Y1 { get; set; } = 1;
        [JsonPropertyName("fillcolor")] public string FillColor { get; set; } = "rgba(0,100,255,0.2)";
        [JsonPropertyName("opacity")] public double Opacity { get; set; } = 0.2;
        [JsonPropertyName("line")] public ShapeLine Line { get; set; } = new ShapeLine();
        [JsonPropertyName("layer")] public string Layer { get; set; } = "below";

        //[JsonPropertyName("label")]
        //public PlotlyTitle Label { get; set; } = new PlotlyTitle
        //{
        //    Text = "label"
        //};
    }

    public class ShapeLine
    {
        [JsonPropertyName("width")] public int Width { get; set; } = 0; // no border
    }
    public class Annotation
    {
        [JsonPropertyName("x")] public string X { get; set; } = "";
        [JsonPropertyName("y")] public double Y { get; set; } = 1.085;
        [JsonPropertyName("xref")] public string XRef { get; set; } = "x";
        [JsonPropertyName("yref")] public string YRef { get; set; } = "paper";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("showarrow")] public bool ShowArrow { get; set; } = false;
        [JsonPropertyName("ax")] public int Ax { get; set; } = 0;   // arrow x-offset (if arrow used)
        [JsonPropertyName("ay")] public int Ay { get; set; } = -20; // arrow y-offset (if arrow used)
        [JsonPropertyName("font")] public PlotlyFont Font { get; set; } = new PlotlyFont();

        [JsonPropertyName("margin")]
        public PlotlyMargin Margin { get; set; } = new PlotlyMargin
        {
            T = 100
        };
    }

    public class PlotlyPad
    {
        [JsonPropertyName("b")] public int B { get; set; } = 0;
        [JsonPropertyName("l")] public int L { get; set; } = 0;
        [JsonPropertyName("r")] public int R { get; set; } = 0;
        [JsonPropertyName("t")] public int T { get; set; } = 0;
    }

    public class PlotlySubtitle
    {
        [JsonPropertyName("font")] public PlotlyFont Font { get; set; } = new PlotlyFont();
        [JsonPropertyName("text")] public string Text { get; set; } = "Subtitle";
    }

    public class PlotlyMargin
    {
        [JsonPropertyName("b")] public int B { get; set; } = 0;
        [JsonPropertyName("l")] public int L { get; set; } = 0;
        [JsonPropertyName("r")] public int R { get; set; } = 0;
        [JsonPropertyName("t")] public int T { get; set; } = 0;
    }
}
