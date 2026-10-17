using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Icons;

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
    private bool HasCommandToolbar => (EffectiveToolbarGroups & CommandRowGroups) != 0;

    /// <summary>Whether the header "⋯" trigger and its view-settings panel render.</summary>
    private bool ShowsViewMenu => (EffectiveToolbarGroups & ViewMenuGroups) != 0;

    /// <summary>Whether the status bar region renders.</summary>
    private bool ShowsStatusBar => ShowStatusBar
        && (Shows(DiagramToolbarGroups.Zoom) || ShowsReadOnlyToggle);

    // ── Inspector state (DI2) ────────────────────────────────────────────────

    /// <summary>Whether the right column shows the inspector (properties and/or layers panel).</summary>
    private bool InspectorVisible =>
        (ShowPropertiesPanel && !_propsCollapsed) || (ShowLayersPanel && !_layersCollapsed);

    /// <summary>The tab the inspector shows: the user's choice while both panels are available,
    /// otherwise the only expanded panel.</summary>
    private string EffectiveInspectorTab
    {
        get
        {
            var propertiesAvailable = ShowPropertiesPanel && !_propsCollapsed;
            var layersAvailable = ShowLayersPanel && !_layersCollapsed;
            if (_inspectorTab == "layers") return layersAvailable ? "layers" : "properties";
            return propertiesAvailable ? "properties" : "layers";
        }
    }

    /// <summary>Header "Properties" toggle: expands/collapses the properties panel and selects its tab.</summary>
    private void TogglePropertiesPanel()
    {
        _propsCollapsed = !_propsCollapsed;
        if (!_propsCollapsed) _inspectorTab = "properties";
    }

    /// <summary>Header "Layers" toggle: expands/collapses the layers panel and selects its tab.</summary>
    private void ToggleLayersPanel()
    {
        _layersCollapsed = !_layersCollapsed;
        if (!_layersCollapsed) _inspectorTab = "layers";
    }

    private void SelectInspectorTab(string tabId) => _inspectorTab = tabId;

    // ── Editor-shell dock state ──────────────────────────────────────────────

    /// <summary>The left dock holds the toolbox: open while the host shows it and the user did not collapse it.</summary>
    private bool LeftDockOpen => ShowToolbox && !_toolboxCollapsed;

    /// <summary>The right dock holds the inspector (and the minimap card): open while the column has content.</summary>
    private bool RightDockOpen => InspectorVisible || ShowMinimap;

    private Task OnShellLeftDockOpenChanged(bool open)
    {
        // The shell's slim reopen toggle is the only writer when our header toggle is not used.
        if (ShowToolbox) _toolboxCollapsed = !open;
        return Task.CompletedTask;
    }

    private Task OnShellRightDockOpenChanged(bool open)
    {
        if (open)
        {
            // Reopen the panel behind the active tab (fall back to the other one when the
            // host hid that panel type).
            if (_inspectorTab == "layers")
            {
                _layersCollapsed = false;
                if (!ShowLayersPanel) _propsCollapsed = false;
            }
            else
            {
                _propsCollapsed = false;
                if (!ShowPropertiesPanel) _layersCollapsed = false;
            }
        }
        else
        {
            // The shell's slim close toggle collapses the whole inspector.
            _propsCollapsed = true;
            _layersCollapsed = true;
        }

        return Task.CompletedTask;
    }

    /// <summary>Accessible name of the right dock: the inspector tab title, or the minimap when it is the only content.</summary>
    private string InspectorTitle =>
        InspectorVisible
            ? (_inspectorTab == "layers" ? Loc["TmDiagramInspector_LayersTab"] : Loc["TmDiagramInspector_PropertiesTab"])
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

    /// <summary>The collapsed Import group offers SQL and CSV. The JSON import keeps the file
    /// picker on the bar item (a menu entry cannot open the browser file dialog without JS
    /// interop); at desktop widths the Import group is pinned to the bar.</summary>
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
