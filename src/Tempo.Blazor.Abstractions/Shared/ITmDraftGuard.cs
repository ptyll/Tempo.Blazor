namespace Tempo.Blazor.Abstractions.Shared;

/// <summary>
/// Uniform dirty-guard surface implemented by every component whose composer text lives in
/// private fields the host cannot observe through parameters (TmActivityComments, TmChat, the
/// Notion comment panels and section, and the DocumentEditor / Signing comment stack).
/// <para>
/// Typical flow: poll <see cref="HasUnsavedDraft"/> (or track the component's
/// <c>OnDraftChanged</c> callback), ask the user for confirmation in the CONSUMER'S OWN dialog —
/// no implementing component shows one itself — and call <see cref="DiscardDraftAsync"/> once the
/// user confirms. Both members are safe to call from outside the render cycle; implementations
/// dispatch through <c>InvokeAsync</c>.
/// </para>
/// </summary>
public interface ITmDraftGuard
{
    /// <summary>
    /// Gets a value indicating whether the component currently holds an unsaved draft that a
    /// reload, a navigation or a panel close would silently discard. The same aggregate value the
    /// component reports through its <c>OnDraftChanged</c> callback; read it on the component's
    /// dispatcher (an event handler or <c>InvokeAsync</c>) from background threads.
    /// </summary>
    bool HasUnsavedDraft { get; }

    /// <summary>
    /// Clears every draft slot of the component (composer texts, open replies, open edits). A
    /// no-op — without any <c>OnDraftChanged</c> notification — when no editor is open and no
    /// draft exists. Intended to be called AFTER the user confirmed the discard in the consumer's
    /// own confirmation dialog.
    /// </summary>
    /// <returns>A <see cref="Task"/> that completes when the discard has been applied.</returns>
    Task DiscardDraftAsync();
}
