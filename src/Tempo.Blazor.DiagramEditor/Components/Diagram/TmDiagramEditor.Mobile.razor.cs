using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Layout;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// Phase 7 (mobile viewing and light edits, G2): with the resolved layout below the tablet
/// breakpoint the editor renders the core shell's mobile presentation — a non-modal bottom
/// sheet (peek/half/full) holding the Shapes | Inspector panels, the F5 action bar
/// (Select | Pan | Properties | More), a floating zoom overlay and the floating minimap,
/// and the long-press context menu as a sheet. Viewing survives everywhere; the full editing
/// chrome (resize/rotate, edge drawing, layout, import) stays tablet+ and the More menu
/// carries the export commands only.
/// </summary>
public partial class TmDiagramEditor
{
    // ── Mobile sheet state ───────────────────────────────────────────────────

    /// <summary>Peek/half/full snap fractions of the shell stage: the sheet snaps to a low
    /// peek (tabs + a strip of content), a working half and the full panel.</summary>
    private static readonly double[] MobileSheetSnapPoints = [0.35, 0.7, 1.0];

    private const int MobilePeekSnapIndex = 0;
    private const int MobileHalfSnapIndex = 1;

    /// <summary>The sheet tab the shell shows, two-way (<see cref="TmEditorShell.ActiveMobilePanel"/>).</summary>
    private EditorShellPanel _activeMobilePanel;

    /// <summary>The sheet snap, two-way (<see cref="TmEditorShell.MobileSheetSnapIndex"/>).</summary>
    private int _mobileSnapIndex;

    /// <summary>Whether the editor resolves the shell's mobile presentation.</summary>
    private bool IsMobileLayout => _resolvedLayout == TmLayoutMode.Mobile;

    /// <summary>
    /// Whether the mobile panels sheet currently covers part of the canvas: the mobile
    /// presentation's non-modal bottom drawer is open while either dock exists. Tap-insert
    /// targets the canvas band above the sheet panel in that state (see
    /// <c>HandleQuickInsert</c>); desktop and tablet keep the plain viewport centre.
    /// </summary>
    private bool IsMobileSheetOpen => IsMobileLayout && MobilePanelsAllowed
        && (LeftDockOpen || RightDockOpen);

    /// <summary>
    /// The centre of the canvas band that stays visible above the open mobile sheet panel,
    /// in document coordinates, or null when JS cannot measure it (tests, disposed host, no
    /// overlap). Pairs with the <c>tmDiagramEditor.visibleBandCentre</c> helper: the sheet
    /// ROOT spans the whole stage (it is click-through), so the measurement must use its
    /// <c>.tm-drawer__panel</c>, not the sheet root.
    /// </summary>
    private async Task<(double X, double Y)?> TryGetVisibleBandCentreAsync()
    {
        try
        {
            var anchor = await JS.InvokeAsync<double[]?>("tmDiagramEditor.visibleBandCentre", _editorRoot);
            return anchor is { Length: 2 } ? (anchor[0], anchor[1]) : null;
        }
        catch (JSException)
        {
            // Keep the viewport-centre fallback; the insert still happens.
        }
        catch (JSDisconnectedException)
        {
            // Host is going away — nothing to measure.
        }

        return null;
    }

    /// <summary>Groups that make the editor an editing surface on mobile: when the effective
    /// selection contains none of them (the Viewer preset, or an equivalent ToolbarGroups set) the
    /// editor is a viewer.</summary>
    private const DiagramToolbarGroups MobileEditingGroups =
        DiagramToolbarGroups.History | DiagramToolbarGroups.Clipboard | DiagramToolbarGroups.Tools
        | DiagramToolbarGroups.Format | DiagramToolbarGroups.Insert | DiagramToolbarGroups.Arrange
        | DiagramToolbarGroups.Layout | DiagramToolbarGroups.Import | DiagramToolbarGroups.Templates
        | DiagramToolbarGroups.Panels;

    /// <summary>
    /// Whether the mobile editing surfaces (panels sheet, action bar) render: not for a host-made
    /// viewer — a host-pinned <see cref="ReadOnly"/> or an effective group set without any editing
    /// group (the Modeling preview contract). The user's own Read-only toggle keeps them (the panels
    /// render read-only) so the user can always switch back.
    /// </summary>
    private bool MobilePanelsAllowed => !ReadOnly && (EffectiveToolbarGroups & MobileEditingGroups) != 0;

    /// <summary>The mobile sheet title of the left (palette) panel: the mockup's "Shapes" tab.</summary>
    private string MobileShapesTitle => Loc["TmDiagramEditor_ShapesTab"];

    // A receiver that is deliberately NOT IHandleEvent: EventCallback only re-renders the
    // receiver when it implements IHandleEvent, so this makes the callback SILENT.
    private static readonly object SilentReceiver = new();
    private static readonly EventCallbackFactory SilentCallbacks = new();

    /// <summary>
    /// SILENT two-way sync of the shell's sheet tab: the null receiver means the callback
    /// never re-renders the editor — it mirrors the value the shell raised and arms the
    /// canvas render skip, so a sheet tab gesture re-renders the shell chrome only.
    /// </summary>
    private EventCallback<EditorShellPanel> SilentActivePanelChanged
        => SilentCallbacks.Create<EditorShellPanel>(SilentReceiver, p =>
        {
            _activeMobilePanel = p;
            _canvas?.BeginChromeOnlyPass();
        });

    /// <summary>SILENT two-way sync of the sheet snap — same contract as the tab callback.</summary>
    private EventCallback<int> SilentSnapChanged
        => SilentCallbacks.Create<int>(SilentReceiver, i =>
        {
            _mobileSnapIndex = i;
            _canvas?.BeginChromeOnlyPass();
        });

    // ── The F5 action bar (Select | Pan | Properties | More) ────────────────

    /// <summary>
    /// The shell's mobile actions, or null when the layout is not mobile or the editor is a
    /// viewer (no edit action bar). MaxVisible stays at the bar default of 3: Select, Pan and
    /// Properties are the tiles; the overflow ("More") carries the export commands only —
    /// layout and import are tablet+ commands (G2).
    /// </summary>
    private List<TmActionItem>? EffectiveMobileActions
    {
        get
        {
            if (!IsMobileLayout || !MobilePanelsAllowed || !ShowToolbar) return null;
            return BuildMobileActions();
        }
    }

    private List<TmActionItem> BuildMobileActions()
    {
        var items = new List<TmActionItem>
        {
            new()
            {
                Id = "select",
                Label = Loc["TmDiagramEditor_ToolSelect"],
                Icon = IconNames.MousePointer,
                Checked = _toolMode == "select",
                OnClick = EventCallback.Factory.Create(this, () => SetToolMode("select")),
            },
            new()
            {
                Id = "pan",
                Label = Loc["TmDiagramEditor_ToolPan"],
                Icon = IconNames.Hand,
                Checked = _toolMode == "pan",
                OnClick = EventCallback.Factory.Create(this, () => SetToolMode("pan")),
            },
            new()
            {
                Id = "properties",
                Label = Loc["TmDiagramInspector_PropertiesTab"],
                Icon = IconNames.Sliders,
                OnClick = EventCallback.Factory.Create(this, () => OpenMobileSheet(EditorShellPanel.Right, MobileHalfSnapIndex)),
            },
        };
        // "More" (the bar overflow): export only on mobile — no layout, no import (G2).
        items.AddRange(BuildExportMenuItems());
        return items;
    }

    // ── Sheet open/close ─────────────────────────────────────────────────────

    /// <summary>
    /// Opens the mobile panels sheet on the given panel: the Shapes | Inspector tab strip with
    /// both panels available, at the requested snap (peek from a selection, half from the bar).
    /// This editor pass moves chrome above the canvas only, so the canvas render is skipped —
    /// the action-bar callback's automatic host re-render carries the change.
    /// </summary>
    private void OpenMobileSheet(EditorShellPanel panel, int snapIndex)
    {
        if (!MobilePanelsAllowed) return;
        _toolboxCollapsed = false;
        SetInspectorOpen(true);
        _activeMobilePanel = panel;
        _mobileSnapIndex = snapIndex;
        _canvas?.BeginChromeOnlyPass();
    }

    /// <summary>
    /// Phase 7 (mobile): a selection is an editing intent — the sheet re-opens PEEKED on the
    /// properties panel so the user sees the context of what they tapped. A sheet the user
    /// already drove to half/full on the properties tab keeps that snap.
    /// </summary>
    private void ExpandPropertiesForSelectionOnMobile()
    {
        if (!IsMobileLayout || !MobilePanelsAllowed) return;
        if (_selectedIds.Length == 0) return;

        var sheetWasOpen = !_toolboxCollapsed || !_propsCollapsed || !_layersCollapsed;
        // The user already had the sheet on the properties at half/full: keep their snap.
        var keepSnap = sheetWasOpen
            && _activeMobilePanel == EditorShellPanel.Right
            && _mobileSnapIndex > MobilePeekSnapIndex;
        // Both panels stay available: the sheet keeps its Shapes | Inspector tab strip.
        _toolboxCollapsed = false;
        SetInspectorOpen(true);
        _inspectorTab = "properties";
        _activeMobilePanel = EditorShellPanel.Right;
        if (!keepSnap)
        {
            _mobileSnapIndex = MobilePeekSnapIndex;
        }
    }

    /// <summary>
    /// The mobile context menu is the shared F3 sheet: closing it (Done, Escape or the swipe)
    /// closes the menu state the same way the desktop backdrop does.
    /// </summary>
    private Task OnContextMenuSheetOpenChanged(bool open)
        => open ? Task.CompletedTask : CloseContextMenu();
}
