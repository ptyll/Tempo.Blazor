using System;

namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One agent as a delegate board column: who it is, how much work it can run at once, how loaded it is right now and
/// until when it is busy.
/// <para>
/// The record is a VALUE — a host recomposes it with <c>agent with { … }</c> when its load changes. The two load
/// facts are the HOST's: <see cref="ActiveCount"/> is how much work the agent is running, and
/// <see cref="MaxConcurrency"/> is how much it may run at once, so capacity is decided by the host's numbers and never
/// by how many cards the board happens to show. A column at capacity REFUSES a drop and says so, which is why the
/// numbers are read here rather than counted from the tasks.
/// </para>
/// <para>
/// Nothing is invented: a <see cref="MaxConcurrency"/> below one is clamped to one (an agent that can never run
/// anything would make its column a permanent, unexplained refusal), a negative <see cref="ActiveCount"/> reads as
/// zero, and a <see cref="BusyUntil"/> in the past means the agent is free NOW — the board compares it with its clock
/// instead of trusting a stale timestamp.
/// </para>
/// </summary>
public sealed record DelegateAgent
{
    /// <summary>Creates a board column for an agent.</summary>
    /// <param name="agent">The shared agent reference the column is named by. Required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is null.</exception>
    public DelegateAgent(AgentRef agent)
    {
        Agent = agent ?? throw new ArgumentNullException(nameof(agent));
    }

    /// <summary>The shared agent identity: the column's name, initials, palette slot and the value a team reports.</summary>
    public AgentRef Agent { get; init; }

    /// <summary>
    /// How many tasks the agent may run at the same time. Default 1. A value below one is clamped to one, so an
    /// agent always has room for exactly one task instead of a column that refuses everything.
    /// </summary>
    public int MaxConcurrency { get; init; } = 1;

    /// <summary>
    /// Until when the agent is busy with the work it is already running. Null means "not busy" (or unknown), which
    /// reads the same way as a timestamp in the past: the agent is free now.
    /// </summary>
    public DateTimeOffset? BusyUntil { get; init; }

    /// <summary>How much work the agent has in flight right now, as the HOST reports it. A negative value reads as zero.</summary>
    public int ActiveCount { get; init; }

    /// <summary>The stable identity of the agent — the value a task's <see cref="DelegateTask.AssignedAgentId"/> matches.</summary>
    public string Id => Agent.Id;

    /// <summary>The concurrency the surface actually uses: the host's value, never below one.</summary>
    public int EffectiveMaxConcurrency => MaxConcurrency < 1 ? 1 : MaxConcurrency;

    /// <summary>The load the surface actually counts: the host's value, never below zero.</summary>
    public int Load => ActiveCount < 0 ? 0 : ActiveCount;

    /// <summary>How many more tasks the agent can take right now (never negative).</summary>
    public int RemainingSlots => Math.Max(0, EffectiveMaxConcurrency - Load);

    /// <summary>
    /// Whether the agent has no room left. A full agent renders a "Full" word plus an icon and REFUSES a drop with an
    /// announcement — a silent refusal would read as a broken drag.
    /// </summary>
    public bool IsFull => RemainingSlots == 0;

    /// <summary>Whether the agent still has room for at least one task.</summary>
    public bool HasCapacity => !IsFull;

    /// <summary>Whether the host reported a busy-until time at all (it may already be in the past).</summary>
    public bool HasBusyUntil => BusyUntil is not null;

    /// <summary>
    /// Whether the agent is busy at <paramref name="now"/>: a timestamp in the past is not busy, because a stale
    /// timestamp would otherwise keep a free agent painted as occupied forever.
    /// </summary>
    /// <param name="now">The instant to compare with, from the component's clock.</param>
    /// <returns>Whether the agent is still occupied at <paramref name="now"/>.</returns>
    public bool IsBusyAt(DateTimeOffset now) => BusyUntil is { } busy && busy > now;

    /// <summary>
    /// The instant the agent becomes free, or <paramref name="now"/> when it is free already — the value the
    /// occupied-until preview projects from.
    /// </summary>
    /// <param name="now">The instant to compare with, from the component's clock.</param>
    /// <returns>The instant the agent's current work ends, never before <paramref name="now"/>.</returns>
    public DateTimeOffset FreeAt(DateTimeOffset now) => IsBusyAt(now) ? BusyUntil!.Value : now;
}
