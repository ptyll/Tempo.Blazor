using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Models;
using Tempo.Blazor.Components.NotionEditor.Services;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionTextCommentPanel : ComponentBase, IDisposable
{
    private const string CurrentUserId = "demo";

    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Cascaded ─────────────────────────────────────────────────────────────

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter] public bool   Visible         { get; set; }
    [Parameter] public string CommentId       { get; set; } = string.Empty;
    [Parameter] public string BlockId         { get; set; } = string.Empty;
    [Parameter] public int    StartOffset     { get; set; }
    [Parameter] public int    EndOffset       { get; set; }
    [Parameter] public string HighlightedText { get; set; } = string.Empty;
    [Parameter] public double Top             { get; set; }
    [Parameter] public double Left            { get; set; }

    [Parameter] public EventCallback          OnClosed        { get; set; }
    [Parameter] public EventCallback          OnResolved      { get; set; }
    [Parameter] public EventCallback<int>     OnCountChanged  { get; set; }
    [Parameter] public EventCallback<string>  OnMentionClicked { get; set; }

    // ── State ─────────────────────────────────────────────────────────────────

    private TmCommentThread? _comment;
    private bool           _loading;
    private bool           _submitting;
    private string         _replyText  = string.Empty;
    private string         _error      = string.Empty;
    private double         _top;
    private double         _left;
    private bool           _wasVisible;
    private string         _activeCommentId = string.Empty;
    private ElementReference _panelRef;
    private DotNetObjectReference<TmNotionTextCommentPanel>? _dotNetRef;

    private string?        _editingEntryId;
    private string         _editText = string.Empty;
    private bool           _showDeleteConfirm;
    private TmCommentEntry? _pendingDeleteEntry;

    private string?        _replyingToEntryId;
    private string         _inlineReplyText = string.Empty;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async Task OnParametersSetAsync()
    {
        if (Visible && !_wasVisible)
        {
            _top              = Top;
            _left             = Left;
            _replyText        = string.Empty;
            _error            = string.Empty;
            _editingEntryId   = null;
            _replyingToEntryId = null;
            _inlineReplyText  = string.Empty;
            _activeCommentId  = CommentId;
            await LoadAsync();
            await SetHighlightActiveAsync(true);
        }

        if (!Visible && _wasVisible)
        {
            await SetHighlightActiveAsync(false);
            _comment = null;
        }

        _wasVisible = Visible;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
        }

        if (Visible)
        {
            try { await JS.InvokeVoidAsync("tmNotionEditor.initMentionClickHandler", _panelRef, _dotNetRef); } catch { /* best-effort */ }
            try { await JS.InvokeVoidAsync("tmNotionEditor.clampFixedElementToViewport", _panelRef, 12); } catch { /* best-effort */ }
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
        if (Context.CommentProvider is null) return;

        _loading = true;
        _error   = string.Empty;
        StateHasChanged();

        try
        {
            if (!string.IsNullOrEmpty(_activeCommentId))
            {
                var list = await Context.CommentProvider.GetBlockCommentsAsync(BlockId);
                _comment = list.FirstOrDefault(c => c.Id == _activeCommentId);
            }
        }
        catch
        {
            _error = Loc["TmNotionTextComment_LoadError"];
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
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
            if (_comment is null)
            {
                _comment = await Context.CommentProvider.AddTextAnchorCommentAsync(
                    BlockId, StartOffset, EndOffset, HighlightedText, _replyText.Trim(), CommentId);
                _activeCommentId = _comment.Id;
            }
            else
            {
                await Context.CommentProvider.ReplyToCommentAsync(_comment.Id, _replyText.Trim());
            }

            _replyText = string.Empty;
            await LoadAsync();
            await OnCountChanged.InvokeAsync(_comment?.Entries.Count ?? 0);
        }
        catch
        {
            _error = Loc["TmNotionTextComment_SendError"];
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
            string.IsNullOrWhiteSpace(_inlineReplyText) || _submitting || _comment is null)
            return;

        _submitting = true;
        _error      = string.Empty;
        StateHasChanged();

        try
        {
            await Context.CommentProvider.ReplyToCommentAsync(
                _comment.Id,
                _inlineReplyText.Trim(),
                _replyingToEntryId);

            _replyingToEntryId = null;
            _inlineReplyText   = string.Empty;
            await LoadAsync();
            await OnCountChanged.InvokeAsync(_comment?.Entries.Count ?? 0);
        }
        catch
        {
            _error = Loc["TmNotionTextComment_SendError"];
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

    // The edit wrap stops keydown propagation, so the textarea needs its own
    // Escape->cancel (it previously relied on the keydown bubbling up to the
    // entry handler). Plain Enter stays unhandled → native newline.
    private void HandleEditKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            CancelEdit();
    }

    private void HandleEntryKeyDownAsync(KeyboardEventArgs e, TmCommentEntry entry)
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
        if (Context.CommentProvider is null || _comment is null) return;
        _error = string.Empty;
        try
        {
            _comment = await Context.CommentProvider.ResolveCommentAsync(_comment.Id);
            await OnResolved.InvokeAsync();
            await OnCountChanged.InvokeAsync(0);
        }
        catch
        {
            _error = Loc["TmNotionTextComment_ActionError"];
        }
    }

    private async Task UnresolveAsync()
    {
        if (Context.CommentProvider is null || _comment is null) return;
        _error = string.Empty;
        try
        {
            _comment = await Context.CommentProvider.UnresolveCommentAsync(_comment.Id);
            await OnCountChanged.InvokeAsync(_comment.Entries.Count);
        }
        catch
        {
            _error = Loc["TmNotionTextComment_ActionError"];
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
        if (Context.CommentProvider is null || _editingEntryId is null || string.IsNullOrWhiteSpace(_editText))
            return;

        _error = string.Empty;
        try
        {
            if (_comment is null)
                return;

            await Context.CommentProvider.EditCommentAsync(_comment.Id, _editingEntryId, _editText.Trim());
            _editingEntryId = null;
            _editText       = string.Empty;
            await LoadAsync();
        }
        catch
        {
            _error = Loc["TmNotionTextComment_ActionError"];
        }
    }

    private void DeleteEntryAsync(TmCommentEntry entry)
    {
        if (Context.CommentProvider is null || _comment is null) return;
        _pendingDeleteEntry = entry;
        _showDeleteConfirm  = true;
        StateHasChanged();
    }

    private async Task HandleDeleteConfirmResult(bool? result)
    {
        _showDeleteConfirm = false;
        if (result != true || _pendingDeleteEntry is null || _comment is null)
        {
            _pendingDeleteEntry = null;
            return;
        }

        _error = string.Empty;
        var threadId = _comment.Id;
        try
        {
            var isOnlyEntry = _comment.Entries.Count <= 1;
            if (isOnlyEntry)
            {
                await Context.CommentProvider.DeleteCommentAsync(threadId);
                try { await JS.InvokeVoidAsync("tmNotionEditor.unwrapCommentHighlight", CommentId); } catch { /* best-effort */ }
                _comment = null;
                _activeCommentId = string.Empty;
                await OnCountChanged.InvokeAsync(0);
                StateHasChanged();
            }
            else
            {
                await Context.CommentProvider.DeleteCommentEntryAsync(threadId, _pendingDeleteEntry.Id);
                await LoadAsync();
                await OnCountChanged.InvokeAsync(_comment?.Entries.Count ?? 0);
            }
        }
        catch
        {
            _error = Loc["TmNotionTextComment_ActionError"];
        }
        finally
        {
            _pendingDeleteEntry = null;
            StateHasChanged();
        }
    }

    private async Task SetHighlightActiveAsync(bool active)
    {
        if (!string.IsNullOrEmpty(CommentId))
        {
            try
            {
                await JS.InvokeVoidAsync("tmNotionEditor.setCommentHighlightActive", CommentId, active);
            }
            catch { /* best-effort */ }
        }
    }

    private async Task CloseAsync() => await OnClosed.InvokeAsync();

    private bool IsSubscribed => _comment?.SubscribedUserIds.Contains(CurrentUserId) ?? false;

    private async Task SubscribeAsync()
    {
        if (Context.CommentProvider is null || _comment is null) return;
        _error = string.Empty;
        try
        {
            await Context.CommentProvider.SubscribeToThreadAsync(_comment.Id, CurrentUserId);
            if (!_comment.SubscribedUserIds.Contains(CurrentUserId))
                _comment.SubscribedUserIds.Add(CurrentUserId);
        }
        catch
        {
            _error = Loc["TmNotionTextComment_ActionError"];
        }
    }

    private async Task UnsubscribeAsync()
    {
        if (Context.CommentProvider is null || _comment is null) return;
        _error = string.Empty;
        try
        {
            await Context.CommentProvider.UnsubscribeFromThreadAsync(_comment.Id, CurrentUserId);
            _comment.SubscribedUserIds.Remove(CurrentUserId);
        }
        catch
        {
            _error = Loc["TmNotionTextComment_ActionError"];
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

    private static string TruncateText(string text, int maxLen)
        => text.Length <= maxLen ? text : text[..maxLen] + "…";

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
