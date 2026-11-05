namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One unit of work an orchestrator split across N workers, as a fan-out surface draws it: the worker it is assigned
/// to, how far it got, how many attempts it took, when it should end, what it cost and — when it failed — why.
/// <para>
/// The record is a VALUE: a host recomposes it with <c>task with { … }</c> when the work moves on, and no component
/// mutates it. <see cref="EstimatedEndAt"/> is an ESTIMATE, never a fact: a surface turns it into a live countdown
/// against its own clock, and a finished task keeps its recorded times untouched.
/// </para>
/// <para>
/// Nothing here carries colour or words: the state is the shared <see cref="AgentActivityStatus"/> (one icon, one tone
/// and one localized word for every agent surface) and the identity is the shared <see cref="AgentRef"/>, so a worker
/// paints the same palette slot here as it does in the lanes and in the graph.
/// </para>
/// </summary>
public sealed record FanOutTask
{
    /// <summary>Creates a fan-out task.</summary>
    /// <param name="id">
    /// Stable identity of the task — the key a retry or a cancel reports. Null or whitespace generates one, so a list
    /// built in a loop still renders; a host that has to match the callback to its own record supplies its own id (the
    /// same rule <see cref="AgentRef"/> follows for agents).
    /// </param>
    /// <param name="label">
    /// What the unit of work is, in the host's own words (a file, a region, an item). Empty renders the surface's own
    /// fallback instead of a blank tile.
    /// </param>
    public FanOutTask(string id, string label)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id!;
        Label = label ?? string.Empty;
    }

    /// <summary>Stable identity of the task. Never empty: a missing id is generated at construction.</summary>
    public string Id { get; init; }

    /// <summary>The unit of work, as the host wants it read. Empty renders the surface's own fallback.</summary>
    public string Label { get; init; }

    /// <summary>
    /// What the task is doing right now. Default <see cref="AgentActivityStatus.Idle"/>; painted as an icon, a
    /// localized word AND a tone, never as a colour alone.
    /// </summary>
    public AgentActivityStatus Status { get; init; }

    /// <summary>
    /// The identity of the worker running the task, or <c>null</c> when the host tracks workers by id only. Setting it
    /// makes the surface paint the worker's palette slot, initials and name.
    /// </summary>
    public AgentRef? Worker { get; init; }

    /// <summary>
    /// The worker's id as a plain string, for a host whose workers are not <see cref="AgentRef"/>s. The surface paints
    /// it as text; <see cref="Worker"/> wins when both are set, because it carries the whole identity.
    /// </summary>
    public string? WorkerId { get; init; }

    /// <summary>
    /// Which attempt is running (or failed), 1-based. Default 1. A value below 1 is READ as 1 by every derived member
    /// and by the surface, because "attempt 0 of 3" is not a state a run can be in.
    /// </summary>
    public int Attempt { get; init; } = 1;

    /// <summary>
    /// How many attempts the host allows, or <c>null</c> when it has no limit. A limit of zero or less is read as "no
    /// limit", so a host that forgets to set it never blocks a retry.
    /// </summary>
    public int? MaxAttempts { get; init; }

    /// <summary>When the task started, or <c>null</c> while it is still waiting for a worker.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// The host's estimate of when the task will finish, or <c>null</c> when it has none. A surface turns it into a
    /// live countdown against its own clock and NEVER treats it as a recorded end; a task in a finished state ignores
    /// it entirely.
    /// </summary>
    public DateTimeOffset? EstimatedEndAt { get; init; }

    /// <summary>
    /// What the task has cost so far, in MICRO-UNITS (1,000,000 is 1.00 of the currency) — the unit
    /// <see cref="AiCostFormatter"/> and the agent-run surfaces already use, so one run never mixes two money shapes.
    /// <c>null</c> means "the host does not price this task"; it is not zero.
    /// </summary>
    public long? CostMicroUnits { get; init; }

    /// <summary>
    /// Why the task failed, in the host's own words (a message, a rejection reason). Empty renders no error; the
    /// surface prints it verbatim and never parses it.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>Whether the host supplied a label. A surface paints its own localized fallback when it is false.</summary>
    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    /// <summary>Whether the task carries an error message worth printing.</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    /// <summary>Whether the host priced the task at all (<c>false</c> is "no price", not "free").</summary>
    public bool HasCost => CostMicroUnits is not null;

    /// <summary>The attempt as a surface reads it: never below 1.</summary>
    public int EffectiveAttempt => Attempt < 1 ? 1 : Attempt;

    /// <summary>Whether the host declared an attempt limit at all (a non-positive limit counts as none).</summary>
    public bool HasAttemptLimit => MaxAttempts is > 0;

    /// <summary>
    /// Whether the retries are used up: an attempt limit exists and the task already reached it. A task without a
    /// limit is never exhausted.
    /// </summary>
    public bool RetryExhausted => MaxAttempts is { } limit && limit > 0 && EffectiveAttempt >= limit;

    /// <summary>
    /// Whether a retry is offered: the task FAILED and its retries are not used up. A running task cannot be retried
    /// (it can be cancelled instead) and a succeeded one has nothing to retry.
    /// </summary>
    public bool CanRetry => Status == AgentActivityStatus.Failed && !RetryExhausted;

    /// <summary>
    /// Whether a cancel is offered: the task is queued, running or waiting for a decision. A finished task has nothing
    /// left to stop.
    /// </summary>
    public bool CanCancel => Status is AgentActivityStatus.Queued
        or AgentActivityStatus.Running
        or AgentActivityStatus.WaitingForApproval;

    /// <summary>Whether the task still holds a worker (queued, running or waiting for a decision).</summary>
    public bool IsActive => CanCancel;

    /// <summary>Whether the task reached a terminal state (succeeded, failed or cancelled).</summary>
    public bool IsFinished => Status is AgentActivityStatus.Succeeded
        or AgentActivityStatus.Failed
        or AgentActivityStatus.Cancelled;

    /// <summary>
    /// The initials the tile's avatar paints: the worker's own initials when <see cref="Worker"/> is set, the initials
    /// of <see cref="Label"/> otherwise, and <c>"?"</c> when there is no name at all.
    /// </summary>
    public string Initials => Worker is { } worker ? worker.Initials : AgentRef.InitialsOf(Label);

    /// <summary>
    /// The agent palette token the avatar paints with, or an empty string when no <see cref="Worker"/> is set. Always
    /// a declared <c>--tm-agent-color-{0..7}</c> token when it is not empty.
    /// </summary>
    public string ColorToken => Worker is { } worker ? worker.ColorToken : string.Empty;

    /// <summary>The worker's name for a surface to print: the agent's name, else <see cref="WorkerId"/>.</summary>
    public string WorkerName => Worker is { HasName: true } worker
        ? worker.Name
        : WorkerId ?? string.Empty;

    /// <summary>Whether the host named the worker in any way.</summary>
    public bool HasWorker => !string.IsNullOrWhiteSpace(WorkerName);

    /// <summary>
    /// The time left until <see cref="EstimatedEndAt"/>, measured on the caller's clock — or <c>null</c> when the host
    /// gave no estimate or the task no longer counts as active. An estimate in the past reads as zero (the task is
    /// overdue), never as a negative duration.
    /// </summary>
    /// <param name="now">The current moment, from the caller's clock.</param>
    /// <returns>The remaining time, or <c>null</c> when there is nothing to count.</returns>
    public TimeSpan? RemainingAt(DateTimeOffset now)
    {
        if (!IsActive || EstimatedEndAt is not { } estimate)
        {
            return null;
        }

        var remaining = estimate - now;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    /// <summary>
    /// Whether the estimate has already passed on the caller's clock — the state a surface paints as "overdue" instead
    /// of a countdown that would sit at zero forever.
    /// </summary>
    /// <param name="now">The current moment, from the caller's clock.</param>
    /// <returns><c>true</c> for an active task whose estimate is in the past.</returns>
    public bool IsOverdueAt(DateTimeOffset now)
        => IsActive && EstimatedEndAt is { } estimate && estimate <= now;
}
