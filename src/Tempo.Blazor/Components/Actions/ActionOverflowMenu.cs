namespace Tempo.Blazor.Components.Actions;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Overlay;

/// <summary>
/// Internal. The single "More" menu of every action surface (the F5 <c>TmMobileActionBar</c>, the F4
/// <c>TmToolbar</c> overflow): the trigger, the controlled <c>TmOverlayPanel</c>, the menu items and
/// the host contract of docs/overlays.md — close-before-invoke, <c>KeepMenuOpen</c>, first enabled item
/// focus, <c>aria-expanded</c> accurate on every close path, <c>aria-controls</c> only while the menu is
/// rendered, focus back to the trigger. Owners compose it from C# (<c>OpenComponent</c>) — a Razor tag
/// would compile to a literal element (RZ10012). The open state lives here, so an owner that stops
/// rendering the component (the bar above the mobile breakpoint, an emptied overflow) drops it with the
/// component and a stale <c>_open</c> can never reopen the menu when the surface comes back.
/// </summary>
internal sealed class ActionOverflowMenu : ComponentBase
{
    private readonly string _panelId = $"tm-aom-panel-{Guid.NewGuid():N}";
    private readonly string _triggerId = $"tm-aom-trigger-{Guid.NewGuid():N}";
    private TmOverlayPanel? _panel;
    private ElementReference _trigger;
    private ElementReference _firstMenuItem;
    private bool _open;
    private bool _focusFirstItemPending;
    private bool _focusTriggerAfterClose;
    private bool _triggerRendered;

    /// <summary>The overflow items, in render order.</summary>
    [Parameter] public IReadOnlyList<TmActionItem> Items { get; set; } = [];

    /// <summary>Content of the trigger button (icon, label spans).</summary>
    [Parameter] public RenderFragment? TriggerContent { get; set; }

    /// <summary>CSS class(es) of the trigger button.</summary>
    [Parameter] public string? TriggerClass { get; set; }

    /// <summary>Accessible name of the trigger when its content carries no text.</summary>
    [Parameter] public string? TriggerAriaLabel { get; set; }

    /// <summary>Native tooltip (title) of the trigger. Null renders none.</summary>
    [Parameter] public string? TriggerTitle { get; set; }

    /// <summary>The menu title (names the popover and titles the sheet).</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>CSS class of the panel surface.</summary>
    [Parameter] public string? PanelClass { get; set; }

    /// <summary>CSS class of the menu list.</summary>
    [Parameter] public string? MenuClass { get; set; }

    /// <summary>CSS class of a menu item; the danger modifier is this class plus <c>--danger</c>.</summary>
    [Parameter] public string? ItemClass { get; set; }

    /// <summary>CSS class of a menu item label.</summary>
    [Parameter] public string? LabelClass { get; set; }

    /// <summary>The layout the panel presentation resolves from before its own measurement.</summary>
    [Parameter] public TmLayoutMode InitialMode { get; set; } = TmLayoutMode.Desktop;

    /// <summary>How the menu presents on a mobile viewport.</summary>
    [Parameter] public PanelPresentation MobilePresentation { get; set; } = PanelPresentation.Auto;

    /// <summary>Preferred side of the trigger the popover opens on.</summary>
    [Parameter] public OverlayPlacement Placement { get; set; } = OverlayPlacement.Bottom;

    /// <summary>Cross-axis alignment of the popover against the trigger.</summary>
    [Parameter] public OverlayAlign Align { get; set; } = OverlayAlign.Start;

    /// <summary>
    /// Closes the menu when it is open (a no-op otherwise). Use after a confirmed
    /// <see cref="TmActionItem.KeepMenuOpen"/> action; focus returns to the trigger (the flag is
    /// consumed in <c>OnAfterRenderAsync</c>, so the restore survives a sibling surface that also
    /// disposes in the same render batch and would otherwise land focus on &lt;body&gt;).
    /// </summary>
    public Task CloseAsync()
    {
        // A closed menu is a strict no-op — arming the focus restore unconditionally stole focus
        // from wherever the user was (F5 review round 2, Y2).
        if (!_open || _panel is null)
        {
            return Task.CompletedTask;
        }

        _focusTriggerAfterClose = true;
        return _panel.SetOpenAsync(false);
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Items.Count == 0)
        {
            // Nothing to offer: no trigger, no panel. The controlled panel is disposed on this
            // render without an IsOpenChanged, so the state is reset here (plain field writes).
            ResetState();
            return;
        }

        _triggerRendered = true;
        var allDisabled = Items.All(static item => item.Disabled);

        // The trigger is a raw button: the panel gets its element as an explicit Anchor, so the
        // popover positions against it and the sheet's focus restore resolves the trigger by
        // element reference. It needs an id — TmOverlayPanel's sheet restores through Anchor.Id.
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "type", "button");
        builder.AddAttribute(2, "id", _triggerId);
        builder.AddAttribute(3, "class", TriggerClass);
        builder.AddAttribute(4, "onclick", EventCallback.Factory.Create(this, OpenAsync));
        builder.AddAttribute(5, "aria-haspopup", "menu");
        builder.AddAttribute(6, "aria-expanded", _open ? "true" : "false");
        // aria-controls only while the controlled panel exists (it renders only while open).
        if (_open) builder.AddAttribute(7, "aria-controls", _panelId);
        if (!string.IsNullOrEmpty(TriggerAriaLabel)) builder.AddAttribute(8, "aria-label", TriggerAriaLabel);
        // An overflow of nothing but disabled items has nothing to invoke.
        builder.AddAttribute(9, "disabled", allDisabled);
        if (!string.IsNullOrEmpty(TriggerTitle)) builder.AddAttribute(27, "title", TriggerTitle);
        builder.AddContent(10, TriggerContent);
        // After the attributes and content: an element-reference capture may not sit between an
        // element frame and its attributes (RenderTreeBuilder assertion).
        builder.AddElementReferenceCapture(11, reference => _trigger = reference);
        builder.CloseElement();

        builder.OpenComponent<TmOverlayPanel>(12);
        builder.AddComponentParameter(13, "Id", _panelId);
        builder.AddComponentParameter(14, "Anchor", _trigger);
        // The panel carries the surface class: without it the zero-specificity reset leaves the
        // popover transparent.
        builder.AddComponentParameter(15, "Class", PanelClass);
        builder.AddComponentParameter(16, "Role", "menu");
        builder.AddComponentParameter(17, "Title", Title);
        builder.AddComponentParameter(18, "MobilePresentation", MobilePresentation);
        builder.AddComponentParameter(19, "InitialMode", InitialMode);
        builder.AddComponentParameter(20, "Placement", Placement);
        builder.AddComponentParameter(21, "Align", Align);
        // The menu is the single owner of the open state. The panel runs CONTROLLED: every close
        // path — item select, CloseAsync, sheet Done, backdrop, swipe, Escape, outside pointer —
        // funnels through IsOpenChanged, so aria-expanded can never go stale (F5 R2-M1).
        builder.AddComponentParameter(22, "IsOpen", _open);
        builder.AddComponentParameter(23, "IsOpenChanged", EventCallback.Factory.Create<bool>(this, HandleOpenChanged));
        // OnOpened stays only for the post-open focus move (the F3 m3 contract).
        builder.AddComponentParameter(24, "OnOpened", EventCallback.Factory.Create(this, HandleOpened));
        builder.AddAttribute(25, "ChildContent", (RenderFragment)BuildMenu);
        builder.AddComponentReferenceCapture(26, value => _panel = (TmOverlayPanel)value);
        builder.CloseComponent();
    }

    private void BuildMenu(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", MenuClass);
        var capturedFirstEnabled = false;
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            var seq = 16 + (i * 16);
            builder.OpenElement(seq, "button");
            builder.AddAttribute(seq + 1, "type", "button");
            builder.AddAttribute(seq + 2, "role", "menuitem");
            builder.AddAttribute(seq + 3, "class", item.Danger ? $"{ItemClass} {ItemClass}--danger" : ItemClass);
            builder.AddAttribute(seq + 4, "disabled", item.Disabled);
            builder.AddAttribute(seq + 5, "data-action-id", ActionId(item));
            builder.SetKey(string.IsNullOrEmpty(item.Id) ? item : item.Id);
            builder.AddAttribute(seq + 6, "onclick", EventCallback.Factory.Create(this, () => InvokeAsync(item)));
            if (!string.IsNullOrEmpty(item.Icon))
            {
                builder.OpenComponent<TmIcon>(seq + 7);
                builder.AddAttribute(seq + 8, "Name", item.Icon);
                builder.AddAttribute(seq + 9, "Size", IconSize.Sm);
                builder.CloseComponent();
            }

            builder.OpenElement(seq + 10, "span");
            builder.AddAttribute(seq + 11, "class", LabelClass);
            builder.AddContent(seq + 12, item.Label);
            builder.CloseElement();
            if (!capturedFirstEnabled && !item.Disabled)
            {
                // The initial focus skips disabled items — focus() on a disabled button is a no-op
                // and would leave focus on <body> (F5 X11).
                capturedFirstEnabled = true;
                builder.AddElementReferenceCapture(seq + 13, reference => _firstMenuItem = reference);
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    internal static string ActionId(TmActionItem item) => string.IsNullOrEmpty(item.Id) ? item.Label : item.Id;

    private void OpenAsync() => _open = true;

    private async Task InvokeAsync(TmActionItem item)
    {
        // Default (KeepMenuOpen=false): close first — the sheet pops down and focus returns to the
        // trigger before the action runs, so the action runs with no menu bookkeeping behind it.
        // KeepMenuOpen=true is for actions that open another surface: the menu stays, the surface
        // promotes AFTER it and always paints above it (top-layer order = open order,
        // docs/overlays.md rule 8), and closing the surface returns focus to this item.
        if (!item.KeepMenuOpen && _panel is not null)
        {
            // The light popover close does not restore focus (overlay.js close() leaves it wherever
            // the destroyed menu had it — often <body>), so the menu restores it itself: the flag is
            // consumed in OnAfterRenderAsync, the TmDropdown.SelectItemAsync pattern (F5 X5).
            _focusTriggerAfterClose = true;
            await _panel.CloseAsync();
            // Force the close re-render NOW and let it complete before the action runs — the focus
            // restore to the trigger lands inside OnAfterRenderAsync, so an action that moves focus
            // elsewhere wins deterministically instead of racing a late restore (F5 Y8).
            StateHasChanged();
            await Task.Yield();
        }

        if (!item.Disabled)
        {
            await item.OnClick.InvokeAsync();
        }
    }

    private void ResetState()
    {
        _open = false;
        _focusFirstItemPending = false;
        _focusTriggerAfterClose = false;
        _triggerRendered = false;
    }

    /// <summary>Fires once per open, after the panel is actually displayed.</summary>
    private void HandleOpened() => _focusFirstItemPending = true;

    private void HandleOpenChanged(bool open)
    {
        _open = open;
        if (!open)
        {
            _focusFirstItemPending = false;
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusFirstItemPending)
        {
            _focusFirstItemPending = false;
            try
            {
                await _firstMenuItem.FocusAsync();
            }
            catch (Exception ex) when (ex is InvalidOperationException or JSException)
            {
                // The menu closed before the focus move (fast Escape) or the element left the DOM —
                // focus() then throws JSException "Unable to focus an invalid element" (a real
                // browser; bUnit throws InvalidOperationException with no JSRuntime). Best-effort.
            }
        }

        if (_focusTriggerAfterClose)
        {
            _focusTriggerAfterClose = false;
            if (!_triggerRendered)
            {
                // The trigger left the DOM before the restore could run — drop the flag instead of
                // stealing focus later, whenever the trigger happens to come back.
                return;
            }

            try
            {
                await _trigger.FocusAsync(preventScroll: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JSException)
            {
                // The trigger left the DOM before the focus move: nothing to focus.
            }
        }
    }
}
