using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Models;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Helpers;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionBlockCommentPanel : ComponentBase, IDisposable, ITmDraftGuard
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private CommentNotificationOrchestrator? NotificationOrchestrator { get; set; }

    // ── Cascaded ─────────────────────────────────────────────────────────────

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the panel is visible. Set it to <see langword="false"/> ONLY after checking
    /// <see cref="HasUnsavedDraft"/> — the panel clears every draft slot when it hides, so a
    /// host that hides it without asking first silently discards the in-progress comment.
    /// </summary>
    [Parameter] public bool   Visible  { get; set; }
    [Parameter] public string BlockId  { get; set; } = string.Empty;
    [Parameter] public double Top      { get; set; }
    [Parameter] public double Left     { get; set; }
    [Parameter] public bool   StartInNewThreadMode { get; set; }

    [Parameter] public EventCallback          OnClosed        { get; set; }
    [Parameter] public EventCallback<int>     OnCountChanged  { get; set; }
    [Parameter] public EventCallback<string>  OnMentionClicked { get; set; }

    /// <summary>
    /// Notifies the parent that the panel's comment draft state changed. All of the panel's
    /// composers — new thread / thread reply, inline reply and the entry edit — hold text the
    /// host cannot observe through a parameter; this callback (together with
    /// <see cref="HasUnsavedDraft"/> and <see cref="DiscardDraftAsync"/>) is the draft-guard
    /// surface. Fires ONLY when the <see cref="TmNotionCommentDraftState"/> value actually changes
    /// (value-equality dedup), never on every keystroke.
    /// <see cref="TmNotionCommentDraftState.HasNewCommentDraft"/> covers the composer in
    /// new-thread mode, <see cref="TmNotionCommentDraftState.HasThreadReplyDraft"/> the same
    /// composer while replying to a thread,
    /// <see cref="TmNotionCommentDraftState.HasInlineReplyDraft"/> the inline reply beyond its
    /// auto-inserted quote, and <see cref="TmNotionCommentDraftState.EditingEntryId"/> the entry
    /// under edit (the aggregate <see cref="TmNotionCommentDraftState.HasUnsavedContent"/> counts
    /// an edit only once its text differs from the entry body as it was when the edit started).
    /// </summary>
    [Parameter] public EventCallback<TmNotionCommentDraftState> OnDraftChanged { get; set; }

    // ── State ─────────────────────────────────────────────────────────────────

    private List<TmCommentThread> _comments = new();
    private TmCommentThread? _selectedComment;
    private bool _isCreatingNewThread;

    private List<CommentThreadNode> _threadTree = new();
    private bool           _loading;
    private bool           _submitting;
    private string         _replyText  = string.Empty;
    private string         _error      = string.Empty;
    private double         _top;
    private double         _left;
    private bool           _wasVisible;

    private string?        _editingEntryId;
    private string         _editText = string.Empty;
    private bool           _showDeleteConfirm;
    private TmCommentEntry? _pendingDeleteEntry;
    private bool           _isReadByCurrentUser;
    private bool           _isSubscribed;

    private string?        _replyingToEntryId;
    private string         _inlineReplyText = string.Empty;

    /// <summary>Body of the entry as it was when its edit started — the differs-from-original baseline for the edit slot.</summary>
    private string _editOriginalText = string.Empty;

    /// <summary>Quote auto-inserted when the inline reply opened — content up to this baseline is not user input.</summary>
    private string _inlineReplyBaseline = string.Empty;

    /// <summary>Last draft state handed to the parent — used to dedup <see cref="OnDraftChanged"/> notifications.</summary>
    private TmNotionCommentDraftState _lastDraftState = new(false, false, false, null, false);

    private ElementReference _editRef;
    private ElementReference _panelRef;
    private DotNetObjectReference<TmNotionBlockCommentPanel>? _dotNetRef;

    private const string CurrentUserId = "demo";

    // ── Derived ──────────────────────────────────────────────────────────────

    private bool IsThreadList => !_isCreatingNewThread && _selectedComment is null && _comments.Count > 1;
    private bool IsThreadDetail => !_isCreatingNewThread && _selectedComment is not null;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async Task OnParametersSetAsync()
    {
        if (Visible && !_wasVisible)
        {
            _top        = Top;
            _left       = Left;
            _replyText  = string.Empty;
            _error      = string.Empty;
            _editingEntryId = null;
            _replyingToEntryId = null;
            _inlineReplyText = string.Empty;
            _inlineReplyBaseline = string.Empty;
            _isCreatingNewThread = StartInNewThreadMode;
            await LoadAsync();
            await NotifyDraftChangedAsync();
        }

        if (!Visible && _wasVisible)
        {
            // Hiding is a hard close for the drafts: every composer unmounts, so the slots are
            // cleared and the cleared state is REPORTED (hosts check HasUnsavedDraft before
            // hiding — a host that hid anyway must not keep a stale draft flag).
            _comments = new();
            _selectedComment = null;
            _isCreatingNewThread = false;
            _replyText = string.Empty;
            _editingEntryId = null;
            _editText = string.Empty;
            _editOriginalText = string.Empty;
            _replyingToEntryId = null;
            _inlineReplyText = string.Empty;
            _inlineReplyBaseline = string.Empty;
            await NotifyDraftChangedAsync();
        }

        _wasVisible = Visible;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
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
    /// Whether the panel currently holds an unsaved comment draft in ANY composer — new thread,
    /// thread reply, inline reply (beyond its auto-inserted quote) or an entry edit whose text
    /// differs from the entry body as it was when the edit started. The SAME value carried by
    /// <see cref="TmNotionCommentDraftState.HasUnsavedContent"/> that
    /// <see cref="OnDraftChanged"/> reports; read live from the composer state, so it can be
    /// polled at any time (for example before closing the panel or navigating away).
    /// <para>
    /// Read it on the component's dispatcher (e.g. from an event handler or via
    /// <c>InvokeAsync</c>) when called from a background thread.
    /// </para>
    /// </summary>
    public bool HasUnsavedDraft => BuildDraftState().HasUnsavedContent;

    /// <summary>
    /// Discards every draft in the panel: clears the new-thread/reply composer, closes the inline
    /// reply and the entry edit (their texts are cleared with them). Intended for the consumer
    /// AFTER the user confirmed the discard in the consumer's OWN confirmation dialog — the
    /// component never shows a confirmation itself; that is deliberately the consumer's
    /// responsibility.
    /// <para>
    /// When no draft exists the call is a no-op — the state does not change and the
    /// value-equality dedup suppresses any <see cref="OnDraftChanged"/> notification. Safe to
    /// call from outside the render cycle; the reset, re-render and notification are dispatched
    /// through <c>InvokeAsync</c>.
    /// </para>
    /// </summary>
    public Task DiscardDraftAsync()
        => InvokeAsync(async () =>
        {
            _replyText           = string.Empty;
            _replyingToEntryId   = null;
            _inlineReplyText     = string.Empty;
            _inlineReplyBaseline = string.Empty;
            _editingEntryId      = null;
            _editText            = string.Empty;
            _editOriginalText    = string.Empty;
            StateHasChanged();
            await NotifyDraftChangedAsync();
        });

    /// <summary>
    /// Builds the panel's draft snapshot. The bottom composer doubles as new-thread and
    /// thread-reply input, so it is reported through the matching slot; the inline reply only
    /// counts once its text moved beyond the auto-inserted quote; the edit slot reports the
    /// editing id while <see cref="TmNotionCommentDraftState.HasUnsavedContent"/> counts it only
    /// once the text differs from the original entry body.
    /// </summary>
    private TmNotionCommentDraftState BuildDraftState()
    {
        var hasReplyText        = !string.IsNullOrWhiteSpace(_replyText);
        var hasNewCommentDraft  = _isCreatingNewThread && hasReplyText;
        var hasThreadReplyDraft = !_isCreatingNewThread && _selectedComment is not null && hasReplyText;
        var hasInlineReplyDraft = !string.IsNullOrWhiteSpace(_inlineReplyText)
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
    /// Notifies <see cref="OnDraftChanged"/> only when the draft state actually changed — the
    /// composers report every keystroke into this path and the value-equality check keeps the
    /// parent from re-rendering without a real transition.
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
        if (Context.CommentProvider is null || string.IsNullOrEmpty(BlockId))
            return;

        _loading = true;
        _error   = string.Empty;
        StateHasChanged();

        try
        {
            _comments = (await Context.CommentProvider.GetBlockCommentsAsync(BlockId)).ToList();

            // Mark all threads as read for current user
            foreach (var c in _comments)
                await Context.CommentProvider.MarkThreadAsReadAsync(c.Id, CurrentUserId);

            if (_isCreatingNewThread)
            {
                _selectedComment = null;
            }
            else if (_comments.Count == 0)
            {
                _isCreatingNewThread = true;
                _selectedComment = null;
            }
            else if (_comments.Count == 1)
            {
                _selectedComment = _comments[0];
                await LoadSelectedCommentAsync();
            }
            else
            {
                _selectedComment = null;
            }
        }
        catch
        {
            _error = Loc["TmNotionBlockComment_LoadError"];
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private async Task LoadSelectedCommentAsync()
    {
        if (_selectedComment is null) return;
        _isReadByCurrentUser = _selectedComment.ReadByUserIds.Contains(CurrentUserId);
        _isSubscribed = _selectedComment.SubscribedUserIds.Contains(CurrentUserId);
            _threadTree = CommentThreadHelper.BuildTree(_selectedComment.Entries);
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private async Task SelectThreadAsync(TmCommentThread comment)
    {
        _selectedComment = comment;
        _isCreatingNewThread = false;
        _replyingToEntryId = null;
        _inlineReplyText = string.Empty;
        _inlineReplyBaseline = string.Empty;
        _editingEntryId = null;
        await LoadSelectedCommentAsync();
        StateHasChanged();
        await NotifyDraftChangedAsync();
    }

    private async Task BackToThreadList()
    {
        _selectedComment = null;
        _isCreatingNewThread = false;
        _replyingToEntryId = null;
        _inlineReplyText = string.Empty;
        _inlineReplyBaseline = string.Empty;
        _editingEntryId = null;
        StateHasChanged();
        await NotifyDraftChangedAsync();
    }

    private async Task StartNewThread()
    {
        _isCreatingNewThread = true;
        _selectedComment = null;
        _replyingToEntryId = null;
        _inlineReplyText = string.Empty;
        _inlineReplyBaseline = string.Empty;
        _editingEntryId = null;
        _replyText = string.Empty;
        StateHasChanged();
        await NotifyDraftChangedAsync();
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private async Task SendReplyAsync()
    {
        if (Context.CommentProvider is null || string.IsNullOrWhiteSpace(_replyText) || _submitting)
            return;

        _submitting = true;
        _error      = string.Empty;
        StateHasChanged();

        try
        {
            var rawText = _replyText.Trim();
            var encoded = await CommentMentionHelper.EncodeAsync(rawText, Context.MentionProvider);

            if (_isCreatingNewThread || _selectedComment is null)
            {
                _selectedComment = await Context.CommentProvider.AddBlockCommentAsync(BlockId, encoded);
                _isCreatingNewThread = false;
                _comments.Add(_selectedComment);
            }
            else
            {
                var reply = await Context.CommentProvider.ReplyToCommentAsync(_selectedComment.Id, encoded);
                _selectedComment.Entries.Add(reply);
            }

            var entry = _selectedComment?.Entries.LastOrDefault();
            if (entry is not null)
                await CommentMentionHelper.NotifyAsync(rawText, entry, _selectedComment!.Id, BlockId, Context.MentionProvider, NotificationOrchestrator);

            _replyText = string.Empty;
            await LoadSelectedCommentAsync();
            await OnCountChanged.InvokeAsync(_comments.Count);
            await NotifyDraftChangedAsync();
        }
        catch
        {
            _error = Loc["TmNotionBlockComment_SendError"];
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

    private async Task SendInlineReplyAsync()
    {
        if (Context.CommentProvider is null || _replyingToEntryId is null ||
            string.IsNullOrWhiteSpace(_inlineReplyText) || _submitting || _selectedComment is null)
            return;

        _submitting = true;
        _error      = string.Empty;
        StateHasChanged();

        try
        {
            var rawText = _inlineReplyText.Trim();
            var encoded = await CommentMentionHelper.EncodeAsync(rawText, Context.MentionProvider);

            var reply = await Context.CommentProvider.ReplyToCommentAsync(
                _selectedComment.Id,
                encoded,
                _replyingToEntryId);
            _selectedComment.Entries.Add(reply);

            var entry = _selectedComment.Entries.LastOrDefault();
            if (entry is not null)
                await CommentMentionHelper.NotifyAsync(rawText, entry, _selectedComment.Id, BlockId, Context.MentionProvider, NotificationOrchestrator);

            _replyingToEntryId = null;
            _inlineReplyText   = string.Empty;
            _inlineReplyBaseline = string.Empty;
            await LoadSelectedCommentAsync();
            await OnCountChanged.InvokeAsync(_comments.Count);
            await NotifyDraftChangedAsync();
        }
        catch
        {
            _error = Loc["TmNotionBlockComment_SendError"];
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

    // The edit wrap stops keydown propagation, so the edit input needs its own
    // Escape->cancel (it previously relied on the keydown bubbling up to the
    // entry handler). Plain Enter stays unhandled → native newline.
    private async Task HandleEditKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            await CancelEdit();
    }

    private async Task HandleEntryKeyDownAsync(KeyboardEventArgs e, TmCommentEntry entry)
    {
        if (e.Key == "Enter" && _replyingToEntryId != entry.Id)
        {
            await StartReplyToEntry(entry);
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

    private async Task StartReplyToEntry(TmCommentEntry entry)
    {
        _replyingToEntryId = entry.Id;
        _inlineReplyText   = QuoteReply(entry);
        // The auto-quote is a baseline, not user content: the draft flag rises only once the
        // reply text moves beyond it.
        _inlineReplyBaseline = _inlineReplyText;
        StateHasChanged();
        await NotifyDraftChangedAsync();
    }

    private async Task CancelInlineReply()
    {
        _replyingToEntryId = null;
        _inlineReplyText   = string.Empty;
        _inlineReplyBaseline = string.Empty;
        await NotifyDraftChangedAsync();
    }

    private async Task ResolveAsync()
    {
        if (Context.CommentProvider is null || _selectedComment is null) return;
        _error = string.Empty;
        try
        {
            _selectedComment = await Context.CommentProvider.ResolveCommentAsync(_selectedComment.Id);
            await OnCountChanged.InvokeAsync(_comments.Count);
        }
        catch
        {
            _error = Loc["TmNotionBlockComment_ActionError"];
        }
    }

    private async Task UnresolveAsync()
    {
        if (Context.CommentProvider is null || _selectedComment is null) return;
        _error = string.Empty;
        try
        {
            _selectedComment = await Context.CommentProvider.UnresolveCommentAsync(_selectedComment.Id);
            _isReadByCurrentUser = false;
            await OnCountChanged.InvokeAsync(_comments.Count);
        }
        catch
        {
            _error = Loc["TmNotionBlockComment_ActionError"];
        }
    }

    private async Task ResolveThreadAsync(TmCommentThread comment)
    {
        if (Context.CommentProvider is null) return;
        _error = string.Empty;
        try
        {
            var updated = await Context.CommentProvider.ResolveCommentAsync(comment.Id);
            var idx = _comments.FindIndex(c => c.Id == comment.Id);
            if (idx >= 0)
                _comments[idx] = updated;
            await OnCountChanged.InvokeAsync(_comments.Count);
        }
        catch
        {
            _error = Loc["TmNotionBlockComment_ActionError"];
        }
    }

    private async Task UnresolveThreadAsync(TmCommentThread comment)
    {
        if (Context.CommentProvider is null) return;
        _error = string.Empty;
        try
        {
            var updated = await Context.CommentProvider.UnresolveCommentAsync(comment.Id);
            var idx = _comments.FindIndex(c => c.Id == comment.Id);
            if (idx >= 0)
                _comments[idx] = updated;
            await OnCountChanged.InvokeAsync(_comments.Count);
        }
        catch
        {
            _error = Loc["TmNotionBlockComment_ActionError"];
        }
    }

    private async Task MarkAllAsReadAsync()
    {
        if (Context.CommentProvider is null || string.IsNullOrEmpty(BlockId)) return;
        _error = string.Empty;
        try
        {
            await Context.CommentProvider.MarkAllBlockThreadsAsReadAsync(BlockId, CurrentUserId);
            _isReadByCurrentUser = true;
            await OnCountChanged.InvokeAsync(0);
        }
        catch
        {
            _error = Loc["TmNotionBlockComment_ActionError"];
        }
    }

    private async Task StartEdit(TmCommentEntry entry)
    {
        _editingEntryId = entry.Id;
        _editText       = StripHtml(entry.HtmlContent());
        _editOriginalText = _editText;
        StateHasChanged();
        // The edit state itself is a transition the consumer must see (editing id set), even
        // though HasUnsavedContent is still false until the text changes.
        await NotifyDraftChangedAsync();
    }

    private async Task CancelEdit()
    {
        _editingEntryId = null;
        _editText       = string.Empty;
        _editOriginalText = string.Empty;
        await NotifyDraftChangedAsync();
    }

    private async Task SaveEditAsync()
    {
        if (Context.CommentProvider is null || _editingEntryId is null || _selectedComment is null || string.IsNullOrWhiteSpace(_editText))
            return;

        _error = string.Empty;
        try
        {
            var rawText = _editText.Trim();
            var encoded = await CommentMentionHelper.EncodeAsync(rawText, Context.MentionProvider);
            var updated = await Context.CommentProvider.EditCommentAsync(_selectedComment.Id, _editingEntryId, encoded);
            var index = _selectedComment.Entries.FindIndex(entry => entry.Id == updated.Id);
            if (index >= 0)
                _selectedComment.Entries[index] = updated;
            _editingEntryId = null;
            _editText       = string.Empty;
            _editOriginalText = string.Empty;
            await LoadSelectedCommentAsync();
            await NotifyDraftChangedAsync();
        }
        catch
        {
            _error = Loc["TmNotionBlockComment_ActionError"];
        }
    }

    private void DeleteEntryAsync(TmCommentEntry entry)
    {
        if (Context.CommentProvider is null || _selectedComment is null) return;
        _pendingDeleteEntry = entry;
        _showDeleteConfirm  = true;
        StateHasChanged();
    }

    private async Task HandleDeleteConfirmResult(bool? result)
    {
        _showDeleteConfirm = false;
        if (result != true || _pendingDeleteEntry is null || _selectedComment is null)
        {
            _pendingDeleteEntry = null;
            return;
        }

        _error = string.Empty;
        try
        {
            var isOnlyEntry = _selectedComment.Entries.Count <= 1;
            if (isOnlyEntry)
            {
                await Context.CommentProvider.DeleteCommentAsync(_selectedComment.Id);
                _comments.Remove(_selectedComment);
                _selectedComment = null;
            }
            else
            {
                await Context.CommentProvider.DeleteCommentEntryAsync(_selectedComment.Id, _pendingDeleteEntry.Id);
            }

            if (_selectedComment is null)
            {
                // Deleted the whole thread — go back to list or new-thread
                if (_comments.Count == 0)
                {
                    _isCreatingNewThread = true;
                    // The detail view's composers unmount with the thread — report the cleared
                    // state instead of leaving the parent on a stale draft report.
                    await NotifyDraftChangedAsync();
                }
                else
                {
                    await BackToThreadList();
                }
            }
            else
            {
                await LoadSelectedCommentAsync();
            }

            await OnCountChanged.InvokeAsync(_comments.Count);
        }
        catch
        {
            _error = Loc["TmNotionBlockComment_ActionError"];
        }
        finally
        {
            _pendingDeleteEntry = null;
        }
    }

    private async Task SubscribeAsync()
    {
        if (Context.CommentProvider is null || _selectedComment is null) return;
        _error = string.Empty;
        try
        {
            await Context.CommentProvider.SubscribeToThreadAsync(_selectedComment.Id, CurrentUserId);
            _isSubscribed = true;
        }
        catch
        {
            _error = Loc["TmNotionBlockComment_ActionError"];
        }
    }

    private async Task UnsubscribeAsync()
    {
        if (Context.CommentProvider is null || _selectedComment is null) return;
        _error = string.Empty;
        try
        {
            await Context.CommentProvider.UnsubscribeFromThreadAsync(_selectedComment.Id, CurrentUserId);
            _isSubscribed = false;
        }
        catch
        {
            _error = Loc["TmNotionBlockComment_ActionError"];
        }
    }

    private async Task CloseAsync() => await OnClosed.InvokeAsync();

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
