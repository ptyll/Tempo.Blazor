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
/// sheet (peek/half/full) holding the flat Shapes | Properties | Layers tabs, the F5 action bar
/// (Select | Shapes | Properties, Pan and the exports in More), a floating zoom overlay and
/// the opt-in floating minimap ("⋯"), and the long-press context menu as a sheet. Viewing
/// survives everywhere; the full editing chrome (resize/rotate, edge drawing, layout, import)
/// stays tablet+ and the More menu carries the export commands only.
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

    /// <summary>X2: the layers panel as the shell's third flat sheet tab (Shapes | Properties | Layers) — mobile only, when both panels exist.</summary>
    private RenderFragment? MobileExtraSlot => IsMobileLayout && MobilePanelsAllowed && InspectorHasTabs
        ? LayersPanelContent(false)
        : null;

    /// <summary>Mobile right sheet tab title: the panel it really holds.</summary>
    private string ShellRightTitle => IsMobileLayout
        ? (PropertiesAvailable ? Loc["TmDiagramInspector_PropertiesTab"] : Loc["TmDiagramInspector_LayersTab"])
        : InspectorTitle;

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

    // ── The F5 action bar (Select | Shapes | Properties | More) ─────────────

    /// <summary>Whether the editor renders its mobile action bar (the sheet's one reopen path —
    /// the shell's own closed-state panels toggle stays hidden then, see
    /// <c>ShowMobilePanelsToggle</c>).</summary>
    private bool HasMobileActionBar => IsMobileLayout && MobilePanelsAllowed && ShowToolbar;

    /// <summary>
    /// The shell's mobile actions, or null when the layout is not mobile or the editor is a
    /// viewer (no edit action bar). MaxVisible stays at the bar default of 3: the mode tile
    /// (Select, or Pan while panning), Shapes and Properties are the tiles; the overflow ("More")
    /// carries Pan (while Select is active) and the export commands — layout and import are
    /// tablet+ commands (G2).
    /// </summary>
    private List<TmActionItem>? EffectiveMobileActions
        => HasMobileActionBar ? BuildMobileActions() : null;

    private List<TmActionItem> BuildMobileActions()
    {
        // UX review: the first tile mirrors the active tool mode — with Pan armed it IS the Pan
        // tile (hand icon, checked; tapping returns to Select), and Pan moves out of "More".
        // With Select active the first tile is Select and Pan stays in "More" (checkable).
        var modeItem = _toolMode == "pan"
            ? new TmActionItem
            {
                Id = "pan",
                Label = Loc["TmDiagramEditor_ToolPan"],
                Icon = IconNames.Hand,
                Checked = true,
                OnClick = EventCallback.Factory.Create(this, () => SetToolMode("select")),
            }
            : new TmActionItem
            {
                Id = "select",
                Label = Loc["TmDiagramEditor_ToolSelect"],
                Icon = IconNames.MousePointer,
                Checked = _toolMode == "select",
                OnClick = EventCallback.Factory.Create(this, () => SetToolMode("select")),
            };

        var items = new List<TmActionItem>
        {
            modeItem,
            new()
            {
                Id = "shapes",
                Label = Loc["TmDiagramEditor_ShapesTab"],
                Icon = IconNames.Square,
                // UX review: the tile is checked while the sheet is open on the Shapes tab;
                // tapping the active tile closes the sheet again.
                Checked = IsMobileSheetOpen && _activeMobilePanel == EditorShellPanel.Left,
                OnClick = EventCallback.Factory.Create(this, ToggleMobileShapesSheet),
            },
            new()
            {
                Id = "properties",
                Label = Loc["TmDiagramInspector_PropertiesTab"],
                Icon = IconNames.Sliders,
                Checked = IsMobileSheetOpen && _activeMobilePanel == EditorShellPanel.Right,
                OnClick = EventCallback.Factory.Create(this, ToggleMobilePropertiesSheet),
            },
        };
        // "More" (the bar overflow): Pan (while Select is the active mode) and the export
        // commands — no layout, no import (G2).
        if (_toolMode != "pan")
        {
            items.Add(new()
            {
                Id = "pan",
                Label = Loc["TmDiagramEditor_ToolPan"],
                Icon = IconNames.Hand,
                Checked = _toolMode == "pan",
                OnClick = EventCallback.Factory.Create(this, () => SetToolMode("pan")),
            });
        }
        items.AddRange(BuildExportMenuItems());
        return items;
    }

    // ── Sheet open/close ─────────────────────────────────────────────────────

    /// <summary>UX review: tapping the active Shapes tile closes the sheet; otherwise it opens
    /// the Shapes tab (half snap).</summary>
    private void ToggleMobileShapesSheet()
    {
        if (IsMobileSheetOpen && _activeMobilePanel == EditorShellPanel.Left)
        {
            CloseMobileSheet();
        }
        else
        {
            OpenMobileSheet(EditorShellPanel.Left, MobileHalfSnapIndex);
        }
    }

    /// <summary>UX review: tapping the active Properties tile closes the sheet; otherwise it
    /// opens the Properties tab (half snap).</summary>
    private void ToggleMobilePropertiesSheet()
    {
        if (IsMobileSheetOpen && _activeMobilePanel == EditorShellPanel.Right)
        {
            CloseMobileSheet();
        }
        else
        {
            OpenMobileSheet(EditorShellPanel.Right, MobileHalfSnapIndex);
        }
    }

    /// <summary>Closes the mobile panels sheet: both panels collapse (the shell's sheet unmounts)
    /// and the canvas render is skipped — the chrome change re-renders the shell only.</summary>
    private void CloseMobileSheet()
    {
        _toolboxCollapsed = true;
        SetInspectorOpen(false);
        _canvas?.BeginChromeOnlyPass();
    }

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
    /// properties panel so the user sees the context of what they tapped. UX review: a sheet the
    /// user already had open on the INSPECTOR (the Properties tab or the Layers tab — the shell's
    /// Extra panel) keeps its tab AND snap; the tab switches to Properties only from a closed
    /// sheet or from the Shapes tab.
    /// </summary>
    private void ExpandPropertiesForSelectionOnMobile()
    {
        if (!IsMobileLayout || !MobilePanelsAllowed) return;
        if (_selectedIds.Length == 0) return;

        var sheetWasOpen = !_toolboxCollapsed || !_propsCollapsed || !_layersCollapsed;
        var keepInspectorContext = sheetWasOpen
            && (_activeMobilePanel == EditorShellPanel.Right || _activeMobilePanel == EditorShellPanel.Extra);
        // Both panels stay available: the sheet keeps its Shapes | Properties | Layers tab strip.
        _toolboxCollapsed = false;
        SetInspectorOpen(true);
        _inspectorTab = "properties";
        if (!keepInspectorContext)
        {
            _activeMobilePanel = EditorShellPanel.Right;
            _mobileSnapIndex = MobilePeekSnapIndex;
        }
        // U3: the peeked sheet covers the lower canvas — pan so the selection stays visible
        // above the sheet once this render lands.
        _revealSelectionPending = true;
    }

    /// <summary>Armed by <see cref="ExpandPropertiesForSelectionOnMobile"/>: the JS pan runs after the render so the sheet's new snap has laid out.</summary>
    private bool _revealSelectionPending;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        // Fáze 9: FitOnDocumentLoad fires after the render that mounted the new document. The
        // editor's render hook runs BEFORE the canvas' own — a direct fit here would race the
        // canvas' JS boot (an empty container ref, a no-op scale) — so the fit is handed to the
        // canvas and consumed right after its init (the engine reports the fitted scale back
        // through the ZoomChanged event, which re-renders the zoom label).
        // Fáze 9 review: the handover is OWED until a canvas actually fitted — a mobile↔docked
        // flip remounts the canvas (the shell renders @Canvas in different places) and the
        // remounted instance has lost the handed-over state, so the fit is re-armed on every
        // canvas that is not the one it was already armed on. The canvas' onFitted callback
        // retires the owed flag after the real fit (an instance that dies before fitting leaves
        // the flag armed for its replacement).
        if (_fitOwed && _pendingFitOnLoad is { } pendingFit
            && _canvas is not null && !ReferenceEquals(_canvas, _fitArmedCanvas))
        {
            _fitArmedCanvas = _canvas;
            _canvas.RequestFitWhenReady(pendingFit, () =>
            {
                _fitOwed = false;
                _fitArmedCanvas = null;
            });
        }

        // The render served the layout generation it was rendered with: from here on a collapse raise is a
        // USER action again (U2/D27).
        SettleLayoutGeneration();

        // UX review: the minimap overlay collapse/reopen and the mobile "⋯" menu actions re-assert
        // focus on the control that owns the map next — through the element reference, so a layout
        // flip that unmounted the target simply skips (TryFocusAsync).
        if (_minimapFocusTarget != MinimapFocusTarget.None)
        {
            var target = _minimapFocusTarget;
            _minimapFocusTarget = MinimapFocusTarget.None;
            switch (target)
            {
                case MinimapFocusTarget.CollapseButton: await TryFocusAsync(_minimapCollapseBtn); break;
                case MinimapFocusTarget.ReopenButton: await TryFocusAsync(_minimapReopenBtn); break;
                case MinimapFocusTarget.ViewMenuTrigger: await TryFocusAsync(_viewMenuTrigger); break;
            }
        }

        if (_viewMenuTriggerFocusPending)
        {
            _viewMenuTriggerFocusPending = false;
            await TryFocusAsync(_viewMenuTrigger);
        }

        if (_mobileActionBarFocusPending)
        {
            _mobileActionBarFocusPending = false;
            await FocusMobileActionBarAsync();
        }

        if (!_revealSelectionPending) return;
        _revealSelectionPending = false;
        if (!IsMobileLayout || _document is null || _selectedIds.Length == 0) return;
        var nodes = _document.Nodes.Where(n => _selectedIds.Contains(n.Id)).ToList();
        if (nodes.Count == 0) return; // edge-only selection: edges have no stored box
        var x = nodes.Min(n => n.X); var y = nodes.Min(n => n.Y);
        var r = nodes.Max(n => n.X + n.W); var b = nodes.Max(n => n.Y + n.H);
        try { await JS.InvokeAsync<bool>("tmDiagramEditor.revealRectAboveSheet", _editorRoot, x, y, r - x, b - y, 16); }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
    }

    /// <summary>
    /// Re-asserts focus on an element that may have unmounted in the same batch (a layout flip) or
    /// whose JS focus call may fail (prerendering, a disconnecting host): every outcome just skips.
    /// </summary>
    private static async Task TryFocusAsync(ElementReference element)
    {
        try
        {
            await element.FocusAsync();
        }
        catch (InvalidOperationException)
        {
            // The target is not in the DOM (a layout flip happened in the same batch).
        }
        catch (JSException)
        {
            // The JS focus call failed; the browser keeps its own focus placement.
        }
        catch (JSDisconnectedException)
        {
            // Host is going away.
        }
    }

    /// <summary>
    /// The mobile context menu is the shared F3 sheet: closing it (Done, Escape or the swipe)
    /// closes the menu state the same way the desktop backdrop does.
    /// </summary>
    private Task OnContextMenuSheetOpenChanged(bool open)
        => open ? Task.CompletedTask : CloseContextMenu();
}
