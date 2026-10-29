namespace Tempo.Blazor.Abstractions.Models;

/// <summary>How a <see cref="TmGanttPortfolio"/> aligns the date axes of its project cards.</summary>
public enum GanttPortfolioAxisMode
{
    /// <summary>The default: every card computes its own range from its own tasks, as before.</summary>
    PerProject,

    /// <summary>
    /// One shared axis: the portfolio computes the union range of all projects, renders a single sticky
    /// axis header above the cards and hands every card the same range, zoom and tree width, so a date
    /// lands at the same x in every card and the cards scroll horizontally in step.
    /// </summary>
    Shared,
}
