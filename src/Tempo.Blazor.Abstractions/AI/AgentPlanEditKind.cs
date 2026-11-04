namespace Tempo.Blazor.AI.Models;

/// <summary>
/// Which edit <c>TmAgentPlan</c> turned into the new step list it reports through
/// <c>OnPlanChanged(IReadOnlyList&lt;AgentPlanStep&gt;, AgentPlanEdit)</c>.
/// </summary>
public enum AgentPlanEditKind
{
    /// <summary>An existing step's <see cref="AgentPlanStep.Title"/> changed.</summary>
    Rename = 0,

    /// <summary>A step was inserted at <see cref="AgentPlanEdit.Index"/>.</summary>
    Add = 1,

    /// <summary>The step at <see cref="AgentPlanEdit.Index"/> was removed; the removed step rides in
    /// <see cref="AgentPlanEdit.Step"/>, so the host can undo it.</summary>
    Remove = 2,

    /// <summary>A step moved from <see cref="AgentPlanEdit.FromIndex"/> to
    /// <see cref="AgentPlanEdit.ToIndex"/>.</summary>
    Reorder = 3,
}
