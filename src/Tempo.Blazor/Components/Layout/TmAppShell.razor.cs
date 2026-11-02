using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Helpers;
using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Components.Layout;

// The state, the resize wiring and the persistence half of the application shell. It lives beside the
// markup file to keep the .razor under the source-file length ceiling; the class is one type.

/// <summary>
/// The application frame: a top bar, one named navigation column, the page content and an optional
/// resizable work area, resolved from the shell's own container width. Desktop renders the docked
/// column; a tablet container renders an icon rail whose full navigation opens in a modal overlay
/// drawer. It composes <c>TmLayoutObserver</c>, <c>TmSidebar</c>, <c>TmTopBar</c> and <c>TmDrawer</c>
/// instead of reimplementing navigation, focus or sheet behaviour, and it owns the page's
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

    /// <summary>Creates the shell and its per-instance DOM ids (one prefix, unique per instance).</summary>
    public TmAppShell()
    {
        _workAreaId = $"tm-app-shell-{_instanceId}-work-area";
        _workAreaTitleId = $"tm-app-shell-{_instanceId}-work-area-title";
        _overlayId = $"tm-app-shell-{_instanceId}-nav-overlay";
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

    // The tablet rail's overlay drawer. A layout that is not desktop always renders the rail.
    private bool _navOverlayOpen;

    private string? _loadedKey;
    private bool _focusContentAfterClose;
    private bool _disposed;

    // ── Derived state ───────────────────────────────────────────────────────

    private static readonly TmLayoutBreakpoints DefaultBreakpoints = new(TmBreakpoints.Md, 1200);

    /// <summary>The thresholds this shell classifies its own width with (768 / 1200 unless the host overrides them).</summary>
    private TmLayoutBreakpoints EffectiveBreakpoints => Breakpoints ?? DefaultBreakpoints;

    /// <summary>The layout in effect right now (never <see cref="TmLayoutMode.Auto"/>).</summary>
    private TmLayoutMode Resolved => TmLayout.Resolve(LayoutMode, Parent, _measured, InitialMode);

    /// <summary>A tablet or phone container renders the navigation as a rail plus an overlay drawer.</summary>
    private bool IsRail => Resolved != TmLayoutMode.Desktop;

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

    private bool HasTopBar => TopBar is not null || !string.IsNullOrWhiteSpace(Title) || TopBarActions is not null || User is not null;

    private bool HasWorkAreaContent => WorkArea is not null;

    /// <summary>
    /// The work area is a docked column on a desktop or tablet layout. A phone layout has no room for
    /// a dragged column (the plan's mobile phase presents it as a sheet).
    /// </summary>
    private bool ShowWorkArea => HasWorkAreaContent && WorkAreaOpen && Resolved != TmLayoutMode.Mobile;

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
            // The navigation overlay belongs to the rail presentation: leaving it closes it, so a
            // desktop frame never shows a drawer over its docked column.
            if (resolved == TmLayoutMode.Desktop) _navOverlayOpen = false;
        }
        _resolved = resolved;
        _resolvedInitialised = true;

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
        if (_disposed || _navOverlayOpen) return;
        _navOverlayOpen = true;
        StateHasChanged();
    }

    private Task OnNavOverlayOpenChanged(bool open)
    {
        if (_navOverlayOpen == open) return Task.CompletedTask;
        // Closing unmounts the drawer, and the focus scope inside it hands the focus back to the
        // element the user opened it from (the rail toggle).
        _navOverlayOpen = open;
        StateHasChanged();
        return Task.CompletedTask;
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
        if (_resizeJs is not null) await _resizeJs.DisposeAsync();
        _dotNetRef?.Dispose();
        _dotNetRef = null;
    }
}
