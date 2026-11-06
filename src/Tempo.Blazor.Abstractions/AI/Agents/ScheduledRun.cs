namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One job a scheduler has to fit into run windows: what it is, how many units it needs and where it currently sits.
/// <para>
/// The record is a VALUE: the host recomposes it with <c>run with { … }</c>, and no component mutates the instance it
/// was given. <see cref="Id"/> is the identity the plan and the reader's own placement are keyed by, so a blank id is
/// REJECTED at construction.
/// </para>
/// <para>
/// <see cref="Segments"/> is what the HOST has already committed: the planner reads the first segment's window as the
/// run's preferred start window, which is how "move this run to Thursday" survives a round-trip. The component
/// recomputes the actual placement from <see cref="EstimatedUnits"/> over the windows it was given, so the plan it
/// draws can never disagree with the capacity bars next to it. An empty list means "no preference — first fit".
/// </para>
/// </summary>
public sealed record ScheduledRun
{
    /// <summary>Creates a scheduled run.</summary>
    /// <param name="id">Stable identity in the host's plan. Required.</param>
    /// <param name="title">What the run does, in the host's own words. Blank is allowed and reads as empty.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null, empty or whitespace.</exception>
    public ScheduledRun(string id, string title)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "A scheduled run needs a stable, non-blank id: the plan and the reader's own placement are keyed by " +
                "it, so an id invented here would move work on every rebuild.",
                nameof(id));
        }

        Id = id;
        Title = string.IsNullOrWhiteSpace(title) ? string.Empty : title;
    }

    /// <summary>Stable identity of the run. Never empty: construction rejects a blank value.</summary>
    public string Id { get; init; }

    /// <summary>The run in the host's own words. A whitespace-only title reads as empty.</summary>
    public string Title { get; init; }

    /// <summary>
    /// How much work the run needs, in abstract units the windows also count in. Zero (the default) means the run
    /// needs no capacity; a negative value reads as zero.
    /// </summary>
    public int EstimatedUnits { get; init; }

    /// <summary>
    /// The placement the host committed, or an empty list. Its FIRST segment's window is the run's preferred start
    /// window; the rest is informational (the planner recomputes the slices so they add up to the capacity it draws).
    /// </summary>
    public IReadOnlyList<RunSegment> Segments { get; init; } = [];

    /// <summary>
    /// How the run is doing. Default <see cref="ScheduledRunStatus.Planned"/>: an unclassified run is planned work,
    /// never a finished one.
    /// </summary>
    public ScheduledRunStatus Status { get; init; }

    /// <summary>The units the plan has to place: the host's value, never below zero.</summary>
    public int PlannedUnits => EstimatedUnits < 0 ? 0 : EstimatedUnits;

    /// <summary>Whether the run needs any capacity at all.</summary>
    public bool NeedsCapacity => PlannedUnits > 0;

    /// <summary>Whether the host supplied a placement.</summary>
    public bool HasSegments => Segments is { Count: > 0 };

    /// <summary>
    /// The window the run prefers to start in: the first segment's window, or null when the host supplied none (the
    /// planner then takes the first window with room).
    /// </summary>
    public string? PreferredWindowId
    {
        get
        {
            if (Segments is not { Count: > 0 })
            {
                return null;
            }

            foreach (var segment in Segments)
            {
                if (segment is not null && !string.IsNullOrWhiteSpace(segment.WindowId))
                {
                    return segment.WindowId;
                }
            }

            return null;
        }
    }

    /// <summary>Whether <see cref="Status"/> is one of the declared states (a cast integer may not be).</summary>
    public bool HasKnownStatus => Status is >= ScheduledRunStatus.Planned and <= ScheduledRunStatus.Paused;

    /// <summary>Whether the run is finished (succeeded or failed) and no longer competes for attention.</summary>
    public bool IsSettled => Status is ScheduledRunStatus.Succeeded or ScheduledRunStatus.Failed;

    /// <summary>Whether the run is running right now.</summary>
    public bool IsRunning => Status == ScheduledRunStatus.Running;

    /// <summary>Whether the run is deliberately not scheduled for now.</summary>
    public bool IsPaused => Status == ScheduledRunStatus.Paused;
}
