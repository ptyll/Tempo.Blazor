using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Components.Charts;

/// <summary>
/// The measuring half of <see cref="TmChart"/> (plan 98c80bc3, phase 10b): the opt-in
/// <see cref="Responsive"/> viewBox that follows the real plot box, the culture-aware number formatting of
/// every TEXT the chart prints (<see cref="ValueFormat"/>, <see cref="Culture"/>), the value-axis gutter that
/// grows with the widest tick label, and the width-derived thinning of category labels.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here changes the default geometry.</b> Without <see cref="Responsive"/> the viewBox is the
/// pre-2.12.0 600 × 400 and the module is never imported. Number text, on the other hand, now follows the
/// reader's culture (a decimal comma under <c>cs-CZ</c>); SVG coordinates and every <c>data-*</c> attribute
/// stay on the invariant culture, because they are a machine contract, not text.
/// </para>
/// </remarks>
public partial class TmChart : IAsyncDisposable
{
    // The viewBox of a chart that does not measure itself — the pre-2.12.0 600 × 400.
    internal const int DefaultSvgW = 600;
    internal const int DefaultSvgH = 400;

    // The smallest viewBox a measured plot may shrink to: below it the padding (PL + PR, PT + PB) would eat the
    // whole plot and every width would turn negative.
    private const int MinSvgW = 160;
    private const int MinSvgH = 160;

    private const int DefaultPl = 50;
    private const string ModulePath = "./_content/Tempo.Blazor/Components/Charts/TmChart.razor.js";

    private ElementReference _plot;
    private LazyJsModule? _lazy;
    private DotNetObjectReference<TmChart>? _dotNetRef;
    private bool _attaching;
    private bool _observing;
    private bool _disposed;
    private double? _plotWidth;
    private double? _plotHeight;
    private int _gutter = DefaultPl;

    // IJSRuntime is resolved on demand, never injected: a default chart must render in a host that registered no JS
    // runtime at all (an HtmlRenderer export, a console app) — only a Responsive chart asks for it.
    [Inject] private IServiceProvider Services { get; set; } = default!;

    /// <summary>
    /// Opt-in: the SVG viewBox follows the MEASURED plot box (one user unit = one CSS pixel) instead of the
    /// fixed 600 × 400 that the SVG meets into. A wide card is then filled edge to edge, the axis type keeps
    /// its real size and the category labels are thinned from the width that is actually there. Until the
    /// first measurement (prerender, the first frame) the chart draws in the default viewBox. Ignored for
    /// <see cref="ChartType.Funnel"/>, whose label scale is calibrated on the 600 × 400 box. Default false
    /// keeps the fixed viewBox and never loads the measuring module. If the JS runtime is unavailable or the
    /// module fails to load, the chart keeps the default 600 × 400 viewBox for the lifetime of the instance.
    /// </summary>
    [Parameter] public bool Responsive { get; set; }

    /// <summary>
    /// .NET numeric format string of every value the chart PRINTS — axis ticks, value labels, tooltips and the
    /// live announcement (for example <c>"0.0 °C"</c>, <c>"N1"</c> or <c>"P0"</c>). Null (default) keeps the
    /// two-decimals-at-most format (<c>0.##</c>). The text is rendered with <see cref="Culture"/>; the
    /// <c>data-*</c> attributes and SVG coordinates always stay invariant. An invalid format string falls
    /// back to the default instead of failing the render.
    /// </summary>
    [Parameter] public string? ValueFormat { get; set; }

    /// <summary>
    /// The culture that formats every number and date the chart prints. Null (default) follows
    /// <see cref="CultureInfo.CurrentCulture"/>, so a <c>cs-CZ</c> page reads <c>16,1</c>; a host that must
    /// pin one language regardless of the thread (a report exported in English) passes it here.
    /// </summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    private CultureInfo EffectiveCulture => Culture ?? CultureInfo.CurrentCulture;

    // The funnel is calibrated on the default viewBox (its type scale and gutter maths), so it never measures.
    private bool IsResponsive => Responsive && Type != ChartType.Funnel;

    private int SvgW => IsResponsive && _plotWidth is { } w ? Math.Max(MinSvgW, (int)w) : DefaultSvgW;

    private int SvgH => IsResponsive && _plotHeight is { } h ? Math.Max(MinSvgH, (int)h) : DefaultSvgH;

    // Pad-left: the value axis' label gutter (grows with the widest tick label, never below 50).
    private int PL => _gutter;

    private int CW => SvgW - PL - PR;

    private int CH => SvgH - PT - PB;

    /// <summary>
    /// Called by <c>TmChart.razor.js</c> with the plot box in CSS pixels whenever its rounded size changed.
    /// An invalid size is ignored and an unchanged one does not re-render.
    /// </summary>
    /// <param name="width">The plot box width in CSS pixels.</param>
    /// <param name="height">The plot box height in CSS pixels.</param>
    [JSInvokable]
    public Task OnPlotResized(double width, double height)
    {
        if (_disposed || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            return Task.CompletedTask;
        }

        width = Math.Round(width);
        height = Math.Round(height);
        if (_plotWidth == width && _plotHeight == height)
        {
            return Task.CompletedTask;
        }

        return InvokeAsync(() =>
        {
            _plotWidth = width;
            _plotHeight = height;
            StateHasChanged();
        });
    }

    // A chart that stops being responsive returns to the default box (the next render is already coming).
    private void ForgetMeasurementUnlessResponsive()
    {
        if (!IsResponsive)
        {
            _plotWidth = null;
            _plotHeight = null;
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed) return;

        var wanted = IsResponsive && HasRenderableData;
        if (wanted && !_observing && !_attaching)
        {
            // Set before the first await, so a second render during the import cannot start another one. As in
            // TmLayoutObserver a failed import (no JS, prerender) is never retried — for the whole lifetime of
            // the instance, also when Responsive is switched off and on again: the chart then keeps the default
            // 600 × 400 viewBox.
            _attaching = true;
            var js = Services.GetService(typeof(IJSRuntime)) as IJSRuntime;
            if (js is null) return;
            _dotNetRef ??= DotNetObjectReference.Create(this);
            _lazy ??= new LazyJsModule(js, ModulePath);
            var lazy = _lazy;
            if (await lazy.GetAsync() is null) return;

            // The component may have been disposed while the import was pending: ReleaseAsync already cleaned up
            // (dropped the reference, disposed the module), so there is nothing to observe.
            var dotNetRef = _dotNetRef;
            if (_disposed || dotNetRef is null) return;

            _observing = true;
            await lazy.TryInvokeVoidAsync("observe", _plot, dotNetRef);
        }
        else if (!wanted && _observing && _lazy is { IsImported: true })
        {
            _observing = false;
            _attaching = false;
            await _lazy.TryInvokeVoidAsync("release", _plot);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await ReleaseAsync();
    }

    private async Task ReleaseAsync()
    {
        var lazy = _lazy;
        var observing = _observing;
        _observing = false;
        _dotNetRef?.Dispose();
        _dotNetRef = null;
        if (lazy is null) return;

        // A module that was never imported has nothing to release; the helper swallows the guarded failures.
        if (observing && lazy.IsImported) await lazy.TryInvokeVoidAsync("release", _plot);
        await lazy.DisposeAsync();
    }

    // ── Culture-aware text ──

    /// <summary>One value as the reader's culture writes it — the text of an axis tick, a value label or a tooltip.</summary>
    private string V(double value)
    {
        var culture = EffectiveCulture;
        if (string.IsNullOrEmpty(ValueFormat))
        {
            return value.ToString("0.##", culture);
        }

        try
        {
            return value.ToString(ValueFormat, culture);
        }
        catch (FormatException)
        {
            return value.ToString("0.##", culture);
        }
    }

    // ── Value axis ──

    // The tick labels of the active value axis, as printed. Empty for the types that have no left value axis.
    private string[] ValueAxisLabels()
    {
        if (!ShowGrid) return [];
        var scale = Type switch
        {
            ChartType.Bar or ChartType.Line => BarValueDomain(),
            ChartType.Area => AreaDomain(),
            ChartType.StackedBar or ChartType.StackedArea => StackedDomain(),
            ChartType.Waterfall => WaterfallDomain(WaterfallBars()),
            _ => (ChartAxisScale?)null,
        };

        return scale is { } s ? [.. s.Ticks().Select(V)] : [];
    }

    // The left gutter the value axis needs: the widest tick label plus its 8-unit gap and a 4-unit margin, never
    // below the 50 units of the pre-2.12.0 layout (a label of up to five characters still fits it).
    private int ComputeGutter()
    {
        var labels = ValueAxisLabels();
        if (labels.Length == 0) return DefaultPl;

        var widest = labels.Max(l => l.Length);
        return Math.Max(DefaultPl, (int)Math.Ceiling(widest * ChartAxisScale.LabelCharWidth) + 12);
    }

    // ── Label thinning ──

    private static int LongestLabel(IEnumerable<string> labels) => labels.Select(l => l?.Length ?? 0).DefaultIfEmpty(0).Max();

    // Every n-th category label so neighbouring labels keep their own width: `slotWidth` is the distance between
    // two neighbouring labels in viewBox units. Replaces the fixed "more than 24 categories" threshold.
    private int CategoryLabelStep(IEnumerable<string> labels, int count, double slotWidth)
        => ChartAxisScale.LabelStep(count, slotWidth, LongestLabel(labels));
}
