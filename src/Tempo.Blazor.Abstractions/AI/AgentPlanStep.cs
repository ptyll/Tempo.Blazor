namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One step of an agent plan: what the agent intends to do, its live execution state and the timestamps the
/// plan turns into durations.
/// <para>
/// The record is a VALUE: a host composes the plan, hands it to <c>TmAgentPlan</c> and replaces a step with
/// <c>step with { … }</c> when the run moves on. A host that only tracks the state can keep everything else
/// at its default — a step without an <see cref="Id"/> gets one, and a step is
/// <see cref="IsEditable"/> unless the host says otherwise.
/// </para>
/// <para>
/// Timestamps are the only source of a duration: <see cref="Duration"/> needs both ends, and a step that is
/// still <see cref="AgentStepStatus.Running"/> elapses from <see cref="StartedAt"/> to the "now" the caller
/// passes (the plan reads it from its injected <see cref="System.TimeProvider"/>), never to the wall clock.
/// </para>
/// </summary>
public sealed record AgentPlanStep
{
    /// <summary>Creates a pending step.</summary>
    /// <param name="title">What the step does, as the reader should see it. Null is stored as an empty
    /// string, so nothing ever renders the word "null".</param>
    /// <param name="id">Stable identity of the step, used as the row's <c>@key</c>, in the
    /// <see cref="AgentPlanEdit"/> a host persists and to restore a removed step on Undo. Null or
    /// whitespace generates one, so two identical steps are still two rows.</param>
    public AgentPlanStep(string title, string? id = null)
    {
        Title = title ?? string.Empty;
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id!;
    }

    /// <summary>Stable identity of the step. Never empty: a missing id is generated at construction, so the
    /// plan always has a key and a host always has something to persist.</summary>
    public string Id { get; init; }

    /// <summary>What the step does, as the reader should see it.</summary>
    public string Title { get; init; }

    /// <summary>Longer explanation of the step, rendered under the title. Null renders no description.</summary>
    public string? Description { get; init; }

    /// <summary>Current execution state. Default <see cref="AgentStepStatus.Pending"/>.</summary>
    public AgentStepStatus Status { get; init; }

    /// <summary>When the step started, from the host's clock. Null means the plan cannot time it.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>When the step finished. Null on a step that is still running (or never started).</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>The failure reason of an <see cref="AgentStepStatus.Failed"/> step, in the agent's own
    /// words. The plan renders it under the title and offers "Retry from this step".</summary>
    public string? Error { get; init; }

    /// <summary>
    /// Whether the reader may rename this step, add a step below it or remove it while the plan is in
    /// <see cref="AgentPlanMode.Review"/>. Default <c>true</c>; set <c>false</c> for a step the plan did not
    /// author (imported from a template, produced by the agent and pinned), so the row keeps its content but
    /// offers no edit action. The mode still wins: a running plan is read-only for every step.
    /// </summary>
    public bool IsEditable { get; init; } = true;

    /// <summary>Whether the step reached a terminal state (<see cref="AgentStepStatus.Done"/>,
    /// <see cref="AgentStepStatus.Failed"/> or <see cref="AgentStepStatus.Skipped"/>).</summary>
    public bool IsFinished =>
        Status is AgentStepStatus.Done or AgentStepStatus.Failed or AgentStepStatus.Skipped;

    /// <summary>
    /// How long the step took, or null while one end of its window is missing. A running step therefore
    /// reports null here (its duration is not a fact yet) — use <see cref="Elapsed"/> for that.
    /// </summary>
    public TimeSpan? Duration
        => StartedAt is { } start && CompletedAt is { } end && end >= start ? end - start : null;

    /// <summary>
    /// How much time the step has consumed at <paramref name="now"/>: its recorded
    /// <see cref="Duration"/> once it finished, otherwise the time since <see cref="StartedAt"/> — never
    /// negative, so a clock that moved backwards cannot paint "-0:03".
    /// </summary>
    /// <param name="now">The current moment, from the caller's <see cref="System.TimeProvider"/>.</param>
    /// <returns>The elapsed time, or null when the step never started.</returns>
    public TimeSpan? Elapsed(DateTimeOffset now)
    {
        if (Duration is { } recorded)
        {
            return recorded;
        }

        if (StartedAt is not { } start)
        {
            return null;
        }

        return now > start ? now - start : TimeSpan.Zero;
    }
}
