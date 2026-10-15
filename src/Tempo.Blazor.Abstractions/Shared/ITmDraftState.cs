namespace Tempo.Blazor.Abstractions.Shared;

/// <summary>
/// Aggregate flag carried by every draft-state record the library reports through an
/// <c>OnDraftChanged</c> parameter (<see cref="TmCommentDraftState"/>, the chat and Notion
/// comment draft states). A consumer that only needs the dirty decision —
/// "is there anything a reload, a navigation or a panel close would throw away?" — can read this
/// one member without caring which concrete record a component reports.
/// <para>
/// The flag deliberately says nothing about WHERE the draft lives or what it contains: the
/// per-slot members stay on the concrete records, and the draft text itself is never reported —
/// it reaches the consumer through the component's own submit callbacks when the user commits.
/// </para>
/// </summary>
public interface ITmDraftState
{
    /// <summary>
    /// Gets a value indicating whether any draft slot currently holds content that would be lost
    /// on discard. Always <see langword="false"/> before the first user input; for an open edit
    /// it means the edited text differs from the original as it was when the edit started.
    /// </summary>
    bool HasUnsavedContent { get; }
}
