using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.NotionEditor.Enums;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionBlockTypeSwitcher : ComponentBase
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter] public bool      Visible       { get; set; }
    [Parameter] public double    Top           { get; set; }
    [Parameter] public double    Left          { get; set; }
    [Parameter] public BlockType CurrentType   { get; set; }

    [Parameter] public EventCallback<BlockType> OnTypeChanged { get; set; }
    [Parameter] public EventCallback            OnClosed      { get; set; }

    // ── Internal model ────────────────────────────────────────────────────────

    private sealed record TypeItem(BlockType Type, string NameKey, string SvgIcon);
    private sealed record TypeGroup(string HeaderKey, IReadOnlyList<TypeItem> Items);

    // ── Static groups ─────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<TypeGroup> _groups = BuildGroups();

    private static IReadOnlyList<TypeGroup> BuildGroups()
    {
        static TypeItem? Item(BlockType type)
        {
            var reg = SlashMenuRegistry.FindByType(type);
            if (reg is null) return null;
            return new TypeItem(type, NameKeyFor(type), reg.SvgIcon);
        }

        return
        [
            new("TmNotionBlockTypeSwitcher_BasicBlocks",
            [
                Item(BlockType.Paragraph)!,
                Item(BlockType.Heading1)!,
                Item(BlockType.Heading2)!,
                Item(BlockType.Heading3)!,
            ]),
            new("TmNotionBlockTypeSwitcher_Lists",
            [
                Item(BlockType.BulletList)!,
                Item(BlockType.NumberedList)!,
                Item(BlockType.TodoItem)!,
                Item(BlockType.Toggle)!,
            ]),
            new("TmNotionBlockTypeSwitcher_Blocks",
            [
                Item(BlockType.Quote)!,
                Item(BlockType.Callout)!,
                Item(BlockType.Code)!,
            ]),
        ];
    }

    private static string NameKeyFor(BlockType type) => type switch
    {
        BlockType.Paragraph    => "TmNotionBlockContextMenu_TurnIntoText",
        BlockType.Heading1     => "TmNotionBlockContextMenu_TurnIntoH1",
        BlockType.Heading2     => "TmNotionBlockContextMenu_TurnIntoH2",
        BlockType.Heading3     => "TmNotionBlockContextMenu_TurnIntoH3",
        BlockType.Quote        => "TmNotionBlockContextMenu_TurnIntoQuote",
        BlockType.Callout      => "TmNotionBlockContextMenu_TurnIntoCallout",
        BlockType.BulletList   => "TmNotionBlockContextMenu_TurnIntoBullet",
        BlockType.NumberedList => "TmNotionBlockContextMenu_TurnIntoNumbered",
        BlockType.TodoItem     => "TmNotionBlockContextMenu_TurnIntoTodo",
        BlockType.Toggle       => "TmNotionBlockContextMenu_TurnIntoToggle",
        BlockType.Code         => "TmNotionBlockContextMenu_TurnIntoCode",
        BlockType.Divider      => "TmNotionBlockContextMenu_TurnIntoDivider",
        _                      => type.ToString()
    };

    // ── Flat item list for keyboard navigation ────────────────────────────────

    private static readonly IReadOnlyList<TypeItem> _flatItems =
        _groups.SelectMany(g => g.Items).ToList();

    // ── State ─────────────────────────────────────────────────────────────────

    private double _top;
    private double _left;
    private bool   _wasVisible;
    private int    _highlightedIndex = -1;

    private ElementReference _panelRef;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        if (Visible && !_wasVisible)
        {
            _top              = Top;
            _left             = Left;
            _highlightedIndex = CurrentTypeIndex();
        }
        _wasVisible = Visible;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Visible)
        {
            try
            {
                await JS.InvokeVoidAsync("tmNotionEditor.adjustTypeSwitcherPosition", _panelRef);
            }
            catch { /* SSR / test */ }
        }
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private async Task SelectTypeAsync(BlockType type)
    {
        if (type == CurrentType)
        {
            await OnClosed.InvokeAsync();
            return;
        }
        await OnTypeChanged.InvokeAsync(type);
    }

    private async Task HandleBackdropClickAsync()
        => await OnClosed.InvokeAsync();

    private async Task HandleKeyDownAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowDown":
                _highlightedIndex = (_highlightedIndex + 1) % _flatItems.Count;
                break;
            case "ArrowUp":
                _highlightedIndex = (_highlightedIndex - 1 + _flatItems.Count) % _flatItems.Count;
                break;
            // No Enter/Space activation: this panel is never focused itself (no
            // tabindex), so those keys only ever arrive by bubbling from a
            // focused item <button> — which the browser also turns into a click
            // that reaches SelectTypeAsync via @onclick. Selecting the
            // highlighted item here too fired OnTypeChanged twice per key press
            // (and could pick a different item than the one activated).
            case "Escape":
                await OnClosed.InvokeAsync();
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private int CurrentTypeIndex()
        => _flatItems.Select((item, i) => (item, i))
                     .FirstOrDefault(t => t.item.Type == CurrentType).i;
}
