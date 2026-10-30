using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.WorkItems;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The timeline of <see cref="TmGantt"/>: the two-row header, the grid, the non-working overlays, the today
/// and current-time markers, the dependency lines, the baseline ghosts and the task bars. The pan, the dependency drag and the
/// long-press are pointer sessions of the core pointer-drag.js attached by the owner (gantt.js); this component renders the
/// hooks they read (data-task-id, the dependency handles) and skips a render pass that changes nothing it draws.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class TmGanttTimeline : TmGanttPart
{
    /// <summary>
    /// What the timeline paints. The list references (<c>_taskBars</c>, headers, dependency lines) are rebuilt
    /// by <c>RefreshVisibleData</c>, so they track the data and the zoom; the scalars are the view state that
    /// is mutated in place (<c>ViewSettings</c> toggles do not refresh the data).
    /// </summary>
    private readonly record struct TimelineState(
        int Version,
        int Tasks,
        object Bars,
        object Headers,
        object Dependencies,
        object NonWorking,
        double Width,
        int Rows,
        string? SelectedTaskId,
        string? SelectedDependencyId,
        string? ActiveTimerTaskId,
        bool OverdueHighlight,
        bool CriticalPath,
        string? Baseline,
        bool DependencyHandles,
        GanttZoomPreset Zoom,
        bool ShowTodayMarker,
        bool ShowDaysOff,
        bool ShowProgressLabel,
        GanttTaskNameLocation NameLocation,
        int RowHeight,
        DateTime Today,
        string? Prefix,
        string Culture,
        int FocusSeq,
        bool Header,
        bool Virtualizing,
        int WindowStart,
        int WindowEnd);

    /// <inheritdoc />
    protected override object? RenderState => new TimelineState(
        Owner.DrawVersion, Owner.TaskFingerprint(), Owner._taskBars, Owner._lowerHeaders, Owner._visibleDependencies, Owner._nonWorkingRects,
        Owner._totalTimelineWidth, Owner._visibleNodes.Count, Owner.SelectedTask?.Id,
        Owner._selectedDependencyId, Owner.ActiveTimerTaskId, Owner.ShowOverdueHighlight, Owner.ShowCriticalPath,
        Owner.ActiveBaselineId, Owner.AllowDependencyCreation && !Owner.IsMobileLayout, Owner.GetEffectiveZoomPreset(),
        Owner.ViewSettings.ShowTodayMarker, Owner.ViewSettings.ShowDaysOff, Owner.ShowBarProgressLabels,
        Owner.ViewSettings.TaskNameLocation, Owner.RowHeight, Owner._today, Owner.TestIdPrefix, Loc.Culture.Name,
        Owner.FocusRequest?.Seq ?? 0, Owner.ShowTimelineHeader,
        Owner.VirtualizationActive, Owner.WindowStart, Owner.WindowEnd);

    private readonly Dictionary<string, ElementReference> _barRefs = [];
    private int _handledFocusSeq;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Element references of bars that are no longer drawn (collapse, filter, a window that moved past them)
        // would otherwise pile up: only what the current window paints is kept.
        var barIds = Owner._taskBars.Where(Owner.IsBarInView).Select(b => b.Task.Id).ToHashSet();
        foreach (var id in _barRefs.Keys.Where(id => !barIds.Contains(id)).ToList()) _barRefs.Remove(id);

        // ArrowRight on a leaf row asks for the focus of its bar; the bar is a roving child (tabindex -1), so it
        // is reached by the request, never by Tab.
        var request = Owner.FocusRequest;
        if (request is not { Target: GanttFocusTarget.Bar } bar || bar.Seq == _handledFocusSeq) return;
        _handledFocusSeq = bar.Seq;
        if (_barRefs.TryGetValue(bar.TaskId, out var element))
        {
            try
            {
                await element.FocusAsync(preventScroll: false);
            }
            catch (InvalidOperationException)
            {
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    /// <summary>Accessible name of a bar: the task and its date range.</summary>
    private string BarLabel(TaskBarInfo bar) => Loc["TmGantt_BarLabel", bar.Task.Title,
        bar.Task.Start.ToString("d", Loc.Culture), bar.Task.End.ToString("d", Loc.Culture)];

    private string MilestoneLabel(TaskBarInfo bar) => Loc["TmGantt_MilestoneLabel", bar.Task.Title, bar.Task.End.ToString("d", Loc.Culture)];

    private string GetBarClass(TaskBarInfo bar)
    {
        var classes = new List<string>();

        if (Owner.SelectedTask?.Id == bar.Task.Id)
            classes.Add("tm-gantt__bar--selected");

        if (bar.IsGroup)
            classes.Add("tm-gantt__bar--group");

        if (bar.Task.Status is TmWorkItemStatus.Done or TmWorkItemStatus.Closed)
            classes.Add("tm-gantt__bar--completed");

        if (Owner.ShowOverdueHighlight && Owner.IsOverdue(bar.Task))
            classes.Add("tm-gantt__bar--overdue");

        if (Owner.ShowCriticalPath && Owner._criticalPathIds.Contains(bar.Task.Id))
            classes.Add("tm-gantt__bar--critical-path");

        var priorityClass = bar.Task.Priority switch
        {
            TmWorkItemPriority.Highest => "tm-gantt__bar--priority-highest",
            TmWorkItemPriority.High => "tm-gantt__bar--priority-high",
            TmWorkItemPriority.Medium => "tm-gantt__bar--priority-medium",
            TmWorkItemPriority.Low => "tm-gantt__bar--priority-low",
            TmWorkItemPriority.Lowest => "tm-gantt__bar--priority-lowest",
            _ => ""
        };
        if (!string.IsNullOrEmpty(priorityClass))
            classes.Add(priorityClass);

        if (Owner.ActiveTimerTaskId == bar.Task.Id)
            classes.Add("tm-gantt__bar--timer-active");

        return string.Join(" ", classes);
    }

    /// <summary>
    /// Modifier of the percentage label: a bar that carries the exceeded-deadline flame (drawn just right
    /// of the bar) pushes the label past it. Computed here so the stylesheet needs no <c>:has()</c>.
    /// </summary>
    private static string GetProgressLabelClass(TaskBarInfo bar) =>
        bar.IsDeadlineExceeded ? "tm-gantt__bar-progress-label--after-deadline" : string.Empty;

    private string GetDepLineClass(DependencyLineInfo dep)
    {
        var classes = new List<string> { "tm-gantt__dependency-line", "tm-gantt__dep-line" };
        if (Owner._selectedDependencyId == dep.DependencyId)
            classes.Add("tm-gantt__dep-line--selected");
        classes.Add(dep.DepType switch
        {
            GanttDependencyType.StartToStart    => "tm-gantt__dep-line--ss",
            GanttDependencyType.FinishToFinish  => "tm-gantt__dep-line--ff",
            GanttDependencyType.StartToFinish   => "tm-gantt__dep-line--sf",
            _                                   => "tm-gantt__dep-line--fs"
        });
        return string.Join(" ", classes);
    }

    private static string GetArrowPoints(double x, double y) =>
        $"{F(x)},{F(y - 4)} {F(x + 6)},{F(y)} {F(x)},{F(y + 4)}";

    private bool ShouldRenderBarLabel(TaskBarInfo bar) =>
        Owner.ViewSettings.TaskNameLocation != GanttTaskNameLocation.Hidden && bar.Width > 60;

    private static string GetBarColorStyle(TaskBarInfo bar)
    {
        var color = string.IsNullOrEmpty(bar.Color) ? "var(--tm-color-primary-solid)" : bar.Color;
        return $"--tm-gantt-task-color: {color};";
    }
}