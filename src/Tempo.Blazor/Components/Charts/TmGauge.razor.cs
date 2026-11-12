using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Charts;

public partial class TmGauge : ComponentBase
{
    [Parameter] public GaugeType Type { get; set; } = GaugeType.Arc;
    [Parameter] public double Value { get; set; }
    [Parameter] public double Min { get; set; } = 0;
    [Parameter] public double Max { get; set; } = 100;
    [Parameter] public IReadOnlyList<GaugeRange> Ranges { get; set; } = [];
    [Parameter] public bool ShowValue { get; set; } = true;
    [Parameter] public string? LabelFormat { get; set; }
    [Parameter] public bool Animated { get; set; }
    [Parameter] public string? Width { get; set; } = "200px";
    [Parameter] public string? Height { get; set; } = "160px";
    [Parameter] public string? Class { get; set; }

    private const int SvgW = 200;
    private const int SvgH = 160;

    private string CssClass => $"tm-gauge{(Animated ? " tm-gauge--animated" : "")}{(string.IsNullOrEmpty(Class) ? "" : " " + Class)}";
    private string WrapperStyle => $"width:{Width};height:{Height};";

    private double ClampedValue => Math.Max(Min, Math.Min(Max, Value));
    private double Ratio => Max <= Min ? 0 : (ClampedValue - Min) / (Max - Min);

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private string FormattedValue => string.IsNullOrEmpty(LabelFormat)
        ? F(ClampedValue)
        : string.Format(CultureInfo.InvariantCulture, LabelFormat, ClampedValue);

    // ── Arc & Circular helpers ────────────────────────────────────────────

    private static (double X, double Y) Polar(double cx, double cy, double r, double angleDeg)
    {
        var rad = angleDeg * Math.PI / 180;
        return (cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
    }

    private static string ArcPath(double cx, double cy, double r, double startDeg, double endDeg, bool largeArc = false)
    {
        var (x1, y1) = Polar(cx, cy, r, startDeg);
        var (x2, y2) = Polar(cx, cy, r, endDeg);
        var large = Math.Abs(endDeg - startDeg) > 180 ? 1 : 0;
        if (largeArc) large = 1;
        var sweep = endDeg > startDeg ? 1 : 0;
        return $"M {F(x1)},{F(y1)} A {F(r)},{F(r)} 0 {large} {sweep} {F(x2)},{F(y2)}";
    }

    // ── Render Arc ────────────────────────────────────────────────────────

    private RenderFragment RenderArc => builder =>
    {
        double cx = SvgW / 2.0;
        double cy = SvgH * 0.75;
        double r = 70;
        double startAngle = -135;
        double endAngle = 135;
        double trackSweep = endAngle - startAngle;

        // Track arc
        builder.OpenElement(0, "path");
        builder.AddAttribute(1, "d", ArcPath(cx, cy, r, startAngle, endAngle, true));
        builder.AddAttribute(2, "class", "tm-gauge__track");
        builder.AddAttribute(3, "fill", "none");
        builder.AddAttribute(4, "stroke", "var(--tm-color-gray-200)");
        builder.AddAttribute(5, "stroke-width", "12");
        builder.AddAttribute(6, "stroke-linecap", "round");
        builder.CloseElement();

        // Ranges
        if (Ranges.Count > 0)
        {
            foreach (var range in Ranges)
            {
                var rStart = startAngle + (range.From - Min) / (Max - Min) * trackSweep;
                var rEnd = startAngle + (range.To - Min) / (Max - Min) * trackSweep;
                builder.OpenElement(0, "path");
                builder.AddAttribute(1, "d", ArcPath(cx, cy, r, rStart, rEnd));
                builder.AddAttribute(2, "class", "tm-gauge__range");
                builder.AddAttribute(3, "fill", "none");
                builder.AddAttribute(4, "stroke", range.Color);
                builder.AddAttribute(5, "stroke-width", "12");
                builder.AddAttribute(6, "stroke-linecap", "butt");
                builder.CloseElement();
            }
        }

        // Value arc
        double valEnd = startAngle + Ratio * trackSweep;
        builder.OpenElement(0, "path");
        builder.AddAttribute(1, "d", ArcPath(cx, cy, r, startAngle, valEnd));
        builder.AddAttribute(2, "class", "tm-gauge__fill");
        builder.AddAttribute(3, "fill", "none");
        builder.AddAttribute(4, "stroke", "var(--tm-chart-series-1)");
        builder.AddAttribute(5, "stroke-width", "12");
        builder.AddAttribute(6, "stroke-linecap", "round");
        builder.CloseElement();

        // Pointer
        var (px, py) = Polar(cx, cy, r, valEnd);
        builder.OpenElement(0, "circle");
        builder.AddAttribute(1, "cx", F(px));
        builder.AddAttribute(2, "cy", F(py));
        builder.AddAttribute(3, "r", "6");
        builder.AddAttribute(4, "class", "tm-gauge__pointer");
        builder.AddAttribute(5, "fill", "var(--tm-chart-series-1)");
        builder.CloseElement();

        // Center value
        if (ShowValue)
        {
            builder.OpenElement(0, "text");
            builder.AddAttribute(1, "x", F(cx));
            builder.AddAttribute(2, "y", F(cy - 5));
            builder.AddAttribute(3, "class", "tm-gauge__value");
            builder.AddAttribute(4, "text-anchor", "middle");
            builder.AddAttribute(5, "dominant-baseline", "middle");
            builder.AddAttribute(6, "font-size", "22");
            builder.AddAttribute(7, "font-weight", "600");
            builder.AddAttribute(8, "fill", "var(--tm-text-primary)");
            builder.AddContent(9, FormattedValue);
            builder.CloseElement();
        }
    };

    // ── Render Circular ───────────────────────────────────────────────────

    private RenderFragment RenderCircular => builder =>
    {
        double cx = SvgW / 2.0;
        double cy = SvgH / 2.0 + 10;
        double r = 55;

        // Track circle
        builder.OpenElement(0, "circle");
        builder.AddAttribute(1, "cx", F(cx));
        builder.AddAttribute(2, "cy", F(cy));
        builder.AddAttribute(3, "r", F(r));
        builder.AddAttribute(4, "class", "tm-gauge__track");
        builder.AddAttribute(5, "fill", "none");
        builder.AddAttribute(6, "stroke", "var(--tm-color-gray-200)");
        builder.AddAttribute(7, "stroke-width", "10");
        builder.CloseElement();

        // Ranges
        if (Ranges.Count > 0)
        {
            foreach (var range in Ranges)
            {
                var rStart = -90 + (range.From - Min) / (Max - Min) * 360;
                var rEnd = -90 + (range.To - Min) / (Max - Min) * 360;
                builder.OpenElement(0, "path");
                builder.AddAttribute(1, "d", ArcPath(cx, cy, r, rStart, rEnd));
                builder.AddAttribute(2, "class", "tm-gauge__range");
                builder.AddAttribute(3, "fill", "none");
                builder.AddAttribute(4, "stroke", range.Color);
                builder.AddAttribute(5, "stroke-width", "10");
                builder.CloseElement();
            }
        }

        // Value arc
        double valEnd = -90 + Ratio * 360;
        builder.OpenElement(0, "path");
        builder.AddAttribute(1, "d", ArcPath(cx, cy, r, -90, valEnd));
        builder.AddAttribute(2, "class", "tm-gauge__fill");
        builder.AddAttribute(3, "fill", "none");
        builder.AddAttribute(4, "stroke", "var(--tm-chart-series-1)");
        builder.AddAttribute(5, "stroke-width", "10");
        builder.AddAttribute(6, "stroke-linecap", "round");
        builder.CloseElement();

        if (ShowValue)
        {
            builder.OpenElement(0, "text");
            builder.AddAttribute(1, "x", F(cx));
            builder.AddAttribute(2, "y", F(cy));
            builder.AddAttribute(3, "class", "tm-gauge__value");
            builder.AddAttribute(4, "text-anchor", "middle");
            builder.AddAttribute(5, "dominant-baseline", "middle");
            builder.AddAttribute(6, "font-size", "20");
            builder.AddAttribute(7, "font-weight", "600");
            builder.AddAttribute(8, "fill", "var(--tm-text-primary)");
            builder.AddContent(9, FormattedValue);
            builder.CloseElement();
        }
    };

    // ── Render Linear ─────────────────────────────────────────────────────

    private RenderFragment RenderLinear => builder =>
    {
        double tx = 20;
        double ty = SvgH / 2.0 - 8;
        double tw = SvgW - 40;
        double th = 16;
        double rx = 8;

        // Track
        builder.OpenElement(0, "rect");
        builder.AddAttribute(1, "x", F(tx));
        builder.AddAttribute(2, "y", F(ty));
        builder.AddAttribute(3, "width", F(tw));
        builder.AddAttribute(4, "height", F(th));
        builder.AddAttribute(5, "class", "tm-gauge__track");
        builder.AddAttribute(6, "fill", "var(--tm-color-gray-200)");
        builder.AddAttribute(7, "rx", F(rx));
        builder.CloseElement();

        // Ranges
        if (Ranges.Count > 0)
        {
            foreach (var range in Ranges)
            {
                var rX = tx + (range.From - Min) / (Max - Min) * tw;
                var rW = (range.To - range.From) / (Max - Min) * tw;
                builder.OpenElement(0, "rect");
                builder.AddAttribute(1, "x", F(rX));
                builder.AddAttribute(2, "y", F(ty));
                builder.AddAttribute(3, "width", F(Math.Max(0, rW)));
                builder.AddAttribute(4, "height", F(th));
                builder.AddAttribute(5, "class", "tm-gauge__range");
                builder.AddAttribute(6, "fill", range.Color);
                builder.AddAttribute(7, "rx", F(rx));
                builder.CloseElement();
            }
        }

        // Fill
        var fw = Ratio * tw;
        builder.OpenElement(0, "rect");
        builder.AddAttribute(1, "x", F(tx));
        builder.AddAttribute(2, "y", F(ty));
        builder.AddAttribute(3, "width", F(Math.Max(0, fw)));
        builder.AddAttribute(4, "height", F(th));
        builder.AddAttribute(5, "class", "tm-gauge__fill");
        builder.AddAttribute(6, "fill", "var(--tm-chart-series-1)");
        builder.AddAttribute(7, "rx", F(rx));
        builder.CloseElement();

        // Labels
        if (ShowValue)
        {
            builder.OpenElement(0, "text");
            builder.AddAttribute(1, "x", F(tx + fw));
            builder.AddAttribute(2, "y", F(ty - 8));
            builder.AddAttribute(3, "class", "tm-gauge__value");
            builder.AddAttribute(4, "text-anchor", "middle");
            builder.AddAttribute(5, "font-size", "14");
            builder.AddAttribute(6, "font-weight", "600");
            builder.AddAttribute(7, "fill", "var(--tm-text-primary)");
            builder.AddContent(8, FormattedValue);
            builder.CloseElement();
        }

        // Min/Max labels
        builder.OpenElement(0, "text");
        builder.AddAttribute(1, "x", F(tx));
        builder.AddAttribute(2, "y", F(ty + th + 16));
        builder.AddAttribute(3, "class", "tm-gauge__axis-label");
        builder.AddAttribute(4, "text-anchor", "start");
        builder.AddAttribute(5, "font-size", "10");
        builder.AddAttribute(6, "fill", "var(--tm-text-secondary)");
        builder.AddContent(7, F(Min));
        builder.CloseElement();

        builder.OpenElement(0, "text");
        builder.AddAttribute(1, "x", F(tx + tw));
        builder.AddAttribute(2, "y", F(ty + th + 16));
        builder.AddAttribute(3, "class", "tm-gauge__axis-label");
        builder.AddAttribute(4, "text-anchor", "end");
        builder.AddAttribute(5, "font-size", "10");
        builder.AddAttribute(6, "fill", "var(--tm-text-secondary)");
        builder.AddContent(7, F(Max));
        builder.CloseElement();
    };
}
