using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Tempo.Blazor.Components.Charts;

/// <summary>
/// The additive <see cref="ChartTooltipMode.Index"/> half of <see cref="TmChart"/> (plan ff043b2e,
/// phase 3): one full-height hover zone per category, a crosshair on the active category, a tooltip
/// that lists every series at that index, arrow-key navigation and ONE polite live region.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here runs unless the host opts in — <see cref="TooltipMode"/> defaults to
/// <see cref="ChartTooltipMode.Point"/>, so the pre-existing per-point tooltip keeps its handlers,
/// its markup and its announcements. The mode is honoured only by the types whose plot has a
/// category axis (<see cref="ChartType.Line"/>, <see cref="ChartType.Area"/>,
/// <see cref="ChartType.Bar"/>, <see cref="ChartType.StackedBar"/>,
/// <see cref="ChartType.StackedArea"/>); a pie, a funnel or a heatmap falls back to
/// <see cref="ChartTooltipMode.Point"/> rather than rendering half of a crosshair.
/// </para>
/// <para>
/// The hover zones paint ABOVE the marks (that is what makes one zone serve a whole category), so a
/// click lands on the zone, not on the bar underneath: in index mode <see cref="TmChart.OnSegmentClick"/>
/// reports the index once, carrying the FIRST visible series' value. That is the only way to keep one
/// category-wide hit area and a click contract at the same time.
/// </para>
/// </remarks>
public partial class TmChart
{
    // Geometry of the index tooltip card. The card is a foreignObject, so these are viewBox units:
    // the SVG scales as a whole, which is why a fixed card is the honest size for every host width.
    private const double IndexCardWidth = 168;
    private const double IndexCardHeadHeight = 18;
    private const double IndexCardRowHeight = 16;
    private const double IndexCardPadding = 8;
    private const double IndexCardGap = 10;

    private static int _indexHintSequence;

    private readonly int _indexHintInstance = Interlocked.Increment(ref _indexHintSequence);

    /// <summary>
    /// How the chart presents its tooltip. <see cref="ChartTooltipMode.Point"/> (default) keeps the
    /// per-point tooltip; <see cref="ChartTooltipMode.Index"/> adds a category crosshair, a hover zone
    /// per index, an all-series tooltip, arrow-key navigation and one polite live region. Only the
    /// line/area/bar/stacked types honour <see cref="ChartTooltipMode.Index"/>; every other type falls
    /// back to <see cref="ChartTooltipMode.Point"/>.
    /// </summary>
    [Parameter] public ChartTooltipMode TooltipMode { get; set; } = ChartTooltipMode.Point;

    /// <summary>
    /// Optional custom content of the index tooltip (<see cref="ChartTooltipMode.Index"/>); when null
    /// the built-in card lists the category label and one color dot, series label and value per visible
    /// series. The context also carries <see cref="ChartIndexTooltipContext.Total"/>.
    /// </summary>
    [Parameter] public RenderFragment<ChartIndexTooltipContext>? TooltipIndexTemplate { get; set; }

    // Where the pointer rests (-1 = outside), where the keyboard cursor stands (-1 = not used yet),
    // and whether Escape dismissed the current index while focus stays on the chart.
    private int _pointerOrdinal = -1;
    private int _keyboardOrdinal = -1;
    private bool _indexDismissed;

    // A tap on a touch screen has no hover: the click PINS the category so the browser's post-tap
    // "the pointer left" event cannot blank the card it just opened. A real hover, an arrow key or
    // Escape unpins it again.
    private int _pinnedOrdinal = -1;

    /// <summary>The id of the visually hidden keyboard hint the focusable SVG points at.</summary>
    private string IndexHintId => $"tm-chart-index-hint-{_indexHintInstance}";

    /// <summary>
    /// True when the host asked for the index tooltip AND the plot can honour it. A mode the chart
    /// cannot render degrades to <see cref="ChartTooltipMode.Point"/> instead of half-working.
    /// </summary>
    private bool IndexTooltipActive => ShowTooltip
        && TooltipMode == ChartTooltipMode.Index
        && Type is ChartType.Line or ChartType.Area or ChartType.Bar
            or ChartType.StackedBar or ChartType.StackedArea;

    private string? IndexTabIndex => IndexTooltipActive ? "0" : null;

    private string? IndexKeyshortcuts => IndexTooltipActive
        ? "ArrowLeft ArrowRight Home End Escape"
        : null;

    private string? IndexDescribedBy => IndexTooltipActive ? IndexHintId : null;

    /// <summary>The ordinal the crosshair stands on, or -1 when nothing is highlighted.</summary>
    private int ActiveIndexOrdinal
    {
        get
        {
            if (_indexDismissed) return -1;
            if (_keyboardOrdinal >= 0) return _keyboardOrdinal;
            return _pinnedOrdinal >= 0 ? _pinnedOrdinal : _pointerOrdinal;
        }
    }

    /// <summary>
    /// One hover zone per category: the data index, the zone's left/right edge inside the plot and
    /// the centre the crosshair and the card are positioned from. Bars own an equal slot per label;
    /// line/area/time plots own the band between two neighbouring X positions, clamped to the plot so
    /// a zone never spills over the Y axis labels.
    /// </summary>
    private IndexSlot[] IndexSlots()
    {
        switch (Type)
        {
            case ChartType.Bar:
            case ChartType.StackedBar:
            {
                var categories = Data.Labels.Length;
                if (categories == 0) return [];
                var slotWidth = CW / (double)categories;
                var barSlots = new IndexSlot[categories];
                for (var i = 0; i < categories; i++)
                {
                    barSlots[i] = new IndexSlot(i, PL + i * slotWidth, PL + (i + 1) * slotWidth);
                }

                return barSlots;
            }

            case ChartType.StackedArea:
            {
                var categories = Data.Labels.Length;
                if (categories == 0) return [];
                var step = CW / (double)Math.Max(categories - 1, 1);
                var points = new (int Index, double X)[categories];
                for (var i = 0; i < categories; i++)
                {
                    points[i] = (i, PL + i * step);
                }

                return BandedSlots(points);
            }

            default:
                return BandedSlots(XPlan());
        }
    }

    private IndexSlot[] BandedSlots((int Index, double X)[] points)
    {
        if (points.Length == 0) return [];
        if (points.Length == 1) return [new IndexSlot(points[0].Index, PL, PL + CW)];

        var slots = new IndexSlot[points.Length];
        for (var k = 0; k < points.Length; k++)
        {
            var x = points[k].X;
            var left = k == 0
                ? x - (points[1].X - x) / 2
                : (points[k - 1].X + x) / 2;
            var right = k == points.Length - 1
                ? x + (x - points[k - 1].X) / 2
                : (x + points[k + 1].X) / 2;

            slots[k] = new IndexSlot(
                points[k].Index,
                Math.Clamp(left, PL, PL + CW),
                Math.Clamp(right, PL, PL + CW));
        }

        return slots;
    }

    /// <summary>
    /// The whole index as a tooltip context: the category label plus every visible series whose value
    /// exists at that index. Hidden legend series, NaN gaps and series shorter than the index are
    /// absent, so a template never renders an empty row.
    /// </summary>
    private ChartIndexTooltipContext? IndexContext(int ordinal)
    {
        var slots = IndexSlots();
        if (ordinal < 0 || ordinal >= slots.Length || Data is null) return null;

        var dataIndex = slots[ordinal].Index;
        var series = new List<ChartIndexSeriesValue>(Data.Datasets.Length);
        for (var datasetIndex = 0; datasetIndex < Data.Datasets.Length; datasetIndex++)
        {
            if (IsSegmentHidden(datasetIndex, -1)) continue;

            var dataset = Data.Datasets[datasetIndex];
            if (dataIndex < 0 || dataIndex >= dataset.Values.Length) continue;
            var value = dataset.Values[dataIndex];
            if (double.IsNaN(value)) continue;

            series.Add(new ChartIndexSeriesValue(datasetIndex, dataset.Label, value, dataset.Color));
        }

        return new ChartIndexTooltipContext(dataIndex, XValueLabel(dataIndex), series);
    }

    /// <summary>
    /// The ONE live message of the chart: the category and every series at the active index, the way a
    /// reader hears it. Empty while nothing is highlighted, so a fresh chart announces nothing.
    /// </summary>
    private string IndexLiveText
    {
        get
        {
            if (!IndexTooltipActive) return string.Empty;

            var ordinal = ActiveIndexOrdinal;
            if (ordinal < 0) return string.Empty;

            var context = IndexContext(ordinal);
            if (context is null) return string.Empty;
            if (context.Series.Count == 0) return context.Label;

            var rows = string.Join(
                Loc["TmChart_IndexSeparator"],
                context.Series.Select(series => string.Format(
                    CultureInfo.CurrentCulture,
                    Loc["TmChart_IndexSeries"],
                    series.Label,
                    CultureValue(series.Value))));

            return string.Format(
                CultureInfo.CurrentCulture,
                Loc["TmChart_IndexAnnouncement"],
                context.Label,
                rows);
        }
    }

    /// <summary>Culture formatting for tooltip text (SVG coordinates stay invariant).</summary>
    private static string CultureValue(double value) =>
        value.ToString("0.##", CultureInfo.CurrentCulture);

    // ── Interaction ──

    private void HoverIndex(int ordinal)
    {
        if (!IndexTooltipActive) return;
        if (_pointerOrdinal == ordinal && _pinnedOrdinal < 0 && !_indexDismissed) return;

        _pointerOrdinal = ordinal;
        _pinnedOrdinal = -1;
        _indexDismissed = false;
    }

    /// <summary>Pins a category for a tap, where no hover precedes the click.</summary>
    private void PinIndex(int ordinal)
    {
        if (!IndexTooltipActive) return;

        _pointerOrdinal = ordinal;
        _pinnedOrdinal = ordinal;
        _indexDismissed = false;
    }

    /// <summary>
    /// Clears the pointer index only when the zone that reports the leave is STILL the active one.
    /// A leaving zone and an entering zone deliver two separate events whose order is not guaranteed
    /// once they cross the JS→.NET boundary: an unguarded "clear" can land after the next zone's
    /// hover and blank a card the reader is looking at. The guard makes the pair commutative.
    /// </summary>
    private void ClearPointerIndex(int ordinal)
    {
        if (_pointerOrdinal == ordinal && _pinnedOrdinal < 0) _pointerOrdinal = -1;
    }

    private void DismissIndex()
    {
        _indexDismissed = true;
        _pointerOrdinal = -1;
        _pinnedOrdinal = -1;
    }

    /// <summary>Clears every index-mode state; called when the host swaps in a different data instance.</summary>
    private void ResetIndexState()
    {
        _pointerOrdinal = -1;
        _keyboardOrdinal = -1;
        _pinnedOrdinal = -1;
        _indexDismissed = false;
    }

    private void HandleChartKeyDown(KeyboardEventArgs args)
    {
        if (!IndexTooltipActive) return;

        switch (args.Key)
        {
            case "ArrowRight":
                MoveIndex(1);
                break;
            case "ArrowLeft":
                MoveIndex(-1);
                break;
            case "Home":
                SetIndex(0);
                break;
            case "End":
                SetIndex(IndexSlots().Length - 1);
                break;
            case "Escape":
                DismissIndex();
                break;
        }
    }

    private void MoveIndex(int delta)
    {
        var count = IndexSlots().Length;
        if (count == 0) return;

        // The first press lands on an edge, never on a neighbour of a cursor that does not exist yet.
        SetIndex(_keyboardOrdinal < 0
            ? delta >= 0 ? 0 : count - 1
            : Math.Clamp(_keyboardOrdinal + delta, 0, count - 1));
    }

    private void SetIndex(int ordinal)
    {
        if (ordinal < 0) return;

        _keyboardOrdinal = ordinal;
        _pointerOrdinal = -1;
        _pinnedOrdinal = -1;
        _indexDismissed = false;
    }

    /// <summary>
    /// Focus leaving the chart clears the index: the tooltip belongs to the focus, so it must not
    /// linger over a chart the reader has left.
    /// </summary>
    private void HandleChartBlur() => ResetIndexState();

    private Task HandleIndexClickAsync(int ordinal)
    {
        // A tap never hovers: pin the card on the tapped category so a coarse pointer has the same
        // path as the keyboard, whether or not the host listens to OnSegmentClick.
        PinIndex(ordinal);

        if (!OnSegmentClick.HasDelegate) return Task.CompletedTask;

        var context = IndexContext(ordinal);
        if (context is null || context.Series.Count == 0) return Task.CompletedTask;

        var first = context.Series[0];
        return OnSegmentClick.InvokeAsync(
            new ChartSegment(first.DatasetIndex, context.Index, context.Label, first.Value));
    }

    // ── Rendering ──

    /// <summary>Crosshair, per-series index dots and the hover zones; empty unless index mode is on.</summary>
    private RenderFragment IndexOverlayContent => builder =>
    {
        if (!IndexTooltipActive) return;

        var slots = IndexSlots();
        if (slots.Length == 0) return;

        var active = ActiveIndexOrdinal;
        if (active >= 0 && active < slots.Length)
        {
            var center = slots[active].Center;

            builder.OpenElement(0, "line");
            builder.AddAttribute(1, "class", "tm-chart__crosshair");
            builder.AddAttribute(2, "data-testid", TestId("chart-crosshair"));
            builder.AddAttribute(3, "x1", F(center));
            builder.AddAttribute(4, "y1", F(PT));
            builder.AddAttribute(5, "x2", F(center));
            builder.AddAttribute(6, "y2", F(PT + CH));
            builder.CloseElement();

            if (Type is ChartType.Line or ChartType.Area)
            {
                BuildIndexDots(builder, slots[active].Index, center);
            }
        }

        builder.OpenElement(0, "g");
        builder.AddAttribute(1, "class", "tm-chart__index-zones");

        for (var k = 0; k < slots.Length; k++)
        {
            var ordinal = k;
            builder.OpenElement(3, "rect");
            builder.AddAttribute(4, "class", "tm-chart__index-zone");
            builder.AddAttribute(5, "data-testid", TestId($"chart-index-zone-{k}"));
            builder.AddAttribute(6, "x", F(slots[k].Left));
            builder.AddAttribute(7, "y", F(PT));
            builder.AddAttribute(8, "width", F(slots[k].Right - slots[k].Left));
            builder.AddAttribute(9, "height", F(CH));
            builder.AddAttribute(10, "onmouseover",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => HoverIndex(ordinal)));
            builder.AddAttribute(11, "onmouseout",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => ClearPointerIndex(ordinal)));
            builder.AddAttribute(12, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => HandleIndexClickAsync(ordinal)));
            builder.CloseElement();
        }

        builder.CloseElement();
    };

    private void BuildIndexDots(RenderTreeBuilder builder, int dataIndex, double center)
    {
        var (min, max) = Type == ChartType.Area ? AreaDomain() : BarValueDomain();

        for (var datasetIndex = 0; datasetIndex < Data.Datasets.Length; datasetIndex++)
        {
            if (IsSegmentHidden(datasetIndex, -1)) continue;

            var dataset = Data.Datasets[datasetIndex];
            if (dataIndex < 0 || dataIndex >= dataset.Values.Length) continue;
            var value = dataset.Values[dataIndex];
            if (double.IsNaN(value)) continue;

            var y = Type == ChartType.Area
                ? AreaY(value, min, max)
                : BarDomainY(value, min, max);

            builder.OpenElement(0, "circle");
            builder.AddAttribute(1, "class", "tm-chart__index-dot");
            builder.AddAttribute(2, "cx", F(center));
            builder.AddAttribute(3, "cy", F(y));
            builder.AddAttribute(4, "r", "4");
            builder.AddAttribute(5, "fill", dataset.Color);
            builder.AddAttribute(6, "aria-hidden", "true");
            builder.CloseElement();
        }
    }

    /// <summary>
    /// The crosshair card: the category label and one row per visible series. A
    /// <see cref="TooltipIndexTemplate"/> replaces the rows but keeps the card, so a host template
    /// inherits the edge-aware placement.
    /// </summary>
    private RenderFragment IndexTooltipContent => builder =>
    {
        if (!IndexTooltipActive) return;

        var ordinal = ActiveIndexOrdinal;
        if (ordinal < 0) return;

        var slots = IndexSlots();
        if (ordinal >= slots.Length) return;

        var context = IndexContext(ordinal);
        if (context is null) return;

        // Every series hidden (or nothing at this index): the crosshair is the whole answer, so the
        // card does not open as an empty plate with a lone label.
        if (context.Series.Count == 0) return;

        var rows = context.Series.Count;
        var cardHeight = IndexCardHeadHeight + rows * IndexCardRowHeight + IndexCardPadding;
        var center = slots[ordinal].Center;

        // Prefer the side with room; clamp into the viewBox so the card is never cropped.
        var cardX = center + IndexCardGap;
        if (cardX + IndexCardWidth > SvgW)
        {
            cardX = center - IndexCardGap - IndexCardWidth;
        }

        cardX = Math.Clamp(cardX, 0, Math.Max(0, SvgW - IndexCardWidth));
        var cardY = Math.Clamp(PT - IndexCardHeadHeight / 2, 0, Math.Max(0, SvgH - cardHeight));

        builder.OpenElement(0, "foreignObject");
        builder.AddAttribute(1, "class", "tm-chart__tooltip-fo");
        builder.AddAttribute(2, "x", F(cardX));
        builder.AddAttribute(3, "y", F(cardY));
        builder.AddAttribute(4, "width", F(IndexCardWidth));
        builder.AddAttribute(5, "height", F(cardHeight));
        builder.AddAttribute(6, "style", "pointer-events:none;overflow:visible;");

        builder.OpenElement(7, "div");
        builder.AddAttribute(8, "xmlns", "http://www.w3.org/1999/xhtml");
        builder.AddAttribute(9, "class", "tm-chart__tooltip tm-chart__tooltip--index");
        builder.AddAttribute(10, "data-testid", TestId("chart-index-tooltip"));

        if (TooltipIndexTemplate is not null)
        {
            builder.AddContent(11, TooltipIndexTemplate(context));
        }
        else
        {
            builder.OpenElement(12, "span");
            builder.AddAttribute(13, "class", "tm-chart__tooltip-label");
            builder.AddContent(14, context.Label);
            builder.CloseElement();

            foreach (var series in context.Series)
            {
                builder.OpenElement(15, "span");
                builder.AddAttribute(16, "class", "tm-chart__tooltip-series");

                builder.OpenElement(17, "span");
                builder.AddAttribute(18, "class", "tm-chart__tooltip-dot");
                builder.AddAttribute(19, "style", $"background-color:{series.Color};");
                builder.CloseElement();

                builder.OpenElement(20, "span");
                builder.AddAttribute(21, "class", "tm-chart__tooltip-series-label");
                builder.AddContent(22, series.Label);
                builder.CloseElement();

                builder.OpenElement(23, "span");
                builder.AddAttribute(24, "class", "tm-chart__tooltip-series-value");
                builder.AddContent(25, CultureValue(series.Value));
                builder.CloseElement();

                builder.CloseElement();
            }
        }

        builder.CloseElement(); // div
        builder.CloseElement(); // foreignObject
    };

    /// <summary>One category's hit area: the data index it reports and its edges in viewBox units.</summary>
    private readonly record struct IndexSlot(int Index, double Left, double Right)
    {
        /// <summary>The X the crosshair and the tooltip card are positioned from.</summary>
        public double Center => (Left + Right) / 2;
    }
}
