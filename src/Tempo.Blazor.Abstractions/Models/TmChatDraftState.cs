using Tempo.Blazor.Abstractions.Shared;

namespace Tempo.Blazor.Models;

/// <summary>
/// Immutable snapshot of the unsaved-draft state exposed by <c>TmChat</c> through its
/// <c>OnDraftChanged</c> parameter. The chat composer, the open thread panel's reply input and the
/// inline message edit all hold text the host cannot observe through parameters — consumers (for
/// example a page that reloads the conversation or navigates away) use this record to build a
/// dirty-guard that asks for confirmation before an in-progress message would be thrown away.
/// <para>
/// The record deliberately carries ONLY state flags — never the draft text itself. This keeps the
/// payload free of user content (it would otherwise leak the typed message into parent render
/// trees and telemetry on every state transition); a consumer that needs the text receives it in
/// <c>OnSendMessage</c> / <c>OnSend</c> / <c>OnEditMessage</c> when the user commits. The record
/// uses value equality, so the component can suppress duplicate notifications for unchanged state.
/// </para>
/// </summary>
/// <param name="HasComposerDraft">
/// <see langword="true"/> when the main composer input holds text that is not whitespace-only.
/// </param>
/// <param name="HasThreadReplyDraft">
/// <see langword="true"/> when the open thread panel's reply input holds text that is not
/// whitespace-only. The input exists only while a thread panel is open; closing the panel clears
/// its reply draft.
/// </param>
/// <param name="EditingMessageId">
/// Id of the message whose inline edit is open; <see langword="null"/> when no edit is in
/// progress.
/// </param>
/// <param name="HasUnsavedContent">
/// Aggregate draft flag: <see langword="true"/> when any slot holds content that would be lost on
/// discard. For the two composer slots that means text that is not whitespace-only; for an open
/// message edit it means the edit text differs from the message text as it was when the edit
/// started — opening an edit without changing anything must not report an unsaved draft.
/// </param>
public sealed record TmChatDraftState(
    bool HasComposerDraft,
    bool HasThreadReplyDraft,
    string? EditingMessageId,
    bool HasUnsavedContent) : ITmDraftState;
