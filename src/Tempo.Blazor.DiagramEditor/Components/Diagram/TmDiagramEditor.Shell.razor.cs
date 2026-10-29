using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Layout;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// Desktop restructure of <see cref="TmDiagramEditor"/> (DiagramEditor redesign, phase 4):
/// the editor renders through the core <c>TmEditorShell</c> slots (header / toolbar / left /
/// canvas / right / status bar), the command row is a core <c>TmToolbar</c> with overflow,
/// the search and panel toggles live in the header, the view settings in the header "⋯" menu,
/// the zoom and read-only controls in a status bar and the inspector presents
/// "Properties | Layers" tabs (DI2).
/// </summary>
public partial class TmDiagramEditor
{
    // ── Phase-4 layout parameters ────────────────────────────────────────────

    /// <summary>
    /// How the right column presents the properties and layers panels.
    /// <see cref="DiagramInspectorLayout.Tabs"/> (the default) renders "Properties | Layers"
    /// tabs; <see cref="DiagramInspectorLayout.Stacked"/> is the opt-in legacy stacked column.
    /// </summary>
    [Parameter] public DiagramInspectorLayout InspectorLayout { get; set; } = DiagramInspectorLayout.Tabs;

    /// <summary>Show the status bar below the canvas (zoom controls, read-only toggle). Default true.</summary>
    [Parameter] public bool ShowStatusBar { get; set; } = true;

    /// <summary>Show the editable document title in the header. Default true. Hosts hide it through
    /// this parameter — never through CSS selectors (the title is deliberately outside the toolbar
    /// group contract).</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    // ── Phase-4 layout state ─────────────────────────────────────────────────

    /// <summary>Whether the header "⋯" view-settings menu is open.</summary>
    private bool _viewMenuOpen;

    /// <summary>Per-instance id of the "⋯" panel — the trigger's aria-controls target.</summary>
    private readonly string _viewMenuId = $"tm-diagram-editor-view-menu-{Guid.NewGuid():N}";

    /// <summary>The selected inspector tab: "properties" or "layers" (DI2).</summary>
    private string _inspectorTab = "properties";

    private void ToggleViewMenu() => _viewMenuOpen = !_viewMenuOpen;

    private void CloseViewMenu() => _viewMenuOpen = false;

    /// <summary>Escape closes the header "⋯" view-settings menu.</summary>
    private void OnViewMenuKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs args)
    {
        if (args.Key == "Escape") _viewMenuOpen = false;
    }

    // ── Effective group selection (new desktop layout) ───────────────────────

    /// <summary>Groups of the single command row (the core TmToolbar), in render order.</summary>
    private const DiagramToolbarGroups CommandRowGroups =
        DiagramToolbarGroups.History | DiagramToolbarGroups.Clipboard | DiagramToolbarGroups.Tools
        | DiagramToolbarGroups.Format | DiagramToolbarGroups.Insert | DiagramToolbarGroups.Arrange
        | DiagramToolbarGroups.Layout | DiagramToolbarGroups.Export | DiagramToolbarGroups.Import
        | DiagramToolbarGroups.Templates;

    /// <summary>Groups that render into the header "⋯" view-settings menu.</summary>
    private const DiagramToolbarGroups ViewMenuGroups =
        DiagramToolbarGroups.View | DiagramToolbarGroups.CanvasSize;

    /// <summary>Whether the command row (the TmToolbar region) renders at all.</summary>
    private bool HasCommandToolbar => ShowToolbar && (EffectiveToolbarGroups & CommandRowGroups) != 0;

    /// <summary>
    /// Whether the mobile "⋯" menu carries the minimap toggle: the toolbar renders, the host
    /// shows the minimap and the menu itself exists (the frozen Panels group — or the View group
    /// that shares the menu). When it is false (e.g. a <c>ToolbarGroups</c> selection without
    /// Panels/View) hiding the map on entry would leave no way back — the mobile X1 default-hide
    /// and this availability check share the one predicate so they can never disagree.
    /// </summary>
    private bool MobileMinimapToggleAvailable => ShowToolbar && ShowMinimap
        && (Shows(DiagramToolbarGroups.Panels) || Shows(DiagramToolbarGroups.View));

    /// <summary>Whether the header "⋯" trigger and its panel render: for the view/canvas-size groups,
    /// and on mobile also whenever it carries the Search or Read-only toggle or the minimap toggle
    /// (no other place there).</summary>
    private bool ShowsViewMenu => ShowToolbar && (
        (EffectiveToolbarGroups & ViewMenuGroups) != 0
        || (IsMobileLayout && (Shows(DiagramToolbarGroups.Search) || ShowsReadOnlyToggle
            || MobileMinimapToggleAvailable)));

    /// <summary>
    /// The floating mobile zoom pill rule (Fáze 9). The explicit <see cref="ShowMobileZoomOverlay"/>
    /// knob wins: <c>true</c> shows the pill on a mobile layout even chrome-less (a viewer keeps
    /// zoom buttons), <c>false</c> never shows it, and the default <c>null</c> keeps today's rule —
    /// mobile + <see cref="ShowToolbar"/> + the Zoom group. The pill is a mobile-band surface: the
    /// docked bands keep the status-bar zoom.
    /// </summary>
    private bool ShowsMobileZoomOverlay => ShowMobileZoomOverlay switch
    {
        true => IsMobileLayout,
        false => false,
        _ => IsMobileLayout && ShowToolbar && Shows(DiagramToolbarGroups.Zoom),
    };

    /// <summary>Whether the status bar region renders.</summary>
    private bool ShowsStatusBar => ShowToolbar && ShowStatusBar
        && (Shows(DiagramToolbarGroups.Zoom) || ShowsReadOnlyToggle);

    /// <summary>Whether the header bar renders its built-in controls (title, mobile history, search and panel toggles).</summary>
    private bool ShowsHeaderControls => ShowToolbar;

    /// <summary>
    /// Whether the header bar region renders at all: built-in controls that actually paint, or a
    /// host slot (an empty shell bar must not stay behind). Final UX review (M1): with
    /// <see cref="ShowToolbar"/> true but NO content — a Zoom-only toolbar with no title and no
    /// host slots, e.g. the Modeling preview — the bar is skipped entirely instead of painting a
    /// dead row above the command row.
    /// </summary>
    private bool ShowsHeaderBar => HeaderStart is not null || HeaderActions is not null
        || (ShowsHeaderControls && ShowsHeaderBarContent);

    /// <summary>Whether the header bar paints any BUILT-IN control: the title input, the mobile
    /// history pair, the search toggle, the named panel toggles (only when a panel exists to
    /// toggle) or the "⋯" view menu.</summary>
    private bool ShowsHeaderBarContent => ShowTitle
        || (IsMobileLayout && Shows(DiagramToolbarGroups.History))
        || (!IsMobileLayout && (Shows(DiagramToolbarGroups.Search)
            || (Shows(DiagramToolbarGroups.Panels) && HeaderPanelTogglesAvailable)))
        || ShowsViewMenu;

    /// <summary>Whether any named panel toggle would paint inside the header bar's panels group.</summary>
    private bool HeaderPanelTogglesAvailable => ShowToolbox || ShowPropertiesPanel || ShowLayersPanel || ShowMinimap;

    // ── Inspector state (DI2) ────────────────────────────────────────────────

    /// <summary>The properties panel is shown in the inspector (host allows it and the user did not collapse it).</summary>
    private bool PropertiesAvailable => ShowPropertiesPanel && !_propsCollapsed;

    /// <summary>The layers panel is shown in the inspector (host allows it and the user did not collapse it).</summary>
    private bool LayersAvailable => ShowLayersPanel && !_layersCollapsed;

    /// <summary>Both panels are available: only then the inspector renders the Properties | Layers tab strip.</summary>
    private bool InspectorHasTabs => PropertiesAvailable && LayersAvailable;

    /// <summary>Whether the right column shows the inspector (properties and/or layers panel).</summary>
    private bool InspectorVisible => PropertiesAvailable || LayersAvailable;

    /// <summary>The tab the inspector shows: the user's choice while both panels are available,
    /// otherwise the only expanded panel.</summary>
    private string EffectiveInspectorTab
    {
        get
        {
            if (_inspectorTab == "layers") return LayersAvailable ? "layers" : "properties";
            return PropertiesAvailable ? "properties" : "layers";
        }
    }

    // ── Header panel toggles ─────────────────────────────────────────────────

    /// <summary>Header label of the inspector toggle: named after its content when only one panel exists.</summary>
    private string InspectorToggleLabel => (ShowPropertiesPanel, ShowLayersPanel) switch
    {
        (true, true) => Loc["TmDiagramEditor_PanelInspector"],
        (true, false) => Loc["TmDiagramEditor_PanelProperties"],
        _ => Loc["TmDiagramEditor_PanelLayers"],
    };

    /// <summary>Opens or closes the whole inspector (both panels move together; the tabs pick the content).</summary>
    private void SetInspectorOpen(bool open)
    {
        _propsCollapsed = !open;
        _layersCollapsed = !open;
    }

    /// <summary>Whether the inspector is expanded: visible, docked and — on tablet — not railed.</summary>
    private bool IsInspectorExpanded => InspectorVisible && RightDockOpen
        && !(_resolvedLayout == TmLayoutMode.Tablet && _shellCollapsed.HasFlag(EditorShellPanel.Right));

    /// <summary>
    /// Header "Inspector" toggle. Desktop: shows/hides the whole inspector. Tablet: the collapsed
    /// form is the rail (like the palette, F7) — only the shell's Right bit flips; expanding a
    /// hidden inspector also re-opens its panels (the shell then rails the palette itself).
    /// </summary>
    private Task ToggleInspectorFromHeaderAsync()
    {
        var expand = !IsInspectorExpanded;
        if (_resolvedLayout == TmLayoutMode.Tablet)
        {
            if (expand)
            {
                SetInspectorOpen(true);
                _shellCollapsed &= ~EditorShellPanel.Right;
            }
            else
            {
                _shellCollapsed |= EditorShellPanel.Right;
            }
        }
        else
        {
            SetInspectorOpen(expand);
            _shellCollapsed &= ~EditorShellPanel.Right;
        }
        return ScheduleCanvasRelayoutAsync();
    }

    /// <summary>Whether the palette is expanded (docked panel visible, not railed/hidden).</summary>
    private bool IsToolboxExpanded => LeftDockOpen && !_shellCollapsed.HasFlag(EditorShellPanel.Left);

    /// <summary>
    /// Expands or collapses the palette from the header toggle or the rail button. Tablet: the
    /// collapsed form is the rail — <c>LeftOpen</c> stays true and only the shell's Left bit flips
    /// (the shell then rails the inspector itself). Desktop: collapsed means hidden — the Left bit
    /// is cleared so no stale rail can appear when the panel comes back.
    /// </summary>
    private Task SetToolboxExpandedAsync(bool expanded)
    {
        if (!ShowToolbox) return Task.CompletedTask;
        if (_resolvedLayout == TmLayoutMode.Tablet)
        {
            _toolboxCollapsed = false;
            _shellCollapsed = expanded
                ? _shellCollapsed & ~EditorShellPanel.Left
                : _shellCollapsed | EditorShellPanel.Left;
        }
        else
        {
            _toolboxCollapsed = !expanded;
            _shellCollapsed &= ~EditorShellPanel.Left;
        }
        return ScheduleCanvasRelayoutAsync();
    }

    private Task ToggleToolboxFromHeaderAsync() => SetToolboxExpandedAsync(!IsToolboxExpanded);

    private void SelectInspectorTab(string tabId) => _inspectorTab = tabId;

    // ── Editor-shell dock state ──────────────────────────────────────────────

    /// <summary>
    /// The left dock holds the toolbox: open while the host shows it and the user did not
    /// collapse it. On a mobile layout the dock exists only when the mobile editing surfaces
    /// are allowed (a viewer gets no panels sheet).
    /// </summary>
    private bool LeftDockOpen => ShowToolbox
        && !(IsMobileLayout && !MobilePanelsAllowed)
        && !_toolboxCollapsed;

    /// <summary>The minimap renders as the docked card inside the right dock (desktop / Panel placement).</summary>
    private bool ShowsMinimapCard => ShowMinimap && !MinimapAsOverlay;

    /// <summary>Header toggle and the minimap card header share one minimap flag (mobile has no toggle until phase 6).</summary>
    private void ToggleMinimap() => _minimapCollapsed = !_minimapCollapsed;

    // ── Minimap overlay focus management (UX review) ─────────────────────────

    /// <summary>Per-instance id of the floating minimap overlay — the collapse/reopen buttons' aria-controls target.</summary>
    private readonly string _minimapOverlayId = $"tm-diagram-editor-minimap-overlay-{Guid.NewGuid():N}";

    private ElementReference _minimapCollapseBtn;
    private ElementReference _minimapReopenBtn;
    private ElementReference _viewMenuTrigger;

    /// <summary>The focus target armed by a minimap collapse/expand, consumed in OnAfterRenderAsync.</summary>
    private enum MinimapFocusTarget { None, CollapseButton, ReopenButton, ViewMenuTrigger }

    private MinimapFocusTarget _minimapFocusTarget;

    /// <summary>Whether the "⋯" trigger should re-take focus after the render (a mobile menu action ran).</summary>
    private bool _viewMenuTriggerFocusPending;

    /// <summary>
    /// Collapses the floating minimap overlay. Focus moves to the reopen button (tablet/desktop)
    /// or — on mobile, where the overlay has no reopen button — back to the "⋯" trigger that owns
    /// the map.
    /// </summary>
    private void CollapseMinimap()
    {
        _minimapCollapsed = true;
        _minimapFocusTarget = IsMobileLayout
            ? MinimapFocusTarget.ViewMenuTrigger
            : MinimapFocusTarget.ReopenButton;
    }

    /// <summary>Expands the floating minimap overlay; focus lands on its collapse button.</summary>
    private void ExpandMinimap()
    {
        _minimapCollapsed = false;
        _minimapFocusTarget = MinimapFocusTarget.CollapseButton;
    }

    /// <summary>
    /// The right dock holds the inspector (and, from tablet up, the collapsible minimap card).
    /// On a mobile layout the minimap floats as an overlay, so the dock exists for the inspector
    /// alone and only when the mobile editing surfaces are allowed.
    /// </summary>
    private bool RightDockOpen
    {
        get
        {
            if (IsMobileLayout) return InspectorVisible && MobilePanelsAllowed;
            return InspectorVisible || ShowsMinimapCard;
        }
    }

    private Task OnShellLeftDockOpenChanged(bool open)
    {
        // The shell's slim reopen toggle is the only writer when our header toggle is not used.
        if (ShowToolbox) _toolboxCollapsed = !open;
        if (IsMobileLayout)
        {
            // UX review: when the shell opens the Shapes side by other means (its own toggle,
            // a host echo), the inspector opens too — the flat sheet only shows a tab strip when
            // BOTH panels exist, so a lone Shapes tab would silently drop the Properties/Layers
            // tabs.
            if (open)
            {
                SetInspectorOpen(true);
            }
            else
            {
                // The shell (or a host) closed a panel: the sheet's close button unmounts with
                // the sheet, so focus moves to the action bar (the editor hides the shell's own
                // panels toggle when the bar renders — it is the one reopen path).
                _mobileActionBarFocusPending = true;
            }
            _canvas?.BeginChromeOnlyPass();
        }
        return ScheduleCanvasRelayoutAsync();
    }

    private Task OnShellRightDockOpenChanged(bool open)
    {
        // Reopen always opens the whole inspector (both panels move together); the choice of
        // tab stays in _inspectorTab. The shell's slim close toggle collapses it entirely.
        SetInspectorOpen(open);

        if (IsMobileLayout)
        {
            if (!open) _mobileActionBarFocusPending = true;
            _canvas?.BeginChromeOnlyPass();
        }
        return ScheduleCanvasRelayoutAsync();
    }

    /// <summary>Armed when a shell-side close unmounts the mobile sheet: after the render focus
    /// moves to the action bar (see <see cref="FocusMobileActionBarAsync"/>).</summary>
    private bool _mobileActionBarFocusPending;

    /// <summary>Moves focus to the first tile of the mobile action bar (the sheet's close button
    /// unmounted with the sheet; the bar is the one reopen path on this layout).</summary>
    private async Task FocusMobileActionBarAsync()
    {
        try
        {
            await JS.InvokeAsync<bool>("tmDiagramEditor.focusFirstAction", _editorRoot);
        }
        catch (JSException)
        {
            // No bar / no JS: focus stays wherever the browser put it.
        }
        catch (JSDisconnectedException)
        {
            // Host is going away.
        }
    }

    /// <summary>Accessible name of the right dock: the inspector tab title, or the minimap when it is the only content.</summary>
    private string InspectorTitle =>
        InspectorVisible
            ? (EffectiveInspectorTab == "layers" ? Loc["TmDiagramInspector_LayersTab"] : Loc["TmDiagramInspector_PropertiesTab"])
            : Loc["TmDiagramEditor_PanelMinimap"];

    // ── Overflow "More" menu representations for the dropdown groups ─────────
    // The core TmToolbar collapses items it has no room for; a TmDropdown cannot be re-opened
    // from a menu entry (no programmatic open), so each dropdown group offers its commands as a
    // labelled TmToolbarItem MenuItems group — the same handlers as the on-bar dropdown.

    private List<TmActionItem> BuildInsertMenuItems() =>
    [
        NewMenuItem("insert-table", Loc["TmDiagramEditor_InsertTable"], IconNames.Grid, HandleInsertSelect, "table", IsReadOnly),
        NewMenuItem("insert-text", Loc["TmDiagramEditor_InsertText"], IconNames.Type, HandleInsertSelect, "text", IsReadOnly),
    ];

    private List<TmActionItem> BuildArrangeMenuItems() =>
    [
        NewMenuItem("arrange-left", Loc["TmDiagramProperties_AlignLeft"], IconNames.AlignLeft, HandleArrangeSelect, "align-left", !CanAlign || IsReadOnly),
        NewMenuItem("arrange-center", Loc["TmDiagramProperties_AlignCenter"], IconNames.AlignCenter, HandleArrangeSelect, "align-center", !CanAlign || IsReadOnly),
        NewMenuItem("arrange-right", Loc["TmDiagramProperties_AlignRight"], IconNames.AlignRight, HandleArrangeSelect, "align-right", !CanAlign || IsReadOnly),
        NewMenuItem("arrange-top", Loc["TmDiagramProperties_AlignTop"], IconNames.ArrowUp, HandleArrangeSelect, "align-top", !CanAlign || IsReadOnly),
        NewMenuItem("arrange-middle", Loc["TmDiagramProperties_AlignMiddle"], IconNames.AlignVerticalJustifyCenter, HandleArrangeSelect, "align-middle", !CanAlign || IsReadOnly),
        NewMenuItem("arrange-bottom", Loc["TmDiagramProperties_AlignBottom"], IconNames.ArrowDown, HandleArrangeSelect, "align-bottom", !CanAlign || IsReadOnly),
        // The on-bar dropdown gated the WHOLE group on CanAlign; the distribute entries were
        // never disabled individually — the menu keeps exactly that contract.
        NewMenuItem("arrange-horizontal", Loc["TmDiagramProperties_DistributeHorizontal"], null, HandleArrangeSelect, "distribute-horizontal", !CanAlign || IsReadOnly),
        NewMenuItem("arrange-vertical", Loc["TmDiagramProperties_DistributeVertical"], null, HandleArrangeSelect, "distribute-vertical", !CanAlign || IsReadOnly),
    ];

    private List<TmActionItem> BuildLayoutMenuItems() =>
    [
        NewMenuItem("layout-dagre-tb", Loc["TmDiagram_LayoutHierarchicalTopDown"], IconNames.ArrowDown, HandleLayoutSelect, "dagre-tb", IsReadOnly),
        NewMenuItem("layout-dagre-lr", Loc["TmDiagram_LayoutHierarchicalLeftRight"], IconNames.ArrowRight, HandleLayoutSelect, "dagre-lr", IsReadOnly),
        NewMenuItem("layout-tree-tb", Loc["TmDiagram_LayoutTree"], IconNames.FolderOpen, HandleLayoutSelect, "tree-tb", IsReadOnly),
        NewMenuItem("layout-tree-lr", Loc["TmDiagram_LayoutTreeLR"], IconNames.FolderOpen, HandleLayoutSelect, "tree-lr", IsReadOnly),
        NewMenuItem("layout-force", Loc["TmDiagram_LayoutForce"], IconNames.Zap, HandleLayoutSelect, "force", IsReadOnly),
        NewMenuItem("layout-circle", Loc["TmDiagram_LayoutCircle"], IconNames.Circle, HandleLayoutSelect, "circle", IsReadOnly),
        NewMenuItem("layout-grid", Loc["TmDiagram_LayoutGrid"], IconNames.Grid, HandleLayoutSelect, "grid", IsReadOnly),
    ];

    private List<TmActionItem> BuildExportMenuItems() =>
    [
        NewMenuItem("export-json", Loc["TmDiagramEditor_ExportJson"], IconNames.FileText, HandleExportSelect, "json", _exporting),
        NewMenuItem("export-svg", Loc["TmDiagramEditor_ExportSvg"], IconNames.Image, HandleExportSelect, "svg", _exporting),
        NewMenuItem("export-png", Loc["TmDiagramEditor_ExportPng"], IconNames.Image, HandleExportSelect, "png", _exporting),
        NewMenuItem("export-pdf", Loc["TmDiagramEditor_ExportPdf"], IconNames.File, HandleExportSelect, "pdf", _exporting),
        NewMenuItem("export-viewport-svg", Loc["TmDiagramEditor_ExportViewportSvg"], IconNames.Image, HandleExportSelect, "viewportSvg", _exporting),
    ];

    /// <summary>SQL and CSV entries of the Import group. The group is Pinned (never collapses) because the JSON import needs the bar dropdown's InputFile; these entries are kept for the menu representation.</summary>
    private List<TmActionItem> BuildImportMenuItems() =>
    [
        NewMenuItem("import-sql", Loc["TmDiagramEditor_ImportSql"], IconNames.Database, value => { HandleImportSelect(value); return Task.CompletedTask; }, "sql", IsReadOnly),
        NewMenuItem("import-csv", Loc["TmDiagramEditor_ImportCsv"], IconNames.FileText, value => { HandleImportSelect(value); return Task.CompletedTask; }, "csv", IsReadOnly),
    ];

    private TmActionItem NewMenuItem(
        string id, string label, string? icon, Func<string, Task> handler, string value, bool disabled) => new()
    {
        Id = id,
        Label = label,
        Icon = icon,
        Disabled = disabled,
        OnClick = EventCallback.Factory.Create(this, () => handler(value)),
    };
}
