namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One committed move of a scheduled run to another window: the run, the window it sat in and the window the reader
/// moved it to.
/// <para>
/// The surface raises at most ONE of these per reader action and changes nothing itself — the host re-places the run
/// in its own plan and re-renders. Until it does, the surface keeps the reader's pick, so the run visibly sits where
/// it was dropped; a host that re-renders the run UNCHANGED (the same instance) does not undo that pick.
/// </para>
/// </summary>
public sealed record ScheduledRunMoveRequest
{
    /// <summary>Creates a move request.</summary>
    /// <param name="run">The run that moved. Required.</param>
    /// <param name="from">The window it left (null when it had no window yet).</param>
    /// <param name="to">The window it moved to. Required, because a move without a destination is not a move.</param>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> or <paramref name="to"/> is null.</exception>
    public ScheduledRunMoveRequest(ScheduledRun run, RunWindow? from, RunWindow to)
    {
        Run = run ?? throw new ArgumentNullException(nameof(run));
        From = from;
        To = to ?? throw new ArgumentNullException(nameof(to));
    }

    /// <summary>The run that moved, as the host supplied it.</summary>
    public ScheduledRun Run { get; init; }

    /// <summary>The window the run left, or null when it had no window yet.</summary>
    public RunWindow? From { get; init; }

    /// <summary>The window the run was moved to.</summary>
    public RunWindow To { get; init; }

    /// <summary>The window id the host should store as the run's start window now.</summary>
    public string WindowId => To.Id;

    /// <summary>Whether the run actually changed window (a move onto the window it already sits in is not a change).</summary>
    public bool IsChange => From is null || !string.Equals(From.Id, To.Id, StringComparison.Ordinal);
}
