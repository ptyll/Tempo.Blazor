using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Helpers;
using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Components.Layout;

// The state, the resize wiring and the persistence half of the application shell. It lives beside the
// markup file to keep the .razor under the source-file length ceiling; the class is one type.

/// <summary>
/// The application frame: a top bar, one named navigation column, the page content and an optional
/// resizable work area, resolved from the shell's own container width. Desktop renders the docked
/// column; a tablet container renders an icon rail whose full navigation opens in a modal overlay
/// drawer; a phone container renders no column at all — a hamburger in the top bar opens the same
/// drawer, or a <c>TmBottomNavigation</c> bar replaces it — and presents the work area as a
/// <c>TmSidePanel</c> bottom sheet. It composes <c>TmLayoutObserver</c>, <c>TmSidebar</c>,
/// <c>TmTopBar</c>, <c>TmDrawer</c>, <c>TmBottomNavigation</c> and <c>TmSidePanel</c> instead of
/// reimplementing navigation, focus or sheet behaviour, and it owns the page's
/// <c>header</c> / <c>nav</c> / <c>main</c> / work-area <c>aside</c> landmarks exactly once.
/// Contract: docs/app-shell.md.
/// </summary>
public partial class TmAppShell
{
    // One instance id, one prefix: every id this shell owns is unique per instance and shares the
    // "tm-app-shell-" stem the markup sweep counts as an unmeasurable interpolated class.
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly string _workAreaId;
    private readonly string _workAreaTitleId;
    private readonly string _overlayId;
    private readonly string _menuButtonId;

    /// <summary>Creates the shell and its per-instance DOM ids (one prefix, unique per instance).</summary>
    public TmAppShell()
    {
        _workAreaId = $"tm-app-shell-{_instanceId}-work-area";
        _workAreaTitleId = $"tm-app-shell-{_instanceId}-work-area-title";
        _overlayId = $"tm-app-shell-{_instanceId}-nav-overlay";
        _menuButtonId = $"tm-app-shell-{_instanceId}-menu-button";
    }

    private ElementReference _content;
    private ElementReference _workArea;
    private ElementReference _resizer;
    private PanelResizeInterop? _resizeJs;
    private DotNetObjectReference<TmAppShell>? _dotNetRef;
    private bool _resizerAttached;
    private string? _resizerKey;

    // The last measured content width (CSS pixels): the resize never takes the content below
    // MinContentWidth, and the separator announces the width the user can really reach. NaN until
    // the first measurement (or when no work area is docked).
    private double _contentWidth = double.NaN;

    private TmLayoutMode? _measured;
    private TmLayoutMode _resolved = TmLayoutMode.Desktop;
    private bool _resolvedInitialised;

    // The collapsed state in effect plus the host value it was adopted from: a user toggle wins until
    // the host CHANGES SidebarCollapsed, so a host that ignores the callback keeps the user's choice.
    private bool _collapsedShadow;
    private bool _collapsedParamSeen;
    private bool _collapsedInitialised;
    // The user toggled in this session: a slow restore must not override what they just did.
    private bool _collapsedTouched;
    // The shadow is a CHOICE — the user's toggle or a value restored from storage — not just a copy of
    // the host parameter, so it is what persistence stores. Cleared when the host picks a value itself.
    private bool _collapsedChosen;

    // The user's work-area width (an overlay on top of WorkAreaWidth) and the host width it was
    // chosen from. Only that overlay is persisted; an explicit host width set afterwards wins.
    private int? _userWidth;
    private int? _userBase;
    private int _widthParamSeen;
    private bool _widthInitialised;

    // The navigation overlay. A tablet rail opens it, and so does the phone hamburger (Drawer mode);
    // a desktop frame never renders it and a phone in BottomNavigation mode has no drawer at all.
    private bool _navOverlayOpen;

    // The phone work-area sheet was dismissed by the user while the host still says WorkAreaOpen: the
    // sheet must be closable even when the host does not bind WorkAreaOpenChanged (a modal surface that
    // cannot close traps the user), and the host's own flag stays its decision.
    private bool _workAreaDismissed;
    private bool _workAreaOpenSeen;

    // The phone bottom bar's entries. They are derived from the navigation model unless the host
    // supplies them, and re-derived only when the model's content changes (a fresh list every render
    // would break the bar's reference-identity selection).
    private IReadOnlyList<BottomNavItem> _bottomItems = Array.Empty<BottomNavItem>();
    private string? _bottomSignature;
    private int _bottomActiveIndex = -1;
    private int _bottomSelectedIndex = -1;

    private string? _loadedKey;
    private bool _focusContentAfterClose;
    private bool _disposed;

    // ── Derived state ───────────────────────────────────────────────────────

    private static readonly TmLayoutBreakpoints DefaultBreakpoints = new(TmBreakpoints.Md, 1200);

    /// <summary>The thresholds this shell classifies its own width with (768 / 1200 unless the host overrides them).</summary>
    private TmLayoutBreakpoints EffectiveBreakpoints => Breakpoints ?? DefaultBreakpoints;

    /// <summary>The layout in effect right now (never <see cref="TmLayoutMode.Auto"/>).</summary>
    private TmLayoutMode Resolved => TmLayout.Resolve(LayoutMode, Parent, _measured, InitialMode);

    /// <summary>A tablet container renders the navigation as a rail plus an overlay drawer.</summary>
    private bool IsRail => Resolved == TmLayoutMode.Tablet;

    /// <summary>A phone container renders no navigation column at all: its navigation is a drawer or a bottom bar.</summary>
    private bool IsMobile => Resolved == TmLayoutMode.Mobile;

    /// <summary>
    /// The full navigation opens in a modal drawer: the tablet rail's presentation and the phone's
    /// <see cref="AppShellMobileNav.Drawer"/> mode (the default). A phone in
    /// <see cref="AppShellMobileNav.BottomNavigation"/> mode has no drawer — the bar IS the navigation.
    /// </summary>
    private bool ShowMobileDrawer => IsMobile && MobileNav == AppShellMobileNav.Drawer;

    /// <summary>Whether a navigation surface (the drawer) exists at all on this layout.</summary>
    private bool HasNavOverlay => HasNavModel && (ShowMobileDrawer || IsRail);

    /// <summary>
    /// Whether the top bar carries the hamburger. A phone drawer needs an opener the bar does not have
    /// by itself; the tablet's opener is the rail toggle, and a desktop column opens nothing.
    /// </summary>
    private bool RendersMenuButton => ShowMobileDrawer && HasNavModel && ShowTopBarMenuButton != false;

    /// <summary>The bottom bar renders on a phone in bottom-navigation mode, and only with entries to show.</summary>
    private bool ShowBottomNav => IsMobile
        && MobileNav == AppShellMobileNav.BottomNavigation
        && EffectiveBottomNavItems.Count > 0;

    private string CssVariant => Variant == AppShellVariant.Flat ? "flat" : "inset";

    private string RootClasses => string.Join(" ", new[] { "tm-app-shell", Class }.Where(static c => !string.IsNullOrWhiteSpace(c)));

    private Dictionary<string, object> ShellAttributes
    {
        get
        {
            var attributes = new Dictionary<string, object>
            {
                ["data-testid"] = RootTestId("app-shell")!,
                ["data-variant"] = CssVariant,
            };
            if (AdditionalAttributes is not null)
            {
                foreach (var pair in AdditionalAttributes) attributes[pair.Key] = pair.Value;
            }
            return attributes;
        }
    }

    /// <summary>The skip link's target and the <c>main</c> id: the host's id when set, a unique one otherwise.</summary>
    private string MainId => string.IsNullOrWhiteSpace(SkipLinkTarget) ? $"tm-app-shell-{_instanceId}-main" : SkipLinkTarget!;

    private string NavigationName => string.IsNullOrWhiteSpace(SidebarNavLabel) ? Loc["TmAppShell_NavLabel"] : SidebarNavLabel!;

    private string WorkAreaName => string.IsNullOrWhiteSpace(WorkAreaLabel) ? Loc["TmAppShell_WorkArea"] : WorkAreaLabel!;

    private bool HasNavModel => Sidebar is not null || NavSections is { Count: > 0 } || NavItems is { Count: > 0 };

    /// <summary>
    /// The flat list the built-in sidebar renders: empty when the host supplies sections, because
    /// <see cref="TmAppShell.NavSections"/> takes precedence over <see cref="TmAppShell.NavItems"/> — a
    /// shell shows ONE navigation model, never the same entries twice.
    /// </summary>
    private IReadOnlyList<ISidebarNavItem> EffectiveNavItems
        => NavSections is { Count: > 0 } ? Array.Empty<ISidebarNavItem>() : NavItems ?? Array.Empty<ISidebarNavItem>();

    private bool HasTopBar => TopBar is not null
        || !string.IsNullOrWhiteSpace(Title)
        || TopBarActions is not null
        || User is not null
        // A phone frame with navigation but no bar content still needs a bar: that is where the
        // hamburger lives. Without it the phone would have no way to reach the navigation at all.
        || RendersMenuButton;

    private bool HasWorkAreaContent => WorkArea is not null;

    /// <summary>
    /// The work area is a docked, resizable column on a desktop or tablet layout. A phone layout has no
    /// room for a dragged column, so the same content is presented as a <c>TmSidePanel</c> bottom sheet
    /// instead (and a phone renders no separator).
    /// </summary>
    private bool ShowWorkArea => HasWorkAreaContent && WorkAreaOpen && !IsMobile;

    /// <summary>
    /// Whether the phone work-area sheet is on screen: the host says the region is open and the user has
    /// not dismissed it. The sheet is mounted even while closed (it renders nothing then), so the
    /// presentation parameters stay stable across an open/close cycle.
    /// </summary>
    private bool ShowWorkAreaSheet => HasWorkAreaContent && IsMobile && WorkAreaOpen && !_workAreaDismissed;

    private int EffectiveMinWidth => Math.Max(1, WorkAreaMinWidth);

    private int EffectiveMaxWidth => Math.Max(EffectiveMinWidth, WorkAreaMaxWidth);

    /// <summary>The width the work area renders at: the user's overlay, else the host width, bounded by the configured limits.</summary>
    private int WorkAreaWidthPx => Math.Clamp(_userWidth ?? WorkAreaWidth, EffectiveMinWidth, EffectiveMaxWidth);

    /// <summary>
    /// The widest the user can actually make the work area: the configured maximum, but never beyond
    /// what the content can give up while keeping <see cref="MinContentWidth"/> (measured after
    /// render). Never below the width in effect, so <c>aria-valuemax</c> stays inside the announced
    /// range.
    /// </summary>
    private int ReachableMaxWidth
    {
        get
        {
            if (double.IsNaN(_contentWidth)) return EffectiveMaxWidth;
            var room = Math.Max(0, (int)Math.Floor(_contentWidth) - MinContentWidth);
            var reachable = Math.Min(EffectiveMaxWidth, WorkAreaWidthPx + room);
            return Math.Max(reachable, Math.Min(EffectiveMaxWidth, WorkAreaWidthPx));
        }
    }

    /// <summary>The collapsed state in effect on a desktop layout (a rail is always collapsed).</summary>
    private bool CollapsedInEffect => IsRail || _collapsedShadow;

    private const string WorkAreaWidthVar = "--tm-app-shell-work-area-width";
    private const string WorkAreaCommitMethod = "OnWorkAreaResizeCommitted";

    private string ResizerId => $"{_instanceId}-work-area";

    // ── Lifecycle ───────────────────────────────────────────────────────────

    /// <summary>
    /// Subscribes to the router: a navigation that started INSIDE the phone drawer has to close it, and
    /// the router is the only reliable signal — the frame cannot tell from a bubbled click whether the
    /// target was a destination, the account area in the footer or a host slot control.
    /// </summary>
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Navigation.LocationChanged += HandleLocationChanged;
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (WorkAreaMinWidth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(WorkAreaMinWidth), WorkAreaMinWidth,
                "WorkAreaMinWidth must be at least 1 CSS pixel.");
        }
        if (WorkAreaMaxWidth < WorkAreaMinWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(WorkAreaMaxWidth), WorkAreaMaxWidth,
                "WorkAreaMaxWidth must not be smaller than WorkAreaMinWidth.");
        }
        if (MinContentWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinContentWidth), MinContentWidth,
                "MinContentWidth must not be negative.");
        }

        var resolved = Resolved;
        if (_resolvedInitialised && resolved != _resolved)
        {
            // The navigation overlay belongs to the rail and phone presentations: leaving them for a
            // desktop layout closes it, so a desktop frame never shows a drawer over its docked column.
            // Leaving the PHONE also forgets a sheet the user dismissed — the host's open flag decides
            // again the next time the frame is that narrow.
            if (resolved == TmLayoutMode.Desktop) _navOverlayOpen = false;
            if (resolved != TmLayoutMode.Mobile) _workAreaDismissed = false;
        }
        _resolved = resolved;
        _resolvedInitialised = true;

        // A phone in bottom-navigation mode has no drawer at all: a stale open flag from the drawer
        // mode must not resurrect one when the host switches modes.
        if (IsMobile && MobileNav != AppShellMobileNav.Drawer) _navOverlayOpen = false;

        // The host re-opened the region: a sheet the user once dismissed may show again.
        if (WorkAreaOpen != _workAreaOpenSeen)
        {
            _workAreaOpenSeen = WorkAreaOpen;
            _workAreaDismissed = false;
        }

        // The bottom bar's entries changed (a new model, in-place edits): rebuild them and hand the
        // current entry back to the model instead of remembering a tap that may name nothing now.
        SyncBottomNav();

        // The collapse shadow adopts a host value only when the host CHANGED it: a host that ignores
        // the callback must not have its stale value re-imposed over what the user just did.
        if (!_collapsedInitialised)
        {
            _collapsedInitialised = true;
            _collapsedShadow = SidebarCollapsed;
            _collapsedParamSeen = SidebarCollapsed;
        }
        else if (SidebarCollapsed != _collapsedParamSeen)
        {
            _collapsedParamSeen = SidebarCollapsed;
            // A host value that is not the echo of the user's own toggle is a host decision: the user's
            // choice no longer describes the layout, so it stops being persisted until they toggle again.
            if (SidebarCollapsed != _collapsedShadow)
            {
                _collapsedTouched = false;
                _collapsedChosen = false;
            }
            _collapsedShadow = SidebarCollapsed;
        }

        // A width the host supplies that is not the echo of the user's own choice is a host override:
        // the user's overlay is dropped (and the stored entry removed) so the host width wins until
        // the user resizes again.
        var persist = false;
        if (!_widthInitialised)
        {
            _widthInitialised = true;
            _widthParamSeen = WorkAreaWidth;
        }
        else if (WorkAreaWidth != _widthParamSeen)
        {
            _widthParamSeen = WorkAreaWidth;
            if (_userWidth is { } userWidth && WorkAreaWidth != userWidth)
            {
                _userWidth = null;
                _userBase = null;
                persist = true;
            }
        }

        if (persist) await PersistAsync();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed) return;
        await RestoreContentFocusAsync();
        await LoadPersistedAsync();
        await SyncResizerAsync();
        await SyncContentWidthAsync();
    }

    /// <summary>
    /// A work area closed by its own close button takes the focused control with it (the button lived
    /// inside the region). The focus moves to the content region — the part of the frame that stays —
    /// so a keyboard user is never dropped back to the document body.
    /// </summary>
    private async Task RestoreContentFocusAsync()
    {
        if (!_focusContentAfterClose || ShowWorkArea) return;
        _focusContentAfterClose = false;
        try { await _content.FocusAsync(); }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException) { /* the circuit or the element is gone */ }
    }

    /// <summary>
    /// The layout observer's report: remember the measurement (the shell resolves its mode from it) and
    /// forward the change to the host. A frame that leaves the rail presentation closes its navigation
    /// overlay, so a desktop layout never shows a drawer over its docked column.
    /// </summary>
    /// <param name="mode">The resolved layout.</param>
    private async Task OnResolvedLayoutChanged(TmLayoutMode mode)
    {
        _measured = mode;
        if (mode == TmLayoutMode.Desktop) _navOverlayOpen = false;
        await ResolvedLayoutChanged.InvokeAsync(mode);
    }

    // ── Navigation handlers ─────────────────────────────────────────────────

    /// <summary>
    /// The navigation toggle: on a desktop layout it collapses or expands the docked column (two-way
    /// through <see cref="SidebarCollapsedChanged"/>); on the rail presentation it opens the overlay
    /// drawer, which has no width of its own to collapse.
    /// </summary>
    private async Task OnSidebarToggleAsync()
    {
        if (IsRail)
        {
            OpenNavOverlay();
            return;
        }

        var next = !_collapsedShadow;
        _collapsedShadow = next;
        _collapsedTouched = true;
        _collapsedChosen = true;
        await SidebarCollapsedChanged.InvokeAsync(next);
        await PersistAsync();
        StateHasChanged();
    }

    private void OpenNavOverlay()
    {
        if (_disposed || _navOverlayOpen || !HasNavOverlay) return;
        _navOverlayOpen = true;
        StateHasChanged();
    }

    /// <summary>
    /// Opens the navigation drawer — the surface a tablet rail toggle and a phone hamburger open. A
    /// host that carries its OWN opener (a custom <see cref="TopBar"/> with
    /// <see cref="ShowTopBarMenuButton"/> = <c>false</c>) drives the drawer through this method, so the
    /// navigation is never unreachable. It is a no-op on a desktop layout (the docked column is always
    /// visible), in <see cref="AppShellMobileNav.BottomNavigation"/> mode (the bar is the navigation)
    /// and while the drawer is already open.
    /// </summary>
    /// <returns>A task that completes once the open has been applied.</returns>
    public Task OpenNavigationAsync() => InvokeAsync(OpenNavOverlay);

    /// <summary>
    /// Closes the navigation drawer, whichever layout opened it. The focus scope inside the drawer
    /// hands the focus back to the opener. A no-op while the drawer is already closed.
    /// </summary>
    /// <returns>A task that completes once the close has been applied.</returns>
    public Task CloseNavigationAsync() => InvokeAsync(() =>
    {
        if (_disposed || !_navOverlayOpen) return;
        _navOverlayOpen = false;
        StateHasChanged();
    });

    private Task OnNavOverlayOpenChanged(bool open)
    {
        if (_navOverlayOpen == open) return Task.CompletedTask;
        // Closing unmounts the drawer, and the focus scope inside it hands the focus back to the
        // element the user opened it from (the rail toggle).
        _navOverlayOpen = open;
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>
    /// A destination CHOSEN inside the phone drawer closes the drawer it was chosen from: the user asked
    /// for a different page, and closing is what keeps the page usable right after the tap. Raised by the
    /// built-in sidebar (<see cref="TmSidebar.OnItemSelected"/>), so a tap on the workspace switcher in the
    /// header, the account in the footer, a section heading or any host slot content leaves the drawer open
    /// — none of those is a destination. It also covers choosing the page the user is already on, which
    /// raises no location change of its own.
    /// </summary>
    /// <param name="item">The entry the user chose.</param>
    private Task OnNavItemSelectedAsync(ISidebarNavItem item)
    {
        _ = item;
        return ClosePhoneDrawerAsync();
    }

    /// <summary>
    /// The location changed: a link inside the phone drawer navigated (a host <see cref="Sidebar"/> slot's
    /// own link, or a built-in entry whose click was already handled), so the surface it was chosen from
    /// stops being useful. Phone-only by design — the tablet overlay keeps its phase-3 behaviour (Escape,
    /// its close button, or the layout leaving the rail) and its slot content may host controls that must
    /// not dismiss it.
    /// </summary>
    private void HandleLocationChanged(object? sender, LocationChangedEventArgs args)
        => _ = InvokeAsync(ClosePhoneDrawerAsync);

    /// <summary>
    /// Closes the PHONE navigation drawer if it is open. Shared by the two signals that mean "a
    /// destination was chosen": the sidebar's own report and a router location change.
    /// </summary>
    private Task ClosePhoneDrawerAsync()
    {
        if (_disposed || !ShowMobileDrawer || !_navOverlayOpen) return Task.CompletedTask;
        _navOverlayOpen = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    // ── Phone bottom navigation ─────────────────────────────────────────────

    /// <summary>
    /// The entries the phone bottom bar renders. The host's list wins; otherwise one entry per top-level
    /// item of the navigation model (<see cref="NavSections"/> in order, or <see cref="NavItems"/>),
    /// which is what makes <see cref="AppShellMobileNav.BottomNavigation"/> work without a second model.
    /// </summary>
    private IReadOnlyList<BottomNavItem> EffectiveBottomNavItems => _bottomItems;

    /// <summary>
    /// The bar's current entry: the one the user tapped, else the navigation model's current entry, else
    /// none. The bar compares entries BY REFERENCE, so this hands back an instance out of the very list
    /// it renders — a rebuilt list would highlight nothing.
    /// </summary>
    private BottomNavItem? SelectedBottomNavItem
    {
        get
        {
            var index = _bottomSelectedIndex >= 0 ? _bottomSelectedIndex : _bottomActiveIndex;
            return index >= 0 && index < _bottomItems.Count ? _bottomItems[index] : null;
        }
    }

    /// <summary>
    /// Rebuilds the bottom bar's entries when — and only when — the model's CONTENT changed, so an
    /// in-place edit is picked up while an unchanged model keeps the same list (and therefore the
    /// user's selection). A changed model returns the current entry to the model's own active flag.
    /// </summary>
    private void SyncBottomNav()
    {
        var signature = BottomNavSignature();
        if (string.Equals(signature, _bottomSignature, StringComparison.Ordinal)) return;

        _bottomSignature = signature;
        _bottomSelectedIndex = -1;

        if (BottomNavItems is { Count: > 0 })
        {
            _bottomItems = BottomNavItems;
            _bottomActiveIndex = -1;
            return;
        }

        var items = new List<BottomNavItem>();
        var active = -1;
        foreach (var item in NavigationModelTopLevel())
        {
            if (string.IsNullOrWhiteSpace(item.Label)) continue;
            if (active < 0 && item.IsActive) active = items.Count;
            items.Add(new BottomNavItem { Text = item.Label, Icon = item.Icon, Href = item.Href });
        }

        _bottomItems = items;
        _bottomActiveIndex = active;
    }

    private IEnumerable<ISidebarNavItem> NavigationModelTopLevel()
        => NavSections is { Count: > 0 }
            ? NavSections.SelectMany(section => section.Items ?? Array.Empty<ISidebarNavItem>())
            : NavItems ?? Array.Empty<ISidebarNavItem>();

    /// <summary>
    /// A cheap value signature of everything the bar renders, so the entries survive an unchanged
    /// re-render and follow an edited model. Identity is deliberately not used: a host page may rebuild
    /// its item list on every render.
    /// </summary>
    private string BottomNavSignature()
    {
        var builder = new StringBuilder();
        if (BottomNavItems is { Count: > 0 } hostItems)
        {
            builder.Append("host:");
            foreach (var item in hostItems) AppendBarEntry(builder, item.Text, item.Icon, item.Href, item.Disabled, false);
            return builder.ToString();
        }

        builder.Append("model:");
        foreach (var item in NavigationModelTopLevel())
            AppendBarEntry(builder, item.Label, item.Icon, item.Href, disabled: false, active: item.IsActive);
        return builder.ToString();

        static void AppendBarEntry(StringBuilder builder, string? text, string? icon, string? href, bool disabled, bool active)
            => builder.Append(text).Append('|').Append(icon).Append('|').Append(href)
                .Append('|').Append(disabled ? '1' : '0').Append('|').Append(active ? '1' : '0').Append('\n');
    }

    /// <summary>
    /// A tap on a bottom-bar entry: the tapped entry becomes the current one, the host hears about it,
    /// and — because a bar of links that never navigates is dead — the shell follows the entry's
    /// <see cref="BottomNavItem.Href"/>. An entry without one is a command: the callback is the whole
    /// behaviour.
    /// </summary>
    /// <param name="item">The activated entry, as it was rendered.</param>
    private async Task OnBottomNavItemClickAsync(BottomNavItem item)
    {
        for (var index = 0; index < _bottomItems.Count; index++)
        {
            if (!ReferenceEquals(_bottomItems[index], item)) continue;
            _bottomSelectedIndex = index;
            break;
        }

        await BottomNavItemClicked.InvokeAsync(item);
        if (!string.IsNullOrWhiteSpace(item.Href)) Navigation.NavigateTo(item.Href);
    }

    // ── Work area ───────────────────────────────────────────────────────────

    /// <summary>The keyboard half of the separator (the pointer half is the shared module).</summary>
    private async Task OnSeparatorKeyAsync(KeyboardEventArgs args)
    {
        if (_disposed) return;
        var step = args.ShiftKey ? 64 : 16;
        var current = WorkAreaWidthPx;
        // The work area is the last column: its separator sits on its left edge, so ArrowLeft grows it.
        var requested = args.Key switch
        {
            "ArrowLeft" => current + step,
            "ArrowRight" => current - step,
            "Home" => EffectiveMinWidth,
            "End" => EffectiveMaxWidth,
            _ => (double?)null,
        };
        if (requested is not { } value) return;

        var content = await MeasureContentAsync();
        var width = (int)PanelResize.Clamp(value, current, EffectiveMinWidth, EffectiveMaxWidth, content, MinContentWidth);
        await CommitWorkAreaWidthAsync(width);
    }

    /// <summary>
    /// Commits a width the shared pointer-drag resize module settled on. Called from JS, so it is
    /// clamped and validated again here.
    /// </summary>
    /// <param name="side"><c>left</c> or <c>right</c>; only <c>right</c> is a work area.</param>
    /// <param name="width">The width in CSS pixels.</param>
    [JSInvokable("OnWorkAreaResizeCommitted")]
    public Task HandleWorkAreaResizeCommitted(string side, int width)
    {
        if (_disposed || side != "right") return Task.CompletedTask;
        return InvokeAsync(() => CommitWorkAreaWidthAsync(width));
    }

    private async Task CommitWorkAreaWidthAsync(int width)
    {
        if (_disposed) return;
        var clamped = Math.Clamp(width, EffectiveMinWidth, EffectiveMaxWidth);
        if (clamped == WorkAreaWidthPx) return;

        _userBase ??= WorkAreaWidth;
        _userWidth = clamped == WorkAreaWidth ? null : clamped;
        if (_userWidth is null) _userBase = null;

        await WorkAreaWidthChanged.InvokeAsync(clamped);
        await PersistAsync();
        StateHasChanged();
    }

    /// <summary>The double-click reset (the non-drag alternative to dragging back): the host width again.</summary>
    private async Task ResetWorkAreaWidthAsync()
    {
        if (_disposed) return;
        await CommitWorkAreaWidthAsync(_userBase ?? WorkAreaWidth);
    }

    /// <summary>
    /// The region's own close button: the host owns the flag, the shell reports the close — and moves the
    /// focus to the content region once the region is gone (the button that had it was inside it).
    /// </summary>
    private async Task CloseWorkAreaAsync()
    {
        if (!WorkAreaOpen) return;
        _focusContentAfterClose = true;
        await WorkAreaOpenChanged.InvokeAsync(false);
    }

    /// <summary>
    /// The phone work-area SHEET closed (its close button, Escape, the backdrop or a swipe — every path
    /// funnels through the panel's open callback). The sheet stops showing in any case: it is a modal
    /// surface, and one whose close does nothing traps the user, so a host that never bound
    /// <see cref="WorkAreaOpenChanged"/> still gets a dismissable sheet. The host hears about the close
    /// when it listens, and its own flag decides again the next time it changes.
    /// </summary>
    /// <param name="open">Always <c>false</c>: the sheet never opens itself.</param>
    private async Task OnWorkAreaSheetOpenChangedAsync(bool open)
    {
        // The sheet only ever reports a close — it is opened by the host's flag, never by itself.
        if (!open) _workAreaDismissed = true;
        StateHasChanged();
        if (WorkAreaOpenChanged.HasDelegate) await WorkAreaOpenChanged.InvokeAsync(open);
    }

    /// <summary>
    /// The content width the resize must protect: the last measured one (so the keyboard steps from
    /// the geometry the separator announced), measured on demand when no render has reported yet.
    /// </summary>
    private async Task<double> MeasureContentAsync()
    {
        if (!double.IsNaN(_contentWidth)) return _contentWidth;
        if (_disposed) return double.NaN;
        _resizeJs ??= new PanelResizeInterop(JS);
        return await _resizeJs.MeasureRegionAsync(_content);
    }

    // ── Resize wiring ───────────────────────────────────────────────────────

    /// <summary>
    /// Keeps the pointer-drag registration in step with what is rendered: attached while a work area
    /// is docked, re-attached only when its clamp inputs change (a re-render must never tear down a
    /// drag in progress) and detached when the region closes, the layout turns mobile or the
    /// component is disposed.
    /// </summary>
    private async Task SyncResizerAsync()
    {
        if (_disposed) return;
        if (!ShowWorkArea)
        {
            _contentWidth = double.NaN;
            if (!_resizerAttached) return;
            _resizerAttached = false;
            _resizerKey = null;
            if (_resizeJs is not null) await _resizeJs.DetachResizeAsync(ResizerId);
            return;
        }

        var key = $"{EffectiveMinWidth}:{EffectiveMaxWidth}:{MinContentWidth}";
        if (_resizerAttached && _resizerKey == key) return;

        _resizeJs ??= new PanelResizeInterop(JS);
        _dotNetRef ??= DotNetObjectReference.Create(this);
        _resizerKey = key;
        _resizerAttached = true;
        await _resizeJs.AttachResizeAsync(ResizerId, _resizer, _workArea, _content, _dotNetRef,
            WorkAreaWidthVar, "right", EffectiveMinWidth, EffectiveMaxWidth, MinContentWidth, WorkAreaCommitMethod);
    }

    /// <summary>
    /// Measures the content region after each render while the work area is docked, so the separator
    /// announces the width the user can really reach. Re-renders only when the measurement changed.
    /// </summary>
    private async Task SyncContentWidthAsync()
    {
        if (_disposed || !ShowWorkArea) return;
        _resizeJs ??= new PanelResizeInterop(JS);
        var measured = await _resizeJs.MeasureRegionAsync(_content);
        if (_disposed || measured.Equals(_contentWidth)) return;
        _contentWidth = measured;
        StateHasChanged();
    }

    // ── Persistence ─────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the stored layout when a key appears (or changes): the work-area width only while the
    /// host still supplies the base width it was resized from, inside the current limits, and the
    /// collapsed state when the user has not toggled it yet. A restore counts as a user value, so it
    /// is reported through the two-way callbacks.
    /// </summary>
    private async Task LoadPersistedAsync()
    {
        if (_disposed) return;
        if (string.IsNullOrEmpty(PersistKey))
        {
            _loadedKey = null;
            return;
        }
        if (_loadedKey == PersistKey) return;
        _loadedKey = PersistKey;

        _resizeJs ??= new PanelResizeInterop(JS);
        var stored = await _resizeJs.LoadAsync(AppShellLayoutState.StoragePrefix, PersistKey);
        if (_disposed || _loadedKey != PersistKey) return;

        var (workArea, collapsed) = AppShellLayoutState.Parse(stored);
        int? restoredWidth = null;
        bool? restoredCollapsed = null;

        if (_userWidth is null && workArea is not null
            && workArea.Base == $"{WorkAreaWidth}px"
            && workArea.W >= EffectiveMinWidth && workArea.W <= EffectiveMaxWidth)
        {
            _userWidth = workArea.W;
            _userBase = WorkAreaWidth;
            restoredWidth = workArea.W;
        }
        // A choice the user already made in this session outranks a slow restore.
        if (!_collapsedTouched && collapsed is { } storedCollapsed && storedCollapsed != _collapsedShadow)
        {
            _collapsedShadow = storedCollapsed;
            // A restored value is the user's own earlier choice, so a later resize keeps persisting it.
            _collapsedChosen = true;
            restoredCollapsed = storedCollapsed;
        }

        if (restoredWidth is null && restoredCollapsed is null) return;

        StateHasChanged();
        if (restoredWidth is { } width) await WorkAreaWidthChanged.InvokeAsync(width);
        if (restoredCollapsed is { } collapsedValue) await SidebarCollapsedChanged.InvokeAsync(collapsedValue);
    }

    /// <summary>
    /// Stores the user's choices: only the work-area width the user resized (with the host width it
    /// was resized from) and the collapsed state the user picked. No key, no storage — and a host that
    /// never binds either keeps today's behaviour.
    /// </summary>
    private async Task PersistAsync()
    {
        if (string.IsNullOrEmpty(PersistKey) || _disposed) return;
        _resizeJs ??= new PanelResizeInterop(JS);
        var workArea = _userWidth is { } width
            ? new StoredWidth(width, $"{_userBase ?? WorkAreaWidth}px")
            : null;
        // Persist the user's INTENT, never the parameter diff: a host that binds SidebarCollapsed writes
        // the value straight back into the parameter, so the two are equal exactly when the host is
        // well-behaved — comparing them would drop the choice of every bound host. The persisted
        // collapse is still the USER's desktop choice, never the rail the tablet presentation imposes: a
        // resize on a tablet must not silently store "collapsed" for the next desktop load.
        var collapsed = _collapsedChosen ? _collapsedShadow : (bool?)null;
        await _resizeJs.SaveAsync(AppShellLayoutState.StoragePrefix, PersistKey,
            AppShellLayoutState.Serialize(workArea, collapsed));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Navigation.LocationChanged -= HandleLocationChanged;
        if (_resizeJs is not null) await _resizeJs.DisposeAsync();
        _dotNetRef?.Dispose();
        _dotNetRef = null;
    }
}
