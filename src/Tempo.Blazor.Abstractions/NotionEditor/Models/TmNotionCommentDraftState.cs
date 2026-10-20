using Tempo.Blazor.Abstractions.Shared;

namespace Tempo.Blazor.NotionEditor.Models;

/// <summary>
/// Immutable snapshot of the unsaved-draft state exposed by <c>TmNotionPageCommentPanel</c>
/// through its <c>OnDraftChanged</c> parameter. All of the panel's composers — the new-comment
/// input, the thread reply, the inline reply and the entry edit — hold text the host cannot
/// observe through parameters; consumers use this record to build a dirty-guard that asks for
/// confirmation before an in-progress comment would be thrown away (for example before a page
/// navigation or a provider swap).
/// <para>
/// The record deliberately carries ONLY state flags — never the draft text itself. This keeps the
/// payload free of user content (it would otherwise leak the typed comment into parent render
/// trees and telemetry on every state transition); a consumer that needs the text receives it
/// through the comment provider when the user submits. The record uses value equality, so the
/// component can suppress duplicate notifications for unchanged state.
/// </para>
/// </summary>
/// <param name="HasNewCommentDraft">
/// <see langword="true"/> when the new-comment input holds text that is not whitespace-only.
/// </param>
/// <param name="HasThreadReplyDraft">
/// <see langword="true"/> when the open thread reply input holds text that is not
/// whitespace-only.
/// </param>
/// <param name="HasInlineReplyDraft">
/// <see langword="true"/> when the open inline reply holds user content beyond the quote the
/// panel auto-inserts when the reply opens — the auto-generated quote alone is not user content
/// and must not report an unsaved draft.
/// </param>
/// <param name="EditingEntryId">
/// Id of the comment entry whose inline edit is open; <see langword="null"/> when no edit is in
/// progress.
/// </param>
/// <param name="HasUnsavedContent">
/// Aggregate draft flag: <see langword="true"/> when any slot holds content that would be lost on
/// discard. For the composer slots that means user content beyond effectively-empty text (and,
/// for the inline reply, beyond the auto-inserted quote); for an open entry edit it means the edit
/// text differs from the entry body as it was when the edit started — opening an edit without
/// changing anything must not report an unsaved draft.
/// </param>
public sealed record TmNotionCommentDraftState(
    bool HasNewCommentDraft,
    bool HasThreadReplyDraft,
    bool HasInlineReplyDraft,
    string? EditingEntryId,
    bool HasUnsavedContent) : ITmDraftState;
