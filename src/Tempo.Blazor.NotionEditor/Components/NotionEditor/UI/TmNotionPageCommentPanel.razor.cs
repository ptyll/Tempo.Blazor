using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Models;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Helpers;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionPageCommentPanel : ComponentBase, IDisposable, ITmDraftGuard
{
    private const string CurrentUserId = "demo";

    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private CommentNotificationOrchestrator? NotificationOrchestrator { get; set; }

    // ── Cascaded ─────────────────────────────────────────────────────────────

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter] public string PageId   { get; set; } = string.Empty;
    [Parameter] public bool   Expanded { get; set; }

    [Parameter] public EventCallback<bool> OnExpandedChanged { get; set; }

    [Parameter] public EventCallback OnCountChanged { get; set; }

    [Parameter] public EventCallback<string> OnMentionClicked { get; set; }

    /// <summary>
    /// Notifies the parent that the comment draft state changed. Fires ONLY when the
    /// <see cref="TmNotionCommentDraftState"/> value actually changes (value-equality dedup),
    /// never on every keystroke — the record carries flags only, and repeated identical states
    /// are suppressed so parents are not re-rendered while the user types. Members:
    /// <see cref="TmNotionCommentDraftState.HasNewCommentDraft"/>,
    /// <see cref="TmNotionCommentDraftState.HasThreadReplyDraft"/>,
    /// <see cref="TmNotionCommentDraftState.HasInlineReplyDraft"/>,
    /// <see cref="TmNotionCommentDraftState.EditingEntryId"/> and the aggregate
    /// <see cref="TmNotionCommentDraftState.HasUnsavedContent"/>.
    /// <para>
    /// None of the panel's composer texts is observable through a parameter — this callback is
    /// the only push channel; pair it with <see cref="HasUnsavedDraft"/> for polling and
    /// <see cref="DiscardDraftAsync"/> for the actual discard after the consumer's own
    /// confirmation.
    /// </para>
    /// <para>
    /// Semantics that mirror <c>TmActivityComments</c>: opening an entry edit reports the editing
    /// id immediately but <see cref="TmNotionCommentDraftState.HasUnsavedContent"/> stays
    /// <see langword="false"/> until the edit text differs from the entry body as it was when the
    /// edit started; the inline reply's auto-inserted quote is likewise not user content and does
    /// not raise the flag until the user types beyond (or changes) it.
    /// </para>
    /// </summary>
    [Parameter] public EventCallback<TmNotionCommentDraftState> OnDraftChanged { get; set; }

    // ── State ─────────────────────────────────────────────────────────────────

    private IReadOnlyList<TmCommentThread> _comments      = [];
    private int                          _unresolvedCount;
    private bool                         _loading;
    private bool                         _submitting;
    private string                       _error           = string.Empty;

    private string _newCommentText    = string.Empty;
    private string _replyText         = string.Empty;
    private string? _replyingToCommentId;

    private string? _editingEntryId;
    private string _editText = string.Empty;
    private bool   _showDeleteConfirm;
    private TmCommentThread?     _pendingDeleteComment;
    private TmCommentEntry?      _pendingDeleteEntry;

    private string? _replyingToEntryId;
    private string _inlineReplyText = string.Empty;
    private TmCommentThread? _inlineReplyComment;

    /// <summary>Body of the entry as it was when its edit started — the differs-from-original baseline for the edit slot.</summary>
    private string _editOriginalText = string.Empty;

    /// <summary>Quote auto-inserted when the inline reply opened — content up to this baseline is not user input.</summary>
    private string _inlineReplyBaseline = string.Empty;

    /// <summary>Last draft state handed to the parent — used to dedup <see cref="OnDraftChanged"/> notifications.</summary>
    private TmNotionCommentDraftState _lastDraftState = new(false, false, false, null, false);

    private bool   _initialized;
    private ElementReference _panelRef;
    private DotNetObjectReference<TmNotionPageCommentPanel>? _dotNetRef;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async Task OnParametersSetAsync()
    {
        if (!_initialized && !string.IsNullOrEmpty(PageId))
        {
            _initialized = true;
            await LoadUnresolvedCountAsync();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !string.IsNullOrEmpty(PageId))
        {
            await LoadUnresolvedCountAsync();
            StateHasChanged();
        }
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            try { await JS.InvokeVoidAsync("tmNotionEditor.initMentionClickHandler", _panelRef, _dotNetRef); } catch { /* best-effort */ }
        }
    }

    [JSInvokable("OnMentionClicked")]
    public void HandleMentionClicked(string userId)
    {
        _ = OnMentionClicked.InvokeAsync(userId);
    }

    public void Dispose()
    {
        _dotNetRef?.Dispose();
        try { JS.InvokeVoidAsync("tmNotionEditor.destroyMentionClickHandler", _panelRef); } catch { }
    }

    // ── Draft guard ───────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the panel currently holds an unsaved comment draft in ANY composer — new comment,
    /// thread reply, inline reply (beyond its auto-inserted quote) or an entry edit whose text
    /// differs from the entry body as it was when the edit started. The SAME value carried by the
    /// <see cref="TmNotionCommentDraftState.HasUnsavedContent"/> flag that
    /// <see cref="OnDraftChanged"/> reports. Read live from the current composer state (not from
    /// the last notification), so consumers can poll it at any time, for example before a page
    /// navigation that would silently discard the draft.
    /// <para>
    /// Read it on the component's dispatcher (e.g. from an event handler or via
    /// <c>InvokeAsync</c>) when called from a background thread.
    /// </para>
    /// </summary>
    public bool HasUnsavedDraft => BuildDraftState().HasUnsavedContent;

    /// <summary>
    /// Closes the open composers (thread reply, inline reply, entry edit) and clears every draft
    /// text, including the new-comment input. Intended for the consumer AFTER the user confirmed
    /// the discard in the consumer's OWN confirmation dialog — the component never shows a
    /// confirmation itself; that is deliberately the consumer's responsibility. Typical flow:
    /// <see cref="HasUnsavedDraft"/> reports a draft, the consumer asks, and on confirmation calls
    /// this method so the next navigation does not ask again.
    /// <para>
    /// When no draft exists the call is a no-op — the state does not change and the
    /// value-equality dedup suppresses any <see cref="OnDraftChanged"/> notification.
    /// Safe to call from outside the render cycle; the reset, re-render and notification are
    /// dispatched through <c>InvokeAsync</c>.
    /// </para>
    /// </summary>
    public Task DiscardDraftAsync()
        => InvokeAsync(async () =>
        {
            _newCommentText       = string.Empty;
            _replyingToCommentId  = null;
            _replyText            = string.Empty;
            _replyingToEntryId    = null;
            _inlineReplyComment   = null;
            _inlineReplyText      = string.Empty;
            _inlineReplyBaseline  = string.Empty;
            _editingEntryId       = null;
            _editText             = string.Empty;
            _editOriginalText     = string.Empty;
            StateHasChanged();
            await NotifyDraftChangedAsync();
        });

    /// <summary>Plain-text empty check for the draft flags (the panel's inputs are plain text, not HTML).</summary>
    private static bool IsEffectivelyEmpty(string? text) => string.IsNullOrWhiteSpace(text);

    /// <summary>
    /// Builds the current draft snapshot. The new-comment and inline-reply slots report
    /// not-whitespace-only text (the inline reply only beyond the auto-inserted quote); the
    /// thread-reply slot counts only while the reply is open (<c>_replyingToCommentId</c> set),
    /// so a closed reply can never report a draft; the edit slot reports the editing id as soon
    /// as the edit is open, while <see cref="TmNotionCommentDraftState.HasUnsavedContent"/> counts
    /// it only once the text differs from the original entry body (see the remarks on
    /// <see cref="OnDraftChanged"/>).
    /// </summary>
    private TmNotionCommentDraftState BuildDraftState()
    {
        var hasNewCommentDraft  = IsEffectivelyEmpty(_newCommentText) is false;
        var hasThreadReplyDraft = _replyingToCommentId is not null && IsEffectivelyEmpty(_replyText) is false;
        var hasInlineReplyDraft = IsEffectivelyEmpty(_inlineReplyText) is false
            && !string.Equals(_inlineReplyText, _inlineReplyBaseline, StringComparison.Ordinal);
        var hasEditDraft        = _editingEntryId is not null
            && !string.Equals(_editText, _editOriginalText, StringComparison.Ordinal);

        return new TmNotionCommentDraftState(
            hasNewCommentDraft,
            hasThreadReplyDraft,
            hasInlineReplyDraft,
            _editingEntryId,
            hasNewCommentDraft || hasThreadReplyDraft || hasInlineReplyDraft || hasEditDraft);
    }

    /// <summary>
    /// Notifies <see cref="OnDraftChanged"/> only when the draft state actually changed. The dedup
    /// is mandatory: all four composers report content changes into this method (via the editors'
    /// bind after-hook, i.e. per keystroke in real usage), and without the value-equality check
    /// each keystroke would invoke the callback and re-render the parent; the record's value
    /// equality makes the comparison cheap and exact.
    /// </summary>
    private async Task NotifyDraftChangedAsync()
    {
        var state = BuildDraftState();
        if (state == _lastDraftState)
        {
            return;
        }

        _lastDraftState = state;
        await OnDraftChanged.InvokeAsync(state);
    }

    // ── Data ──────────────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        if (Context.CommentProvider is null || string.IsNullOrEmpty(PageId)) return;

        _loading = true;
        _error   = string.Empty;
        StateHasChanged();

        try
        {
            var list = await Context.CommentProvider.GetPageCommentsAsync(PageId);
            _comments        = list.ToList();
            _unresolvedCount = _comments.Count(c => !c.IsResolved());
        }
        catch
        {
            _error = Loc["TmNotionPageComment_LoadError"];
        }
        finally
        {
            _loading = false;
            StateHasChanged();
            await OnCountChanged.InvokeAsync();
        }
    }

    private async Task LoadUnresolvedCountAsync()
    {
        if (Context.CommentProvider is null || string.IsNullOrEmpty(PageId)) return;
        try
        {
            _unresolvedCount = await Context.CommentProvider.GetUnresolvedCommentsCountAsync(PageId);
        }
        catch { /* best-effort */ }
    }

    // ── Expand ────────────────────────────────────────────────────────────────

    private async Task ToggleExpandedAsync()
    {
        var next = !Expanded;
        await OnExpandedChanged.InvokeAsync(next);

        if (next && _comments.Count == 0)
            await LoadAsync();
    }

    // ── New comment ───────────────────────────────────────────────────────────

    private async Task SendNewCommentAsync()
    {
        if (Context.CommentProvider is null || string.IsNullOrWhiteSpace(_newCommentText) || _submitting)
            return;

        _submitting = true;
        _error      = string.Empty;
        StateHasChanged();

        try
        {
            var rawText = _newCommentText.Trim();
            var encoded = await CommentMentionHelper.EncodeAsync(rawText, Context.MentionProvider);

            await Context.CommentProvider.AddPageCommentAsync(PageId, encoded);

            // Re-load to get the created entry for notification
            await LoadAsync();
            var comment = _comments.LastOrDefault(c => c.EntityRef.EntityId == PageId);
            var entry = comment?.Entries.LastOrDefault();
            if (entry is not null && comment is not null)
                await CommentMentionHelper.NotifyAsync(rawText, entry, comment.Id, PageId, Context.MentionProvider, NotificationOrchestrator);

            _newCommentText  = string.Empty;
            await NotifyDraftChangedAsync();
        }
        catch
        {
            _error = Loc["TmNotionPageComment_SendError"];
        }
        finally
        {
            _submitting = false;
        }
    }

    private async Task HandleNewKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && (e.CtrlKey || e.MetaKey))
            await SendNewCommentAsync();
    }

    // ── Reply to thread ───────────────────────────────────────────────────────

    private async Task StartReply(TmCommentThread comment)
    {
        _replyingToCommentId = comment.Id;
        _replyText           = string.Empty;
        await NotifyDraftChangedAsync();
    }

    private async Task CancelReply()
    {
        _replyingToCommentId = null;
        _replyText           = string.Empty;
        await NotifyDraftChangedAsync();
    }

    private async Task SendReplyAsync()
    {
        if (Context.CommentProvider is null || _replyingToCommentId is null ||
            string.IsNullOrWhiteSpace(_replyText) || _submitting)
            return;

        _submitting = true;
        _error      = string.Empty;
        StateHasChanged();

        try
        {
            var rawText = _replyText.Trim();
            var encoded = await CommentMentionHelper.EncodeAsync(rawText, Context.MentionProvider);

            await Context.CommentProvider.ReplyToCommentAsync(_replyingToCommentId, encoded);

            var comment = _comments.FirstOrDefault(c => c.Id == _replyingToCommentId);
            var entry = comment?.Entries.LastOrDefault();
            if (entry is not null && comment is not null)
                await CommentMentionHelper.NotifyAsync(rawText, entry, comment.Id, PageId, Context.MentionProvider, NotificationOrchestrator);

            _replyingToCommentId = null;
            _replyText           = string.Empty;
            await LoadAsync();
            await NotifyDraftChangedAsync();
        }
        catch
        {
            _error = Loc["TmNotionPageComment_SendError"];
        }
        finally
        {
            _submitting = false;
        }
    }

    private async Task HandleReplyKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && (e.CtrlKey || e.MetaKey))
            await SendReplyAsync();
        else if (e.Key == "Escape")
            await CancelInlineReply();
    }

    // The edit wrap stops keydown propagation, so the edit input needs its own
    // Escape->cancel (it previously relied on the keydown bubbling up to the
    // entry handler). Plain Enter stays unhandled → native newline.
    private async Task HandleEditKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            await CancelEdit();
    }

    // ── Inline reply to entry ─────────────────────────────────────────────────

    private async Task HandleEntryKeyDownAsync(KeyboardEventArgs e, TmCommentEntry entry, TmCommentThread comment)
    {
        if (e.Key == "Enter" && _replyingToEntryId != entry.Id)
        {
            await StartReplyToEntry(entry, comment);
        }
        else if (e.Key == "Escape" && _replyingToEntryId == entry.Id)
        {
            await CancelInlineReply();
        }
        else if (e.Key == "Escape" && _editingEntryId == entry.Id)
        {
            await CancelEdit();
        }
    }

    private async Task StartReplyToEntry(TmCommentEntry entry, TmCommentThread comment)
    {
        _replyingToEntryId  = entry.Id;
        _inlineReplyComment = comment;
        _inlineReplyText    = QuoteReply(entry);
        // The auto-quote is a baseline, not user content: the draft flag rises only once the
        // reply text moves beyond it.
        _inlineReplyBaseline = _inlineReplyText;
        StateHasChanged();
        await NotifyDraftChangedAsync();
    }

    private async Task CancelInlineReply()
    {
        _replyingToEntryId   = null;
        _inlineReplyComment  = null;
        _inlineReplyText     = string.Empty;
        _inlineReplyBaseline = string.Empty;
        await NotifyDraftChangedAsync();
    }

    private async Task SendInlineReplyAsync()
    {
        if (Context.CommentProvider is null || _replyingToEntryId is null ||
            _inlineReplyComment is null || string.IsNullOrWhiteSpace(_inlineReplyText) || _submitting)
            return;

        _submitting = true;
        _error      = string.Empty;
        StateHasChanged();

        try
        {
            var rawText = _inlineReplyText.Trim();
            var encoded = await CommentMentionHelper.EncodeAsync(rawText, Context.MentionProvider);

            await Context.CommentProvider.ReplyToCommentAsync(
                _inlineReplyComment.Id,
                encoded,
                _replyingToEntryId);

            var entry = _inlineReplyComment.Entries.LastOrDefault();
            if (entry is not null)
                await CommentMentionHelper.NotifyAsync(rawText, entry, _inlineReplyComment.Id, PageId, Context.MentionProvider, NotificationOrchestrator);

            _replyingToEntryId   = null;
            _inlineReplyComment  = null;
            _inlineReplyText     = string.Empty;
            _inlineReplyBaseline = string.Empty;
            await LoadAsync();
            await NotifyDraftChangedAsync();
        }
        catch
        {
            _error = Loc["TmNotionPageComment_SendError"];
        }
        finally
        {
            _submitting = false;
        }
    }

    private async Task HandleInlineReplyKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && (e.CtrlKey || e.MetaKey))
            await SendInlineReplyAsync();
        else if (e.Key == "Escape")
            await CancelInlineReply();
    }

    // ── Mark all as read ──────────────────────────────────────────────────────

    private async Task MarkAllAsReadAsync()
    {
        if (Context.CommentProvider is null || string.IsNullOrEmpty(PageId)) return;
        _error = string.Empty;
        try
        {
            await Context.CommentProvider.MarkAllPageThreadsAsReadAsync(PageId, "demo");
            await LoadAsync();
        }
        catch
        {
            _error = Loc["TmNotionPageComment_ActionError"];
        }
    }

    // ── Resolve / Unresolve ───────────────────────────────────────────────────

    private async Task ResolveAsync(TmCommentThread comment)
    {
        if (Context.CommentProvider is null) return;
        _error = string.Empty;
        try
        {
            await Context.CommentProvider.ResolveCommentAsync(comment.Id);
            await LoadAsync();
        }
        catch
        {
            _error = Loc["TmNotionPageComment_ActionError"];
        }
    }

    private async Task UnresolveAsync(TmCommentThread comment)
    {
        if (Context.CommentProvider is null) return;
        _error = string.Empty;
        try
        {
            await Context.CommentProvider.UnresolveCommentAsync(comment.Id);
            await LoadAsync();
        }
        catch
        {
            _error = Loc["TmNotionPageComment_ActionError"];
        }
    }

    // ── Edit ──────────────────────────────────────────────────────────────────

    private async Task StartEdit(TmCommentEntry entry)
    {
        _editingEntryId   = entry.Id;
        _editText         = StripHtml(entry.HtmlContent());
        _editOriginalText = _editText;
        StateHasChanged();
        // The edit state itself is a transition the consumer must see (editing id set), even
        // though HasUnsavedContent is still false until the text changes.
        await NotifyDraftChangedAsync();
    }

    private async Task CancelEdit()
    {
        _editingEntryId   = null;
        _editText         = string.Empty;
        _editOriginalText = string.Empty;
        await NotifyDraftChangedAsync();
    }

    private async Task SaveEditAsync()
    {
        if (Context.CommentProvider is null || _editingEntryId is null || string.IsNullOrWhiteSpace(_editText))
            return;

        _error = string.Empty;
        try
        {
            var rawText = _editText.Trim();
            var encoded = await CommentMentionHelper.EncodeAsync(rawText, Context.MentionProvider);
            var thread = _comments.FirstOrDefault(comment => comment.Entries.Any(entry => entry.Id == _editingEntryId));
            if (thread is null)
                return;

            await Context.CommentProvider.EditCommentAsync(thread.Id, _editingEntryId, encoded);
            _editingEntryId   = null;
            _editText         = string.Empty;
            _editOriginalText = string.Empty;
            await LoadAsync();
            await NotifyDraftChangedAsync();
        }
        catch
        {
            _error = Loc["TmNotionPageComment_ActionError"];
        }
    }

    private void DeleteEntryAsync(TmCommentThread comment, TmCommentEntry entry)
    {
        if (Context.CommentProvider is null) return;
        _pendingDeleteComment = comment;
        _pendingDeleteEntry   = entry;
        _showDeleteConfirm    = true;
        StateHasChanged();
    }

    private async Task HandleDeleteConfirmResult(bool? result)
    {
        _showDeleteConfirm = false;
        if (result != true || _pendingDeleteComment is null)
        {
            _pendingDeleteComment = null;
            _pendingDeleteEntry   = null;
            return;
        }

        _error = string.Empty;
        try
        {
            var isOnlyEntry = _pendingDeleteComment.Entries.Count <= 1;
            if (isOnlyEntry)
            {
                await Context.CommentProvider.DeleteCommentAsync(_pendingDeleteComment.Id);
            }
            else
            {
                await Context.CommentProvider.DeleteCommentEntryAsync(_pendingDeleteComment.Id, _pendingDeleteEntry?.Id!);
            }
            await LoadAsync();
        }
        catch
        {
            _error = Loc["TmNotionPageComment_ActionError"];
        }
        finally
        {
            _pendingDeleteComment = null;
            _pendingDeleteEntry   = null;
        }
    }

    // ── Subscribe / Unsubscribe ───────────────────────────────────────────────

    private static bool IsSubscribed(TmCommentThread comment)
        => comment.SubscribedUserIds.Contains(CurrentUserId);

    private async Task SubscribeAsync(TmCommentThread comment)
    {
        if (Context.CommentProvider is null) return;
        _error = string.Empty;
        try
        {
            await Context.CommentProvider.SubscribeToThreadAsync(comment.Id, CurrentUserId);
            if (!comment.SubscribedUserIds.Contains(CurrentUserId))
                comment.SubscribedUserIds.Add(CurrentUserId);
        }
        catch
        {
            _error = Loc["TmNotionPageComment_ActionError"];
        }
    }

    private async Task UnsubscribeAsync(TmCommentThread comment)
    {
        if (Context.CommentProvider is null) return;
        _error = string.Empty;
        try
        {
            await Context.CommentProvider.UnsubscribeFromThreadAsync(comment.Id, CurrentUserId);
            comment.SubscribedUserIds.Remove(CurrentUserId);
        }
        catch
        {
            _error = Loc["TmNotionPageComment_ActionError"];
        }
    }

    private static IEnumerable<CommentThreadNode> FlattenTree(List<CommentThreadNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in FlattenTree(node.Children))
                yield return child;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string AvatarInitial(string name)
        => name.Length > 0 ? name[0].ToString().ToUpperInvariant() : "?";

    private static string FormatTime(DateTimeOffset dt)
    {
        var diff = DateTimeOffset.UtcNow - dt.ToUniversalTime();
        if (diff.TotalMinutes < 1)  return "just now";
        if (diff.TotalHours   < 1)  return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalDays    < 1)  return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays    < 7)  return $"{(int)diff.TotalDays}d ago";
        return dt.ToString("MMM d, yyyy");
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", string.Empty);
    }

    private static string QuoteReply(TmCommentEntry entry)
    {
        var text = StripHtml(entry.HtmlContent()).Trim();
        if (text.Length > 120) text = text[..120] + "…";
        return $"> {entry.AuthorDisplayName()}: \"{text}\"\n\n";
    }
}
