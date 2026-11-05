namespace Tempo.Blazor.Components.AI;

/// <summary>
/// How a <see cref="TmMemoryPanel"/> reports a forgotten memory to its host.
/// <para>
/// <see cref="UndoWindow"/> (the default) removes the row at once but tells the host only when the undo window closes —
/// losing a fact by mistake is the one action a mismatch between the reader and the store should not cause.
/// <see cref="Immediate"/> reports the removal at once and offers no undo, which is what a host with its own
/// confirmation flow wants.
/// </para>
/// </summary>
public enum MemoryForgetMode
{
    /// <summary>Hide the row, offer Undo for the window, and report the removal when the window closes.</summary>
    UndoWindow = 0,

    /// <summary>Report the removal at once and offer no undo.</summary>
    Immediate = 1,
}
