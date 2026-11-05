using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Models;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Localization;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionTextCommentPanel : ComponentBase, IDisposable, ITmDraftGuard
{
    /// <summary>Author id from the cascaded editor context. Never the historical "demo" stand-in.</summary>
    private string CurrentUserId => Context?.CurrentUserId ?? string.Empty;

    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IServiceProvider Services { get; set; } = default!;

    private ITmRelativeTimeFormatter? _relativeTime;

    /// <summary>Cached per component — <see cref="TmRelativeTime.Resolve"/> allocates a fallback when none is registered.</summary>
    private ITmRelativeTimeFormatter RelativeTime => _relativeTime ??= TmRelativeTime.Resolve(Services, Loc);

    private void OnReplyText(string text) => _replyText = text;

    private void OnInlineText(string text) => _inlineReplyText = text;

    private Task OnReplyDraft(bool _) => NotifyDraftChangedAsync();

    private Task OnEditTextChanged(string text)
    {
        _editText = text;
        return NotifyDraftChangedAsync();
    }

    private Task OnEntryKey(TmNotionCommentThreadView.TmNotionCommentEntryKey key)
        => HandleEntryKeyDownAsync(key.Args, key.Entry);

    private Task SubmitReply(string text)
    {
        _replyText = text;
        return SendReplyAsync();
    }

    private MarkupString FormatEntry(TmCommentEntry entry)
        => (MarkupString)CommentHtmlFormatter.Format(NotionHtmlSanitizer.SanitizeBlockContent(entry.HtmlContent()));

    private string FormatTime(DateTimeOffset dt) => RelativeTime.Format(dt);

    private string AbsoluteTime(DateTimeOffset dt) => RelativeTime.Absolute(dt);

    // ── Cascaded ─────────────────────────────────────────────────────────────

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    /// <summary>The viewport layout scope — resolves <see cref="NotionCommentPresentation.Auto"/>.</summary>
    [CascadingParameter] private TmLayoutContext? Layout { get; set; }

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the panel is visible. Set it to <see langword="false"/> ONLY after checking
    /// <see cref="HasUnsavedDraft"/> — the panel clears every draft slot when it hides, so a
    /// host that hides it without asking first silently discards the in-progress comment.
    /// </summary>
    [Parameter] public bool   Visible         { get; set; }
    [Parameter] public string CommentId       { get; set; } = string.Empty;
    [Parameter] public string BlockId         { get; set; } = string.Empty;
    [Parameter] public int    StartOffset     { get; set; }
    [Parameter] public int    EndOffset       { get; set; }
    [Parameter] public string HighlightedText { get; set; } = string.Empty;
    [Parameter] public double Top             { get; set; }
    [Parameter] public double Left            { get; set; }

    /// <summary>
    /// How the panel presents (NO1) — see <see cref="TmNotionBlockCommentPanel.Presentation"/>.
    /// </summary>
    [Parameter] public NotionCommentPresentation Presentation { get; set; } = NotionCommentPresentation.Auto;

    [Parameter] public EventCallback          OnClosed        { get; set; }
    [Parameter] public EventCallback          OnResolved      { get; set; }
    [Parameter] public EventCallback<int>     OnCountChanged  { get; set; }
    [Parameter] public EventCallback<string>  OnMentionClicked { get; set; }

    /// <summary>
    /// Notifies the parent that the panel's comment draft state changed. The reply composer, the
    /// inline reply and the entry edit hold text the host cannot observe through a parameter —
    /// this callback (together with <see cref="HasUnsavedDraft"/> and
    /// <see cref="DiscardDraftAsync"/>) is the draft-guard surface. Fires ONLY when the
    /// <see cref="TmNotionCommentDraftState"/> value actually changes (value-equality dedup),
    /// never on every keystroke. The bottom composer is the NEW anchored-comment composer while
    /// no thread is loaded (<see cref="TmNotionCommentDraftState.HasNewCommentDraft"/>) and the
    /// thread reply composer afterwards (<see cref="TmNotionCommentDraftState.HasThreadReplyDraft"/>);
    /// <see cref="TmNotionCommentDraftState.HasInlineReplyDraft"/> covers the inline reply beyond
    /// its auto-inserted quote and <see cref="TmNotionCommentDraftState.EditingEntryId"/> the
    /// entry under edit (the aggregate <see cref="TmNotionCommentDraftState.HasUnsavedContent"/>
    /// counts an edit only once its text differs from the entry body as it was when the edit
    /// started).
    /// </summary>
    [Parameter] public EventCallback<TmNotionCommentDraftState> OnDraftChanged { get; set; }

    // ── State ─────────────────────────────────────────────────────────────────

    private TmCommentThread? _comment;
    private bool           _loading;
    private bool           _submitting;
    private string         _replyText  = string.Empty;
    private string         _error      = string.Empty;
    private double         _top;
    private double         _left;
    private bool           _wasVisible;
    private bool           _mentionHandlerAttached;
    private string         _activeCommentId = string.Empty;
    private ElementReference _panelRef;

    /// <summary>The fixed zero-size anchor at the bubble position — the popover-mode
    /// <c>TmOverlayPanel</c> positions against it and returns focus to it on dismissal.</summary>
    private ElementReference _anchorRef;
    private DotNetObjectReference<TmNotionTextCommentPanel>? _dotNetRef;

    private string?        _editingEntryId;
    private string         _editText = string.Empty;
    private bool           _showDeleteConfirm;
    private TmCommentEntry? _pendingDeleteEntry;

    private string?        _replyingToEntryId;
    private string         _inlineReplyText = string.Empty;

    /// <summary>Body of the entry as it was when its edit started — the differs-from-original baseline for the edit slot.</summary>
    private string _editOriginalText = string.Empty;

    /// <summary>Quote auto-inserted when the inline reply opened — content up to this baseline is not user input.</summary>
    private string _inlineReplyBaseline = string.Empty;
    private int _composerReset;
    private int _inlineReset;

    /// <summary>Last draft state handed to the parent — used to dedup <see cref="OnDraftChanged"/> notifications.</summary>
    private TmNotionCommentDraftState _lastDraftState = new(false, false, false, null, false);

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
            _inlineReplyBaseline = string.Empty;
            _composerReset++;
            _inlineReset++;
            _activeCommentId  = CommentId;
            await LoadAsync();
            await SetHighlightActiveAsync(true);
            await NotifyDraftChangedAsync();
        }

        if (!Visible && _wasVisible)
        {
            await SetHighlightActiveAsync(false);
            _comment = null;
            // Hiding is a hard close for the drafts: every composer unmounts, so the slots are
            // cleared and the cleared state is REPORTED (hosts check HasUnsavedDraft before
            // hiding — a host that hid anyway must not keep a stale draft flag).
            _replyText = string.Empty;
            _editingEntryId = null;
            _editText = string.Empty;
            _editOriginalText = string.Empty;
            _replyingToEntryId = null;
            _inlineReplyText = string.Empty;
            _inlineReplyBaseline = string.Empty;
            _composerReset++;
            _inlineReset++;
            await NotifyDraftChangedAsync();
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
            // Re-bind on every false→true transition: the panel root is not in the DOM while hidden,
            // so a firstRender-only init never sees a panel the host opens later.
            if (!_mentionHandlerAttached)
            {
                _mentionHandlerAttached = true;
                try { await JS.InvokeVoidAsync("tmNotionEditor.initMentionClickHandler", _panelRef, _dotNetRef); } catch { /* best-effort */ }
            }
            try { await JS.InvokeVoidAsync("tmNotionEditor.clampFixedElementToViewport", _panelRef, 12); } catch { /* best-effort */ }
        }
        else
        {
            _mentionHandlerAttached = false;
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
    /// Whether the panel currently holds an unsaved comment draft in ANY composer — the bottom
    /// reply/new-comment composer, the inline reply (beyond its auto-inserted quote) or an entry
    /// edit whose text differs from the entry body as it was when the edit started. The SAME
    /// value carried by <see cref="TmNotionCommentDraftState.HasUnsavedContent"/> that
    /// <see cref="OnDraftChanged"/> reports; read live from the composer state, so it can be
    /// polled at any time (for example before closing the panel or navigating away).
    /// <para>
    /// Read it on the component's dispatcher (e.g. from an event handler or via
    /// <c>InvokeAsync</c>) when called from a background thread.
    /// </para>
    /// </summary>
    public bool HasUnsavedDraft => BuildDraftState().HasUnsavedContent;

    /// <summary>
    /// Discards every draft in the panel: clears the bottom reply/new-comment composer, closes
    /// the inline reply and the entry edit (their texts are cleared with them). Intended for the
    /// consumer AFTER the user confirmed the discard in the consumer's OWN confirmation dialog —
    /// the component never shows a confirmation itself; that is deliberately the consumer's
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
            _composerReset++;
            _inlineReset++;
            _editingEntryId      = null;
            _editText            = string.Empty;
            _editOriginalText    = string.Empty;
            StateHasChanged();
            await NotifyDraftChangedAsync();
        });

    /// <summary>
    /// Builds the panel's draft snapshot. The bottom composer is the new anchored-comment
    /// composer while no thread is loaded and the thread reply composer afterwards; the inline
    /// reply only counts once its text moved beyond the auto-inserted quote; the edit slot
    /// reports the editing id while the aggregate counts it only once the text differs from the
    /// original entry body.
    /// </summary>
    private TmNotionCommentDraftState BuildDraftState()
    {
        var hasReplyText        = !string.IsNullOrWhiteSpace(_replyText);
        var hasNewCommentDraft  = _comment is null && hasReplyText;
        var hasThreadReplyDraft = _comment is not null && hasReplyText;
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
                    BlockId, StartOffset, EndOffset, HighlightedText, _replyText.Trim(), CommentId, CurrentUserId);
                _activeCommentId = _comment.Id;
            }
            else
            {
                await Context.CommentProvider.ReplyToCommentAsync(_comment.Id, _replyText.Trim(), parentEntryId: null, CurrentUserId);
            }

            _replyText = string.Empty;
            _composerReset++;
            await LoadAsync();
            await OnCountChanged.InvokeAsync(_comment?.Entries.Count ?? 0);
            await NotifyDraftChangedAsync();
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
            await CancelInlineReply();
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
                _replyingToEntryId,
                CurrentUserId);

            _replyingToEntryId = null;
            _inlineReplyText   = string.Empty;
            _inlineReplyBaseline = string.Empty;
            _inlineReset++;
            await LoadAsync();
            await OnCountChanged.InvokeAsync(_comment?.Entries.Count ?? 0);
            await NotifyDraftChangedAsync();
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
            await CancelInlineReply();
    }

    // The edit wrap stops keydown propagation, so the textarea needs its own
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
        _inlineReset++;
        StateHasChanged();
        await NotifyDraftChangedAsync();
    }

    private async Task CancelInlineReply()
    {
        _replyingToEntryId = null;
        _inlineReplyText   = string.Empty;
        _inlineReplyBaseline = string.Empty;
        _inlineReset++;
        await NotifyDraftChangedAsync();
    }

    private async Task ResolveAsync()
    {
        if (Context.CommentProvider is null || _comment is null) return;
        _error = string.Empty;
        try
        {
            _comment = await Context.CommentProvider.ResolveCommentAsync(_comment.Id, CurrentUserId);
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
            _comment = await Context.CommentProvider.UnresolveCommentAsync(_comment.Id, CurrentUserId);
            await OnCountChanged.InvokeAsync(_comment.Entries.Count);
        }
        catch
        {
            _error = Loc["TmNotionTextComment_ActionError"];
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
        if (Context.CommentProvider is null || _editingEntryId is null || string.IsNullOrWhiteSpace(_editText))
            return;

        _error = string.Empty;
        try
        {
            if (_comment is null)
                return;

            await Context.CommentProvider.EditCommentAsync(_comment.Id, _editingEntryId, _editText.Trim(), CurrentUserId);
            _editingEntryId = null;
            _editText       = string.Empty;
            _editOriginalText = string.Empty;
            await LoadAsync();
            await NotifyDraftChangedAsync();
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

    private static string TruncateText(string text, int maxLen)
        => text.Length <= maxLen ? text : text[..maxLen] + "…";

    // ── Presentation (NO1) ────────────────────────────────────────────────────

    /// <summary>Auto resolves per layout: popover bubble on desktop, core modal/sheet on a touch
    /// or narrow (tablet / mobile) layout.</summary>
    private bool UseDialog => Presentation switch
    {
        NotionCommentPresentation.Dialog  => true,
        NotionCommentPresentation.Popover => false,
        _                                  => Layout?.IsMobile == true || Layout?.IsTablet == true,
    };

    /// <summary>Length beyond which the dialog quote clamps (with a "Show full quote" toggle).</summary>
    private const int QuoteClampLength = 140;

    private bool _quoteExpanded;

    /// <summary>The popover truncates the quote; the dialog shows it whole.</summary>
    private string QuoteText => UseDialog ? HighlightedText : TruncateText(HighlightedText, 60);

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
