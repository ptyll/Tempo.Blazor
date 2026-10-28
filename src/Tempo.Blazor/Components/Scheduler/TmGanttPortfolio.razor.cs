using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>Portfolio view: one card per project, each embedding a <see cref="TmGantt"/> of the project's tasks and dependencies. Each nested Gantt gets its own test-id prefix (<c>gantt-{project.Id}</c>, after the portfolio's own prefix), so ids of tasks that share an id across projects stay unique.</summary>
public partial class TmGanttPortfolio
{
    /// <summary>Projects to show; each card draws the project's <c>Tasks</c> and <c>Dependencies</c> in its own Gantt.</summary>
    [Parameter] public IReadOnlyList<GanttPortfolioProject> Projects { get; set; } = [];

    /// <summary>Additional CSS classes appended to the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes splatted onto the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }
}
