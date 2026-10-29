using Tempo.Blazor.Abstractions.WorkItems;

namespace Tempo.Blazor.Abstractions.Models;

/// <summary>
/// The rollup behind the Gantt's optional summary band: the counts, times, budget and structure depth
/// of the tasks the caller hands in. The caller decides the scope (the Gantt computes it from its
/// <b>filtered</b> tasks — after the filters and "show closed", ignoring collapse — and sets
/// <see cref="IsFiltered"/>), so the record is a plain value: everything is already aggregated, and
/// rendering it is free.
/// <para>
/// Parents never contribute their own time or cost a second time: a task whose id is another task's
/// <see cref="TmWorkItem.ParentId"/> in the given set is a parent, and only leaves add hours and cost.
/// </para>
/// </summary>
/// <param name="StatusCounts">Tasks per <see cref="TmWorkItemStatus"/>; only statuses with at least one task are present.</param>
/// <param name="OverdueCount">Tasks whose end is before <c>today</c> and that are neither Done nor Closed (the same rule <c>TmGantt.IsOverdue</c> draws).</param>
/// <param name="LoggedHours">Sum of the logged hours of the leaf tasks.</param>
/// <param name="EstimatedHours">Sum of the estimated hours of the leaf tasks.</param>
/// <param name="MilestonesDone">Milestones that are Done or Closed.</param>
/// <param name="MilestonesTotal">All milestones in the set.</param>
/// <param name="ActualCost">Sum of the actual cost of the leaf tasks.</param>
/// <param name="BudgetTotal">The host's overall budget, when it has one; <c>null</c> otherwise (the band then hides the budget card when no actual cost exists either).</param>
/// <param name="WbsLevels">Deepest WBS level of the set (2 for tasks one level below their parents).</param>
/// <param name="TaskCount">Number of tasks in the set.</param>
/// <param name="IsFiltered">True when the caller knows the set is smaller than its full data (a filter is active or closed tasks are hidden) — the band then shows a "Filtered" note.</param>
public sealed record GanttSummary(
    IReadOnlyDictionary<TmWorkItemStatus, int> StatusCounts,
    int OverdueCount,
    double LoggedHours,
    double EstimatedHours,
    int MilestonesDone,
    int MilestonesTotal,
    decimal ActualCost,
    decimal? BudgetTotal,
    int WbsLevels,
    int TaskCount,
    bool IsFiltered);
