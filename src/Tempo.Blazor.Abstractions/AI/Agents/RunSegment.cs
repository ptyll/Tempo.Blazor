namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One slice of a scheduled run's work: how many units of it sit in one window, and which part of the window's time
/// they occupy.
/// <para>
/// A run longer than a window is SPLIT, and each slice is one of these — so a "3 h batch" over three one-hour windows
/// reads as three slices of the same run instead of a run that magically appears three times. The record is a VALUE
/// the planner and the host both produce; <see cref="Units"/> is what the window's capacity gives up.
/// </para>
/// </summary>
public sealed record RunSegment
{
    /// <summary>Creates a segment.</summary>
    /// <param name="windowId">The id of the <see cref="RunWindow"/> the slice sits in. Required.</param>
    /// <param name="start">When the slice starts inside the window.</param>
    /// <param name="end">When it ends.</param>
    /// <param name="units">How many units of the run this slice covers.</param>
    /// <exception cref="ArgumentException"><paramref name="windowId"/> is null, empty or whitespace.</exception>
    public RunSegment(string windowId, DateTimeOffset start, DateTimeOffset end, int units)
    {
        if (string.IsNullOrWhiteSpace(windowId))
        {
            throw new ArgumentException(
                "A run segment names the window it sits in, so its id cannot be blank.",
                nameof(windowId));
        }

        WindowId = windowId;
        Start = start;
        End = end;
        Units = units;
    }

    /// <summary>The id of the window the slice sits in.</summary>
    public string WindowId { get; init; }

    /// <summary>When the slice starts, in the host's timeline.</summary>
    public DateTimeOffset Start { get; init; }

    /// <summary>When the slice ends.</summary>
    public DateTimeOffset End { get; init; }

    /// <summary>How many units of the run this slice covers. A negative value reads as zero.</summary>
    public int Units { get; init; }

    /// <summary>The units the slice actually covers: never below zero.</summary>
    public int EffectiveUnits => Units < 0 ? 0 : Units;

    /// <summary>How long the slice lasts, or <see cref="TimeSpan.Zero"/> when it ends before it starts.</summary>
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;
}
