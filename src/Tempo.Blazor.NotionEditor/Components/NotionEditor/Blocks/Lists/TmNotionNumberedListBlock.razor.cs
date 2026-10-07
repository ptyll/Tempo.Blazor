using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Lists;

/// <summary>
/// Self-contained numbered list item. Renders its sequential number (computed by the parent
/// by counting preceding sibling NumberedList blocks at the same indent level).
/// Enter on an empty item fires OnTypeConvert("paragraph").
/// Enter on a non-empty item fires OnEnterSplit to create a new sibling.
/// Tab/Shift+Tab fires OnIndentChange so the parent can clamp (0–3) and persist.
/// </summary>
public partial class TmNotionNumberedListBlock : ComponentBase, IAsyncDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter] public IListBlockContent? Content     { get; set; }
    [Parameter] public int                Number      { get; set; } = 1;
    [Parameter] public bool               ReadOnly    { get; set; }
    [Parameter] public bool               IsFocused   { get; set; }
    [Parameter] public string?            Placeholder { get; set; }

    /// <summary>Fired on blur when HTML has changed. Arg = new HTML.</summary>
    [Parameter] public EventCallback<string>                    OnContentSaved    { get; set; }

    /// <summary>Fired when Enter is pressed on a non-empty item. Arg = HTML after the split point.</summary>
    [Parameter] public EventCallback<string>                    OnEnterSplit      { get; set; }

    /// <summary>Fired when Backspace is pressed on an empty item.</summary>
    [Parameter] public EventCallback                            OnDeleteRequested { get; set; }

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

    /// <summary>Fired when Tab / Shift+Tab. Bool = shiftKey (true = outdent).</summary>
    [Parameter] public EventCallback<bool>                      OnIndentChange    { get; set; }

    /// <summary>
    /// Fired when a markdown or conversion shortcut is detected.
    /// Special value "paragraph" means the user pressed Enter on an empty item.
    /// </summary>
    [Parameter] public EventCallback<string>                    OnTypeConvert     { get; set; }

    /// <summary>Fired when the editable receives focus.</summary>
    [Parameter] public EventCallback                            OnFocused         { get; set; }

    /// <summary>Fired when '/' is typed. Args = (top, left) caret coords.</summary>
    [Parameter] public EventCallback<(double Top, double Left)> OnSlashMenu       { get; set; }

    /// <summary>Fired when '@' is typed. Args = (top, left) caret coords.</summary>
    [Parameter] public EventCallback<(double Top, double Left)> OnMentionMenu     { get; set; }

    /// <summary>Fired when '[[' is typed. Args = (top, left) caret coords.</summary>
    [Parameter] public EventCallback<(double Top, double Left)> OnPageLinkMenu    { get; set; }

    /// <summary>Fired when '{{' token syntax is typed. Args = (top, left) caret coords.</summary>
    [Parameter] public EventCallback<(double Top, double Left)> OnTokenMenu       { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private ElementReference                                   _editableRef;
    private DotNetObjectReference<TmNotionNumberedListBlock>?  _dotNetRef;
    private bool                                               _kbInitialized;
    private bool                                               _dirty;
    private string?                                            _lastHtml;
    private string?                                            _lastPropHtml;
    private IListBlockContent?                                 _lastContent;

    // ── Computed ─────────────────────────────────────────────────────────────

    private string _numberLabel => (Content?.IndentLevel ?? 0) switch
    {
        1 => $"{(char)('a' + Math.Min(Number - 1, 25))}.",
        2 => $"{ToRoman(Number)}.",
        _ => $"{Number}."
    };

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
                if (IsFocused)
                    await JS.InvokeVoidAsync("tmNotionEditor.focusAtStart", _editableRef);
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

    private async Task HandleFocusAsync() => await OnFocused.InvokeAsync();

    // ── JS keyboard callbacks — names MUST match notion-editor.js ─────────────

    [JSInvokable]
    public async Task OnEnterPressed(string beforeHtml, string afterHtml)
    {
        if (IsEmptyHtml(beforeHtml))
        {
            await OnTypeConvert.InvokeAsync("paragraph");
            return;
        }

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
    public void OnArrowUp() { }

    [JSInvokable]
    public void OnArrowDown() { }

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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsEmptyHtml(string html) =>
        string.IsNullOrWhiteSpace(html) || html.Trim() is "" or "<br>" or "<br/>" or "&nbsp;";

    private static string ToRoman(int n)
    {
        if (n < 1) return "i";
        var result = new System.Text.StringBuilder();
        int[] vals  = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] syms = ["m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i"];
        for (var i = 0; i < vals.Length; i++)
        {
            while (n >= vals[i]) { result.Append(syms[i]); n -= vals[i]; }
        }
        return result.ToString();
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
}
