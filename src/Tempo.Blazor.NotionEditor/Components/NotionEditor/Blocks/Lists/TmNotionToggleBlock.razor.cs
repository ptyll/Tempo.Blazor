using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Lists;

/// <summary>
/// Self-contained toggle block. Owns the header contenteditable JS lifecycle and manages
/// child blocks (lazy-loaded from <see cref="NotionEditorContext.BlockService"/>).
/// Arrow click fires OnOpenChanged so the parent can persist the new IsOpen state.
/// Enter on an empty header fires OnTypeConvert("paragraph") to escape the toggle.
/// Enter on a non-empty header fires OnEnterSplit to create a new sibling block after.
/// </summary>
public partial class TmNotionToggleBlock : ComponentBase, IAsyncDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Cascaded context ─────────────────────────────────────────────────────

    [CascadingParameter]
    private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired]
    public IPageBlock Block { get; set; } = default!;

    [Parameter] public IToggleBlockContent? Content     { get; set; }
    [Parameter] public bool                 ReadOnly    { get; set; }
    [Parameter] public bool                 IsFocused   { get; set; }
    [Parameter] public string?              Placeholder { get; set; }

    /// <summary>Fired on blur when header HTML has changed. Arg = new HTML.</summary>
    [Parameter] public EventCallback<string>                    OnContentSaved    { get; set; }

    /// <summary>Fired when Enter is pressed on a non-empty header. Arg = HTML after the split point.</summary>
    [Parameter] public EventCallback<string>                    OnEnterSplit      { get; set; }

    /// <summary>Fired when Backspace is pressed on an empty header.</summary>
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

    /// <summary>
    /// Fired when a markdown or conversion shortcut is detected.
    /// Special value "paragraph" means the user pressed Enter on an empty header.
    /// </summary>
    [Parameter] public EventCallback<string>                    OnTypeConvert     { get; set; }

    /// <summary>Fired when the header editable receives focus.</summary>
    [Parameter] public EventCallback                            OnFocused         { get; set; }

    /// <summary>Fired when the open/closed state changes. Arg = new IsOpen value.</summary>
    [Parameter] public EventCallback<(bool IsOpen, string? Html)> OnOpenChanged     { get; set; }

    /// <summary>Fired when '/' is typed. Args = (blockId, top, left) caret coords.</summary>
    [Parameter] public EventCallback<(string BlockId, double Top, double Left)> OnSlashMenu       { get; set; }

    /// <summary>Fired when '@' is typed. Args = (blockId, top, left) caret coords.</summary>
    [Parameter] public EventCallback<(string BlockId, double Top, double Left)> OnMentionMenu     { get; set; }

    /// <summary>Fired when '[[' is typed. Args = (blockId, top, left) caret coords.</summary>
    [Parameter] public EventCallback<(string BlockId, double Top, double Left)> OnPageLinkMenu    { get; set; }

    /// <summary>Fired when '{{' token syntax is typed. Args = (blockId, top, left) caret coords.</summary>
    [Parameter] public EventCallback<(string BlockId, double Top, double Left)> OnTokenMenu       { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private ElementReference                              _editableRef;
    private DotNetObjectReference<TmNotionToggleBlock>?  _dotNetRef;
    private bool                                         _kbInitialized;
    private bool                                         _dirty;
    private string?                                      _lastHtml;
    private string?                                      _lastPropHtml;
    private IToggleBlockContent?                         _lastContent;
    private bool                                         _isOpen;
    private List<IPageBlock>                             _children       = [];
    private bool                                         _loadingChildren;
    private bool                                         _childrenLoaded;
    private Guid?                                        _activeChildId;

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

    protected override void OnInitialized()
    {
        Context.BlockConverted += OnBlockConverted;
    }

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(Content, _lastContent)) return;
        _lastContent   = Content;
        _lastPropHtml  = Content?.Html;
        _dirty         = false;
        _kbInitialized = false;
        _lastHtml      = null;
        _isOpen        = Content?.IsOpen ?? false;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!ReadOnly)
        {
            var html = Content?.Html ?? string.Empty;

            if (!_kbInitialized)
            {
                _lastHtml = html;
                _dotNetRef?.Dispose();
                _dotNetRef = DotNetObjectReference.Create(this);
                try
                {
                    await JS.InvokeVoidAsync("tmNotionEditor.initKeyboardHandler", _editableRef, _dotNetRef);
                    _kbInitialized = true;
                }
                catch { }
                try
                {
                    // A re-init can follow a Content-instance swap while the editable
                    // still holds unsaved DOM text (e.g. Enter-split keystrokes
                    // replayed by focusAtStart); the JS dirty flag is the only
                    // trustworthy witness.
                    if (await JS.InvokeAsync<bool>("tmNotionEditor.isEditableDirty", _editableRef))
                        _dirty = true;
                    else
                        await JS.InvokeVoidAsync("tmNotionEditor.setHtml", _editableRef, html);
                    if (IsFocused)
                        await JS.InvokeVoidAsync("tmNotionEditor.focusAtStart", _editableRef);
                }
                catch { }
            }
            else if (!_dirty && html != _lastHtml)
            {
                // A same-instance prop can only legitimately advance via in-place mutation.
                // When it still carries the value we already consumed, this render is stale
                // (e.g. queued before an Enter-split setHtml) and writing it would revert live edits.
                if (html != _lastPropHtml)
                {
                    _lastHtml     = html;
                    _lastPropHtml = html;
                    try { await JS.InvokeVoidAsync("tmNotionEditor.setHtml", _editableRef, html); }
                    catch { }
                }
            }
        }

        if (_isOpen && !_childrenLoaded && !_loadingChildren)
            await LoadChildrenAsync();
    }

    // ── Arrow / open state ────────────────────────────────────────────────────

    private async Task HandleArrowClickAsync()
    {
        string? html = null;
        if (_dirty && !ReadOnly)
        {
            try
            {
                html = await JS.InvokeAsync<string>("tmNotionEditor.getHtml", _editableRef);
                await OnContentSaved.InvokeAsync(html);
            }
            catch { }
            finally
            {
                _dirty = false;
            }
        }
        _isOpen = !_isOpen;
        await OnOpenChanged.InvokeAsync((_isOpen, html));
    }

    // ── Children loading ──────────────────────────────────────────────────────

    /// <summary>A block dragged in from another list: persist the move, then reload the subtree.</summary>
    private async Task HandleExternalChildDroppedAsync(MoveNotionBlockRequest request)
    {
        try
        {
            await Context.BlockService.MoveBlockAsync(request);
        }
        catch
        {
            // Fall through: reloading resyncs the toggle with whatever the server actually has.
        }

        _childrenLoaded = false;
        await LoadChildrenAsync();
    }

    /// <summary>A child that was dragged out of this toggle into another list.</summary>
    private Task HandleExternalChildRemovedAsync(string childId)
    {
        var child = _children.FirstOrDefault(block => block.Id.ToString() == childId);
        if (child is not null)
        {
            _children.Remove(child);
            if (_activeChildId == child.Id) _activeChildId = null;
            StateHasChanged();
        }

        return Task.CompletedTask;
    }

    private async Task LoadChildrenAsync()
    {
        _loadingChildren = true;
        StateHasChanged();
        try
        {
            var result = await Context.BlockService.GetChildBlocksAsync(Block.Id.ToString());
            _children       = [.. result.OrderBy(b => b.Order)];
            _childrenLoaded = true;
        }
        catch { }
        finally
        {
            _loadingChildren = false;
            StateHasChanged();
        }
    }

    // ── Blur / focus ──────────────────────────────────────────────────────────

    private async Task OnBlurAsync()
    {
        if (!_dirty || ReadOnly) return;
        try
        {
            var html = await JS.InvokeAsync<string>("tmNotionEditor.getHtml", _editableRef);
            await OnContentSaved.InvokeAsync(html);
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

        _lastHtml = beforeHtml;
        _dirty    = false;
        await OnContentSaved.InvokeAsync(beforeHtml);
        await OnEnterSplit.InvokeAsync(afterHtml);
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
    public void OnTabPressed(bool shiftKey) { }

    [JSInvokable]
    public void OnArrowUp() { }

    [JSInvokable]
    public void OnArrowDown() { }

    [JSInvokable]
    public async Task OnMarkdownShortcut(string shortcut) =>
        await OnTypeConvert.InvokeAsync(shortcut);

    [JSInvokable]
    public async Task OnSlashTriggered(double top, double left) =>
        await OnSlashMenu.InvokeAsync((Block.Id.ToString(), top, left));

    [JSInvokable]
    public async Task OnMentionTriggered(double top, double left) =>
        await OnMentionMenu.InvokeAsync((Block.Id.ToString(), top, left));

    [JSInvokable]
    public async Task OnPageLinkTriggered(double top, double left) =>
        await OnPageLinkMenu.InvokeAsync((Block.Id.ToString(), top, left));

    [JSInvokable]
    public async Task OnTokenTriggered(double top, double left) =>
        await OnTokenMenu.InvokeAsync((Block.Id.ToString(), top, left));

    // ── Child block handlers ──────────────────────────────────────────────────

    private async Task HandleChildReorderAsync((int source, int target) args)
    {
        if (args.source < 0 || args.source >= _children.Count) return;

        var block            = _children[args.source];
        var normalizedTarget = Math.Clamp(
            args.source < args.target ? args.target - 1 : args.target,
            0, _children.Count - 1);

        _children.RemoveAt(args.source);
        _children.Insert(normalizedTarget, block);
        RenumberChildOrder();
        StateHasChanged();

        try
        {
            await Context.BlockService.ReorderBlocksAsync(
                Block.PageId.ToString(),
                _children.Select(b => b.Id.ToString()));
        }
        catch { await LoadChildrenAsync(); }
    }

    private Task HandleChildFocusedAsync(string childId)
    {
        if (Guid.TryParse(childId, out var id)) _activeChildId = id;
        return Task.CompletedTask;
    }

    private async Task HandleChildDeletedAsync(string childId)
    {
        var child = _children.FirstOrDefault(b => b.Id.ToString() == childId);
        if (child is null) return;
        try
        {
            await Context.BlockService.DeleteBlockAsync(childId);
            _children.Remove(child);
            if (_activeChildId == child.Id) _activeChildId = null;
            StateHasChanged();
        }
        catch { }
    }

    private Task HandleChildUpdatedAsync(IPageBlock updated)
    {
        var idx = _children.FindIndex(b => b.Id == updated.Id);
        if (idx >= 0) _children[idx] = updated;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task HandleChildDuplicatedAsync(IPageBlock source)
    {
        try
        {
            var duplicated = await Context.BlockService.DuplicateBlockAsync(source.Id.ToString());
            var srcIdx     = _children.FindIndex(b => b.Id == source.Id);
            _children.Insert(Math.Clamp(srcIdx + 1, 0, _children.Count), duplicated);
            _activeChildId = duplicated.Id;
            StateHasChanged();
        }
        catch { }
    }

    private void OnBlockConverted(IPageBlock converted)
    {
        var idx = _children.FindIndex(b => b.Id == converted.Id);
        if (idx >= 0)
        {
            _children[idx] = converted;
            if (_activeChildId == converted.Id)
                _activeChildId = converted.Id;
            StateHasChanged();
        }
    }

    private async Task HandleChildConvertAsync((string childId, BlockType newType) args)
    {
        var child = _children.FirstOrDefault(b => b.Id.ToString() == args.childId);
        if (child is null) return;
        try
        {
            var converted = await Context.BlockService.ConvertBlockTypeAsync(args.childId, args.newType);
            var idx       = _children.FindIndex(b => b.Id == child.Id);
            if (idx >= 0) _children[idx] = converted;
            StateHasChanged();
        }
        catch { }
    }

    private async Task HandleChildAddAfterAsync(
        (string AfterChildId, BlockType Type, string? InitialHtml) args)
    {
        var afterChild  = _children.FirstOrDefault(b => b.Id.ToString() == args.AfterChildId);
        var insertOrder = afterChild is null
            ? (_children.Count > 0 ? _children.Max(b => b.Order) + 1 : 0)
            : afterChild.Order + 1;

        var newBlock = new PageBlock
        {
            Id            = Guid.NewGuid(),
            PageId        = Block.PageId,
            ParentBlockId = Block.Id,
            Type          = args.Type,
            Order         = insertOrder,
            Content       = CreateDefaultContent(args.Type, args.InitialHtml)
        };

        try
        {
            var created   = await Context.BlockService.CreateBlockAsync(
                Block.PageId.ToString(), newBlock, args.AfterChildId);
            var insertIdx = afterChild is null
                ? _children.Count
                : _children.IndexOf(afterChild) + 1;
            _children.Insert(Math.Clamp(insertIdx, 0, _children.Count), created);
            _activeChildId = created.Id;
            StateHasChanged();
        }
        catch { }
    }

    private async Task HandleChildAddAtEndAsync()
    {
        var newBlock = new PageBlock
        {
            Id            = Guid.NewGuid(),
            PageId        = Block.PageId,
            ParentBlockId = Block.Id,
            Type          = BlockType.Paragraph,
            Order         = _children.Count > 0 ? _children.Max(b => b.Order) + 1 : 0,
            Content       = new TextBlockContent()
        };
        try
        {
            var created = await Context.BlockService.CreateBlockAsync(
                Block.PageId.ToString(), newBlock, null);
            _children.Add(created);
            _activeChildId = created.Id;
            StateHasChanged();
        }
        catch { }
    }

    private Task HandleChildSlashAsync((string BlockId, double Top, double Left) args) =>
        OnSlashMenu.InvokeAsync(args);

    private Task HandleChildMentionAsync((string BlockId, double Top, double Left) args) =>
        OnMentionMenu.InvokeAsync(args);

    private Task HandleChildPageLinkAsync((string BlockId, double Top, double Left) args) =>
        OnPageLinkMenu.InvokeAsync(args);

    private Task HandleChildTokenAsync((string BlockId, double Top, double Left) args) =>
        OnTokenMenu.InvokeAsync(args);

    // ── Dispose ───────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        Context.BlockConverted -= OnBlockConverted;

        if (_kbInitialized)
        {
            try { await JS.InvokeVoidAsync("tmNotionEditor.destroyBlock", _editableRef); }
            catch { }
        }
        _dotNetRef?.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RenumberChildOrder()
    {
        for (var i = 0; i < _children.Count; i++)
        {
            if (_children[i] is PageBlock pb) pb.Order = i;
        }
    }

    private static IBlockContent CreateDefaultContent(BlockType type, string? initialHtml = null) =>
        type switch
        {
            BlockType.Heading1 => new HeadingBlockContent { Html = initialHtml ?? string.Empty, Level = 1 },
            BlockType.Heading2 => new HeadingBlockContent { Html = initialHtml ?? string.Empty, Level = 2 },
            BlockType.Heading3 => new HeadingBlockContent { Html = initialHtml ?? string.Empty, Level = 3 },
            BlockType.Quote    => new TextBlockContent    { Html = initialHtml ?? string.Empty },
            BlockType.Callout  => new CalloutBlockContent { Html = initialHtml ?? string.Empty },
            BlockType.BulletList or BlockType.NumberedList =>
                                  new ListBlockContent    { Html = initialHtml ?? string.Empty },
            BlockType.TodoItem => new TodoBlockContent    { Html = initialHtml ?? string.Empty },
            BlockType.Toggle   => new ToggleBlockContent  { Html = initialHtml ?? string.Empty },
            BlockType.Code     => new CodeBlockContent(),
            _                  => new TextBlockContent    { Html = initialHtml ?? string.Empty }
        };

    private static bool IsEmptyHtml(string html) =>
        string.IsNullOrWhiteSpace(html) || html.Trim() is "" or "<br>" or "<br/>" or "&nbsp;";
}
