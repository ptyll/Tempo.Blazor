namespace Tempo.Blazor.AI.Models;

/// <summary>
/// What the agent is allowed to do with one stored fact.
/// <para>
/// The state is always painted as a localized word plus an icon; the tone is a third, redundant cue. A forgotten memory
/// stays in the list only while its undo window is open, and a memory the agent proposed is
/// <see cref="PendingReview"/> until a human accepts or rejects it.
/// </para>
/// </summary>
public enum MemoryItemStatus
{
    /// <summary>The memory is stored and in use. The default, so an unclassified memory is never "forgotten".</summary>
    Active = 0,

    /// <summary>The memory is kept but not used by the agent until it is resumed.</summary>
    Paused = 1,

    /// <summary>The memory was removed; it is listed only while the reader can still undo the removal.</summary>
    Forgotten = 2,

    /// <summary>The agent proposed the memory and is waiting for a human to accept or reject it.</summary>
    PendingReview = 3,
}
