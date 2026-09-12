using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Models;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Helpers;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionBlockCommentPanel : ComponentBase, IDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private CommentNotificationOrchestrator? NotificationOrchestrator { get; set; }

    // ── Cascaded ─────────────────────────────────────────────────────────────

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter] public bool   Visible  { get; set; }
    [Parameter] public string BlockId  { get; set; } = string.Empty;
    [Parameter] public double Top      { get; set; }
    [Parameter] public double Left     { get; set; }
    [Parameter] public bool   StartInNewThreadMode { get; set; }

    [Parameter] public EventCallback          OnClosed        { get; set; }
    [Parameter] public EventCallback<int>     OnCountChanged  { get; set; }
    [Parameter] public EventCallback<string>  OnMentionClicked { get; set; }

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
            _isCreatingNewThread = StartInNewThreadMode;
            await LoadAsync();
        }

        if (!Visible && _wasVisible)
        {
            _comments = new();
            _selectedComment = null;
            _isCreatingNewThread = false;
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
        _editingEntryId = null;
        await LoadSelectedCommentAsync();
        StateHasChanged();
    }

    private void BackToThreadList()
    {
        _selectedComment = null;
        _isCreatingNewThread = false;
        _replyingToEntryId = null;
        _inlineReplyText = string.Empty;
        _editingEntryId = null;
        StateHasChanged();
    }

    private void StartNewThread()
    {
        _isCreatingNewThread = true;
        _selectedComment = null;
        _replyingToEntryId = null;
        _inlineReplyText = string.Empty;
        _editingEntryId = null;
        _replyText = string.Empty;
        StateHasChanged();
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
            CancelInlineReply();
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
            await LoadSelectedCommentAsync();
            await OnCountChanged.InvokeAsync(_comments.Count);
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
            CancelInlineReply();
    }

    // The edit wrap stops keydown propagation, so the edit input needs its own
    // Escape->cancel (it previously relied on the keydown bubbling up to the
    // entry handler). Plain Enter stays unhandled → native newline.
    private void HandleEditKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            CancelEdit();
    }

    private async Task HandleEntryKeyDownAsync(KeyboardEventArgs e, TmCommentEntry entry)
    {
        if (e.Key == "Enter" && _replyingToEntryId != entry.Id)
        {
            StartReplyToEntry(entry);
        }
        else if (e.Key == "Escape" && _replyingToEntryId == entry.Id)
        {
            CancelInlineReply();
        }
        else if (e.Key == "Escape" && _editingEntryId == entry.Id)
        {
            CancelEdit();
        }
    }

    private void StartReplyToEntry(TmCommentEntry entry)
    {
        _replyingToEntryId = entry.Id;
        _inlineReplyText   = QuoteReply(entry);
        StateHasChanged();
    }

    private void CancelInlineReply()
    {
        _replyingToEntryId = null;
        _inlineReplyText   = string.Empty;
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

    private void StartEdit(TmCommentEntry entry)
    {
        _editingEntryId = entry.Id;
        _editText       = StripHtml(entry.HtmlContent());
        StateHasChanged();
    }

    private void CancelEdit()
    {
        _editingEntryId = null;
        _editText       = string.Empty;
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
            await LoadSelectedCommentAsync();
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
                    _isCreatingNewThread = true;
                else
                    BackToThreadList();
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
