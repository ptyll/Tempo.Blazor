using System;

namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One window a scheduler may place work in — "Monday 09:00–12:00, 60 units" — with the capacity it offers and the
/// load the host has already committed to it.
/// <para>
/// The record is a VALUE: the host recomposes it with <c>window with { … }</c>. <see cref="Capacity"/> is the amount
/// of work the window accepts and <see cref="Used"/> the amount already taken by work the board does not show (other
/// schedules, reservations, the host's own backlog), so a window can be FULL or even OVERLOADED before the board
/// places anything in it. Both are reported as words plus an icon, never as a colour alone.
/// </para>
/// <para>
/// Nothing is invented: a window whose <see cref="End"/> is not after its <see cref="Start"/> offers no time at all
/// (the planner skips it, and the surface never divides by its length), a negative <see cref="Capacity"/> reads as
/// zero and a negative <see cref="Used"/> as zero. A blank <see cref="Id"/> is REJECTED at construction, because the
/// run segments and the reader's own placement are keyed by it.
/// </para>
/// </summary>
public sealed record RunWindow
{
    /// <summary>Creates a run window.</summary>
    /// <param name="id">Stable identity of the window in the host's plan. Required.</param>
    /// <param name="start">When the window opens.</param>
    /// <param name="end">When it closes. Not after <paramref name="start"/> means the window offers no time.</param>
    /// <param name="capacity">How much work the window accepts. A negative value reads as zero.</param>
    /// <param name="used">How much of that capacity the host has already committed elsewhere.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null, empty or whitespace.</exception>
    public RunWindow(string id, DateTimeOffset start, DateTimeOffset end, int capacity, int used = 0)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "A run window needs a stable, non-blank id: the run segments and the reader's own placement are " +
                "keyed by it, so an id invented here would move work on every rebuild.",
                nameof(id));
        }

        Id = id;
        Start = start;
        End = end;
        Capacity = capacity;
        Used = used;
    }

    /// <summary>Stable identity of the window. Never empty: construction rejects a blank value.</summary>
    public string Id { get; init; }

    /// <summary>When the window opens.</summary>
    public DateTimeOffset Start { get; init; }

    /// <summary>When the window closes.</summary>
    public DateTimeOffset End { get; init; }

    /// <summary>How much work the window accepts. A negative value reads as zero through <see cref="EffectiveCapacity"/>.</summary>
    public int Capacity { get; init; }

    /// <summary>How much capacity the host has already committed to work this surface does not show.</summary>
    public int Used { get; init; }

    /// <summary>The capacity the planner actually uses: the host's value, never below zero.</summary>
    public int EffectiveCapacity => Capacity < 0 ? 0 : Capacity;

    /// <summary>The committed load the planner actually uses: the host's value, never below zero.</summary>
    public int EffectiveUsed => Used < 0 ? 0 : Used;

    /// <summary>How long the window offers, or <see cref="TimeSpan.Zero"/> when it closes before it opens.</summary>
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;

    /// <summary>Whether the window offers any time at all.</summary>
    public bool HasDuration => Duration > TimeSpan.Zero;

    /// <summary>The units still free for the board, never negative (an overloaded window offers nothing).</summary>
    public int Available => Math.Max(0, EffectiveCapacity - EffectiveUsed);

    /// <summary>Whether the host has committed more work than the window accepts.</summary>
    public bool IsOverloaded => EffectiveUsed > EffectiveCapacity;

    /// <summary>Whether the host's committed work already fills the window exactly (or beyond).</summary>
    public bool IsFull => Available == 0;

    /// <summary>How many units the window is over its capacity by (zero when it is not overloaded).</summary>
    public int Overflow => IsOverloaded ? EffectiveUsed - EffectiveCapacity : 0;

    /// <summary>The share of the capacity that is already committed, in <c>0…1</c> (0 for a window with no capacity).</summary>
    public double Utilization => EffectiveCapacity == 0
        ? 0d
        : Math.Min(1d, (double)EffectiveUsed / EffectiveCapacity);

    /// <summary>
    /// Whether <paramref name="instant"/> falls inside the window. The end is EXCLUSIVE: a run starting exactly when a
    /// window closes belongs to the next one, which is what makes two adjacent windows partition the timeline rather
    /// than overlap by an instant.
    /// </summary>
    /// <param name="instant">The instant to test.</param>
    /// <returns>Whether the instant is inside the window.</returns>
    public bool Contains(DateTimeOffset instant) => HasDuration && instant >= Start && instant < End;

    /// <summary>
    /// The day the window belongs to, in its own offset. A window that starts at 22:00 belongs to the day it starts
    /// on; a window that starts before midnight and ends after it is still that day's window.
    /// </summary>
    public DateOnly Day => DateOnly.FromDateTime(Start.DateTime);
}
