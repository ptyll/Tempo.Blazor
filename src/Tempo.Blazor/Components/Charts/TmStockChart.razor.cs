using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Charts;

public partial class TmStockChart : ComponentBase
{
    [Parameter] public IReadOnlyList<StockChartDataPoint> Data { get; set; } = [];
    [Parameter] public StockChartType Type { get; set; } = StockChartType.Candlestick;
    [Parameter] public bool ShowVolume { get; set; } = true;
    [Parameter] public bool ShowGrid { get; set; } = true;
    [Parameter] public bool Animated { get; set; }
    [Parameter] public string? Width { get; set; }
    [Parameter] public string? Height { get; set; } = "400px";
    [Parameter] public string? Class { get; set; }

    // SVG constants (same as TmChart for consistency)
    private const int SvgW = 600;
    private const int SvgH = 400;
    private const int PL = 50;
    private const int PR = 20;
    private const int PT = 20;
    private const int PB = 40;
    private const int VolumeH = 80;
    private int CW => SvgW - PL - PR;
    private int CH => ShowVolume ? SvgH - PT - PB - VolumeH : SvgH - PT - PB;

    private string CssClass => $"tm-stock-chart{(Animated ? " tm-stock-chart--animated" : "")}{(string.IsNullOrEmpty(Class) ? "" : " " + Class)}";
    private string WrapperStyle => $"width:{Width ?? "100%"};height:{Height};";

    private double MaxVal => Data.Count > 0 ? Data.Max(d => d.High) : 0;
    private double MinVal => Data.Count > 0 ? Data.Min(d => d.Low) : 0;
    private double ValRange => MaxVal - MinVal;
    private double MaxVolume => Data.Count > 0 ? Data.Max(d => d.Volume ?? 0) : 0;

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private double ScaleY(double value)
    {
        if (ValRange <= 0) return PT + CH;
        return PT + CH - ((value - MinVal) / ValRange) * CH;
    }

    private double ScaleX(int index)
    {
        if (Data.Count <= 1) return PL + CW / 2.0;
        return PL + (index * CW / (double)(Data.Count - 1));
    }

    private const string BullColor = "var(--tm-chart-positive)";
    private const string BearColor = "var(--tm-chart-negative)";
    private const string WickColor = "var(--tm-chart-neutral)";

    // ── Render fragments ──────────────────────────────────────────────────

    private RenderFragment RenderGrid => builder =>
    {
        if (!ShowGrid || Data.Count == 0) return;
        for (int i = 0; i <= 5; i++)
        {
            var y = PT + CH - (CH * i / 5.0);
            builder.OpenElement(0, "line");
            builder.AddAttribute(1, "x1", F(PL));
            builder.AddAttribute(2, "y1", F(y));
            builder.AddAttribute(3, "x2", F(PL + CW));
            builder.AddAttribute(4, "y2", F(y));
            builder.AddAttribute(5, "class", "tm-stock-chart__grid-line");
            builder.CloseElement();
        }
        for (int i = 0; i < Data.Count; i++)
        {
            var x = ScaleX(i);
            builder.OpenElement(0, "line");
            builder.AddAttribute(1, "x1", F(x));
            builder.AddAttribute(2, "y1", F(PT));
            builder.AddAttribute(3, "x2", F(x));
            builder.AddAttribute(4, "y2", F(PT + CH));
            builder.AddAttribute(5, "class", "tm-stock-chart__grid-line");
            builder.CloseElement();
        }
    };

    private RenderFragment RenderAxes => builder =>
    {
        if (Data.Count == 0) return;
        // Y-axis labels
        for (int i = 0; i <= 5; i++)
        {
            var val = MinVal + (ValRange * i / 5.0);
            var y = PT + CH - (CH * i / 5.0);
            builder.OpenElement(0, "text");
            builder.AddAttribute(1, "x", F(PL - 6));
            builder.AddAttribute(2, "y", F(y + 4));
            builder.AddAttribute(3, "class", "tm-stock-chart__axis-label");
            builder.AddAttribute(4, "text-anchor", "end");
            builder.AddContent(5, F(val));
            builder.CloseElement();
        }
        // X-axis labels (every nth to avoid overlap)
        int step = Math.Max(1, Data.Count / 6);
        for (int i = 0; i < Data.Count; i += step)
        {
            var x = ScaleX(i);
            builder.OpenElement(0, "text");
            builder.AddAttribute(1, "x", F(x));
            builder.AddAttribute(2, "y", F(PT + CH + 16));
            builder.AddAttribute(3, "class", "tm-stock-chart__axis-label");
            builder.AddAttribute(4, "text-anchor", "middle");
            builder.AddContent(5, Data[i].Date.ToString("MM/dd"));
            builder.CloseElement();
        }
    };

    private RenderFragment RenderCandlesticks => builder =>
    {
        var bw = CW / (double)Math.Max(Data.Count, 1) * 0.6;
        for (int i = 0; i < Data.Count; i++)
        {
            var d = Data[i];
            var x = ScaleX(i);
            var isBull = d.Close >= d.Open;
            var color = isBull ? BullColor : BearColor;
            var bodyTop = ScaleY(Math.Max(d.Open, d.Close));
            var bodyBottom = ScaleY(Math.Min(d.Open, d.Close));
            var bodyH = Math.Max(1, bodyBottom - bodyTop);

            // Wick
            builder.OpenElement(0, "line");
            builder.AddAttribute(1, "x1", F(x));
            builder.AddAttribute(2, "y1", F(ScaleY(d.High)));
            builder.AddAttribute(3, "x2", F(x));
            builder.AddAttribute(4, "y2", F(ScaleY(d.Low)));
            builder.AddAttribute(5, "class", "tm-stock-chart__wick");
            builder.AddAttribute(6, "stroke", WickColor);
            builder.AddAttribute(7, "stroke-width", "1");
            builder.CloseElement();

            // Body
            builder.OpenElement(0, "rect");
            builder.AddAttribute(1, "x", F(x - bw / 2));
            builder.AddAttribute(2, "y", F(bodyTop));
            builder.AddAttribute(3, "width", F(bw));
            builder.AddAttribute(4, "height", F(bodyH));
            builder.AddAttribute(5, "class", "tm-stock-chart__body");
            builder.AddAttribute(6, "fill", color);
            builder.AddAttribute(7, "rx", "1");
            builder.CloseElement();
        }
    };

    private RenderFragment RenderOHLC => builder =>
    {
        var bw = CW / (double)Math.Max(Data.Count, 1) * 0.5;
        for (int i = 0; i < Data.Count; i++)
        {
            var d = Data[i];
            var x = ScaleX(i);
            var isBull = d.Close >= d.Open;
            var color = isBull ? BullColor : BearColor;

            // High-Low vertical line
            builder.OpenElement(0, "line");
            builder.AddAttribute(1, "x1", F(x));
            builder.AddAttribute(2, "y1", F(ScaleY(d.High)));
            builder.AddAttribute(3, "x2", F(x));
            builder.AddAttribute(4, "y2", F(ScaleY(d.Low)));
            builder.AddAttribute(5, "class", "tm-stock-chart__ohlc");
            builder.AddAttribute(6, "stroke", color);
            builder.AddAttribute(7, "stroke-width", "2");
            builder.CloseElement();

            // Open tick (left)
            builder.OpenElement(0, "line");
            builder.AddAttribute(1, "x1", F(x - bw / 2));
            builder.AddAttribute(2, "y1", F(ScaleY(d.Open)));
            builder.AddAttribute(3, "x2", F(x));
            builder.AddAttribute(4, "y2", F(ScaleY(d.Open)));
            builder.AddAttribute(5, "class", "tm-stock-chart__ohlc");
            builder.AddAttribute(6, "stroke", color);
            builder.AddAttribute(7, "stroke-width", "2");
            builder.CloseElement();

            // Close tick (right)
            builder.OpenElement(0, "line");
            builder.AddAttribute(1, "x1", F(x));
            builder.AddAttribute(2, "y1", F(ScaleY(d.Close)));
            builder.AddAttribute(3, "x2", F(x + bw / 2));
            builder.AddAttribute(4, "y2", F(ScaleY(d.Close)));
            builder.AddAttribute(5, "class", "tm-stock-chart__ohlc");
            builder.AddAttribute(6, "stroke", color);
            builder.AddAttribute(7, "stroke-width", "2");
            builder.CloseElement();
        }
    };

    private RenderFragment RenderLine => builder =>
    {
        var pts = string.Join(" ", Data.Select((d, i) => $"{F(ScaleX(i))},{F(ScaleY(d.Close))}"));
        builder.OpenElement(0, "polyline");
        builder.AddAttribute(1, "points", pts);
        builder.AddAttribute(2, "class", "tm-stock-chart__line");
        builder.AddAttribute(3, "fill", "none");
        builder.AddAttribute(4, "stroke", "var(--tm-chart-series-1)");
        builder.AddAttribute(5, "stroke-width", "2");
        builder.AddAttribute(6, "stroke-linejoin", "round");
        builder.AddAttribute(7, "stroke-linecap", "round");
        builder.CloseElement();

        // Data points
        for (int i = 0; i < Data.Count; i++)
        {
            builder.OpenElement(0, "circle");
            builder.AddAttribute(1, "cx", F(ScaleX(i)));
            builder.AddAttribute(2, "cy", F(ScaleY(Data[i].Close)));
            builder.AddAttribute(3, "r", "3");
            builder.AddAttribute(4, "class", "tm-stock-chart__point");
            builder.AddAttribute(5, "fill", "var(--tm-chart-series-1)");
            builder.CloseElement();
        }
    };

    private RenderFragment RenderVolume => builder =>
    {
        if (MaxVolume <= 0) return;
        var vTop = SvgH - PB - VolumeH;
        var barW = CW / (double)Math.Max(Data.Count, 1) * 0.7;
        for (int i = 0; i < Data.Count; i++)
        {
            var d = Data[i];
            if (d.Volume is null or <= 0) continue;
            var h = (d.Volume.Value / MaxVolume) * VolumeH;
            var x = ScaleX(i);
            var isBull = d.Close >= d.Open;
            var color = isBull ? BullColor : BearColor;

            builder.OpenElement(0, "rect");
            builder.AddAttribute(1, "x", F(x - barW / 2));
            builder.AddAttribute(2, "y", F(vTop + VolumeH - h));
            builder.AddAttribute(3, "width", F(barW));
            builder.AddAttribute(4, "height", F(h));
            builder.AddAttribute(5, "class", "tm-stock-chart__volume");
            builder.AddAttribute(6, "fill", color);
            builder.AddAttribute(7, "opacity", "0.5");
            builder.CloseElement();
        }
    };
}
