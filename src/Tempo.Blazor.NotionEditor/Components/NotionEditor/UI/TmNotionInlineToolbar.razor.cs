using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionInlineToolbar : TmComponentBase
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter] public bool           Visible          { get; set; }
    [Parameter] public double         Top              { get; set; }
    [Parameter] public double         Left             { get; set; }
    [Parameter] public bool           IsBold           { get; set; }
    [Parameter] public bool           IsItalic         { get; set; }
    [Parameter] public bool           IsUnderline      { get; set; }
    [Parameter] public bool           IsStrikethrough  { get; set; }
    [Parameter] public bool           IsCode           { get; set; }
    [Parameter] public string?        CurrentHref      { get; set; }
    [Parameter] public TextAlignment  CurrentAlign     { get; set; }

    [Parameter] public EventCallback<BlockType>     OnTurnInto    { get; set; }
    [Parameter] public EventCallback<TextAlignment> OnAlignChange { get; set; }
    [Parameter] public EventCallback<string>        OnComment     { get; set; }
    [Parameter] public EventCallback                OnAI          { get; set; }
    [Parameter] public bool                         ShowAI        { get; set; }

    [CascadingParameter] private TmLayoutContext? Layout { get; set; }

    private TmLayoutMode LayoutMode => Layout?.Resolved ?? TmLayoutMode.Auto;
    [Parameter] public string?                      BlockId       { get; set; }
    [Parameter] public object?                      DotNetRef     { get; set; }

    // ── Static data ───────────────────────────────────────────────────────────

    private static readonly (string Name, string? TextHex, string? BgHex)[] Colors =
    [
        ("default", null,      null),
        ("gray",    "gray",    "gray"),
        ("brown",   "brown",   "brown"),
        ("orange",  "orange",  "orange"),
        ("yellow",  "yellow",  "yellow"),
        ("green",   "green",   "green"),
        ("blue",    "blue",    "blue"),
        ("purple",  "purple",  "purple"),
        ("pink",    "pink",    "pink"),
        ("red",     "red",     "red"),
    ];

    private static readonly (BlockType Type, string NameKey)[] TurnIntoItems =
    [
        (BlockType.Paragraph,    "TmNotionBlockContextMenu_TurnIntoText"),
        (BlockType.Heading1,     "TmNotionBlockContextMenu_TurnIntoH1"),
        (BlockType.Heading2,     "TmNotionBlockContextMenu_TurnIntoH2"),
        (BlockType.Heading3,     "TmNotionBlockContextMenu_TurnIntoH3"),
        (BlockType.Quote,        "TmNotionBlockContextMenu_TurnIntoQuote"),
        (BlockType.Callout,      "TmNotionBlockContextMenu_TurnIntoCallout"),
        (BlockType.BulletList,   "TmNotionBlockContextMenu_TurnIntoBullet"),
        (BlockType.NumberedList, "TmNotionBlockContextMenu_TurnIntoNumbered"),
        (BlockType.TodoItem,     "TmNotionBlockContextMenu_TurnIntoTodo"),
        (BlockType.Toggle,       "TmNotionBlockContextMenu_TurnIntoToggle"),
        (BlockType.Code,         "TmNotionBlockContextMenu_TurnIntoCode"),
        (BlockType.Divider,      "TmNotionBlockContextMenu_TurnIntoDivider"),
    ];

    // ── State ────────────────────────────────────────────────────────────────

    private bool   _showLinkInput;
    private bool   _showColorPanel;
    private bool   _showTurnIntoPanel;
    private bool   _showAlignPanel;
    private string _linkUrl          = string.Empty;
    private bool   _wasVisible;
    private bool   _needsFocusLink;
    private double _top;
    private double _leftClamped;

    private ElementReference _toolbarRef;
    private ElementReference _linkInputRef;
    private ElementReference _colorTriggerRef;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        if (Visible && !_wasVisible)
        {
            _top             = Top;
            _leftClamped     = Left;
            _showLinkInput    = false;
            _showColorPanel   = false;
            _showTurnIntoPanel = false;
            _showAlignPanel   = false;
        }
        else if (!Visible && _wasVisible)
        {
            _showLinkInput    = false;
            _showColorPanel   = false;
            _showTurnIntoPanel = false;
            _showAlignPanel   = false;
        }
        _wasVisible = Visible;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Visible)
        {
            try
            {
                await JS.InvokeVoidAsync("tmNotionEditor.adjustInlineToolbarPosition", _toolbarRef);
            }
            catch { /* SSR / test */ }
        }

        if (_needsFocusLink && Visible && _showLinkInput)
        {
            _needsFocusLink = false;
            try { await _linkInputRef.FocusAsync(); }
            catch { /* SSR / test */ }
            return;
        }
    }

    // ── Format handlers ────────────────────────────────────────────────────────

    private async Task HandleBoldAsync()
    {
        try { await JS.InvokeVoidAsync("tmNotionEditor.applyFormat", "bold"); }
        catch { }
    }

    private async Task HandleItalicAsync()
    {
        try { await JS.InvokeVoidAsync("tmNotionEditor.applyFormat", "italic"); }
        catch { }
    }

    private async Task HandleUnderlineAsync()
    {
        try { await JS.InvokeVoidAsync("tmNotionEditor.applyFormat", "underline"); }
        catch { }
    }

    private async Task HandleStrikethroughAsync()
    {
        try { await JS.InvokeVoidAsync("tmNotionEditor.applyFormat", "strikeThrough"); }
        catch { }
    }

    private async Task HandleCodeAsync()
    {
        try { await JS.InvokeVoidAsync("tmNotionEditor.toggleInlineCode"); }
        catch { }
    }

    // ── Link handlers ─────────────────────────────────────────────────────────

    private async Task HandleLinkClickAsync()
    {
        try { await JS.InvokeVoidAsync("tmNotionEditor.saveSelection"); }
        catch { }
        _linkUrl         = CurrentHref ?? string.Empty;
        _showLinkInput   = true;
        _needsFocusLink  = true;
        CloseOtherPanels(link: true);
    }

    private async Task HandleApplyLinkAsync()
    {
        if (string.IsNullOrWhiteSpace(_linkUrl)) return;
        try { await JS.InvokeVoidAsync("tmNotionEditor.insertLinkOnSavedSelection", _linkUrl.Trim()); }
        catch { }
        _showLinkInput = false;
    }

    private async Task HandleRemoveLinkAsync()
    {
        try { await JS.InvokeVoidAsync("tmNotionEditor.restoreSavedSelection"); } catch { }
        try { await JS.InvokeVoidAsync("tmNotionEditor.applyFormat", "unlink", CurrentHref, BlockId); }
        catch { }
        _showLinkInput = false;
    }

    private async Task HandleLinkKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await HandleApplyLinkAsync();
        else if (e.Key == "Escape") _showLinkInput = false;
    }

    // ── Color handlers ────────────────────────────────────────────────────────

    private void HandleColorClickAsync()
    {
        _showColorPanel = !_showColorPanel;
        if (_showColorPanel) CloseOtherPanels(color: true);
    }

    private Task OnColorPanelOpenChanged(bool open)
    {
        if (!open)
            _showColorPanel = false;
        return Task.CompletedTask;
    }

    private async Task HandleTextColorAsync(string? name)
    {
        // The swatch paints from the token; execCommand only keeps a real colour, so the
        // allowlisted legacy hex is the one value this call may send.
        try { await JS.InvokeVoidAsync("tmNotionEditor.applyInlineColor", "text", NotionColorTokens.InlineHex(name, background: false)); }
        catch { }
        _showColorPanel = false;
    }

    private async Task HandleBgColorAsync(string? name)
    {
        try { await JS.InvokeVoidAsync("tmNotionEditor.applyInlineColor", "bg", NotionColorTokens.InlineHex(name, background: true)); }
        catch { }
        _showColorPanel = false;
    }

    // ── Turn Into handlers ────────────────────────────────────────────────────

    private void HandleTurnIntoClickAsync()
    {
        _showTurnIntoPanel = !_showTurnIntoPanel;
        if (_showTurnIntoPanel) CloseOtherPanels(turnInto: true);
    }

    private async Task HandleTurnIntoAsync(BlockType type)
    {
        _showTurnIntoPanel = false;
        await OnTurnInto.InvokeAsync(type);
    }

    // ── Align handlers ────────────────────────────────────────────────────────

    private void HandleAlignClickAsync()
    {
        _showAlignPanel = !_showAlignPanel;
        if (_showAlignPanel) CloseOtherPanels(align: true);
    }

    private async Task HandleAlignAsync(TextAlignment alignment)
    {
        _showAlignPanel = false;
        await OnAlignChange.InvokeAsync(alignment);
    }

    // ── Comment / Math handlers ───────────────────────────────────────────────

    private async Task HandleCommentAsync()
    {
        var commentId = Guid.NewGuid().ToString();
        try
        {
            if (DotNetRef is not null && !string.IsNullOrEmpty(BlockId))
                await JS.InvokeVoidAsync("tmNotionEditor.wrapSelectionWithComment", commentId, BlockId, DotNetRef, "OnTextCommentCreated");
            else
                await JS.InvokeVoidAsync("tmNotionEditor.wrapSelectionWithComment", commentId);
        }
        catch { }
        await OnComment.InvokeAsync(commentId);
    }

    private async Task HandleMathAsync()
    {
        try { await JS.InvokeVoidAsync("tmNotionEditor.insertInlineMath"); }
        catch { }
    }

    private async Task HandleAIAsync()
    {
        try { await JS.InvokeVoidAsync("tmNotionEditor.saveSelection"); }
        catch { }

        await OnAI.InvokeAsync();
    }

    // ── Keyboard handler ──────────────────────────────────────────────────────

    private void HandleKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            _showLinkInput    = false;
            _showColorPanel   = false;
            _showTurnIntoPanel = false;
            _showAlignPanel   = false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void CloseOtherPanels(bool link = false, bool color = false, bool turnInto = false, bool align = false)
    {
        if (!link)     _showLinkInput    = false;
        if (!color)    _showColorPanel   = false;
        if (!turnInto) _showTurnIntoPanel = false;
        if (!align)    _showAlignPanel   = false;
    }
}
