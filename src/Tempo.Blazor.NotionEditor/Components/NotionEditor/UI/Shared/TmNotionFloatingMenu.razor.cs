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

    /// <summary>The ARIA role of the panel (a dialog or a listbox menu).</summary>
    [Parameter] public string Role { get; set; } = "menu";

    /// <summary>Extra CSS classes for the core overlay panel root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>The menu body.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Sheet-only footer.</summary>
    [Parameter] public RenderFragment? Footer { get; set; }

    private bool IsSheet =>
        (LayoutMode != TmLayoutMode.Auto ? LayoutMode : Layout?.Resolved) == TmLayoutMode.Mobile;

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
