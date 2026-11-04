namespace Tempo.Blazor.AI.Models;

/// <summary>
/// What <c>TmAgentPlan</c> may show and edit: the reviewable plan before a run, or the read-only plan of a
/// run in progress.
/// <para>
/// The mode is a switch of SHAPE, not only of permissions: a <see cref="Review"/> plan offers renaming,
/// adding, removing and reordering (through <c>TmSortableList</c>) plus the Approve / Run actions, while a
/// <see cref="Running"/> plan renders the same rows read-only, counts the running step's live elapsed time,
/// shows a failed step's error with "Retry from this step" and hides every plan-level action.
/// </para>
/// </summary>
public enum AgentPlanMode
{
    /// <summary>The plan is being reviewed; every edit path and every plan action is available.</summary>
    Review = 0,

    /// <summary>The plan is running; the rows are read-only and only a failed step still offers Retry.</summary>
    Running = 1,
}
