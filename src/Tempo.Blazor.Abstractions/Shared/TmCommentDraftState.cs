namespace Tempo.Blazor.Abstractions.Shared;

/// <summary>
/// Immutable snapshot of the comment editor draft state exposed by <c>TmActivityComments</c>
/// through its <c>OnDraftChanged</c> parameter. Consumers (for example an outage-report page)
/// use it to build a dirty-guard: when a draft is in progress, a refresh or retry asks for
/// confirmation before the unsaved comment would be thrown away.
/// <para>
/// The record deliberately carries ONLY state flags — never the draft text itself. This keeps
/// the payload free of user content (it would otherwise leak the typed comment into parent
/// render trees and telemetry on every state transition); a consumer that needs the text
/// receives it in <c>OnCommentAdded</c> / <c>OnCommentEdited</c> when the comment is submitted.
/// The record uses value equality, so the component can suppress duplicate notifications for
/// unchanged state.
/// </para>
/// </summary>
/// <param name="IsEditorOpen">
/// <see langword="true"/> when a comment editor is visible — either the new-comment editor or
/// the inline editor of an existing comment.
/// </param>
/// <param name="EditingCommentId">
/// Id of the comment being edited while its inline editor is open; <see langword="null"/> when
/// no existing comment is being edited (including the new-comment editor case).
/// </param>
/// <param name="HasUnsavedContent">
/// <see langword="true"/> when the open editor holds content that would be lost on discard.
/// Two semantics apply: for a NEW comment it means the content is not empty; for an EDIT of an
/// existing comment it means the content differs from the comment's original body — opening an
/// edit without changing anything must not report an unsaved draft.
/// </param>
public sealed record TmCommentDraftState(bool IsEditorOpen, string? EditingCommentId, bool HasUnsavedContent);
