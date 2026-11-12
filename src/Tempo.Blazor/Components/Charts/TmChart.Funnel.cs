using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Tempo.Blazor.Components.Charts;

/// <summary>
/// The funnel half of <see cref="TmChart"/>: centered stage trapezoids with per-stage conversion, an
/// opt-in label placement (<see cref="FunnelLabelPlacement"/>), a token-driven label ink, a minimum
/// width for a zero-value stage, a type scale that compensates the viewBox scale and the
/// screen-reader stage table.
/// </summary>
/// <remarks>
/// <para>
/// <b>Placement.</b> <see cref="FunnelLabelPlacement.Auto"/> keeps the pre-2.11.0 placement rule and
/// geometry (labels now carry classes instead of inline fill/font-size): a band at least
/// <see cref="InsideLabelMinWidth"/> wide keeps its label centered inside, a thinner one moves it beside
/// the funnel. <see cref="FunnelLabelPlacement.Inside"/> and
/// <see cref="FunnelLabelPlacement.Outside"/> make that choice the host's — Outside narrows the funnel
/// to <see cref="OutsidePlotShare"/> of the plot width and moves it to the left edge of the plot, so the
/// labels own ONE gutter instead of a symmetric hole. A label that would still run past the viewBox
/// edge (a stage name longer than any gutter can hold) is truncated with an ellipsis and its full text
/// moves into the label's <c>&lt;title&gt;</c>.
/// </para>
/// <para>
/// <b>Ink.</b> No label carries an inline colour: the placement class selects a
/// <c>--tm-chart-funnel-label-*</c> or <c>--tm-chart-series-n-ink</c> token. An inside label on a
/// built-in series fill takes the fill's PAIRED ink token (the palette is theme-relative since TG-002);
/// an inside label on a host colour takes the ink that actually contrasts with its resolved value —
/// <c>--tm-chart-funnel-label-inside</c> on a dark fill, <c>…-inside-on-light</c> on a light one. An
/// outside label sits on the chart surface, so it uses the ordinary secondary text token. The colour
/// arithmetic for host colours lives in <see cref="ChartColorContrast"/>.
/// </para>
/// <para>
/// <b>Type scale.</b> The viewBox is 600 × 400 and the SVG meets into its box, so a funnel in a 672 × 320
/// card renders its type at 0.8 of the viewBox units — a 12-unit label reached the reader at ≈ 7 px on a
/// desktop card and ≈ 6 px on a phone. The type is therefore no longer a fixed unit count: the plot is a
/// SIZE container (<c>container-type: size</c>, see <c>_chart.css</c>) and the font size is
/// <c>max(token, token × 600 / 100cqw, token × 400 / 100cqh)</c> wherever the engine can divide a length
/// by a container length — so the rendered label never drops below
/// <c>--tm-chart-funnel-label-size</c> (12 px) and the value line never below
/// <c>--tm-chart-funnel-value-size</c> (11 px). An engine without that division keeps two discrete
/// viewBox steps (the <c>…-viewbox</c> and <c>…-narrow</c> tokens), which are dimensioned to reach the
/// same rendered floor at the scales a funnel actually runs at.
/// </para>
/// <para>
/// <b>Zero stages.</b> A value of 0 (and NaN, which the renderer treats as 0) is a real stage: the
/// band keeps <see cref="TmChart.FunnelMinSegmentWidth"/> viewBox units so the row and its
/// <c>0 (0 %)</c> label stay visible instead of vanishing from the funnel.
/// </para>
/// </remarks>
public partial class TmChart
{
    /// <summary>
    /// <see cref="ChartType.Funnel"/> only: where each stage's label is drawn. The default
    /// <see cref="FunnelLabelPlacement.Auto"/> keeps the pre-2.11.0 behavior — a label inside a band
    /// that is wide enough for it, beside the funnel otherwise. <see cref="FunnelLabelPlacement.Inside"/>
    /// always centers the label in the band, <see cref="FunnelLabelPlacement.Outside"/> always draws it
    /// in the reserved gutter to the right of the funnel. Ignored by every other chart type.
    /// </summary>
    [Parameter] public FunnelLabelPlacement FunnelLabelPlacement { get; set; } = FunnelLabelPlacement.Auto;

    /// <summary>
    /// <see cref="ChartType.Funnel"/> only: the minimum width, in viewBox units, that a stage's band
    /// keeps. A stage whose value is 0 (or NaN, which is drawn as 0) would otherwise collapse to a
    /// zero-width path and disappear from the funnel; the floor draws it as a thin, visible band that
    /// still carries its label. Defaults to 4; 0 restores the pre-2.11.0 zero-width band. Ignored by
    /// every other chart type.
    /// </summary>
    [Parameter] public double FunnelMinSegmentWidth { get; set; } = 4;

    /// <summary>The band width (viewBox units) at which a label still fits inside the stage.</summary>
    private const double InsideLabelMinWidth = 150;

    /// <summary>
    /// Share of the plot width the funnel keeps when the labels live in a gutter. 0.62 leaves ≈ 200
    /// viewBox units to the right of the widest band — enough for a long stage name at the narrow
    /// (container-query) type scale.
    /// </summary>
    private const double OutsidePlotShare = 0.62;

    /// <summary>
    /// The narrowest the funnel may get when the longest label asks for a wider gutter than
    /// <see cref="OutsidePlotShare"/> leaves. Below this the label is truncated instead: a funnel that
    /// keeps shrinking stops being the chart's main shape.
    /// </summary>
    private const double MinOutsidePlotShare = 0.45;

    /// <summary>The gap (viewBox units) between the widest side of a band and its outside label.</summary>
    private const double OutsideLabelGap = 10;

    /// <summary>Right-edge room (viewBox units) no label may use, so a glyph bearing never clips.</summary>
    private const double TextEdgeSafety = 4;

    /// <summary>The single character a truncated stage name ends with.</summary>
    private const string Ellipsis = "…";

    /// <summary>
    /// Mean advance width of one character, in em, used to estimate a line's width. Measured against the
    /// funnel's own type: a letter of a stage name advances ≈ 0.50 em, a digit or a parenthesis in a
    /// value line ≈ 0.56 em, so 0.6 rounds up and the estimate never promises room the glyphs then take.
    /// </summary>
    internal const double TextCharWidthEm = 0.6;

    /// <summary>
    /// The largest stage-label type the responsive scale reaches, in viewBox units — the
    /// <c>--tm-chart-funnel-label-size-narrow</c> token. The Outside gutter is reserved for the WORST
    /// case, because clipping happens in viewBox units whatever the container's rendered scale is.
    /// <c>TmChartFunnelLabelContrastTests</c> pins the pair.
    /// </summary>
    internal const double LabelWorstFontUnits = 23;

    /// <summary>The matching worst case for a value line (the <c>…-value-size-narrow</c> token).</summary>
    internal const double ValueWorstFontUnits = 21;

    private void BuildFunnel(RenderTreeBuilder b)
    {
        var ds = Data.Datasets[0];
        var stages = VisibleFunnelStages(ds);
        if (stages.Count == 0)
        {
            return;
        }

        var values = stages.Select(i => FunnelValue(ds, i)).ToArray();
        var labels = stages.Select(i => i < Data.Labels.Length ? Data.Labels[i] : "").ToArray();
        var first = values[0];
        var valueLines = new string?[values.Length];
        if (ShowValues)
        {
            for (var s = 0; s < values.Length; s++)
            {
                valueLines[s] = FunnelValueLine(values[s], first > 0 ? values[s] / first * 100 : 0);
            }
        }

        var max = values.Max();
        if (max <= 0)
        {
            max = 1;
        }

        var sh = CH / (double)values.Length;
        var plotW = FunnelPlotWidth(labels, valueLines);

        // Outside labels own a gutter, so the funnel gives up part of the plot width AND starts at the
        // plot's left edge: the gutter is one column of text, never a symmetric hole. Auto and Inside
        // keep the pre-2.11.0 centered geometry.
        var outside = FunnelLabelPlacement == FunnelLabelPlacement.Outside;
        var cx = outside ? PL + plotW / 2.0 : PL + CW / 2.0;
        var minWidth = double.IsFinite(FunnelMinSegmentWidth)
            ? Math.Clamp(FunnelMinSegmentWidth, 0, plotW)
            : 0;

        double WidthOf(double value) => Math.Max(plotW * (value / max), minWidth);

        for (var s = 0; s < values.Length; s++)
        {
            var i = stages[s];
            var topW = WidthOf(values[s]);
            var botW = WidthOf(s + 1 < values.Length ? values[s + 1] : values[s]);
            var yTop = PT + s * sh;
            var yBot = yTop + sh - 3;
            var fill = ResolveFillColor(ds, i, PaletteColor(i));
            var ci = i;

            b.OpenElement(0, "path");
            b.AddAttribute(1, "class", "tm-chart__funnel-segment");
            b.AddAttribute(2, "d",
                $"M {F(cx - topW / 2)} {F(yTop)} L {F(cx + topW / 2)} {F(yTop)} " +
                $"L {F(cx + botW / 2)} {F(yBot)} L {F(cx - botW / 2)} {F(yBot)} Z");
            b.AddAttribute(3, "fill", fill);
            b.AddAttribute(4, "data-index", ci.ToString(CultureInfo.InvariantCulture));
            b.AddAttribute(5, "data-value", F(values[s]));
            b.AddAttribute(6, "data-width", F(topW));
            b.AddAttribute(7, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => HandleClick(0, ci)));
            b.AddAttribute(8, "onmouseover",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => ShowTip(0, ci, cx, yTop)));
            b.AddAttribute(9, "onmouseout",
                EventCallback.Factory.Create<MouseEventArgs>(this, HideTip));
            b.CloseElement();

            var inside = FunnelLabelIsInside(topW);
            var anchor = inside ? "middle" : "start";

            // Outside labels are a COLUMN in the gutter (one x for every stage, right of the widest
            // band); an Auto outside label follows its own band, which is what "beside the stage" meant
            // before the placement existed.
            var textX = inside
                ? cx
                : outside
                    ? PL + plotW + OutsideLabelGap
                    : cx + Math.Max(topW, botW) / 2 + OutsideLabelGap;

            // An outside line is fitted to the gutter; an inside one is centered over a band that is at
            // least InsideLabelMinWidth wide, so it has the room without a truncation pass.
            var label = labels[s];
            var labelTruncated = false;
            string? valueLine = valueLines[s];
            var valueTruncated = false;
            if (!inside)
            {
                (label, labelTruncated) = FunnelTextFit(label, textX, LabelWorstFontUnits);
                if (valueLine is not null)
                {
                    (valueLine, valueTruncated) = FunnelTextFit(valueLine, textX, ValueWorstFontUnits);
                }
            }

            var ink = FunnelLabelInkClass(fill, inside);

            SvgFunnelLabel(
                b,
                ink,
                textX,
                valueLine is null ? yTop + sh / 2 + 4 : yTop + sh / 2,
                anchor,
                label,
                valueLine,
                FunnelLabelTitle(labels[s], valueLines[s], labelTruncated, valueTruncated));
        }
    }

    /// <summary>
    /// The stages the funnel actually draws: the indices <see cref="ChartData.Labels"/> and the first
    /// dataset share, minus the ones the interactive legend hid. BuildFunnel and the screen-reader table
    /// read the SAME list, so a table row can never describe a stage that is not on screen.
    /// </summary>
    private List<int> VisibleFunnelStages(ChartDataset dataset)
    {
        var stages = new List<int>();
        var count = Math.Min(Data.Labels.Length, dataset.Values.Length);
        for (var i = 0; i < count; i++)
        {
            if (!IsSegmentHidden(0, i))
            {
                stages.Add(i);
            }
        }

        return stages;
    }

    /// <summary>
    /// The plot width the funnel keeps. Auto and Inside keep the full chart width (the pre-2.11.0
    /// geometry). Outside reserves a right-hand gutter for the labels: the funnel keeps
    /// <see cref="OutsidePlotShare"/> of the width and gives up more only when the LONGEST stage label or
    /// value line would still run past the viewBox edge — the estimate is a plain average-advance
    /// measure, so the funnel stops at <see cref="MinOutsidePlotShare"/> and
    /// <see cref="FunnelTextFit"/> ends the text with an ellipsis instead.
    /// </summary>
    private double FunnelPlotWidth(string[] labels, string?[] valueLines)
    {
        if (FunnelLabelPlacement != FunnelLabelPlacement.Outside)
        {
            return CW;
        }

        var needed = 0d;
        for (var s = 0; s < labels.Length; s++)
        {
            needed = Math.Max(needed, FunnelTextEstimate(labels[s], LabelWorstFontUnits));
            if (valueLines[s] is { } line)
            {
                needed = Math.Max(needed, FunnelTextEstimate(line, ValueWorstFontUnits));
            }
        }

        // The label starts one gap past the funnel's widest band, which itself reaches PL + plotW.
        var room = SvgW - PL - OutsideLabelGap - TextEdgeSafety - needed;
        return Math.Clamp(Math.Min(CW * OutsidePlotShare, room), CW * MinOutsidePlotShare, CW);
    }

    /// <summary>Estimated advance width of one line, in viewBox units, at <paramref name="fontUnits"/>.</summary>
    internal static double FunnelTextEstimate(string? text, double fontUnits) =>
        (text?.Length ?? 0) * fontUnits * TextCharWidthEm;

    /// <summary>
    /// Truncates one line to the room left between <paramref name="x"/> and the viewBox edge, so an
    /// unbounded stage name ends in an ellipsis — with the full text in the label's tooltip — instead of
    /// running off the chart. The estimate is deliberately conservative, so this only fires for text no
    /// gutter can hold.
    /// </summary>
    private static (string Text, bool Truncated) FunnelTextFit(string text, double x, double fontUnits)
    {
        var room = SvgW - x - TextEdgeSafety;
        var perCharacter = fontUnits * TextCharWidthEm;
        if (room <= 0)
        {
            return (string.Empty, text.Length > 0);
        }

        if (text.Length * perCharacter <= room)
        {
            return (text, false);
        }

        var keep = Math.Clamp((int)Math.Floor(room / perCharacter) - 1, 0, text.Length);
        return (keep <= 0 ? Ellipsis : text[..keep] + Ellipsis, true);
    }

    /// <summary>
    /// The tooltip of a truncated label: the full stage name and/or value line, so a clipped name is
    /// still readable. <c>null</c> when nothing was truncated (a title on every stage would be noise).
    /// </summary>
    private static string? FunnelLabelTitle(string label, string? valueLine, bool labelTruncated,
        bool valueTruncated) => (labelTruncated, valueTruncated) switch
    {
        (true, true) => $"{label} — {valueLine}",
        (true, false) => label,
        (false, true) => valueLine,
        _ => null
    };

    /// <summary>Whether this stage's label is drawn inside its band.</summary>
    private bool FunnelLabelIsInside(double topW) => FunnelLabelPlacement switch
    {
        FunnelLabelPlacement.Inside => true,
        FunnelLabelPlacement.Outside => false,
        _ => topW >= InsideLabelMinWidth
    };

    /// <summary>
    /// The ink class of one funnel text node. An outside label is ordinary secondary text on the
    /// surface and needs no per-fill choice. An inside label takes the PAIRED series ink when the fill
    /// IS a built-in series token (the palette is theme-relative since TG-002, so the ink cannot be
    /// decided from a fixed lightness table); a host's own colour keeps the
    /// <see cref="ChartColorContrast"/> decision against its resolved value.
    /// </summary>
    private static string FunnelLabelInkClass(string fill, bool inside)
    {
        if (!inside)
        {
            return "tm-chart__funnel-label--outside";
        }

        if (ChartSeriesTokens.IndexOf(fill) is { } series)
        {
            return $"tm-chart__funnel-label--inside tm-chart__funnel-label--series-{series + 1}";
        }

        return ChartColorContrast.IsLight(fill)
            ? "tm-chart__funnel-label--inside tm-chart__funnel-label--on-light"
            : "tm-chart__funnel-label--inside";
    }

    /// <summary>
    /// A funnel stage label: ONE <c>&lt;text&gt;</c> whose lines are <c>&lt;tspan&gt;</c>s, so the gap
    /// between the stage name and its value follows the type scale the container resolves instead of a
    /// pair of fixed baselines. Everything except the position is a CLASS: the ink and both font sizes
    /// are tokens, so a theme retunes the ink and a narrow container grows the type.
    /// </summary>
    private static void SvgFunnelLabel(
        RenderTreeBuilder b,
        string ink,
        double x,
        double y,
        string anchor,
        string label,
        string? valueLine,
        string? title)
    {
        b.OpenElement(0, "text");
        b.AddAttribute(1, "class", $"tm-chart__funnel-text {ink}");
        b.AddAttribute(2, "x", F(x));
        b.AddAttribute(3, "y", F(y));
        b.AddAttribute(4, "text-anchor", anchor);
        b.AddAttribute(5, "style", "pointer-events:none;");

        if (!string.IsNullOrEmpty(title))
        {
            b.OpenElement(6, "title");
            b.AddContent(7, title);
            b.CloseElement();
        }

        b.OpenElement(8, "tspan");
        b.AddAttribute(9, "class", $"tm-chart__funnel-label {ink}");
        b.AddAttribute(10, "x", F(x));
        if (valueLine is not null)
        {
            // The two lines are anchored on the stage's middle: the name sits a touch above it, the
            // value a full line below, and both distances follow the resolved font size.
            b.AddAttribute(11, "dy", "-0.2em");
        }

        b.AddContent(12, label);
        b.CloseElement();

        if (valueLine is not null)
        {
            b.OpenElement(13, "tspan");
            b.AddAttribute(14, "class", $"tm-chart__funnel-value {ink}");
            b.AddAttribute(15, "x", F(x));
            b.AddAttribute(16, "dy", "1.25em");
            b.AddContent(17, valueLine);
            b.CloseElement();
        }

        b.CloseElement();
    }

    /// <summary>
    /// The stages that are actually drawn, formatted for the screen-reader table: the stage label, its
    /// value and its conversion from the funnel's first stage as <c>value / first (pct %)</c>.
    /// </summary>
    private List<(string Label, string Value, string Conversion)> FunnelTableRows()
    {
        var rows = new List<(string, string, string)>();
        if (Data is null || Data.Datasets.Length == 0)
        {
            return rows;
        }

        var ds = Data.Datasets[0];
        var stages = VisibleFunnelStages(ds);
        if (stages.Count == 0)
        {
            return rows;
        }

        var first = FunnelValue(ds, stages[0]);
        foreach (var i in stages)
        {
            var value = FunnelValue(ds, i);
            var pct = first > 0 ? value / first * 100 : 0;
            var conversion = string.Format(
                CultureInfo.CurrentCulture,
                Loc["TmChart_FunnelConversionValue"],
                FunnelNumber(value), FunnelNumber(first), FunnelNumber(pct));
            rows.Add((i < Data.Labels.Length ? Data.Labels[i] : "", FunnelNumber(value), conversion));
        }

        return rows;
    }

    /// <summary>
    /// One number the way the reader's culture writes it: thousands grouped, at most two decimals — so a
    /// conversion reads <c>49,19 %</c> under <c>cs-CZ</c> and <c>49.19 %</c> under <c>en-US</c>, and an
    /// exact half stays <c>12,5 %</c> rather than <c>12,50 %</c>.
    /// </summary>
    private static string FunnelNumber(double value) =>
        value.ToString("#,0.##", CultureInfo.CurrentCulture);

    /// <summary>The value line of one stage: the value and its conversion from the first stage.</summary>
    private static string FunnelValueLine(double value, double pct) =>
        $"{FunnelNumber(value)} ({FunnelNumber(pct)}\u00A0%)";

    /// <summary>
    /// One stage's value as the funnel draws it: a non-finite value (NaN/Infinity) and a negative one
    /// both count as 0, so neither the SVG path nor the label ever receives <c>NaN</c>.
    /// </summary>
    private static double FunnelValue(ChartDataset dataset, int index)
    {
        var value = dataset.Values[index];
        return double.IsFinite(value) && value > 0 ? value : 0;
    }
}
