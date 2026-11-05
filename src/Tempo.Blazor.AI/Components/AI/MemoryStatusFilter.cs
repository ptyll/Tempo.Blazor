namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Which memories a <see cref="TmMemoryPanel"/> shows.
/// <para>
/// The filter is a VIEW, never a change: it only narrows the rows on screen and never reports anything to the host.
/// <see cref="All"/> shows every stored memory, including the forgotten ones whose undo window is still open, so a reader
/// can find a fact they just removed.
/// </para>
/// </summary>
public enum MemoryStatusFilter
{
    /// <summary>Every memory, whatever its status.</summary>
    All = 0,

    /// <summary>Memories the agent is using.</summary>
    Active = 1,

    /// <summary>Memories kept but not used.</summary>
    Paused = 2,

    /// <summary>Memories the agent proposed and a human has to accept or reject.</summary>
    PendingReview = 3,

    /// <summary>Memories that were removed.</summary>
    Forgotten = 4,
}
