using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;

namespace Tempo.Blazor.Components.NotionEditor.Blocks;

/// <summary>
/// Dropdown context menu for a single block — provides Delete, Duplicate, Turn into,
/// Move to, Copy link, Comment, and Color actions.
/// </summary>
public partial class TmNotionBlockContextMenu : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Cascaded context ─────────────────────────────────────────────────────

    [CascadingParameter]
    private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired]
    public IPageBlock Block { get; set; } = default!;

    [Parameter] public EventCallback          OnClose              { get; set; }
    [Parameter] public EventCallback          OnDelete             { get; set; }
    [Parameter] public EventCallback          OnDuplicate          { get; set; }
    [Parameter] public EventCallback<BlockType> OnTurnInto         { get; set; }
    [Parameter] public EventCallback          OnMoveTo             { get; set; }
    [Parameter] public EventCallback          OnCopyLink           { get; set; }
    [Parameter] public EventCallback          OnComment            { get; set; }
    [Parameter] public EventCallback          OnNewThread          { get; set; }
    [Parameter] public EventCallback<CalloutVariant> OnCalloutVariantChange { get; set; }
    [Parameter] public EventCallback<string?> OnTextColorChange    { get; set; }
    [Parameter] public EventCallback<string?> OnBackgroundChange   { get; set; }

    // ── Submenu state ────────────────────────────────────────────────────────

    private bool _showTurnInto;
    private bool _showPanelType;
    private bool _showColor;
    private bool _focusPending = true;
    private ElementReference _menuRef;

    private bool HasCommentProvider => Context.CommentProvider is not null;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusPending)
            return;

        _focusPending = false;

        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.positionContextMenu", _menuRef);
            await JS.InvokeVoidAsync("tmNotionEditor.initFocusTrap", _menuRef);
        }
        catch
        {
        }
    }

    private enum Sub { TurnInto, PanelType, Color }

    private void OpenSub(Sub sub)
    {
        _showTurnInto  = sub == Sub.TurnInto;
        _showPanelType = sub == Sub.PanelType;
        _showColor     = sub == Sub.Color;
    }

    private void CloseSub(Sub sub)
    {
        if (sub == Sub.TurnInto)  _showTurnInto = false;
        if (sub == Sub.PanelType) _showPanelType = false;
        if (sub == Sub.Color)     _showColor = false;
    }

    // ── Action handlers ───────────────────────────────────────────────────────

    private async Task HandleDeleteAsync()
    {
        await OnClose.InvokeAsync();
        await OnDelete.InvokeAsync();
    }

    private async Task HandleDuplicateAsync()
    {
        await OnClose.InvokeAsync();
        await OnDuplicate.InvokeAsync();
    }

    private async Task HandleTurnIntoAsync(BlockType type)
    {
        await OnClose.InvokeAsync();
        await OnTurnInto.InvokeAsync(type);
    }

    private async Task HandleMoveToAsync()
    {
        await OnClose.InvokeAsync();
        await OnMoveTo.InvokeAsync();
    }

    private async Task HandleCopyLinkAsync()
    {
        await OnClose.InvokeAsync();
        await OnCopyLink.InvokeAsync();
    }

    private async Task HandleCommentAsync()
    {
        await OnClose.InvokeAsync();
        await OnComment.InvokeAsync();
    }

    private async Task HandleNewThreadAsync()
    {
        await OnClose.InvokeAsync();
        await OnNewThread.InvokeAsync();
    }

    private async Task HandleCalloutVariantAsync(CalloutVariant variant)
    {
        await OnClose.InvokeAsync();
        await OnCalloutVariantChange.InvokeAsync(variant);
    }

    private async Task HandleTextColorAsync(string? color)
    {
        await OnClose.InvokeAsync();
        await OnTextColorChange.InvokeAsync(color);
    }

    private async Task HandleBackgroundAsync(string? color)
    {
        await OnClose.InvokeAsync();
        await OnBackgroundChange.InvokeAsync(color);
    }

    private async Task CloseAsync() => await OnClose.InvokeAsync();

    private async Task HandleKeyDownAsync(KeyboardEventArgs args)
    {
        if (string.Equals(args.Key, "Escape", StringComparison.Ordinal))
            await CloseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.destroyFocusTrap", _menuRef);
        }
        catch
        {
        }
    }

    // ── Static data ───────────────────────────────────────────────────────────

    private static readonly TurnIntoItem[] _turnIntoItems =
    [
        new(BlockType.Paragraph,    "TmNotionBlockContextMenu_TurnIntoText"),
        new(BlockType.Heading1,     "TmNotionBlockContextMenu_TurnIntoH1"),
        new(BlockType.Heading2,     "TmNotionBlockContextMenu_TurnIntoH2"),
        new(BlockType.Heading3,     "TmNotionBlockContextMenu_TurnIntoH3"),
        new(BlockType.Quote,        "TmNotionBlockContextMenu_TurnIntoQuote"),
        new(BlockType.Callout,      "TmNotionBlockContextMenu_TurnIntoCallout"),
        new(BlockType.BulletList,   "TmNotionBlockContextMenu_TurnIntoBullet"),
        new(BlockType.NumberedList, "TmNotionBlockContextMenu_TurnIntoNumbered"),
        new(BlockType.TodoItem,     "TmNotionBlockContextMenu_TurnIntoTodo"),
        new(BlockType.Toggle,       "TmNotionBlockContextMenu_TurnIntoToggle"),
        new(BlockType.Code,         "TmNotionBlockContextMenu_TurnIntoCode"),
        new(BlockType.Divider,      "TmNotionBlockContextMenu_TurnIntoDivider"),
    ];

    private static readonly ColorItem[] _colorItems =
    [
        new(null,     "TmNotionBlockContextMenu_ColorDefault"),
        new("gray",   "TmNotionBlockContextMenu_ColorGray"),
        new("brown",  "TmNotionBlockContextMenu_ColorBrown"),
        new("orange", "TmNotionBlockContextMenu_ColorOrange"),
        new("yellow", "TmNotionBlockContextMenu_ColorYellow"),
        new("green",  "TmNotionBlockContextMenu_ColorGreen"),
        new("blue",   "TmNotionBlockContextMenu_ColorBlue"),
        new("purple", "TmNotionBlockContextMenu_ColorPurple"),
        new("pink",   "TmNotionBlockContextMenu_ColorPink"),
        new("red",    "TmNotionBlockContextMenu_ColorRed"),
    ];

    private static readonly PanelTypeItem[] _panelTypeItems =
    [
        new(CalloutVariant.Info, "TmNotionSlashMenu_ItemName_InfoPanel"),
        new(CalloutVariant.Note, "TmNotionSlashMenu_ItemName_NotePanel"),
        new(CalloutVariant.Warning, "TmNotionSlashMenu_ItemName_WarningPanel"),
        new(CalloutVariant.Error, "TmNotionSlashMenu_ItemName_ErrorPanel"),
        new(CalloutVariant.Success, "TmNotionSlashMenu_ItemName_SuccessPanel")
    ];

    private IEnumerable<TurnIntoItem> AllowedTurnIntoItems =>
        Context is null
            ? _turnIntoItems
            : _turnIntoItems.Where(i => Context.IsBlockTypeAllowed(i.Type));

    private sealed record TurnIntoItem(BlockType Type, string LabelKey);
    private sealed record ColorItem(string? Value, string LabelKey);
    private sealed record PanelTypeItem(CalloutVariant Variant, string LabelKey);
}
