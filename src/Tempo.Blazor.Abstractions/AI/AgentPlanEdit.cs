namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One edit of an agent plan, reported next to the whole new step list by
/// <c>TmAgentPlan.OnPlanChanged</c>: <c>OnPlanChanged(IReadOnlyList&lt;AgentPlanStep&gt; steps, AgentPlanEdit edit)</c>.
/// <para>
/// The list is authoritative — a host that keeps the steps applies it and is done. The edit answers the
/// questions a list cannot: WHICH operation the reader performed, at WHICH position, and (for a removal) the
/// removed step itself, so an undo does not have to diff two snapshots. The factories
/// (<see cref="Rename"/>, <see cref="Add"/>, <see cref="Remove"/>, <see cref="Reorder"/>) are the only way
/// to build one, so a half-filled edit cannot describe an operation that never happened.
/// </para>
/// </summary>
public sealed record AgentPlanEdit
{
    private AgentPlanEdit(
        AgentPlanEditKind kind,
        int index,
        string? stepId,
        string? title,
        int? fromIndex,
        int? toIndex,
        AgentPlanStep? step)
    {
        Kind = kind;
        Index = index;
        StepId = stepId;
        Title = title;
        FromIndex = fromIndex;
        ToIndex = toIndex;
        Step = step;
    }

    /// <summary>Which operation the reader performed.</summary>
    public AgentPlanEditKind Kind { get; }

    /// <summary>
    /// The zero-based position the edit concerns in the NEW list: the renamed row, the insertion point of an
    /// added step, the hole a removed step left behind, or the new position of a reordered one.
    /// </summary>
    public int Index { get; }

    /// <summary>The <see cref="AgentPlanStep.Id"/> of the step the edit concerns.</summary>
    public string? StepId { get; }

    /// <summary>The new title; set for <see cref="AgentPlanEditKind.Rename"/> only.</summary>
    public string? Title { get; }

    /// <summary>The index the step moved from; set for <see cref="AgentPlanEditKind.Reorder"/> only.</summary>
    public int? FromIndex { get; }

    /// <summary>The index the step moved to; set for <see cref="AgentPlanEditKind.Reorder"/> only.</summary>
    public int? ToIndex { get; }

    /// <summary>
    /// The step itself: the inserted one for <see cref="AgentPlanEditKind.Add"/> (which is also how an undo
    /// of a removal comes back) and the removed one for <see cref="AgentPlanEditKind.Remove"/>.
    /// </summary>
    public AgentPlanStep? Step { get; }

    /// <summary>An existing step's title changed.</summary>
    /// <param name="index">The zero-based position of the renamed step.</param>
    /// <param name="stepId">The step's <see cref="AgentPlanStep.Id"/>.</param>
    /// <param name="title">The new title, already trimmed and guaranteed non-empty.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="title"/> is null, empty or whitespace.</exception>
    public static AgentPlanEdit Rename(int index, string stepId, string title)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A rename needs a title.", nameof(title));
        }

        return new AgentPlanEdit(AgentPlanEditKind.Rename, index, stepId, title.Trim(), null, null, null);
    }

    /// <summary>A step was inserted at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based position the new step occupies.</param>
    /// <param name="step">The inserted step.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is null.</exception>
    public static AgentPlanEdit Add(int index, AgentPlanStep step)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentNullException.ThrowIfNull(step);

        return new AgentPlanEdit(AgentPlanEditKind.Add, index, step.Id, null, null, null, step);
    }

    /// <summary>The step at <paramref name="index"/> was removed.</summary>
    /// <param name="index">The zero-based position the step occupied before its removal.</param>
    /// <param name="step">The removed step, kept so the host can undo the removal.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is null.</exception>
    public static AgentPlanEdit Remove(int index, AgentPlanStep step)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentNullException.ThrowIfNull(step);

        return new AgentPlanEdit(AgentPlanEditKind.Remove, index, step.Id, null, null, null, step);
    }

    /// <summary>The step at <paramref name="fromIndex"/> moved to <paramref name="toIndex"/>.</summary>
    /// <param name="fromIndex">The zero-based position the step started at.</param>
    /// <param name="toIndex">The zero-based position it occupies in the new list.</param>
    /// <param name="stepId">The moved step's <see cref="AgentPlanStep.Id"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either index is negative.</exception>
    public static AgentPlanEdit Reorder(int fromIndex, int toIndex, string stepId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(toIndex);

        return new AgentPlanEdit(AgentPlanEditKind.Reorder, toIndex, stepId, null, fromIndex, toIndex, null);
    }
}
