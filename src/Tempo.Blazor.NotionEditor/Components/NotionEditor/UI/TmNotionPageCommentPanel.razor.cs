using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Models;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Helpers;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionPageCommentPanel : ComponentBase, IDisposable
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

    private void StartReply(TmCommentThread comment)
    {
        _replyingToCommentId = comment.Id;
        _replyText           = string.Empty;
    }

    private void CancelReply()
    {
        _replyingToCommentId = null;
        _replyText           = string.Empty;
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

    // ── Inline reply to entry ─────────────────────────────────────────────────

    private void HandleEntryKeyDownAsync(KeyboardEventArgs e, TmCommentEntry entry, TmCommentThread comment)
    {
        if (e.Key == "Enter" && _replyingToEntryId != entry.Id)
        {
            StartReplyToEntry(entry, comment);
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

    private void StartReplyToEntry(TmCommentEntry entry, TmCommentThread comment)
    {
        _replyingToEntryId  = entry.Id;
        _inlineReplyComment = comment;
        _inlineReplyText    = QuoteReply(entry);
        StateHasChanged();
    }

    private void CancelInlineReply()
    {
        _replyingToEntryId  = null;
        _inlineReplyComment = null;
        _inlineReplyText    = string.Empty;
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

            _replyingToEntryId  = null;
            _inlineReplyComment = null;
            _inlineReplyText    = string.Empty;
            await LoadAsync();
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
            CancelInlineReply();
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
            _editingEntryId = null;
            _editText       = string.Empty;
            await LoadAsync();
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
