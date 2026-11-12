using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Common;

namespace Tempo.Blazor.Components.Charts;

public enum SparklineType
{
    Line,
    Bar,
    Area,
    Pie
}

/// <summary>
/// Compact inline SVG sparkline. Renders a Line, Bar, Area or Pie mini-chart over one series and
/// shows an empty state when no data is supplied.
/// </summary>
/// <remarks>
/// Plan ff043b2e, phase 3 adds the opt-in extras: a text alternative that summarizes the series
/// (<see cref="AriaLabel"/>), a colour override (<see cref="Color"/>), the min/max caption
/// (<see cref="ShowMinMax"/>), the last-value marker (<see cref="ShowLastPoint"/>) and a
/// pointer/keyboard tooltip (<see cref="ShowTooltip"/>). Every default keeps the pre-2.9.1 markup,
/// so an existing host renders exactly what it rendered before.
/// </remarks>
public partial class TmSparkline : TmComponentBase
{
    /// <summary>The series to plot, in display order. An empty (or null) series renders the empty state.</summary>
    [Parameter] public double[] Data { get; set; } = [];

    /// <summary>Which mini-chart to draw. Defaults to <see cref="SparklineType.Line"/>.</summary>
    [Parameter] public SparklineType Type { get; set; } = SparklineType.Line;

    /// <summary>CSS height of the sparkline box. Defaults to <c>"40px"</c>.</summary>
    [Parameter] public string? Height { get; set; } = "40px";

    /// <summary>CSS width of the sparkline box. Defaults to <c>"100%"</c>.</summary>
    [Parameter] public string? Width { get; set; } = "100%";

    /// <summary>Additional CSS classes on the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Root element of the sparkline box: <see cref="TmRootElement.Div"/> (default) or
    /// <see cref="TmRootElement.Span"/>. It exists so a sparkline placed inside PHRASING content — a
    /// whole-card <c>&lt;a&gt;</c>/<c>&lt;button&gt;</c> target such as <c>TmStatCard</c> — does not
    /// nest a block element in an inline one. <see cref="TmRootElement.Div"/> keeps the historical
    /// markup, so a host that never sets the parameter renders exactly what it rendered before; an
    /// undefined enum value falls back to <c>div</c> as well.
    /// </summary>
    [Parameter] public TmRootElement Element { get; set; } = TmRootElement.Div;

    /// <summary>True when the root must be a <c>&lt;span&gt;</c>.</summary>
    private bool IsSpanRoot => Element == TmRootElement.Span;

    /// <summary>
    /// Text alternative of the graphic. Null (default) builds a localized summary of the series —
    /// minimum, maximum and last value in the current culture — so a reader who cannot see the shape
    /// still gets the numbers. <see cref="SparklineType.Pie"/> ignores it no more than any other type:
    /// the summary describes the same series the slices are cut from.
    /// </summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>
    /// Stroke/fill colour of a Line, Bar or Area sparkline (any CSS colour value, typically a
    /// <c>var(--tm-*)</c> token). Null (default) keeps the built-in series colour
    /// (<c>--tm-chart-series-1</c>, theme-relative since TG-002). A <see cref="SparklineType.Pie"/>
    /// keeps its categorical palette and ignores this.
    /// </summary>
    [Parameter] public string? Color { get; set; }

    /// <summary>
    /// Shows a small <c>min … max</c> caption in the sparkline's corner. It is
    /// <c>aria-hidden</c> — the same two numbers already live in <see cref="AriaLabel"/>'s summary, so
    /// announcing them twice would be a duplicate. Default false.
    /// </summary>
    [Parameter] public bool ShowMinMax { get; set; }

    /// <summary>
    /// Marks the final value with a ring, on Line and Area sparklines (a bar already ends visibly and
    /// a pie has no "last" slice). The marker is decorative — the value is in the
    /// <see cref="AriaLabel"/> summary. Default false.
    /// </summary>
    [Parameter] public bool ShowLastPoint { get; set; }

    /// <summary>
    /// Shows a tooltip for the hovered point or bar, and makes the graphic focusable so the arrow
    /// keys walk the points, Home/End jump to the first and last one and Escape hides the tooltip.
    /// The value is also announced through one polite live region. Pie sparklines ignore it. Default
    /// false (the pre-2.9.1 markup has no focus stop and no live region).
    /// </summary>
    [Parameter] public bool ShowTooltip { get; set; }

    private const int SvgW = 200;
    private const int SvgH = 60;
    private const int Pad = 4;

    /// <summary>The colour a Line/Bar/Area sparkline has always used when the host sets no <see cref="Color"/>.</summary>
    private const string DefaultColor = "var(--tm-chart-series-1)";

    private static int _hintSequence;

    private readonly int _hintInstance = Interlocked.Increment(ref _hintSequence);

    // Index of the point the tooltip shows (-1 = none) and whether it is hidden right now (the
    // pointer left the marker, or Escape dismissed it while focus stays on the graphic).
    private int _activeIndex = -1;
    private bool _hidden;
    private double[]? _lastData;

    private string CssClass => $"tm-sparkline{(string.IsNullOrEmpty(Class) ? "" : " " + Class)}";
    private string WrapperStyle => $"width:{Width};height:{Height};";

    private double MaxVal => Data.Length > 0 ? Data.Max() : 0;
    private double MinVal => Data.Length > 0 ? Data.Min() : 0;
    private double Range => MaxVal - MinVal;

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    // Tooltip text follows the reader's culture; SVG coordinates stay invariant.
    private static string C(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);

    private string StrokeColor => string.IsNullOrEmpty(Color) ? DefaultColor : Color;

    /// <summary>True when the host opted into the tooltip and the drawn type can carry one.</summary>
    private bool TooltipEnabled => ShowTooltip
        && Data is { Length: > 0 }
        && Type is SparklineType.Line or SparklineType.Area or SparklineType.Bar;

    private string HintId => $"tm-sparkline-hint-{_hintInstance}";

    private string? TooltipTabIndex => TooltipEnabled ? "0" : null;

    private string? TooltipKeyshortcuts => TooltipEnabled ? "ArrowLeft ArrowRight Home End Escape" : null;

    private string? TooltipDescribedBy => TooltipEnabled ? HintId : null;

    private int ActiveIndex => _hidden ? -1 : _activeIndex;

    /// <summary>The graphic's text alternative: the host's label, or the localized series summary.</summary>
    private string ResolvedAriaLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AriaLabel)) return AriaLabel!;
            if (Data is not { Length: > 0 }) return Loc["TmSparkline_AriaLabel"];

            return string.Format(
                CultureInfo.CurrentCulture,
                Loc["TmSparkline_Summary"],
                C(MinVal),
                C(MaxVal),
                C(Data[^1]));
        }
    }

    private string MinMaxText => string.Format(
        CultureInfo.CurrentCulture, Loc["TmSparkline_MinMax"], C(MinVal), C(MaxVal));

    private string ActiveValueText =>
        ActiveIndex >= 0 && ActiveIndex < Data.Length ? C(Data[ActiveIndex]) : string.Empty;

    /// <summary>
    /// The ONE live message: "Point n of m: value". Empty while nothing is highlighted, so a fresh
    /// sparkline announces nothing.
    /// </summary>
    private string LiveText => ActiveIndex < 0 || ActiveIndex >= Data.Length
        ? string.Empty
        : string.Format(
            CultureInfo.CurrentCulture,
            Loc["TmSparkline_PointAnnouncement"],
            ActiveIndex + 1,
            Data.Length,
            C(Data[ActiveIndex]));

    private double ScaleX(int index)
    {
        if (Data.Length <= 1) return SvgW / 2.0;
        return Pad + (index * (SvgW - 2 * Pad) / (double)(Data.Length - 1));
    }

    private double ScaleY(double value)
    {
        if (Range <= 0) return SvgH - Pad;
        return Pad + (SvgH - 2 * Pad) - ((value - MinVal) / Range) * (SvgH - 2 * Pad);
    }

    /// <summary>The categorical palette a Pie sparkline slices through — the shared chart series tokens.</summary>
    private static string PaletteColor(int index) => ChartSeriesTokens.Series(index);

    // ── Tooltip interaction ──

    private void HoverPoint(int index)
    {
        if (!TooltipEnabled) return;
        if (_activeIndex == index && !_hidden) return;

        _activeIndex = index;
        _hidden = false;
    }

    /// <summary>
    /// Hides the card only when the marker that reports the leave is STILL the active one. A leave and
    /// an enter deliver two events whose order is not guaranteed across the JS→.NET boundary, so an
    /// unguarded hide can blank the card of the marker the pointer just entered.
    /// </summary>
    private void ClearPoint(int index)
    {
        if (!TooltipEnabled || _activeIndex != index) return;
        _hidden = true;
    }

    private void ResetPoint()
    {
        _activeIndex = -1;
        _hidden = false;
    }

    /// <summary>A new data instance drops the cursor: the old index may not even exist any more.</summary>
    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_lastData, Data))
        {
            ResetPoint();
            _lastData = Data;
        }
    }

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (!TooltipEnabled) return;

        switch (args.Key)
        {
            case "ArrowRight":
                MovePoint(1);
                break;
            case "ArrowLeft":
                MovePoint(-1);
                break;
            case "Home":
                SetPoint(0);
                break;
            case "End":
                SetPoint(Data.Length - 1);
                break;
            case "Escape":
                _hidden = true;
                break;
        }
    }

    private void MovePoint(int delta)
    {
        // The first press lands on an edge, never on a neighbour of a cursor that does not exist yet.
        SetPoint(_activeIndex < 0
            ? delta >= 0 ? 0 : Data.Length - 1
            : Math.Clamp(_activeIndex + delta, 0, Data.Length - 1));
    }

    private void SetPoint(int index)
    {
        if (index < 0 || index >= Data.Length) return;

        _activeIndex = index;
        _hidden = false;
    }

    // ── Rendering ──

    private RenderFragment RenderLine => builder =>
    {
        var pts = string.Join(" ", Data.Select((d, i) => $"{F(ScaleX(i))},{F(ScaleY(d))}"));
        builder.OpenElement(0, "polyline");
        builder.AddAttribute(1, "points", pts);
        builder.AddAttribute(2, "class", "tm-sparkline__line");
        builder.AddAttribute(3, "fill", "none");
        builder.AddAttribute(4, "stroke", StrokeColor);
        builder.AddAttribute(5, "stroke-width", "2");
        builder.AddAttribute(6, "stroke-linecap", "round");
        builder.AddAttribute(7, "stroke-linejoin", "round");
        builder.CloseElement();

        BuildPoints(builder);
    };

    /// <summary>
    /// One marker per value. The same circles carry the hover target, so a Line sparkline shows them
    /// always (pre-2.9.1 behaviour) while an Area sparkline adds them only when the tooltip is on.
    /// Each marker keeps its <c>&lt;title&gt;</c> so a host without the tooltip still gets the native
    /// browser tooltip.
    /// </summary>
    private void BuildPoints(RenderTreeBuilder builder)
    {
        for (var i = 0; i < Data.Length; i++)
        {
            var index = i;
            var active = TooltipEnabled && ActiveIndex == index;

            builder.OpenElement(0, "circle");
            builder.AddAttribute(1, "class", active
                ? "tm-sparkline__point tm-sparkline__point--active"
                : "tm-sparkline__point");
            builder.AddAttribute(2, "data-index", index.ToString(CultureInfo.InvariantCulture));
            builder.AddAttribute(3, "cx", F(ScaleX(index)));
            builder.AddAttribute(4, "cy", F(ScaleY(Data[index])));
            builder.AddAttribute(5, "r", active ? "3" : "2");
            builder.AddAttribute(6, "fill", StrokeColor);
            if (TooltipEnabled)
            {
                builder.AddAttribute(7, "onmouseover",
                    EventCallback.Factory.Create<MouseEventArgs>(this, () => HoverPoint(index)));
                builder.AddAttribute(8, "onmouseout",
                    EventCallback.Factory.Create<MouseEventArgs>(this, () => ClearPoint(index)));
            }

            builder.OpenElement(9, "title");
            builder.AddContent(10, F(Data[index]));
            builder.CloseElement();
            builder.CloseElement();
        }
    }

    private RenderFragment RenderBars => builder =>
    {
        var bw = (SvgW - 2 * Pad) / (double)Math.Max(Data.Length, 1) * 0.8;
        var step = (SvgW - 2 * Pad) / (double)Math.Max(Data.Length, 1);
        for (int i = 0; i < Data.Length; i++)
        {
            var index = i;
            var h = ((Data[i] - MinVal) / Math.Max(Range, 1)) * (SvgH - 2 * Pad);
            var x = Pad + i * step + (step - bw) / 2;
            var y = SvgH - Pad - h;
            var active = TooltipEnabled && ActiveIndex == index;

            builder.OpenElement(0, "rect");
            builder.AddAttribute(1, "x", F(x));
            builder.AddAttribute(2, "y", F(y));
            builder.AddAttribute(3, "width", F(bw));
            builder.AddAttribute(4, "height", F(Math.Max(1, h)));
            builder.AddAttribute(5, "class", active
                ? "tm-sparkline__bar tm-sparkline__bar--active"
                : "tm-sparkline__bar");
            builder.AddAttribute(6, "data-index", index.ToString(CultureInfo.InvariantCulture));
            builder.AddAttribute(7, "fill", StrokeColor);
            builder.AddAttribute(8, "rx", "1");
            if (TooltipEnabled)
            {
                builder.AddAttribute(9, "onmouseover",
                    EventCallback.Factory.Create<MouseEventArgs>(this, () => HoverPoint(index)));
                builder.AddAttribute(10, "onmouseout",
                    EventCallback.Factory.Create<MouseEventArgs>(this, () => ClearPoint(index)));
            }

            builder.OpenElement(11, "title");
            builder.AddContent(12, F(Data[index]));
            builder.CloseElement();
            builder.CloseElement();
        }
    };

    private RenderFragment RenderArea => builder =>
    {
        var pts = string.Join(" ", Data.Select((d, i) => $"{F(ScaleX(i))},{F(ScaleY(d))}"));
        var path = $"M {F(ScaleX(0))},{F(SvgH - Pad)} L {pts} L {F(ScaleX(Data.Length - 1))},{F(SvgH - Pad)} Z";

        builder.OpenElement(0, "path");
        builder.AddAttribute(1, "d", path);
        builder.AddAttribute(2, "class", "tm-sparkline__area");
        builder.AddAttribute(3, "fill", StrokeColor);
        builder.AddAttribute(4, "opacity", "0.25");
        builder.CloseElement();

        builder.OpenElement(5, "polyline");
        builder.AddAttribute(6, "points", pts);
        builder.AddAttribute(7, "class", "tm-sparkline__line");
        builder.AddAttribute(8, "fill", "none");
        builder.AddAttribute(9, "stroke", StrokeColor);
        builder.AddAttribute(10, "stroke-width", "2");
        builder.CloseElement();

        // An area has no markers of its own; the tooltip needs a target per value, so they appear
        // only when the host asked for it (the default markup stays a bare fill + line).
        if (TooltipEnabled)
        {
            BuildPoints(builder);
        }
    };

    private RenderFragment RenderPie => builder =>
    {
        var total = Data.Sum();
        if (total <= 0) return;

        double cx = SvgW / 2.0;
        double cy = SvgH / 2.0;
        double r = Math.Min(SvgW, SvgH) / 2.0 - Pad;
        double startAngle = -Math.PI / 2;

        for (int i = 0; i < Data.Length; i++)
        {
            var angle = (Data[i] / total) * 2 * Math.PI;
            var endAngle = startAngle + angle;

            var x1 = cx + r * Math.Cos(startAngle);
            var y1 = cy + r * Math.Sin(startAngle);
            var x2 = cx + r * Math.Cos(endAngle);
            var y2 = cy + r * Math.Sin(endAngle);
            var largeArc = angle > Math.PI ? 1 : 0;

            var d = $"M {F(cx)},{F(cy)} L {F(x1)},{F(y1)} A {F(r)},{F(r)} 0 {largeArc} 1 {F(x2)},{F(y2)} Z";

            builder.OpenElement(0, "path");
            builder.AddAttribute(1, "d", d);
            builder.AddAttribute(2, "class", "tm-sparkline__slice");
            builder.AddAttribute(3, "fill", PaletteColor(i));
            builder.AddAttribute(4, "stroke", "var(--tm-bg-surface)");
            builder.AddAttribute(5, "stroke-width", "1");
            builder.OpenElement(6, "title");
            builder.AddContent(7, F(Data[i]));
            builder.CloseElement();
            builder.CloseElement();

            startAngle = endAngle;
        }
    };

    /// <summary>
    /// The last-value ring. It carries no text: the value is in the
    /// <see cref="ResolvedAriaLabel"/> summary, so it stays out of the accessibility tree.
    /// </summary>
    private RenderFragment RenderLastPoint => builder =>
    {
        var index = Data.Length - 1;
        builder.OpenElement(0, "circle");
        builder.AddAttribute(1, "class", "tm-sparkline__last-point");
        builder.AddAttribute(2, "data-value", C(Data[index]));
        builder.AddAttribute(3, "cx", F(ScaleX(index)));
        builder.AddAttribute(4, "cy", F(ScaleY(Data[index])));
        builder.AddAttribute(5, "r", "3.5");
        builder.AddAttribute(6, "fill", "none");
        builder.AddAttribute(7, "stroke", StrokeColor);
        builder.AddAttribute(8, "stroke-width", "2");
        builder.AddAttribute(9, "aria-hidden", "true");
        builder.CloseElement();
    };
}
