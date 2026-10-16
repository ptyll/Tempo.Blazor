using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Helpers;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Dashboard;

/// <summary>
/// Code-behind of <see cref="TmDashboard"/> (F2): the chrome — header, switcher, widget card —
/// renders in TmDashboard.razor; the layout state, the dashboard CRUD, the grid interop and the
/// focus hand-offs live here as the other half of the partial component.
/// </summary>
public partial class TmDashboard : IAsyncDisposable
{
    /// <summary>Id of the dashboard to load initially. When empty, the provider's default dashboard loads.</summary>
    [Parameter] public string? DashboardId { get; set; }

    /// <summary>Owner id passed through to <see cref="IDashboardProvider"/>; null means the provider's default scope.</summary>
    [Parameter] public string? UserId { get; set; }

    /// <summary>Additional CSS classes for the dashboard root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Chrome of the root surface. <see cref="DashboardAppearance.Plain"/> (default) paints the
    /// dashboard directly on the workspace background — the widget cards are the only surfaces;
    /// <see cref="DashboardAppearance.Card"/> keeps the pre-2.9.1 wrapping surface card
    /// (<c>.tm-dashboard--framed</c>) for hosts that embed the dashboard on a contrasting page.
    /// </summary>
    [Parameter] public DashboardAppearance Appearance { get; set; } = DashboardAppearance.Plain;

    /// <summary>
    /// Heading text in view and edit mode, overriding the current dashboard's name. Null keeps
    /// the dashboard name. Without a <see cref="Title"/> (and with a provider) the heading is
    /// visually hidden in view mode, because the switcher trigger already shows the name.
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Secondary line under the heading in view mode. Empty hides it.</summary>
    [Parameter] public string? Subtitle { get; set; }

    /// <summary>
    /// Host content at the start of the header, before the heading and the dashboard switcher
    /// (breadcrumbs, back link, page-level controls).
    /// </summary>
    [Parameter] public RenderFragment? HeaderStart { get; set; }

    /// <summary>
    /// Host actions at the end of the view-mode header, rendered BEFORE the Edit button so the
    /// DOM order matches the visual (and tab) order. Not rendered in edit mode.
    /// </summary>
    [Parameter] public RenderFragment? HeaderActions { get; set; }

    /// <summary>
    /// Whether the header toolbar renders in view mode. Edit mode always renders the toolbar,
    /// because it is the only surface carrying Save/Cancel. Defaults to true.
    /// </summary>
    [Parameter] public bool ShowHeader { get; set; } = true;

    /// <summary>
    /// Whether the widget header renders the 40px category icon tile from the widget definition.
    /// Defaults to true.
    /// </summary>
    [Parameter] public bool ShowWidgetIcons { get; set; } = true;

    /// <summary>
    /// Whether the widget header renders the definition's <see cref="WidgetDefinition.Description"/>
    /// as a secondary line under the title. Defaults to false.
    /// </summary>
    [Parameter] public bool ShowWidgetDescriptions { get; set; }

    /// <summary>
    /// Host slot in every widget header (a badge, a "view all" link, a period select), receiving
    /// the <see cref="WidgetInstance"/>. Renders before the collapse chevron in view mode only —
    /// edit mode owns the header — and only when set; the host owns its content's accessible names.
    /// </summary>
    [Parameter] public RenderFragment<WidgetInstance>? WidgetHeaderActions { get; set; }

    /// <summary>Whether the dashboard title renders in the toolbar. Defaults to true.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>Whether the user may enter edit mode (move, resize, add or remove widgets). Defaults to true.</summary>
    [Parameter] public bool AllowEdit { get; set; } = true;

    /// <summary>
    /// Raised after a different dashboard becomes current: the initial load (the default dashboard
    /// or <see cref="DashboardId"/>) and a switcher selection. A dashboard created through the
    /// create dialog does NOT raise it, and neither does the cancel-restore that discards such an
    /// unsaved creation (DA3) — both stay client-side until "Save changes" commits the dashboard,
    /// at which point the saved dashboard is reported. In other words, the event only ever names a
    /// dashboard the provider knows about.
    /// </summary>
    [Parameter] public EventCallback<DashboardConfig> OnDashboardChanged { get; set; }

    /// <summary>
    /// Host template for a widget's content. Receives the <see cref="WidgetInstance"/>; without it the
    /// dashboard renders a localized placeholder naming the widget.
    /// </summary>
    [Parameter] public RenderFragment<WidgetInstance>? WidgetTemplate { get; set; }

    /// <summary>
    /// The layout the dashboard renders. <see cref="TmLayoutMode.Auto"/> measures the dashboard's own
    /// width; a forced value never measures, so a test can render a mode without a DOM. The grid
    /// follows the resolved mode (12 columns on desktop, six on tablet, one on mobile), and the
    /// two-column step inside tablet follows the container.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>
    /// The layout rendered before the first measurement. Desktop unless the host says otherwise,
    /// forwarded to the observer so an owner can start from tablet or mobile.
    /// </summary>
    [Parameter] public TmLayoutMode InitialMode { get; set; } = TmLayoutMode.Desktop;

    /// <summary>
    /// Raised when the rendered layout changes. Never pair <see cref="LayoutMode"/> with this
    /// callback: it reports the resolved mode, and a two-way bind would pin that mode.
    /// </summary>
    [Parameter] public EventCallback<TmLayoutMode> ResolvedLayoutChanged { get; set; }

    /// <summary>
    /// Whether selecting a widget in the widget selector closes it. Defaults to <c>true</c> (the
    /// historical behaviour). With <c>false</c> the selector stays open while the host keeps
    /// receiving <see cref="OnWidgetSelected"/>'s additions; the freshly added widget still gets
    /// the highlight and the scroll-into-view once the selector closes (DA1).
    /// </summary>
    [Parameter] public bool CloseOnSelect { get; set; } = true;

    private Task OnResolvedLayoutChanged(TmLayoutMode mode) => ResolvedLayoutChanged.InvokeAsync(mode);

    [Inject] public IDashboardProvider? DashboardProvider { get; set; }
    [Inject] public IWidgetRegistry? WidgetRegistry { get; set; }
    [Inject] public IJSRuntime JS { get; set; } = default!;
    
    private ElementReference _dashboardElement;
    private DotNetObjectReference<TmDashboard>? _dotNetRef;
    private bool _jsInitialized;
    private DashboardConfig? _currentDashboard;
    private List<WidgetInstance> _widgets = [];
    private List<DashboardConfig> _availableDashboards = [];
    private bool _isEditMode;
    private bool _showWidgetSelector;
    private bool _showDashboardMenu;

    // F4 keyboard grab mode: Enter/Space on the drag-handle button lifts the widget, the arrow
    // keys move it one cell, Shift+arrows resize it one step, Enter/Space drops and Escape
    // restores the pre-grab geometry. The mirror of the JS drag, driven entirely from C#.
    private WidgetInstance? _grabbedWidget;
    private (int X, int Y, int Width, int Height)? _grabOriginal;

    // F4 polite live region: every grab, cell move, resize step, drop and cancel is announced.
    private string? _moveAnnouncement;

    // F4 compact edit mode: a freshly added widget is scrolled into view and highlighted briefly.
    private string? _highlightedInstanceId;
    // F4: instance id of the widget added from the selector; cleared in OnAfterRenderAsync after
    // the scroll-into-view interop ran (the element only exists after the re-render).
    private string? _scrollToInstanceId;
    private readonly string _dragInstructionsId = $"tm-dashboard-drag-instructions-{Guid.NewGuid():N}";

    // Dashboard switcher menu: the panel is a TmOverlayPanel, which owns the top-layer popover,
    // the Escape/outside/Tab-out dismissal and the shared menu-keyboard attachment (arrow keys/
    // typeahead). This component hands focus to the first item via OnOpened and restores the
    // trigger focus on a selection close (a selection close does not route through overlay.js).
    private ElementReference _viewsElement;
    private MenuNavInterop? _menuNav;
    private bool _focusViewsButtonAfterClose;
    // F4: cancels the pending highlight-clear timer on disposal so the fire-and-forget
    // continuation never touches a disposed component.
    private readonly CancellationTokenSource _highlightCts = new();

    // Focus hand-offs that land in OnAfterRenderAsync, after the re-render that changed the markup:
    private bool _focusAddWidgetButtonAfterCreate;
    private bool _focusAddWidgetButtonAfterEditModeEntry;
    private bool _focusRenameButtonAfterNameEdit;
    private bool _focusViewEntryAfterEditModeExit;
    private ElementReference _addWidgetButton;
    private ElementReference _renameButton;
    private ElementReference _editButton;

    // F2: the heading carries a stable id (kept for region labelling and host scripting) and the
    // header resolves its two name sources once per render.
    private readonly string _titleElementId = $"tm-dashboard-title-{Guid.NewGuid():N}";

    /// <summary>Heading text: the host Title, else the current dashboard's name, else the localized default.</summary>
    private string HeaderTitle => Title ?? _currentDashboard?.Name ?? Loc["TmDashboard_DefaultName"];

    /// <summary>Switcher text: the current dashboard's name, else the localized default.</summary>
    private string SwitcherName => _currentDashboard?.Name ?? Loc["TmDashboard_DefaultName"];

    /// <summary>Root class of the Appearance parameter: Card wraps the dashboard in a surface card.</summary>
    private string AppearanceClass => Appearance == DashboardAppearance.Card ? "tm-dashboard--framed" : "";

    /// <summary>
    /// Whether the h2 heading renders: the toolbar renders (ShowHeader, or edit mode) and the
    /// heading renders with it (ShowTitle, or edit mode, where it names the rename target). The
    /// root region reads its accessible name from the heading through <c>aria-labelledby</c> when
    /// this is true, and from <see cref="HeaderTitle"/> through <c>aria-label</c> otherwise.
    /// </summary>
    private bool HasHeading => (ShowHeader || _isEditMode) && (ShowTitle || _isEditMode) && !(_isEditMode && _isEditingName);

    /// <summary>
    /// F3 toolbar modifiers, computed in C# (never <c>:has()</c>): <c>tm-dashboard-toolbar--with-heading</c>
    /// when a VISIBLE heading renders in view mode (the sr-only name and <c>ShowTitle=false</c> keep
    /// the single-row header — an empty stacked row would only add row-gap), and
    /// <c>tm-dashboard-toolbar--with-actions</c> when host <see cref="HeaderActions"/> render. Edit
    /// mode emits neither: its header is the desktop flex-wrap row in every layout.
    /// </summary>
    private string ToolbarModifiers
    {
        get
        {
            if (_isEditMode) return "";
            var withHeading = ShowTitle && !(Title is null && DashboardProvider is not null);
            return (withHeading ? "tm-dashboard-toolbar--with-heading" : "")
                   + (withHeading && HeaderActions is not null ? " " : "")
                   + (HeaderActions is not null ? "tm-dashboard-toolbar--with-actions" : "");
        }
    }

    // DA3: a dashboard created in the dialog is unsaved until "Save changes" — cancelling the edit
    // brings the previous dashboard back instead of leaving an empty unnamed dashboard behind.
    private DashboardConfig? _dashboardBeforeCreate;
    private bool _createdNewDashboardPending;

    // Name editing
    private bool _isEditingName;
    private string _editingName = "";
    
    // New dashboard dialog
    private bool _showNewDashboardDialog;
    private ElementReference _dashboardsButton;
    private readonly string _dashboardsButtonId = $"tm-dashboard-views-{Guid.NewGuid():N}";
    private readonly string _newDashboardNameId = $"tm-dashboard-name-{Guid.NewGuid():N}";
    private string _newDashboardName = "";
    
    // Delete dashboard dialog
    private bool _showDeleteDialog;
    private string? _dashboardToDeleteId;
    private string? _dashboardToDeleteName;
    
    private bool _dataLoaded;

    // The edit-mode action items, the provider-error alert state and the mobile rename dialog
    // live in the F7b partial (TmDashboard.Resilience.razor.cs).

    protected override async Task OnInitializedAsync()
    {
        if (_dataLoaded) return;
        await LoadAllAsync();
    }

    // F7b (UX §5.8): the whole first-load sequence lives in one guarded method so the initial
    // load AND the error alert's "Try again" (which re-enters with _currentDashboard still null)
    // share the same protection — a provider that fails twice must never leak the exception to
    // the host. Both the switcher list and the dashboard load run inside the single catch: a
    // retry that succeeds re-populates the list too, so the switcher is never left empty.
    private async Task LoadAllAsync()
    {
        try
        {
            await LoadDashboardsList();

            if (!string.IsNullOrEmpty(DashboardId))
            {
                await LoadDashboard(DashboardId);
            }
            else
            {
                await LoadDefaultDashboard();
            }

            _loadError = false;
        }
        catch (Exception)
        {
            _loadError = true;
        }

        _dataLoaded = true;
    }
    
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            try
            {
                await JS.InvokeVoidAsync("tempoDashboard.init", _dashboardElement, _dotNetRef);
                _jsInitialized = true;
            }
            catch (JSDisconnectedException) { }
            catch (InvalidOperationException) { }
        }

        // F4: a widget freshly added from the selector is brought into view (it may render below
        // the fold on a 390 px layout) before the brief highlight fades.
        if (_scrollToInstanceId is not null)
        {
            _scrollToInstanceId = null;
            try
            {
                await JS.InvokeVoidAsync("tempoDashboard.scrollWidgetIntoView", _dashboardElement, _highlightedInstanceId);
            }
            catch (JSDisconnectedException) { }
            catch (InvalidOperationException) { }
        }

        // The freshly opened menu hands focus to its first item from the panel's OnOpened (after
        // overlay.js displayed the popover), so the arrow keys (which the shared menu keyboard
        // listens for on the menu) are reachable — see FocusFirstMenuItemAsync.

        // Every non-trigger close path (item selection) returns focus to the trigger, so a
        // keyboard selection never strands focus on <body>. Escape and the outside click close
        // through overlay.js, which restores the anchor itself.
        if (_focusViewsButtonAfterClose)
        {
            _focusViewsButtonAfterClose = false;
            try
            {
                await _dashboardsButton.FocusAsync(preventScroll: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JSException)
            {
                // The trigger left the DOM with the close — nothing to focus.
            }
        }

        // After creating a dashboard the user lands in edit mode on an empty grid: the next step
        // ("Add widget") receives focus, so keyboard work can continue without a Tab hunt.
        if (_focusAddWidgetButtonAfterCreate)
        {
            _focusAddWidgetButtonAfterCreate = false;
            try
            {
                await _addWidgetButton.FocusAsync(preventScroll: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JSException)
            {
                // No desktop toolbar Add-widget button (mobile presents it on the action bar).
            }
        }

        // Entering edit mode removes the Edit trigger, so focus would fall to <body>: it moves to
        // the next step ("Add widget") instead. On a mobile layout the desktop button does not
        // exist (the action bar owns the actions) — the move is skipped silently.
        if (_focusAddWidgetButtonAfterEditModeEntry)
        {
            _focusAddWidgetButtonAfterEditModeEntry = false;
            try
            {
                await _addWidgetButton.FocusAsync(preventScroll: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JSException)
            {
                // No desktop toolbar Add-widget button (mobile presents it on the action bar).
            }
        }

        // Leaving name editing returns to the rename trigger.
        if (_focusRenameButtonAfterNameEdit)
        {
            _focusRenameButtonAfterNameEdit = false;
            try
            {
                await _renameButton.FocusAsync(preventScroll: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JSException)
            {
                // The rename trigger left the DOM with the close.
            }
        }

        // Leaving edit mode (Save or Cancel) returns to the view-mode entry point: the switcher
        // trigger when a provider supplies dashboards, the Edit button otherwise.
        if (_focusViewEntryAfterEditModeExit)
        {
            _focusViewEntryAfterEditModeExit = false;
            try
            {
                var target = DashboardProvider is not null ? _dashboardsButton : _editButton;
                await target.FocusAsync(preventScroll: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JSException)
            {
                // The entry point left the DOM (e.g. the mobile action-bar layout) — nothing to focus.
            }
        }
    }
    
    private async Task LoadDashboardsList()
    {
        if (DashboardProvider is null) return;
        _availableDashboards = (await DashboardProvider.GetDashboardsAsync(UserId)).ToList();
    }
    
    private async Task LoadDefaultDashboard()
    {
        if (DashboardProvider is null) return;
        var defaultDash = await DashboardProvider.GetDefaultDashboardAsync(UserId);
        if (defaultDash is not null)
        {
            await LoadDashboard(defaultDash.Id);
        }
        else
        {
            CreateEmptyDashboard();
        }
    }
    
    private async Task LoadDashboard(string dashboardId)
    {
        if (DashboardProvider is null) return;

        // F7b: a switcher selection that fails keeps the current dashboard on screen and
        // raises the load alert instead of crashing.
        try
        {
            _currentDashboard = await DashboardProvider.GetDashboardAsync(dashboardId);
        }
        catch (Exception)
        {
            _loadError = true;
            StateHasChanged();
            return;
        }

        _loadError = false;
        if (_currentDashboard is not null)
        {
            _widgets = _currentDashboard.Widgets.ToList();
            // A provider-loaded dashboard is persisted: an unsaved-creation restore (DA3) no longer applies.
            _createdNewDashboardPending = false;
            _dashboardBeforeCreate = null;
            CloseDashboardMenu();
            await OnDashboardChanged.InvokeAsync(_currentDashboard);
            StateHasChanged();
        }
    }

    /// <summary>Shared grid of a fresh dashboard (the dialog path and the no-default fallback create the same shape).</summary>
    private static GridConfig NewDashboardGrid() => new() { Columns = 12, RowHeight = 60, Gap = 16, AllowOverlap = false };

    private void CreateEmptyDashboard()
    {
        _currentDashboard = new DashboardConfig
        {
            Name = Loc["TmDashboard_NewDashboardName"],
            CreatedBy = UserId ?? "anonymous",
            Grid = NewDashboardGrid()
        };
        _widgets = [];
    }
    
    private void EnterEditMode()
    {
        if (!AllowEdit) return;
        _isEditMode = true;
        // The Edit trigger leaves the DOM with this render: hand focus to the next step
        // ("Add widget") after the render, not to <body>.
        _focusAddWidgetButtonAfterEditModeEntry = true;
        StateHasChanged();
    }

    /// <summary>Test hook: enters edit mode without a browser (bUnit renders no measurement scope).</summary>
    internal void EnterEditModeForTests() => EnterEditMode();
    
    private async Task SaveLayout()
    {
        if (_currentDashboard is null || DashboardProvider is null) return;

        _currentDashboard.Widgets = _widgets.ToList();
        _currentDashboard.ModifiedAt = DateTime.UtcNow;

        // F7b (UX §5.8): a failed save keeps edit mode open (nothing is lost) and raises the
        // save alert; "Try again" re-runs this method.
        try
        {
            _currentDashboard = await DashboardProvider.SaveDashboardAsync(_currentDashboard);
        }
        catch (Exception)
        {
            _saveError = true;
            StateHasChanged();
            return;
        }

        _saveError = false;
        _isEditMode = false;
        _isEditingName = false;
        // The dashboard is persisted: a later "Cancel" keeps it (DA3 applies to unsaved creations only).
        _createdNewDashboardPending = false;
        _dashboardBeforeCreate = null;

        await LoadDashboardsList();
        await OnDashboardChanged.InvokeAsync(_currentDashboard);
        // Leaving edit mode via Save returns focus to the view-mode entry point.
        _focusViewEntryAfterEditModeExit = true;
        StateHasChanged();
    }

    private void CancelEdit()
    {
        // DA3: the current dashboard came from the create dialog and was never saved — "Cancel"
        // restores the previous dashboard (widgets included) and never touches the provider.
        if (_createdNewDashboardPending)
        {
            _currentDashboard = _dashboardBeforeCreate;
            _widgets = _currentDashboard?.Widgets.ToList() ?? [];
            _dashboardBeforeCreate = null;
            _createdNewDashboardPending = false;
        }
        else if (_currentDashboard is not null)
        {
            _widgets = _currentDashboard.Widgets.ToList();
        }
        _isEditMode = false;
        _isEditingName = false;
        // Leaving edit mode via Cancel returns focus to the view-mode entry point.
        _focusViewEntryAfterEditModeExit = true;
        StateHasChanged();
    }

    private void OpenWidgetSelector()
    {
        _showWidgetSelector = true;
        StateHasChanged();
    }
    
    private void OnWidgetSelected(WidgetDefinition widget)
    {
        var instance = new WidgetInstance
        {
            WidgetId = widget.Id,
            Width = widget.DefaultWidth,
            Height = widget.DefaultHeight,
            X = 0,
            Y = 0
        };
        
        // Find non-overlapping position
        var position = FindNonOverlappingPosition(instance);
        instance.X = position.X;
        instance.Y = position.Y;
        
        _widgets.Add(instance);
        // DA1: with CloseOnSelect=false the selector stays open so the host can add several
        // widgets in a row; the highlight/scroll pair still fires, so the latest addition is
        // emphasized (and scrolled to) once the selector eventually closes.
        if (CloseOnSelect)
        {
            _showWidgetSelector = false;
        }
        // F4 compact mode: highlight the fresh widget briefly so the user sees what the selector
        // just added (the CSS keeps the highlight sub-second-quiet under prefers-reduced-motion).
        _highlightedInstanceId = instance.InstanceId;
        // F4: on small layouts the new card can land below the fold — scroll it into view after
        // this render so the highlight is actually visible.
        _scrollToInstanceId = instance.InstanceId;
        _ = ClearHighlightLaterAsync(instance.InstanceId);
        StateHasChanged();
    }

    private async Task ClearHighlightLaterAsync(string instanceId)
    {
        // The fire-and-forget delay must not touch the component after disposal (a navigated-away
        // dashboard would throw on StateHasChanged) — bail out if the cancellation fired first.
        try
        {
            await Task.Delay(1600, _highlightCts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        if (_highlightedInstanceId == instanceId)
        {
            _highlightedInstanceId = null;
            await InvokeAsync(StateHasChanged);
        }
    }
    
    private (int X, int Y) FindNonOverlappingPosition(WidgetInstance newWidget)
    {
        if (_widgets.Count == 0) return (0, 0);
        
        var grid = _currentDashboard?.Grid ?? new GridConfig();
        int maxColumns = grid.Columns;
        
        // Try to find a position starting from top-left
        for (int y = 0; y < 100; y++)
        {
            for (int x = 0; x <= maxColumns - newWidget.Width; x++)
            {
                newWidget.X = x;
                newWidget.Y = y;
                
                if (!WouldOverlap(newWidget))
                {
                    return (x, y);
                }
            }
        }
        
        // Fallback: place below all widgets
        int maxY = _widgets.Any() ? _widgets.Max(w => w.Y + w.Height) : 0;
        return (0, maxY);
    }
    
    private bool WouldOverlap(WidgetInstance widget)
    {
        foreach (var existing in _widgets.Where(w => w.InstanceId != widget.InstanceId))
        {
            if (HasOverlap(widget, existing))
                return true;
        }
        return false;
    }
    
    private void PushOverlappingWidgets(WidgetInstance movedWidget)
    {
        var widgetsToPush = _widgets
            .Where(w => w.InstanceId != movedWidget.InstanceId && HasOverlap(movedWidget, w))
            .ToList();
        
        foreach (var widget in widgetsToPush)
        {
            int pushAmount = movedWidget.Y + movedWidget.Height - widget.Y;
            widget.Y += pushAmount;
            
            // Recursively push widgets that this one now overlaps
            PushOverlappingWidgets(widget);
        }
    }
    
    private void RemoveWidget(WidgetInstance widget)
    {
        _widgets.Remove(widget);
        if (_grabbedWidget == widget) ResetGrabState();
        StateHasChanged();
    }

    // ── F4: reordering and resizing without drag ─────────────────────

    /// <summary>
    /// Whether the widget renders its compact move controls: edit mode on a layout without the
    /// desktop grid (tablet portrait, mobile), where drag and resize handles do not exist.
    /// </summary>
    private bool IsCompactEditMode(TmLayoutContext layout) => _isEditMode && !layout.IsDesktop;

    /// <summary>Whether another widget precedes this one in the linear (Y, X) order (Move up enabled).</summary>
    private bool CanMoveUp(WidgetInstance widget)
    {
        var ordered = OrderedWidgets();
        return ordered.IndexOf(widget) > 0;
    }

    /// <summary>Whether another widget follows this one in the linear (Y, X) order (Move down enabled).</summary>
    private bool CanMoveDown(WidgetInstance widget)
    {
        var ordered = OrderedWidgets();
        return ordered.IndexOf(widget) < ordered.Count - 1;
    }

    private List<WidgetInstance> OrderedWidgets() => _widgets
        .OrderBy(w => w.Y).ThenBy(w => w.X).ThenBy(w => w.InstanceId, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// DA2 compact-mode move: swaps the widget's <c>(X, Y)</c> with the previous neighbour in the
    /// linear (Y, X) order and pushes overlaps — the widget model (instance identity) is unchanged.
    /// </summary>
    private void MoveWidgetUp(WidgetInstance widget) => SwapWithNeighbour(widget, -1);

    /// <summary>DA2 compact-mode move: swaps with the next neighbour in the linear (Y, X) order.</summary>
    private void MoveWidgetDown(WidgetInstance widget) => SwapWithNeighbour(widget, +1);

    private void SwapWithNeighbour(WidgetInstance widget, int direction)
    {
        if (!_isEditMode) return;

        var ordered = OrderedWidgets();
        var index = ordered.IndexOf(widget);
        var neighbourIndex = index + direction;
        if (index < 0 || neighbourIndex < 0 || neighbourIndex >= ordered.Count) return;

        var neighbour = ordered[neighbourIndex];
        (widget.X, neighbour.X) = (neighbour.X, widget.X);
        (widget.Y, neighbour.Y) = (neighbour.Y, widget.Y);

        if (_currentDashboard?.Grid.AllowOverlap == false)
        {
            PushOverlappingWidgets(widget);
            PushOverlappingWidgets(neighbour);
        }

        AnnounceMove(PositionText(widget));
        StateHasChanged();
    }

    /// <summary>Native button activation (Enter/Space, docs/keyboard-activation-convention.md): first press grabs, second press drops.</summary>
    private void ToggleGrab(WidgetInstance widget)
    {
        if (!_isEditMode || !IsDraggable(widget)) return;

        if (_grabbedWidget == widget)
        {
            DropGrabbed();
            return;
        }

        ResetGrabState();
        _grabbedWidget = widget;
        _grabOriginal = (widget.X, widget.Y, widget.Width, widget.Height);
        AnnounceMove(Loc["TmDashboard_Grabbed"] + " " + PositionText(widget));
        StateHasChanged();
    }

    /// <summary>Arrow keys move the grabbed widget one grid cell; Shift+arrows resize one step; Escape cancels.</summary>
    internal void HandleHandleKeyDown(KeyboardEventArgs e, WidgetInstance widget)
    {
        if (!_isEditMode || _grabbedWidget != widget) return;

        // Shift+arrow resizes one step, a bare arrow moves one cell (spec F4 / WCAG 2.5.7).
        if (e.ShiftKey)
        {
            switch (e.Key)
            {
                case "ArrowLeft":
                    ResizeGrabbedBy(-1, 0);
                    break;
                case "ArrowRight":
                    ResizeGrabbedBy(1, 0);
                    break;
                case "ArrowUp":
                    ResizeGrabbedBy(0, -1);
                    break;
                case "ArrowDown":
                    ResizeGrabbedBy(0, 1);
                    break;
                default:
                    return;
            }

            StateHasChanged();
            return;
        }

        switch (e.Key)
        {
            case "ArrowLeft":
                MoveGrabbedBy(-1, 0);
                break;
            case "ArrowRight":
                MoveGrabbedBy(1, 0);
                break;
            case "ArrowUp":
                MoveGrabbedBy(0, -1);
                break;
            case "ArrowDown":
                MoveGrabbedBy(0, 1);
                break;
            case "Escape":
                CancelGrab();
                break;
            default:
                return;
        }

        StateHasChanged();
    }

    private void MoveGrabbedBy(int deltaX, int deltaY)
    {
        if (_grabbedWidget is not { } widget) return;

        widget.X = Math.Clamp(widget.X + deltaX, 0, Math.Max(0, Columns - widget.Width));
        widget.Y = Math.Max(0, widget.Y + deltaY);
        PushAfterGrab(widget);
        AnnounceMove(PositionText(widget));
    }

    private void ResizeGrabbedBy(int deltaWidth, int deltaHeight)
    {
        if (_grabbedWidget is not { } widget) return;

        var definition = WidgetRegistry?.GetWidget(widget.WidgetId);
        var minWidth = Math.Max(1, definition?.MinWidth ?? 1);
        var minHeight = Math.Max(1, definition?.MinHeight ?? 1);
        // MaxWidth/MaxHeight of 0 means unlimited — the grid itself is the ceiling for the width.
        var maxWidth = definition is { MaxWidth: > 0 } ? definition.MaxWidth : Columns;
        var maxHeight = definition is { MaxHeight: > 0 } ? definition.MaxHeight : int.MaxValue;

        widget.Width = Math.Clamp(widget.Width + deltaWidth, minWidth, Math.Min(maxWidth, Columns));
        widget.Height = Math.Clamp(widget.Height + deltaHeight, minHeight, maxHeight);
        // A growth at the right edge would leave the grid: pull the position back inside.
        widget.X = Math.Clamp(widget.X, 0, Math.Max(0, Columns - widget.Width));
        PushAfterGrab(widget);
        AnnounceMove(PositionText(widget));
    }

    private void PushAfterGrab(WidgetInstance widget)
    {
        if (_currentDashboard?.Grid.AllowOverlap == false)
        {
            PushOverlappingWidgets(widget);
        }
    }

    private void DropGrabbed()
    {
        if (_grabbedWidget is not { } widget) return;

        PushAfterGrab(widget);
        ResetGrabState();
        AnnounceMove(PositionText(widget));
        StateHasChanged();
    }

    private void CancelGrab()
    {
        if (_grabbedWidget is not { } widget || _grabOriginal is not { } original)
        {
            ResetGrabState();
            StateHasChanged();
            return;
        }

        widget.X = original.X;
        widget.Y = original.Y;
        widget.Width = original.Width;
        widget.Height = original.Height;
        PushAfterGrab(widget);
        ResetGrabState();
        AnnounceMove(Loc["TmDashboard_MoveCancelled"] + " " + PositionText(widget));
        StateHasChanged();
    }

    private void ResetGrabState()
    {
        _grabbedWidget = null;
        _grabOriginal = null;
    }

    /// <summary>Human-readable geometry for the live region: "column 3, row 2, 4 × 3" (1-based).</summary>
    private string PositionText(WidgetInstance widget) => string.Format(
        Loc["TmDashboard_PositionAnnouncement"],
        GetWidgetName(widget), widget.X + 1, widget.Y + 1, widget.Width, widget.Height);

    private void AnnounceMove(string message) => _moveAnnouncement = message;

    private void ToggleCollapse(WidgetInstance widget)
    {
        widget.IsCollapsed = !widget.IsCollapsed;
        StateHasChanged();
    }

    private string GetWidgetClasses(WidgetInstance widget)
    {
        var classes = new List<string>();
        if (widget.IsCollapsed) classes.Add("tm-widget--collapsed");
        if (widget.IsMinimized) classes.Add("tm-widget--minimized");
        // tm-widget--dragging / --resizing are the live pointer states painted by dashboard.js
        // (Fáze 5, pointer-drag.js); the grab state below stays the keyboard mirror in C#.
        if (_grabbedWidget == widget) classes.Add("tm-widget--grabbed");
        if (_highlightedInstanceId == widget.InstanceId) classes.Add("tm-widget--highlight");
        // Wider than half the desktop grid: the compact container query gives it the full row.
        if (widget.Width > Columns / 2) classes.Add("tm-widget--wide");
        return string.Join(" ", classes);
    }

    private int Columns => _currentDashboard?.Grid.Columns ?? 12;

    private string GetWidgetStyles(WidgetInstance widget, Dictionary<string, int> order)
    {
        // Variables, not grid-column: an inline grid-column beats every @container rule, so the
        // container query in _dashboard.css is what turns these into 12, 2 or 1 column.
        var position = order[widget.InstanceId];
        // Tablet span: wider than half the desktop grid takes the whole row, a quarter or wider
        // takes half, anything narrower takes a third. A width of three and four both take half, so
        // the demo's second row fills the six tablet tracks instead of leaving a one-track hole.
        var spanMd = widget.Width > Columns / 2 ? 6 : widget.Width >= Columns / 4 ? 3 : 2;
        return $"--tm-w-x: {widget.X + 1}; --tm-w-span: {widget.Width}; --tm-w-span-md: {spanMd}; --tm-w-y: {widget.Y + 1}; --tm-w-rows: {widget.Height}; --tm-w-order: {position}; z-index: {widget.ZIndex};";
    }

    /// <summary>
    /// One sort per render: the widgets in (Y, X, InstanceId) order with their 1-based order index.
    /// The grid renders in this order (F3, M-2 — Tab and screen readers follow the visual row-then-
    /// column order in every mode) and <see cref="GetWidgetStyles"/> emits the same value as
    /// <c>--tm-w-order</c>. Positions change only at a drop (<see cref="OnGridPositionChanged"/>) and
    /// at a resize end (<see cref="OnWidgetResized"/>) — both re-render right after — so no DOM node
    /// ever moves under an active pointer.
    /// </summary>
    private (List<WidgetInstance> Widgets, Dictionary<string, int> Order) WidgetLayout()
    {
        var sorted = _widgets
            .OrderBy(widget => widget.Y)
            .ThenBy(widget => widget.X)
            .ThenBy(widget => widget.InstanceId, StringComparer.Ordinal)
            .ToList();
        var order = sorted
            .Select((widget, index) => (widget.InstanceId, Index: index + 1))
            .ToDictionary(item => item.InstanceId, item => item.Index, StringComparer.Ordinal);
        return (sorted, order);
    }
    
    private string GetWidgetName(WidgetInstance widget)
    {
        return WidgetRegistry?.GetWidget(widget.WidgetId)?.Name ?? Loc["TmDashboard_UnknownWidget"];
    }

    /// <summary>Icon of the widget's header tile: the definition's icon resolved through the shared helper — the Box fallback when there is no definition (or the name is not renderable).</summary>
    private static string GetWidgetIcon(WidgetDefinition? definition)
        => definition is null ? IconNames.Box : DashboardWidgetIcons.ResolveIcon(definition.Icon);

    /// <summary>Id of the widget's title — collapse/remove controls point at it through aria-describedby.</summary>
    private static string GetWidgetTitleId(WidgetInstance widget) => $"tm-widget-title-{widget.InstanceId}";

    /// <summary>Id of the widget's description line, when rendered.</summary>
    private static string GetWidgetDescriptionId(WidgetInstance widget) => $"tm-widget-desc-{widget.InstanceId}";

    /// <summary>Categories the host registered, for the shared inline category colour resolution.</summary>
    private IEnumerable<WidgetCategory> GetCategories() => WidgetRegistry?.GetCategories() ?? [];

    /// <summary>Id of the widget's content region — the collapse control points at it through aria-controls.</summary>
    private static string GetWidgetContentId(WidgetInstance widget) => $"tm-widget-content-{widget.InstanceId}";
    
    private bool IsDraggable(WidgetInstance widget)
    {
        return WidgetRegistry?.GetWidget(widget.WidgetId)?.IsDraggable ?? true;
    }

    /// <summary>Drag is a desktop gesture. Below desktop the grid has restacked and the coordinates no longer match.</summary>
    private bool CanDrag(WidgetInstance widget, TmLayoutContext layout) => IsDraggable(widget) && layout.IsDesktop;
    
    private bool IsResizable(WidgetInstance widget)
    {
        return WidgetRegistry?.GetWidget(widget.WidgetId)?.IsResizable ?? true;
    }

    /// <summary>Resize handles follow the desktop grid. Below desktop they would resize a track that is no longer there.</summary>
    private bool CanResize(WidgetInstance widget, TmLayoutContext layout) => IsResizable(widget) && layout.IsDesktop;
    
    /// <summary>
    /// Collapse follows the widget definition (F2): a widget registered with
    /// <see cref="WidgetDefinition.IsCollapsible"/> = false renders no chevron. Without a
    /// definition the widget stays collapsible.
    /// </summary>
    private bool CanCollapse(WidgetInstance widget)
    {
        return WidgetRegistry?.GetWidget(widget.WidgetId)?.IsCollapsible ?? true;
    }
    
    private bool HasOverlap(WidgetInstance a, WidgetInstance b)
    {
        return a.X < b.X + b.Width &&
               a.X + a.Width > b.X &&
               a.Y < b.Y + b.Height &&
               a.Y + a.Height > b.Y;
    }
    
    private RenderFragment RenderWidgetContent(WidgetInstance widget)
    {
        if (WidgetTemplate is not null)
        {
            return WidgetTemplate(widget);
        }

        return builder =>
        {
            builder.AddContent(0, string.Format(Loc["TmDashboard_WidgetPlaceholder"], GetWidgetName(widget)));
        };
    }

    private void ToggleDashboardMenu()
    {
        // Closing via the trigger itself: focus is already on the trigger — no restore needed.
        // Escape, the outside pointerdown and a Tab that leaves menu and trigger dismiss through
        // overlay.js → NotifyDismissedAsync → SetDashboardMenuOpen.
        _showDashboardMenu = !_showDashboardMenu;
        StateHasChanged();
    }

    /// <summary>Two-way binding target of the panel's <see cref="TmOverlayPanel.IsOpenChanged"/>.</summary>
    private void SetDashboardMenuOpen(bool open) => _showDashboardMenu = open;

    /// <summary>
    /// The panel's <see cref="TmOverlayPanel.OnOpened"/>: overlay.js has displayed the popover, so a
    /// focus move into it lands. Hands focus to the first enabled item, or the roving arrow keys
    /// (which the shared menu keyboard listens for on the menu) are unreachable. Best-effort —
    /// without JS the menu keeps the native tab order.
    /// </summary>
    private Task FocusFirstMenuItemAsync() => (_menuNav ??= new MenuNavInterop(JS)).FocusFirstAsync(_viewsElement);

    /// <summary>Closes the switcher menu on a selection, so the trigger is refocused after the close render.</summary>
    private void CloseDashboardMenu()
    {
        if (!_showDashboardMenu) return;
        _showDashboardMenu = false;
        _focusViewsButtonAfterClose = true;
    }

    private async Task CreateNewDashboard()
    {
        // The dialog owns the focus story from here (TmModal restores the views trigger on close),
        // so the menu closes without arming its own focus return.
        _showDashboardMenu = false;
        _newDashboardName = "";
        _showNewDashboardDialog = true;
        await InvokeAsync(StateHasChanged);
    }

    private void ConfirmCreateDashboard()
    {
        if (string.IsNullOrWhiteSpace(_newDashboardName)) return;

        // DA3: remember what was on screen — "Cancel" in edit mode brings it back untouched.
        _dashboardBeforeCreate = _currentDashboard;
        _createdNewDashboardPending = true;
        _currentDashboard = new DashboardConfig
        {
            Name = _newDashboardName.Trim(),
            CreatedBy = UserId ?? "anonymous",
            Grid = NewDashboardGrid()
        };
        _widgets = [];
        _showNewDashboardDialog = false;
        _isEditMode = true;
        // The dialog's own focus restore (TmModal) lands first; this moves focus on to the next
        // step — adding widgets to the fresh, empty dashboard.
        _focusAddWidgetButtonAfterCreate = true;
        StateHasChanged();
    }

    private void CancelNewDashboard()
    {
        _showNewDashboardDialog = false;
        _newDashboardName = "";
        StateHasChanged();
    }
    
    private async Task SetAsDefault(string dashboardId)
    {
        if (DashboardProvider is null) return;
        
        await DashboardProvider.SetDefaultDashboardAsync(dashboardId, UserId);
        await LoadDashboardsList();
        StateHasChanged();
    }
    
    private void DeleteDashboard(string dashboardId)
    {
        // Don't allow deleting the default dashboard
        var dashboard = _availableDashboards.FirstOrDefault(d => d.Id == dashboardId);
        if (dashboard?.IsDefault == true)
        {
            return;
        }
        
        _dashboardToDeleteId = dashboardId;
        _dashboardToDeleteName = dashboard?.Name;
        _showDeleteDialog = true;
        // The confirm dialog owns the focus story from here (TmDialog restores the views trigger).
        _showDashboardMenu = false;
        StateHasChanged();
    }
    
    private async Task HandleDeleteDialogResult(bool? result)
    {
        if (result == true)
        {
            await ConfirmDeleteDashboard();
        }
        else
        {
            CancelDeleteDashboard();
        }
    }
    
    private async Task ConfirmDeleteDashboard()
    {
        if (DashboardProvider is null || string.IsNullOrEmpty(_dashboardToDeleteId)) return;
        
        await DashboardProvider.DeleteDashboardAsync(_dashboardToDeleteId);
        
        // If we deleted the current dashboard, load the default one
        if (_currentDashboard?.Id == _dashboardToDeleteId)
        {
            await LoadDefaultDashboard();
        }
        
        await LoadDashboardsList();
        CancelDeleteDashboard();
    }
    
    private void CancelDeleteDashboard()
    {
        _showDeleteDialog = false;
        _dashboardToDeleteId = null;
        _dashboardToDeleteName = null;
        StateHasChanged();
    }
    
    private void StartNameEdit()
    {
        _editingName = _currentDashboard?.Name ?? "";
        _isEditingName = true;
        StateHasChanged();
    }
    
    /// <summary>Escape inside the inline name editor cancels the edit (container-level dismiss).</summary>
    private void HandleNameEditKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape") CancelNameEdit();
    }

    private void SaveNameEdit()
    {
        if (_currentDashboard is not null && !string.IsNullOrWhiteSpace(_editingName))
        {
            _currentDashboard.Name = _editingName.Trim();
        }
        _isEditingName = false;
        _focusRenameButtonAfterNameEdit = true;
        StateHasChanged();
    }

    private void CancelNameEdit()
    {
        _isEditingName = false;
        _editingName = "";
        _focusRenameButtonAfterNameEdit = true;
        StateHasChanged();
    }
    
    [JSInvokable]
    public void OnGridPositionChanged(string instanceId, int x, int y)
    {
        var widget = _widgets.FirstOrDefault(w => w.InstanceId == instanceId);
        if (widget is not null)
        {
            // A wide widget dropped near the right edge must not overflow the grid (the JS
            // preview clamps the same way); the row is only bounded below.
            widget.X = Math.Clamp(x, 0, Math.Max(0, Columns - widget.Width));
            widget.Y = Math.Max(0, y);
            
            // Push overlapping widgets
            if (_currentDashboard?.Grid.AllowOverlap == false)
            {
                PushOverlappingWidgets(widget);
            }

            _ = InvokeAsync(StateHasChanged);
        }
    }

    [JSInvokable]
    public void OnWidgetResized(string instanceId, int width, int height)
    {
        var widget = _widgets.FirstOrDefault(w => w.InstanceId == instanceId);
        if (widget is not null)
        {
            widget.Width = width;
            widget.Height = height;

            // Push overlapping widgets after resize
            if (_currentDashboard?.Grid.AllowOverlap == false)
            {
                PushOverlappingWidgets(widget);
            }

            _ = InvokeAsync(StateHasChanged);
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_jsInitialized)
        {
            try
            {
                await JS.InvokeVoidAsync("tempoDashboard.destroy", _dashboardElement);
            }
            catch (JSDisconnectedException) { }
            catch (InvalidOperationException) { }
            catch (TaskCanceledException) { }
        }
        if (_menuNav is not null) await _menuNav.DisposeAsync();
        _highlightCts.Cancel();
        _highlightCts.Dispose();
        _dotNetRef?.Dispose();
    }
}
