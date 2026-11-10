namespace Tempo.Blazor.Components.AI;

/// <summary>
/// How a <see cref="TmMemoryPanel"/> keeps the removals a reader may still take back.
/// <para>
/// <see cref="Single"/> (the default) is the pre-2.11.0 rule: ONE undo window exists at a time, so a second Forget
/// reports the first removal immediately. <see cref="Queue"/> keeps one window PER pending removal — each with its own
/// countdown on the injected <see cref="System.TimeProvider"/> and its own undo row — so a reader who removes three
/// memories in a row can still take back any of them. The stack is bounded by
/// <see cref="TmMemoryPanel.MaxVisibleUndoRows"/>: past that bound the OLDEST pending removal is reported immediately
/// rather than dropped off the screen, because a removal nobody can see and nobody was told about would be lost.
/// </para>
/// </summary>
public enum MemoryUndoMode
{
    /// <summary>One undo window at a time; a second removal reports the previous one at once.</summary>
    Single = 0,

    /// <summary>One undo row and one countdown per pending removal, up to the visible limit.</summary>
    Queue = 1,
}
