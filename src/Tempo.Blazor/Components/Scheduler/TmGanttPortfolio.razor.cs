using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
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

    /// <summary>
    /// The layout the cards follow. <see cref="TmLayoutMode.Auto"/> (default) keeps today's card with the embedded
    /// chart; a forced <see cref="TmLayoutMode.Mobile"/> — or the host passing the chart's resolved layout — presents
    /// the read-only mobile card: the summary line, the percent with its progress bar and a mini-gantt of bars only
    /// (a tap on the card raises <see cref="OnProjectSelected"/>; nothing is editable).
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>True on the mobile read-mostly container: the card is the summary + mini-gantt, not the embedded chart.</summary>
    internal bool IsMobile => LayoutMode == TmLayoutMode.Mobile;

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

    /// <summary>The bars of the mini-gantt: every task as a percentage span of the project's own range.</summary>
    private sealed record MiniBar(TmWorkItem Task, double Left, double Width);

    /// <summary>The mini-gantt of a mobile card: month header spans plus one bar per task, percentages of the project range.</summary>
    private static (IReadOnlyList<(DateTime Month, double Left, double Width)> Months, IReadOnlyList<MiniBar> Bars) MiniLayout(GanttPortfolioProject project)
    {
        var range = Range(project);
        if (range is not { } r)
        {
            return ([], []);
        }

        var totalDays = Math.Max(1, (r.End.Date - r.Start.Date).TotalDays);
        var bars = project.Tasks
            .Select(task =>
            {
                var (left, width) = GanttHelper.CalculateBarPosition(task.Start, task.End, r.Start, r.End, 100);
                return new MiniBar(task, Math.Clamp(left, 0, 100), Math.Max(1.5, Math.Min(width, 100 - Math.Clamp(left, 0, 100))));
            })
            .ToList();

        var months = new List<(DateTime, double, double)>();
        var month = new DateTime(r.Start.Year, r.Start.Month, 1);
        while (month <= r.End.Date)
        {
            var monthEnd = month.AddMonths(1).AddDays(-1);
            var start = month < r.Start ? r.Start : month;
            var end = monthEnd > r.End ? r.End : monthEnd;
            var left = (start.Date - r.Start.Date).TotalDays / totalDays * 100;
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

    /// <summary>Invariant-culture number, the way every percent span reaches an inline style.</summary>
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