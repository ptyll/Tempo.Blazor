using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The workload grid of <see cref="TmGantt"/>: allocated hours (or percent of capacity) per person and day. It renders
/// as an alternate Gantt view (a sparse window over the days that have an allocation) or, with <see cref="Docked"/>, as
/// a panel under the timeline over the Gantt's own continuous date range.
/// </summary>
public partial class TmGanttWorkloadView : IAsyncDisposable
{
    private ElementReference _scroller;
    private bool _linked;
    private bool _disposed;

    /// <summary>Tasks whose assignees' workload is shown.</summary>
    [Parameter] public IReadOnlyList<TmWorkItem> Tasks { get; set; } = [];

    /// <summary>Working days and hours per day (the capacity).</summary>
    [Parameter] public WorkingSchedule WorkingSchedule { get; set; } = new();

    /// <summary>Whether cells show hours or a percentage of the day's capacity.</summary>
    [Parameter] public WorkloadDisplayMode DisplayMode { get; set; } = WorkloadDisplayMode.Hours;

    /// <summary>Raised by the Hours | Percent segment of the docked header (arg = the new mode).</summary>
    [Parameter] public EventCallback<WorkloadDisplayMode> DisplayModeChanged { get; set; }

    /// <summary>
    /// Today's date in the display zone — caps the rolling window at (today − 7 days) and seeds it
    /// when no data exists. <see cref="TmGantt"/> passes the user-zone today; defaults to
    /// <see cref="DateTime.Today"/>.
    /// </summary>
    [Parameter] public DateTime Today { get; set; } = DateTime.Today;

    /// <summary>
    /// Docked presentation: a card with a header (title, Hours | Percent segment), a continuous date axis over
    /// <see cref="RangeStart"/>..<see cref="RangeEnd"/> (every day of the Gantt's range, not only the days with an
    /// allocation), a legend and a range note. Default false keeps the sparse alternate-view grid.
    /// </summary>
    [Parameter] public bool Docked { get; set; }

    /// <summary>First day of the docked date axis (the Gantt's range start). Ignored unless <see cref="Docked"/> and <see cref="RangeEnd"/> are set.</summary>
    [Parameter] public DateTime? RangeStart { get; set; }

    /// <summary>Last day of the docked date axis (the Gantt's range end).</summary>
    [Parameter] public DateTime? RangeEnd { get; set; }

    /// <summary>
    /// The layout the view follows. <see cref="TmLayoutMode.Auto"/> (default) keeps the grid; a forced
    /// <see cref="TmLayoutMode.Mobile"/> — or the chart passing its resolved layout — presents the mobile person
    /// list: one week (the week of <see cref="Today"/>), a utilization bar per person and the person column pinned
    /// while the day columns scroll horizontally.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>True on the mobile container: the week person list with the utilization bar.</summary>
    internal bool IsMobile => LayoutMode == TmLayoutMode.Mobile;

    /// <summary>The week the mobile list shows: Monday .. Sunday of <see cref="Today"/>'s week.</summary>
    internal List<DateTime> MobileWeekDates()
    {
        var start = Today.Date.AddDays(-(((int)Today.DayOfWeek + 6) % 7));
        return Enumerable.Range(0, 7).Select(i => start.AddDays(i)).ToList();
    }

    /// <summary>One utilization figure per person: the allocated hours of the week against the capacity of the week.</summary>
    internal sealed record PersonWeek(string AssigneeId, string Name, double Allocated, double Capacity, bool Over);

    internal List<PersonWeek> MobileWeek(IEnumerable<WorkloadEntry> entries, IReadOnlyList<DateTime> weekDates)
    {
        var set = weekDates.ToHashSet();
        return entries
            .GroupBy(e => e.AssigneeId)
            .Select(g =>
            {
                var inWeek = g.Where(e => set.Contains(e.Date)).ToList();
                var allocated = inWeek.Sum(e => e.AllocatedHours);
                var capacity = inWeek.Sum(e => e.CapacityHours);
                var name = Tasks.SelectMany(t => t.Assignees).FirstOrDefault(a => a.Id == g.Key)?.Name ?? g.Key;
                return new PersonWeek(g.Key, name, allocated, capacity, capacity > 0 && allocated > capacity);
            })
            .OrderBy(p => p.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    [Inject] private IJSRuntime JS { get; set; } = default!;

    private bool HasRange => Docked && RangeStart.HasValue && RangeEnd.HasValue && RangeEnd.Value.Date >= RangeStart.Value.Date;

    /// <summary>The docked axis: one column per calendar day of the range, capped so a years-long project stays renderable.</summary>
    private const int MaxDockedDays = 366;

    /// <summary>The range holds more days than the capped axis shows: it no longer lines up with the timeline above it.</summary>
    private bool RangeTruncated => HasRange && (RangeEnd!.Value.Date - RangeStart!.Value.Date).TotalDays + 1 > MaxDockedDays;

    /// <summary>The scroll link needs an axis that is the timeline's own: a truncated one is not, so it is never linked.</summary>
    private bool ShouldLinkScroll => HasRange && !RangeTruncated;

    private List<DateTime> BuildDockedDates()
    {
        var dates = new List<DateTime>();
        var day = RangeStart!.Value.Date;
        var end = RangeEnd!.Value.Date;
        while (day <= end && dates.Count < MaxDockedDays)
        {
            dates.Add(day);
            day = day.AddDays(1);
        }

        return dates;
    }

    private Task SetModeAsync(WorkloadDisplayMode mode)
    {
        DisplayMode = mode;
        return DisplayModeChanged.InvokeAsync(mode);
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The docked scroller follows the timeline (and vice versa) entirely in the browser; the link is
        // (re)established whenever the placement is docked and released when it is not.
        // linkScroll is idempotent (it keeps a link to the current timeline and remakes one to a replaced
        // timeline), so it runs on every render while linkable; its answer says whether a link exists now.
        if (ShouldLinkScroll)
        {
            _linked = await InvokeJsAsync<bool>("tmGantt.linkScroll", _scroller);
        }
        else if (_linked)
        {
            _linked = false;
            await InvokeJsAsync<bool>("tmGantt.unlinkScroll", _scroller);
        }
    }

    private async Task<T?> InvokeJsAsync<T>(string identifier, params object[] args)
    {
        if (_disposed) return default;
        try
        {
            return await JS.InvokeAsync<T>(identifier, args);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (JSException)
        {
        }

        return default;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_linked)
        {
            try
            {
                await JS.InvokeVoidAsync("tmGantt.unlinkScroll", _scroller);
            }
            catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException or JSException or TaskCanceledException)
            {
            }
        }
    }
}