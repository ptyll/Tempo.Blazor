using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Text;

/// <summary>
/// Self-contained contenteditable paragraph. Owns its JS keyboard-handler lifecycle
/// and surfaces high-level EventCallbacks so the parent dispatcher can react.
/// The [JSInvokable] method names must match the strings used in notion-editor.js.
/// EventCallback parameter names are distinct from those method names to avoid C# conflicts.
/// </summary>
public partial class TmNotionTextBlock : ComponentBase, IAsyncDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    [CascadingParameter]
    private NotionEditorContext? Context { get; set; }

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter] public IPageBlock?        Block       { get; set; }
    [Parameter] public ITextBlockContent? Content     { get; set; }
    [Parameter] public bool               ReadOnly    { get; set; }
    [Parameter] public bool               IsFocused   { get; set; }
    [Parameter] public string?            Placeholder { get; set; }

    /// <summary>Fired on blur when the HTML has changed. Arg = new HTML.</summary>
    [Parameter] public EventCallback<string>                    OnContentSaved     { get; set; }

    /// <summary>Fired after Enter splits the block. Arg = HTML fragment after the split point.</summary>
    [Parameter] public EventCallback<string>                    OnEnterSplit       { get; set; }

    /// <summary>Fired when Backspace is pressed on an empty block.</summary>
    [Parameter] public EventCallback                            OnDeleteRequested  { get; set; }

    /// <summary>
    /// Raised when Backspace is pressed at the start of a non-empty block. The payload is the
    /// block's current, sanitized HTML, which the previous block absorbs before this one is deleted.
    /// </summary>
    [Parameter] public EventCallback<string>                    OnMergeWithPrevious { get; set; }

    /// <summary>
    /// Raised when pasted HTML carries more than one block element. The payload is the raw
    /// clipboard HTML; the consumer turns it into page blocks.
    /// </summary>
    [Parameter] public EventCallback<string>                    OnStructuredPaste { get; set; }

    /// <summary>Fired when Tab / Shift+Tab is pressed. Bool = shiftKey (true = outdent).</summary>
    [Parameter] public EventCallback<bool>                      OnIndentChange     { get; set; }

    /// <summary>Fired when a markdown pattern is recognised. Arg = shortcut key (e.g. "heading1").</summary>
    [Parameter] public EventCallback<string>                    OnTypeConvert      { get; set; }

    /// <summary>Fired when the element receives focus.</summary>
    [Parameter] public EventCallback                            OnFocused          { get; set; }

    /// <summary>Fired when ArrowUp requests focus movement to the previous block.</summary>
    [Parameter] public EventCallback                            OnMoveFocusPrevious { get; set; }

    /// <summary>Fired when ArrowDown requests focus movement to the next block.</summary>
    [Parameter] public EventCallback                            OnMoveFocusNext     { get; set; }

    /// <summary>Fired when '/' is typed in a trigger position. Args = (top, left) caret coords.</summary>
    [Parameter] public EventCallback<(double Top, double Left)> OnSlashMenu        { get; set; }

    /// <summary>Fired when '@' mention syntax is typed. Args = (top, left) caret coords.</summary>
    [Parameter] public EventCallback<(double Top, double Left)> OnMentionMenu      { get; set; }

    /// <summary>Fired when '[[' page-link syntax is typed. Args = (top, left) caret coords.</summary>
    [Parameter] public EventCallback<(double Top, double Left)> OnPageLinkMenu     { get; set; }

    /// <summary>Fired when '{{' token syntax is typed. Args = (top, left) caret coords.</summary>
    [Parameter] public EventCallback<(double Top, double Left)> OnTokenMenu        { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private ElementReference                           _editableRef;
    private DotNetObjectReference<TmNotionTextBlock>?  _dotNetRef;
    private bool                                       _kbInitialized;
    private bool                                       _dirty;
    private bool                                       _lastIsFocused;
    private string?                                    _lastHtml;
    private string?                                    _lastPropHtml;
    private ITextBlockContent?                         _lastContent;

    // ── Computed CSS ─────────────────────────────────────────────────────────

    private string _alignClass => Content?.Alignment switch
    {
        TextAlignment.Center => "tm-notion-align-center",
        TextAlignment.Right  => "tm-notion-align-right",
        _                    => string.Empty
    };

    private string _bgClass =>
        string.IsNullOrEmpty(Content?.BackgroundColor)
            ? string.Empty
            : $"tm-notion-bg-{Content.BackgroundColor}";

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(Content, _lastContent)) return;
        _lastContent   = Content;
        _lastPropHtml  = Content?.Html;
        _dirty         = false;
        _kbInitialized = false;
        _lastHtml      = null;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (ReadOnly) return;

        var html = Content?.Html ?? string.Empty;
        var shouldFocus = IsFocused && !_lastIsFocused;
        var focusedThisRender = false;

        if (!_kbInitialized)
        {
            _kbInitialized = true;
            _lastHtml      = html;
            _dotNetRef?.Dispose();
            _dotNetRef = DotNetObjectReference.Create(this);
            try
            {
                await JS.InvokeVoidAsync("tmNotionEditor.initKeyboardHandler", _editableRef, _dotNetRef);
                // A re-init can follow a Content-instance swap while the editable still
                // holds unsaved DOM text (e.g. Enter-split keystrokes replayed by
                // focusAtStart); the JS dirty flag is the only trustworthy witness.
                if (await JS.InvokeAsync<bool>("tmNotionEditor.isEditableDirty", _editableRef))
                    _dirty = true;
                else
                    await JS.InvokeVoidAsync("tmNotionEditor.setHtml", _editableRef, SanitizeForRender(html));
                if (shouldFocus)
                {
                    await JS.InvokeVoidAsync("tmNotionEditor.focusAtStart", _editableRef);
                    focusedThisRender = true;
                }
            }
            catch { }
        }
        else if (!_dirty && html != _lastHtml)
        {
            // The dirty flag only tracks `input` events. DOM surgery done elsewhere can leave
            // unsaved edits behind, so compare the live DOM before overwriting it.
            if (await HasUnsavedDomEditsAsync())
            {
                _dirty = true;
                return;
            }

            // A same-instance prop can only legitimately advance via in-place mutation.
            // When it still carries the value we already consumed, this render is stale
            // (e.g. queued before an Enter-split setHtml) and writing it would revert live edits.
            if (html != _lastPropHtml)
            {
                _lastHtml     = html;
                _lastPropHtml = html;
                try { await JS.InvokeVoidAsync("tmNotionEditor.setHtml", _editableRef, SanitizeForRender(html)); }
                catch { }
            }
        }

        if (_kbInitialized && shouldFocus && !focusedThisRender)
        {
            try { await JS.InvokeVoidAsync("tmNotionEditor.focusAtStart", _editableRef); }
            catch { }
        }

        _lastIsFocused = IsFocused;
    }

    // ── Blur / focus ──────────────────────────────────────────────────────────

    private async Task OnBlurAsync()
    {
        if (!_dirty || ReadOnly) return;
        try
        {
            var html = await JS.InvokeAsync<string>("tmNotionEditor.getHtml", _editableRef);
            var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);
            _lastHtml = sanitized;
            await OnContentSaved.InvokeAsync(sanitized);
        }
        catch { }
        finally
        {
            _dirty = false;
        }
    }

    /// <summary>
    /// Block content is written into the DOM with innerHTML, so a stored onerror payload would run
    /// on render. Sanitize on the way in and on the way out.
    /// </summary>
    private static string SanitizeForRender(string html) =>
        NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

    /// <summary>True when the DOM holds edits the dirty flag never saw (JS-driven DOM surgery).</summary>
    private async Task<bool> HasUnsavedDomEditsAsync()
    {
        try
        {
            var live = await JS.InvokeAsync<string>("tmNotionEditor.getHtml", _editableRef);
            return _lastHtml is not null && live != _lastHtml;
        }
        catch
        {
            return false;
        }
    }

    private async Task HandleFocusAsync() => await OnFocused.InvokeAsync();

    // ── JS keyboard callbacks — names MUST match notion-editor.js ─────────────

    [JSInvokable]
    public async Task OnEnterPressed(string beforeHtml, string afterHtml)
    {
        var before = NotionInlineHtmlSanitizer.SanitizeBlockContent(beforeHtml);
        _lastHtml = before;
        _dirty    = false;

        // Write the left-hand half back explicitly. Relying on the save producing a new Content
        // reference would leave the source block showing the whole pre-split text whenever the
        // provider hands the same instance back.
        try { await JS.InvokeVoidAsync("tmNotionEditor.setHtml", _editableRef, before); }
        catch { }

        await OnContentSaved.InvokeAsync(before);
        await OnEnterSplit.InvokeAsync(NotionInlineHtmlSanitizer.SanitizeBlockContent(afterHtml));
    }

    [JSInvokable]
    public async Task OnBackspaceOnEmpty() => await OnDeleteRequested.InvokeAsync();

    /// <summary>
    /// Called from notion-editor.js when Backspace is pressed while the caret sits before the
    /// first character. Blocks used without a merge consumer simply keep their text.
    /// </summary>
    [JSInvokable]
    public async Task OnBackspaceAtStart(string html)
    {
        if (!OnMergeWithPrevious.HasDelegate) return;
        await OnMergeWithPrevious.InvokeAsync(NotionInlineHtmlSanitizer.SanitizeBlockContent(html));
    }

    /// <summary>
    /// Called from notion-editor.js when the clipboard HTML has several block elements. Blocks
    /// used without a consumer fall back to the inline paste that JS already performed.
    /// </summary>
    [JSInvokable]
    public async Task OnHtmlPasted(string html)
    {
        if (OnStructuredPaste.HasDelegate) await OnStructuredPaste.InvokeAsync(html);
    }

    [JSInvokable]
    public async Task OnTabPressed(bool shiftKey) => await OnIndentChange.InvokeAsync(shiftKey);

    [JSInvokable]
    public async Task OnArrowUp() => await OnMoveFocusPrevious.InvokeAsync();

    [JSInvokable]
    public async Task OnArrowDown() => await OnMoveFocusNext.InvokeAsync();

    [JSInvokable]
    public async Task OnMarkdownShortcut(string shortcut) =>
        await OnTypeConvert.InvokeAsync(shortcut);

    [JSInvokable]
    public async Task OnSlashTriggered(double top, double left) =>
        await OnSlashMenu.InvokeAsync((top, left));

    [JSInvokable]
    public async Task OnMentionTriggered(double top, double left) =>
        await OnMentionMenu.InvokeAsync((top, left));

    [JSInvokable]
    public async Task OnPageLinkTriggered(double top, double left) =>
        await OnPageLinkMenu.InvokeAsync((top, left));

    [JSInvokable]
    public async Task OnTokenTriggered(double top, double left) =>
        await OnTokenMenu.InvokeAsync((top, left));

    [JSInvokable]
    public bool HasSmartLinkProvider() => Context?.SmartLinkProvider is not null;

    [JSInvokable]
    public async Task OnSmartLinkPasteRequested(string rawUrl, string displayMode)
    {
        var url = NormalizeUrl(rawUrl);
        SmartLinkDto? resolved = null;

        if (Context?.SmartLinkProvider is not null)
        {
            try { resolved = await Context.SmartLinkProvider.ResolveAsync(url); }
            catch { resolved = null; }
        }

        if (resolved is not null &&
            string.Equals(displayMode, "Card", StringComparison.OrdinalIgnoreCase) &&
            Context?.BlockService is not null &&
            Block is not null)
        {
            var bookmark = new PageBlock
            {
                Id = Guid.NewGuid(),
                PageId = Block.PageId,
                Type = BlockType.Bookmark,
                Order = Block.Order + 1,
                Content = new BookmarkBlockContent
                {
                    Url = resolved.Url,
                    Title = resolved.Title,
                    Description = resolved.Description,
                    CoverImageUrl = resolved.ImageUrl,
                    FaviconUrl = resolved.FaviconUrl,
                    Domain = resolved.ProviderName
                },
                CreatedAt = DateTime.UtcNow,
                LastEditedAt = DateTime.UtcNow
            };

            var created = await Context.BlockService.CreateBlockAsync(Block.PageId.ToString("D"), bookmark, Block.Id.ToString("D"));
            await Context.RaiseBlockCreatedAsync(created);
            return;
        }

        if (resolved is not null)
        {
            try
            {
                await JS.InvokeVoidAsync(
                    "tmNotionEditor.insertSmartLinkChip",
                    _editableRef,
                    resolved.Url,
                    resolved.Title,
                    resolved.FaviconUrl,
                    resolved.ProviderName);
            }
            catch { }
        }
        else
        {
            try
            {
                await JS.InvokeVoidAsync("tmNotionEditor.insertPlainSmartLink", _editableRef, url);
            }
            catch { }
        }

        await SaveCurrentHtmlAsync();
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_kbInitialized)
        {
            try { await JS.InvokeVoidAsync("tmNotionEditor.destroyBlock", _editableRef); }
            catch { }
        }
        _dotNetRef?.Dispose();
    }

    private async Task SaveCurrentHtmlAsync()
    {
        try
        {
            var html = await JS.InvokeAsync<string>("tmNotionEditor.getHtml", _editableRef);
            await OnContentSaved.InvokeAsync(html);
            _dirty = false;
            _lastHtml = html;
        }
        catch { }
    }

    private static string NormalizeUrl(string rawUrl)
    {
        var trimmed = rawUrl.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return trimmed;

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute) &&
               !string.IsNullOrWhiteSpace(absolute.Scheme)
            ? absolute.ToString()
            : $"https://{trimmed.TrimStart('/')}";
    }
}
