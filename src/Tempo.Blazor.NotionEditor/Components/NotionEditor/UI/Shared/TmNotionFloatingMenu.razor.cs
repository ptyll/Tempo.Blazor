using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components;
using Tempo.Blazor.Components.Overlay;

namespace Tempo.Blazor.Components.NotionEditor.UI;

/// <summary>
/// The one floating menu of the Notion package. Desktop is an anchored popover; mobile is the core
/// sheet. Title and footer render only in the sheet.
/// </summary>
public partial class TmNotionFloatingMenu : TmComponentBase
{
    private ElementReference _anchor;

    /// <summary>Whether the menu is open.</summary>
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>Raised when the core overlay changes the open state.</summary>
    [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }

    /// <summary>Raised by Escape, an outside click and the sheet's Close button.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>The sheet title. A desktop popover renders none.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>
    /// The accessible name of the desktop popover. Defaults to <see cref="Title"/> (a desktop
    /// popover with no title needs an explicit value to stay a named dialog).
    /// </summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Forced layout. <see cref="TmLayoutMode.Auto"/> reads the cascaded context.</summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>
    /// The cascaded viewport layout an <see cref="TmLayoutMode.Auto"/> menu resolves from — a menu
    /// inside a component pinned to Desktop still sheets when the viewport is mobile.
    /// </summary>
    [CascadingParameter(Name = TmLayoutScopes.Viewport)] public TmLayoutContext? Layout { get; set; }

    /// <summary>
    /// Viewport anchor coordinates of the zero-size anchor (a caret or a measured trigger). Null
    /// anchors the menu where it renders in the DOM flow.
    /// </summary>
    [Parameter] public double? Top { get; set; }

    /// <summary>See <see cref="Top"/>.</summary>
    [Parameter] public double? Left { get; set; }

    /// <summary>The edge the popover opens from. The sheet presentation ignores it.</summary>
    [Parameter] public OverlayPlacement Placement { get; set; } = OverlayPlacement.Bottom;

    /// <summary>
    /// Cross-axis alignment against the anchor. A trigger on the viewport's trailing edge passes
    /// <see cref="OverlayAlign.End"/> so the panel opens inward; shift still clamps the rest.
    /// </summary>
    [Parameter] public OverlayAlign Align { get; set; } = OverlayAlign.Start;

    /// <summary>
    /// The ARIA role of the panel. Default dialog: a consumer whose menu role lives on an inner
    /// element (a submenu host) must not also put role=menu on the panel.
    /// </summary>
    [Parameter] public string Role { get; set; } = "dialog";

    /// <summary>
    /// Trap Tab inside a desktop popover and move initial focus into it. A menu opened from a
    /// handle otherwise leaves focus on the trigger. The mobile sheet traps through its drawer.
    /// </summary>
    [Parameter] public bool TrapFocus { get; set; }

    /// <summary>
    /// CSS selector of the element a trapped desktop popover focuses first. Null keeps the
    /// scope default (the first focusable).
    /// </summary>
    [Parameter] public string? InitialFocusSelector { get; set; }

    /// <summary>
    /// CSS selector of the element a mobile sheet focuses first. Null keeps the role default
    /// (a menu opens on its first item; a dialog opens on the first focusable).
    /// </summary>
    [Parameter] public string? SheetInitialFocusSelector { get; set; }

    /// <summary>Extra CSS classes for the menu body (both the popover and the sheet).</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// The element the popover anchors to. Null uses the zero-size pin (a caret menu supplies
    /// <see cref="Top"/>/<see cref="Left"/>); a trigger menu passes its button so the panel stays
    /// visible when that pin has no box.
    /// </summary>
    [Parameter] public ElementReference? AnchorElement { get; set; }

    /// <summary>The menu body.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Footer rendered in both the desktop popover and the mobile sheet. A host that only wants
    /// it on the sheet must not also render the same action in <see cref="ChildContent"/>.
    /// </summary>
    [Parameter] public RenderFragment? Footer { get; set; }

    private bool IsSheet =>
        (LayoutMode != TmLayoutMode.Auto ? LayoutMode : Layout?.Resolved) == TmLayoutMode.Mobile;

    private ElementReference ResolvedAnchor =>
        AnchorElement is { Id: not null } anchor ? anchor : _anchor;

    private string? AnchorStyle => Top is { } top && Left is { } left
        ? string.Create(CultureInfo.InvariantCulture, $"top:{top}px;left:{left}px")
        : null;

    private async Task OnOpenChanged(bool open)
    {
        await IsOpenChanged.InvokeAsync(open);
        if (!open)
            await OnClose.InvokeAsync();
    }

    private Task OnDismissed(string _) => OnClose.InvokeAsync();

    private async Task OnKeyDown(KeyboardEventArgs args)
    {
        if (args.Key != "Escape")
            return;
        await IsOpenChanged.InvokeAsync(false);
        await OnClose.InvokeAsync();
    }

    private Task CloseAsync() => OnClose.InvokeAsync();
}
