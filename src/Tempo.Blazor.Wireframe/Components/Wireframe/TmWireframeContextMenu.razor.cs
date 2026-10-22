using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>How <see cref="TmWireframeContextMenu"/> presents itself.</summary>
public enum WireframeContextMenuPresentation
{
    /// <summary>A <c>position:fixed</c> menu at the pointer (desktop and tablet).</summary>
    Anchored,

    /// <summary>The shared bottom action sheet of the core overlay panel (mobile, G2): finger-sized rows and a Done button; align, distribute and grouping are tablet+ and are not offered.</summary>
    Sheet,
}

/// <summary>Types of context menu for the wireframe editor.</summary>
public enum WireframeContextMenuType
{
    None,
    Canvas,
    Element,
    MultiSelect,
    Connector
}

/// <summary>
/// Floating context menu for the wireframe editor. Rendered at screen coordinates
/// via <c>position:fixed</c>. Supports Canvas, Element and MultiSelect menu variants.
/// </summary>
public partial class TmWireframeContextMenu : ComponentBase
{
    private ElementReference _menuRef;

    // ── Position / visibility ────────────────────────────────────────────────

    /// <summary>Anchored fixed menu (default) or the mobile action sheet.</summary>
    [Parameter] public WireframeContextMenuPresentation Presentation { get; set; } = WireframeContextMenuPresentation.Anchored;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public double X { get; set; }
    [Parameter] public double Y { get; set; }
    [Parameter] public WireframeContextMenuType MenuType { get; set; }

    // ── State flags ──────────────────────────────────────────────────────────

    [Parameter] public bool CanUndo { get; set; }
    [Parameter] public bool CanRedo { get; set; }
    [Parameter] public bool HasClipboardStyle { get; set; }
    [Parameter] public int SelectedCount { get; set; }
    [Parameter] public bool HasGroupInSelection { get; set; }
    [Parameter] public bool IsSelectionLocked { get; set; }
    [Parameter] public bool ReadOnly { get; set; }
    [Parameter] public bool ShowGrid { get; set; }
    [Parameter] public bool SnapToObjects { get; set; }

    /// <summary>True when there are enough selected elements to group (≥2).</summary>
    public bool CanGroup => SelectedCount >= 2;

    // ── Actions ──────────────────────────────────────────────────────────────

    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnUndo { get; set; }
    [Parameter] public EventCallback OnRedo { get; set; }
    [Parameter] public EventCallback OnSelectAll { get; set; }
    [Parameter] public EventCallback OnDuplicate { get; set; }
    [Parameter] public EventCallback OnDelete { get; set; }
    [Parameter] public EventCallback OnBringToFront { get; set; }
    [Parameter] public EventCallback OnSendToBack { get; set; }
    [Parameter] public EventCallback OnGroup { get; set; }
    [Parameter] public EventCallback OnUngroup { get; set; }
    [Parameter] public EventCallback OnLock { get; set; }
    [Parameter] public EventCallback OnUnlock { get; set; }
    [Parameter] public EventCallback OnCopyStyle { get; set; }
    [Parameter] public EventCallback OnPasteStyle { get; set; }
    [Parameter] public EventCallback OnPasteSize { get; set; }
    [Parameter] public EventCallback<WireframeAlignment> OnAlign { get; set; }
    [Parameter] public EventCallback<WireframeDistribution> OnDistribute { get; set; }
    [Parameter] public EventCallback OnToggleGrid { get; set; }
    [Parameter] public EventCallback OnToggleSnapToObjects { get; set; }
    [Parameter] public EventCallback OnFitToView { get; set; }

    // Connector-specific actions
    [Parameter] public EventCallback OnEditConnectorLabel { get; set; }
    [Parameter] public EventCallback<string> OnSetConnectorRouting { get; set; }

    /// <summary>The sheet was dismissed (Done, Escape, outside press or a swipe): the menu closes like it does for a press outside the anchored menu.</summary>
    private Task OnSheetOpenChanged(bool open) => open ? Task.CompletedTask : OnClose.InvokeAsync();

    /// <summary>Reference to the underlying menu div (used by JS for positioning).</summary>
    public ElementReference GetMenuRef() => _menuRef;

    // ── Wrappers that close menu then invoke action ──────────────────────────

    private async Task CtxUndo()         { await CloseThen(OnUndo); }
    private async Task CtxRedo()         { await CloseThen(OnRedo); }
    private async Task CtxSelectAll()    { await CloseThen(OnSelectAll); }
    private async Task CtxCopyStyle()    { await CloseThen(OnCopyStyle); }
    private async Task CtxDuplicate()    { await CloseThen(OnDuplicate); }
    private async Task CtxDelete()       { await CloseThen(OnDelete); }
    private async Task CtxBringToFront() { await CloseThen(OnBringToFront); }
    private async Task CtxSendToBack()   { await CloseThen(OnSendToBack); }
    private async Task CtxGroup()        { await CloseThen(OnGroup); }
    private async Task CtxUngroup()      { await CloseThen(OnUngroup); }
    private async Task CtxLock()         { await CloseThen(OnLock); }
    private async Task CtxUnlock()       { await CloseThen(OnUnlock); }
    private async Task CtxPasteStyle()   { await CloseThen(OnPasteStyle); }
    private async Task CtxPasteSize()    { await CloseThen(OnPasteSize); }
    private async Task CtxToggleGrid()   { await CloseThen(OnToggleGrid); }
    private async Task CtxToggleSnapToObjects() { await CloseThen(OnToggleSnapToObjects); }
    private async Task CtxFitToView()    { await CloseThen(OnFitToView); }
    private async Task CtxEditConnectorLabel() { await CloseThen(OnEditConnectorLabel); }
    private async Task CtxSetRouting(string routing)
    {
        await OnClose.InvokeAsync();
        await OnSetConnectorRouting.InvokeAsync(routing);
    }

    private async Task CtxAlign(WireframeAlignment a)
    {
        await OnClose.InvokeAsync();
        await OnAlign.InvokeAsync(a);
    }

    private async Task CtxDistribute(WireframeDistribution d)
    {
        await OnClose.InvokeAsync();
        await OnDistribute.InvokeAsync(d);
    }

    private async Task CloseThen(EventCallback callback)
    {
        await OnClose.InvokeAsync();
        await callback.InvokeAsync();
    }
}
