using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// Portfolio view: one card per project with a collapsible header (name, date range, task count, weighted progress,
/// host actions), each embedding a <see cref="TmGantt"/> of the project's tasks and dependencies. Each nested Gantt gets
/// its own test-id prefix (<c>gantt-{project.Id}</c>, after the portfolio's own prefix), so ids of tasks that share an
/// id across projects stay unique.
/// <para>
/// <see cref="AxisMode"/> (phase 11): <see cref="GanttPortfolioAxisMode.PerProject"/> (default) keeps today's
/// independent mini-gantts; <see cref="GanttPortfolioAxisMode.Shared"/> computes the union range of <b>all</b>
/// projects, renders ONE sticky axis header above the cards and hands every card the same range
/// (<see cref="TmGantt.TimelineStart"/> / <see cref="TmGantt.TimelineEnd"/>), the portfolio's
/// <see cref="ZoomPreset"/> and one tree width (<see cref="TmGantt.TreeWidth"/>) — so the same date draws at the
/// same x in every card. The header's horizontal scroll and the cards' scroll stay in step in the browser through
/// <c>tmGantt.linkScrollGroup</c> (no interop per scroll tick); a collapsed card never shifts the axis.
/// </para>
/// </summary>
public partial class TmGanttPortfolio : IAsyncDisposable
{
    private readonly HashSet<string> _collapsed = [];
    private readonly string _instanceId = Guid.NewGuid().ToString("N")[..8];
    private readonly string _scrollGroupId;
    private readonly Dictionary<string, TmGantt> _cardGanttRefs = new();
    private ElementReference _axisScrollRef;
    private bool _sharedAxisLinked;

    public TmGanttPortfolio()
    {
        _scrollGroupId = $"tm-gantt-portfolio-{Guid.NewGuid():N}";
    }

    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Projects to show; each card draws the project's <c>Tasks</c> and <c>Dependencies</c> in its own Gantt.</summary>
    [Parameter] public IReadOnlyList<GanttPortfolioProject> Projects { get; set; } = [];

    /// <summary>
    /// How the cards' date axes align. <see cref="GanttPortfolioAxisMode.PerProject"/> (default) keeps every card on
    /// its own range; <see cref="GanttPortfolioAxisMode.Shared"/> renders one sticky axis header and pins the union
    /// range, zoom and tree width on every card (see the type's summary).
    /// </summary>
    [Parameter] public GanttPortfolioAxisMode AxisMode { get; set; } = GanttPortfolioAxisMode.PerProject;

    /// <summary>The zoom preset of the shared axis header and, in <see cref="GanttPortfolioAxisMode.Shared"/>, of every card. Default Weeks.</summary>
    [Parameter] public GanttZoomPreset ZoomPreset { get; set; } = GanttZoomPreset.Weeks;

    /// <summary>
    /// The task tree width every card is pinned to in <see cref="GanttPortfolioAxisMode.Shared"/> (a CSS length, e.g.
    /// "200px") — one width so the timelines start at the same x. Null uses the shared default (12rem).
    /// </summary>
    [Parameter] public string? TreeWidth { get; set; }

    /// <summary>
    /// The layout the cards follow. <see cref="TmLayoutMode.Auto"/> (default) keeps today's card with the embedded
    /// chart; a forced <see cref="TmLayoutMode.Mobile"/> — or the host passing the chart's resolved layout — presents
    /// the read-only mobile card: the summary line, the percent with its progress bar and a mini-gantt of bars only
    /// (a tap on the card raises <see cref="OnProjectSelected"/>; nothing is editable).
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>True on the mobile read-mostly container: the card is the summary + mini-gantt, not the embedded chart.</summary>
    internal bool IsMobile => LayoutMode == TmLayoutMode.Mobile;

    /// <summary>True when the one shared axis renders (the shared header, the card overrides and the scroll group).</summary>
    internal bool SharedAxis => AxisMode == GanttPortfolioAxisMode.Shared && _unionRange is not null;

    /// <summary>The union range of ALL projects (collapsed cards included): the shared axis never moves when a card collapses.
    /// Computed once per parameter change in <see cref="OnParametersSet"/> — never in render.</summary>
    internal (DateTime Start, DateTime End)? _unionRange;

    /// <summary>Total pixel width of the shared axis (= every card's timeline width). See <see cref="_unionRange"/>.</summary>
    internal double _axisTotalWidth;

    /// <summary>The shared axis header rows (upper + lower), positioned in pixels of <see cref="_axisTotalWidth"/>.</summary>
    internal (IReadOnlyList<TimelineHeader> Upper, IReadOnlyList<TimelineHeader> Lower) _axisRows = ([], []);

    /// <summary>Mobile (G2): the ONE shared monthly band over the union range, as percentages.</summary>
    internal IReadOnlyList<(DateTime Month, double Left, double Width)> _sharedMobileMonths = [];

    private IReadOnlyList<GanttPortfolioProject>? _computedForProjects;
    private GanttPortfolioAxisMode _computedForAxisMode;
    private GanttZoomPreset _computedForZoomPreset;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // The union range, the header rows and the total width are pure functions of the projects, the axis
        // mode and the zoom preset: computed once per parameter change, read as fields in the markup.
        if (ReferenceEquals(Projects, _computedForProjects) && AxisMode == _computedForAxisMode && ZoomPreset == _computedForZoomPreset)
        {
            return;
        }

        _computedForProjects = Projects;
        _computedForAxisMode = AxisMode;
        _computedForZoomPreset = ZoomPreset;
        ComputeSharedAxis();
    }

    private void ComputeSharedAxis()
    {
        _unionRange = null;
        _axisRows = ([], []);
        _axisTotalWidth = 0;
        _sharedMobileMonths = [];

        DateTime? start = null;
        DateTime? end = null;
        foreach (var project in Projects)
        {
            foreach (var task in project.Tasks)
            {
                start = start is null || task.Start < start ? task.Start : start;
                end = end is null || task.End > end ? task.End : end;
            }
        }

        if (start is null || end is null) return;
        var rangeEnd = end.Value;
        if (rangeEnd <= start) rangeEnd = start.Value.AddDays(1);
        var range = (start.Value, rangeEnd);

        _unionRange = range;
        var pixelsPerDay = AxisPixelPerDay(ZoomPreset);
        _axisTotalWidth = Math.Max(1, (range.Item2 - range.Item1).TotalDays * pixelsPerDay);
        var rows = GanttHelper.BuildTimelineHeaderRows(ZoomPreset, range.Item1, range.Item2, pixelsPerDay);
        _axisRows = (rows.Upper.ToList(), rows.Lower.ToList());
        _sharedMobileMonths = BuildSharedMobileMonths(range.Item1, range.Item2);
    }

    /// <summary>Pixels per day of the shared axis header, at zoom 100 — the same value every card lays out with.</summary>
    internal static double AxisPixelPerDay(GanttZoomPreset preset) => preset switch
    {
        GanttZoomPreset.Hours => DayWidth * 3,
        GanttZoomPreset.Days => DayWidth,
        GanttZoomPreset.Weeks => DayWidth * 0.6,
        GanttZoomPreset.Months => DayWidth * 0.25,
        GanttZoomPreset.Quarters => DayWidth * 0.12,
        GanttZoomPreset.Years => DayWidth * 0.04,
        _ => DayWidth,
    };

    private const double DayWidth = 40;

    /// <summary>The tree width the shared cards are pinned to (the host's value, else the shared default).</summary>
    internal string? SharedTreeWidth => TreeWidth is { } width && !string.IsNullOrWhiteSpace(width) ? width : "12rem";

    private static IReadOnlyList<(DateTime Month, double Left, double Width)> BuildSharedMobileMonths(DateTime rangeStart, DateTime rangeEnd)
    {
        var totalDays = Math.Max(1, (rangeEnd.Date - rangeStart.Date).TotalDays);
        var months = new List<(DateTime, double, double)>();
        var month = new DateTime(rangeStart.Year, rangeStart.Month, 1);
        while (month <= rangeEnd.Date)
        {
            var monthEnd = month.AddMonths(1).AddDays(-1);
            var start = month < rangeStart ? rangeStart : month;
            var end = monthEnd > rangeEnd ? rangeEnd : monthEnd;
            var left = (start.Date - rangeStart.Date).TotalDays / totalDays * 100;
            var width = (end.Date - start.Date).TotalDays / totalDays * 100 + 1;
            months.Add((month, Math.Clamp(left, 0, 100), Math.Min(Math.Max(width, 2), 100 - Math.Clamp(left, 0, 100))));
            month = month.AddMonths(1);
        }

        return months;
    }

    /// <summary>Additional CSS classes appended to the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Fires when the user clicks a project's name. Without a handler the name is plain text.</summary>
    [Parameter] public EventCallback<GanttPortfolioProject> OnProjectSelected { get; set; }

    /// <summary>Host content at the end of each card header (a "⋯" menu, a status chip); the project is the context.</summary>
    [Parameter] public RenderFragment<GanttPortfolioProject>? ProjectActions { get; set; }

    /// <summary>Maximum height of the Gantt area of a card as a CSS length (e.g. <c>400px</c>). Default: the stylesheet's 15rem.</summary>
    [Parameter] public string? ProjectMaxHeight { get; set; }

    /// <summary>Additional HTML attributes splatted onto the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await LinkSharedAxisAsync();
    }

    /// <summary>
    /// Registers the shared axis's scroll group (the header plus every expanded card's timeline) in the browser.
    /// A collapsed card unmounts its Gantt, so the registration repeats on every render whose member set changed —
    /// <c>linkScrollGroup</c> itself is idempotent for an unchanged set. Without JS (prerender) this is a no-op.
    /// </summary>
    private async Task LinkSharedAxisAsync()
    {
        if (!SharedAxis || IsMobile)
        {
            await UnlinkSharedAxisAsync();
            return;
        }

        // References of cards that left (a removed project, a collapsed card whose Gantt unmounted) would
        // otherwise pile up in the dictionary.
        var projectIds = Projects.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _cardGanttRefs.Keys.Where(id => !projectIds.Contains(id)).ToList())
        {
            _cardGanttRefs.Remove(stale);
        }

        var elements = new List<ElementReference>();
        if (_axisScrollRef.Context is not null) elements.Add(_axisScrollRef);
        foreach (var project in Projects)
        {
            if (IsExpanded(project) && _cardGanttRefs.TryGetValue(project.Id, out var gantt)
                && gantt is not null && gantt._timelineContainerRef.Context is not null)
            {
                elements.Add(gantt._timelineContainerRef);
            }
        }

        if (elements.Count < 2)
        {
            await UnlinkSharedAxisAsync();
            return;
        }

        try
        {
            _sharedAxisLinked = await JS.InvokeAsync<bool>("tmGantt.linkScrollGroup", _scrollGroupId, elements);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException or JSException or TaskCanceledException)
        {
            // The linking is an enhancement: without the script the cards still show the shared alignment,
            // only the synchronized scrolling is missing.
        }
    }

    private async Task UnlinkSharedAxisAsync()
    {
        if (!_sharedAxisLinked) return;
        _sharedAxisLinked = false;
        try
        {
            await JS.InvokeVoidAsync("tmGantt.unlinkScrollGroup", _scrollGroupId);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException or JSException or TaskCanceledException)
        {
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_sharedAxisLinked)
            {
                await JS.InvokeVoidAsync("tmGantt.unlinkScrollGroup", _scrollGroupId);
            }
        }
        catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException or JSException or TaskCanceledException)
        {
        }
    }

    private bool IsExpanded(GanttPortfolioProject project) => !_collapsed.Contains(project.Id);

    private void Toggle(GanttPortfolioProject project)
    {
        if (!_collapsed.Remove(project.Id)) _collapsed.Add(project.Id);
    }

    private string BodyId(GanttPortfolioProject project) => $"gantt-portfolio-{_instanceId}-{project.Id}";

    private static (DateTime Start, DateTime End)? Range(GanttPortfolioProject project)
        => project.Tasks.Count == 0 ? null : (project.Tasks.Min(t => t.Start), project.Tasks.Max(t => t.End));

    /// <summary>The bars of the mini-gantt: every task as a percentage span of the project's own range.</summary>
    internal sealed record MiniBar(TmWorkItem Task, double Left, double Width);

    /// <summary>
    /// The mini-gantt of a mobile card: month header spans plus one bar per task, percentages of the range.
    /// The shared axis passes the union range, so every card's percentages describe the same span.
    /// </summary>
    internal (IReadOnlyList<(DateTime Month, double Left, double Width)> Months, IReadOnlyList<MiniBar> Bars) MiniLayout(
        GanttPortfolioProject project, (DateTime Start, DateTime End)? range = null)
    {
        var r = range ?? Range(project);
        if (r is not { } bounds)
        {
            return ([], []);
        }

        var totalDays = Math.Max(1, (bounds.End.Date - bounds.Start.Date).TotalDays);
        var bars = project.Tasks
            .Select(task =>
            {
                var (left, width) = GanttHelper.CalculateBarPosition(task.Start, task.End, bounds.Start, bounds.End, 100);
                return new MiniBar(task, Math.Clamp(left, 0, 100), Math.Max(1.5, Math.Min(width, 100 - Math.Clamp(left, 0, 100))));
            })
            .ToList();

        var months = new List<(DateTime, double, double)>();
        var month = new DateTime(bounds.Start.Year, bounds.Start.Month, 1);
        while (month <= bounds.End.Date)
        {
            var monthEnd = month.AddMonths(1).AddDays(-1);
            var start = month < bounds.Start ? bounds.Start : month;
            var end = monthEnd > bounds.End ? bounds.End : monthEnd;
            var left = (start.Date - bounds.Start.Date).TotalDays / totalDays * 100;
            var width = (end.Date - start.Date).TotalDays / totalDays * 100 + 1;
            months.Add((month, Math.Clamp(left, 0, 100), Math.Min(Math.Max(width, 2), 100 - Math.Clamp(left, 0, 100))));
            month = month.AddMonths(1);
        }

        return (months, bars);
    }

    /// <summary>Completion weighted by task length: a 50-day task counts five times a 10-day one. Zero-length tasks weigh one day.</summary>
    internal static double WeightedProgress(GanttPortfolioProject project)
    {
        var weight = 0d;
        var done = 0d;
        foreach (var task in project.Tasks)
        {
            var days = Math.Max(1d, (task.End - task.Start).TotalDays);
            weight += days;
            done += days * Math.Clamp(task.PercentComplete, 0, 100);
        }

        return weight > 0 ? done / weight : 0;
    }

    private string? SummaryStyle => string.IsNullOrWhiteSpace(ProjectMaxHeight) ? null : $"max-height: {ProjectMaxHeight}";

    /// <summary>The whole class list of the root (the shared-axis modifier included).</summary>
    private string RootClass => string.Join(" ",
        "tm-gantt-portfolio",
        IsMobile ? "tm-gantt-portfolio--mobile" : "",
        SharedAxis ? "tm-gantt-portfolio--shared" : "",
        Class ?? "").Trim();

    /// <summary>Percent-positioned left of a shared axis cell (or bar).</summary>
    private static string F(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The whole class list of a mini-gantt bar (whole names, no interpolated stems).</summary>
    private static string MiniBarClass(string? color) => string.IsNullOrEmpty(color)
        ? "tm-gantt-portfolio__mini-bar"
        : "tm-gantt-portfolio__mini-bar tm-gantt-portfolio__mini-bar--colored";

    /// <summary>Position (percent) plus the task colour, when the task carries one.</summary>
    private static string MiniBarStyle(MiniBar bar) => string.IsNullOrEmpty(bar.Task.Color)
        ? $"left: {F(bar.Left)}%; width: {F(bar.Width)}%;"
        : $"left: {F(bar.Left)}%; width: {F(bar.Width)}%; --tm-gantt-portfolio-mini-color: {bar.Task.Color};";
}
