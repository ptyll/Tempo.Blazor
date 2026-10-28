using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// Portfolio view: one card per project with a collapsible header (name, date range, task count, weighted progress,
/// host actions), each embedding a <see cref="TmGantt"/> of the project's tasks and dependencies. Each nested Gantt gets
/// its own test-id prefix (<c>gantt-{project.Id}</c>, after the portfolio's own prefix), so ids of tasks that share an
/// id across projects stay unique.
/// </summary>
public partial class TmGanttPortfolio
{
    private readonly HashSet<string> _collapsed = [];
    private readonly string _instanceId = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Projects to show; each card draws the project's <c>Tasks</c> and <c>Dependencies</c> in its own Gantt.</summary>
    [Parameter] public IReadOnlyList<GanttPortfolioProject> Projects { get; set; } = [];

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

    private bool IsExpanded(GanttPortfolioProject project) => !_collapsed.Contains(project.Id);

    private void Toggle(GanttPortfolioProject project)
    {
        if (!_collapsed.Remove(project.Id)) _collapsed.Add(project.Id);
    }

    private string BodyId(GanttPortfolioProject project) => $"gantt-portfolio-{_instanceId}-{project.Id}";

    private static (DateTime Start, DateTime End)? Range(GanttPortfolioProject project)
        => project.Tasks.Count == 0 ? null : (project.Tasks.Min(t => t.Start), project.Tasks.Max(t => t.End));

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
}