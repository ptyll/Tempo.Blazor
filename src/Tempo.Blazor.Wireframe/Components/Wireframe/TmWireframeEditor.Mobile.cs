using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Wireframe.Commands;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.Wireframe;

// Mobile layout (wireframe redesign plan ce19aa8c, phase 7, decision G2: viewing + light edits). In the editor container's < 768 px
// band the shell renders its mobile presentation: the canvas plus ONE non-modal bottom sheet (peek / half / full) holding the
// Components palette, the Properties inspector (with the quick actions of the selection) and the Pages | Layers segment, driven by a
// TmMobileActionBar (Components | Pages & layers | Export | More). The rulers, the minimap, resize / rotate handles, the rubber band,
// edge drawing, align / distribute, grouping and import stay tablet+ (the canvas runs with LightEditing). Opening, switching or
// closing the sheet never re-renders the canvas: the canvas gates parent passes (ContentVersion) and the layout is re-measured by script.
public partial class TmWireframeEditor
{
    /// <summary>Peek / half / full snap fractions of the shell stage: a strip with the quick actions, a working half and the full panel.</summary>
    private static readonly double[] MobileSnapPoints = [0.35, 0.7, 1.0];

    private const int MobilePeekSnapIndex = 0;
    private const int MobileHalfSnapIndex = 1;

    // Whether the panels sheet is open (the shell's LeftOpen / RightOpen on a phone: the sheet starts closed at load), the tab it shows
    // and its snap - all two-way with the shell.
    private bool _mobileOpen;
    private EditorShellPanel _mobilePanel;
    private int _mobileSnap;
    // The sheet was opened by a selection (not by an action-bar tile): when the selection becomes empty it closes again.
    private bool _mobileOpenedBySelection;
    private bool _mobileFocusBarPending;
    private bool _mobileScrollResetPending;
    private ElementReference _editorRoot;

    // A token the canvas compares (LightEditing parent-render gate): bumped by every path that may change the document behind its back.
    private int _contentVersion;

    private bool IsMobile => _resolvedLayout == TmLayoutMode.Mobile;

    /// <summary>The action bar, the tool row and the floating zoom render on a phone while the editor shows its toolbar (an embedded viewer has none).</summary>
    private bool HasMobileChrome => IsMobile && ShowToolbar;

    /// <summary>Whether the Pages | Layers segment exists: it is the shell's Extra sheet tab, which needs the inspector beside it.</summary>
    private bool HasMobilePagesLayers => IsMobile && ShowPropertiesPanel;

    /// <summary>With no inspector (so no Extra sheet tab) the pages stay reachable as the classic page tabs above the canvas.</summary>
    private bool ShowsMobilePageTabs => IsMobile && !ShowPropertiesPanel && ShowPageTabs && _document?.Pages.Count > 0;

    private bool RendersPageTabs => IsMobile ? ShowsMobilePageTabs : ShowsPageTabs;

    private bool RendersHeader => HasHeader || HasMobileChrome;

    private bool ShellLeftOpen => IsMobile ? _mobileOpen : IsTablet ? LeftSheetOpen : _leftOpen;

    private bool ShellRightOpen => IsMobile ? _mobileOpen : IsTablet ? InspectorSheetOpen : _rightOpen;

    private bool MobileHasSelection => _selectedIds.Length > 0 || _selectedConnectorIds.Length > 0;

    // ── Slots ─────────────────────────────────────────────────────────────────

    private RenderFragment? ShellLeftSlot => IsMobile ? MobileComponentsSlot : LeftSlot;

    private RenderFragment? ShellRightSlot => IsMobile ? MobileInspectorSlot : RightSlot;

    private RenderFragment? MobileComponentsSlot => !ShowToolbox ? null : SidePanel(components: true, pages: false, layers: false, singleColumn: true);

    private RenderFragment? MobileExtraSlot => !HasMobilePagesLayers
        ? null
        : SidePanel(components: false, pages: ShowPageTabs && _document?.Pages.Count > 0, layers: true, singleColumn: true);

    /// <summary>The Properties sheet tab: the quick actions of the selection (what the peek snap shows) above the full inspector.</summary>
    private RenderFragment? MobileInspectorSlot => !ShowPropertiesPanel ? null : builder =>
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "tm-wd-mobile-inspector");
        if (MobileHasSelection)
        {
            builder.AddContent(2, MobileQuickActions);
        }

        builder.AddContent(3, RightSlot);
        builder.CloseElement();
    };

    private string MobileSelectionTitle
    {
        get
        {
            if (_selectedConnectorIds.Length > 0) return Loc["TmWireframeProps_SectionConnector"];
            if (_selectedIds.Length > 1) return Loc.Plural("TmWireframeProps_ElementsCount", _selectedIds.Length);
            var element = _document?.Elements.FirstOrDefault(e => e.Id == _selectedIds[0]);
            if (element is null) return string.Empty;
            return string.IsNullOrWhiteSpace(element.Name)
                ? _registry.GetDef(element.Type, ComponentScope)?.DisplayName ?? element.Type
                : element.Name!;
        }
    }

    /// <summary>One of the shell's sheet panels: the internal side panel with the requested tabs (a lone tab drops the strip).</summary>
    private RenderFragment SidePanel(bool components, bool pages, bool layers, bool singleColumn) => builder =>
    {
        builder.OpenComponent<TmWireframeSidePanel>(0);
        builder.AddComponentParameter(1, nameof(TmWireframeSidePanel.Document), _document);
        builder.AddComponentParameter(2, nameof(TmWireframeSidePanel.DocumentChanged), EventCallback.Factory.Create<WireframeDocument>(this, OnDocumentChanged));
        builder.AddComponentParameter(3, nameof(TmWireframeSidePanel.ShowComponents), components);
        builder.AddComponentParameter(4, nameof(TmWireframeSidePanel.ShowPages), pages);
        builder.AddComponentParameter(5, nameof(TmWireframeSidePanel.ShowLayers), layers);
        builder.AddComponentParameter(6, nameof(TmWireframeSidePanel.ReadOnly), IsReadOnly);
        builder.AddComponentParameter(7, nameof(TmWireframeSidePanel.ComponentScope), ComponentScope);
        builder.AddComponentParameter(8, nameof(TmWireframeSidePanel.OnComponentActivated), EventCallback.Factory.Create<string>(this, OnToolboxActivated));
        builder.AddComponentParameter(9, nameof(TmWireframeSidePanel.SelectedElementIds), _selectedIds);
        builder.AddComponentParameter(10, nameof(TmWireframeSidePanel.ActiveLayerId), _activeLayerId);
        builder.AddComponentParameter(11, nameof(TmWireframeSidePanel.ActiveLayerIdChanged), EventCallback.Factory.Create<string?>(this, OnActiveLayerChanged));
        builder.AddComponentParameter(12, nameof(TmWireframeSidePanel.ActivePageChanged), EventCallback.Factory.Create<string>(this, OnPagesPanelActivePageChanged));
        builder.AddComponentParameter(13, nameof(TmWireframeSidePanel.SingleColumn), singleColumn);
        builder.AddComponentParameter(14, nameof(TmWireframeSidePanel.TestIdPrefix), TestIdPrefix);
        builder.CloseComponent();
    };

    // ── Shell two-way state ───────────────────────────────────────────────────

    private void OnShellMobilePanelChanged(EditorShellPanel panel)
    {
        _mobilePanel = panel;
        _mobileOpenedBySelection = false;   // the user chose a tab: the sheet is theirs now, an empty-canvas tap must not close it
        _mobileScrollResetPending = true;   // the sheet body is ONE scroller shared by every tab: each tab starts at its top
    }

    private void OnShellMobileSnapChanged(int index)
    {
        _mobileSnap = index;
        _mobileOpenedBySelection = false;   // dragging the sheet to another snap is the user taking it over
    }

    /// <summary>The shell closed or reopened its sheet (the × button, Escape, a swipe, or its own panels toggle on an embedded editor).</summary>
    private void OnMobileShellOpenChanged(bool open)
    {
        if (_mobileOpen == open) return;
        _mobileOpen = open;
        _mobileOpenedBySelection = false;
        if (!open)
        {
            // The sheet (with its close button) unmounts: the action bar is the one reopen path, so the focus goes there.
            _mobileFocusBarPending = HasMobileChrome;
        }

        _relayoutPending = true;
    }

    private void OpenMobileSheet(EditorShellPanel panel, int snapIndex, bool bySelection = false)
    {
        _mobileOpen = true;
        _mobilePanel = panel;
        _mobileSnap = snapIndex;
        _mobileOpenedBySelection = bySelection;
        _mobileScrollResetPending = true;
        _relayoutPending = true;
    }

    private void CloseMobileSheet()
    {
        if (!_mobileOpen) return;
        _mobileOpen = false;
        _mobileOpenedBySelection = false;
        // A quick action (Delete, Lock) that closes the sheet unmounts the very button that had the focus: hand it to the action bar
        // (the script moves it only when it was really lost, so a focus the user placed elsewhere is never taken away).
        _mobileFocusBarPending = HasMobileChrome;
        _relayoutPending = true;
    }

    private bool MobileSheetShows(EditorShellPanel panel) => _mobileOpen && _mobilePanel == panel;

    private void ToggleMobileComponents()
    {
        if (MobileSheetShows(EditorShellPanel.Left)) CloseMobileSheet();
        else OpenMobileSheet(EditorShellPanel.Left, MobileHalfSnapIndex);
    }

    private void ToggleMobilePagesLayers()
    {
        if (MobileSheetShows(EditorShellPanel.Extra)) CloseMobileSheet();
        else OpenMobileSheet(EditorShellPanel.Extra, MobileHalfSnapIndex);
    }

    /// <summary>
    /// A selection is an editing intent: the sheet opens PEEKED on the properties so the user sees what they touched. A sheet the user
    /// already had open on the inspector (Properties, or the Pages | Layers tab) keeps its tab and its snap.
    /// </summary>
    private void RaiseMobileSheetForSelection()
    {
        if (!IsMobile || !ShowPropertiesPanel) return;
        var keepContext = _mobileOpen && (_mobilePanel == EditorShellPanel.Right || _mobilePanel == EditorShellPanel.Extra);
        if (keepContext) return;
        OpenMobileSheet(EditorShellPanel.Right, MobilePeekSnapIndex, bySelection: !_mobileOpen);
    }

    /// <summary>The selection became empty: a sheet that a selection opened has nothing left to show and gives the canvas its room back.</summary>
    private void MobileSelectionCleared()
    {
        if (!IsMobile) return;
        // A quick action (Delete / Lock) replaces the button that held the focus, whether the sheet closes or stays: ask the script to put
        // the focus on the action bar - it moves it only when it was really lost, so a focus the user placed elsewhere is never taken.
        _mobileFocusBarPending = HasMobileChrome;
        if (_mobileOpenedBySelection) CloseMobileSheet();
    }

    // ── Action bar ────────────────────────────────────────────────────────────

    /// <summary>
    /// The shell's mobile actions: Components, Pages &amp; layers and Export are the tiles; the "More" menu carries the device presets,
    /// the grid and read-only toggles and the JSON / SVG exports (align, grouping, rulers and import are tablet+). The host's
    /// <see cref="MobileActions"/> follow the built-ins: a host action pinned <see cref="ActionOverflow.Never"/> keeps a tile, the rest
    /// join the "More" menu by their rank.
    /// </summary>
    private List<TmActionItem>? EffectiveMobileActions => HasMobileChrome ? BuildMobileActions() : null;

    private List<TmActionItem> BuildMobileActions()
    {
        var items = new List<TmActionItem>();
        if (ShowToolbox)
        {
            items.Add(new TmActionItem
            {
                Id = "components",
                // "Add": a one-word, one-line tile at 320 px (the palette it opens is titled Components) - "Components" broke mid-word.
                Label = Loc["TmWireframe_MobileAdd"],
                Icon = IconNames.Plus,
                Priority = 30,
                Checked = MobileSheetShows(EditorShellPanel.Left),
                OnClick = EventCallback.Factory.Create(this, ToggleMobileComponents),
            });
        }

        if (HasMobilePagesLayers)
        {
            items.Add(new TmActionItem
            {
                Id = "pages-layers",
                Label = Loc["TmWireframe_MobilePagesLayers"],
                Icon = IconNames.Layers,
                Priority = 20,
                Checked = MobileSheetShows(EditorShellPanel.Extra),
                OnClick = EventCallback.Factory.Create(this, ToggleMobilePagesLayers),
            });
        }

        items.Add(new TmActionItem
        {
            Id = "export",
            Label = Loc["TmWireframe_Export"],
            Icon = IconNames.Download,
            Priority = 10,
            OnClick = EventCallback.Factory.Create(this, ExportPng),
        });

        if (MobileActions is { Count: > 0 }) items.AddRange(MobileActions);

        var page = _activePage;
        foreach (var preset in WireframeDevicePresets.All.Where(p => !p.IsCustom))
        {
            var current = page is not null && Math.Abs(page.Width - preset.Width!.Value) < 0.5 && Math.Abs(page.Height - preset.Height!.Value) < 0.5;
            var chosen = preset;
            items.Add(new TmActionItem
            {
                Id = "wd-device-" + preset.Id,
                Label = $"{Loc[preset.NameKey]} ({(int)preset.Width!.Value}×{(int)preset.Height!.Value})",
                Overflow = ActionOverflow.Always,
                Checked = current,
                Disabled = IsReadOnly,
                Group = "device",
                GroupLabel = Loc["TmWireframeProps_DevicePreset"],
                OnClick = EventCallback.Factory.Create(this, () => ApplyDevicePresetAsync(chosen)),
            });
        }

        items.Add(new TmActionItem
        {
            Id = "wd-grid",
            Label = Loc["TmWireframeProps_ShowGrid"],
            Overflow = ActionOverflow.Always,
            Checked = _showGrid,
            Group = "view",
            GroupLabel = Loc["TmWireframe_GroupView"],
            OnClick = EventCallback.Factory.Create(this, () => OnInspectorShowGridChanged(!_showGrid)),
        });
        if (ShowReadOnlyToggle)
        {
            items.Add(new TmActionItem
            {
                Id = "wd-readonly",
                Label = Loc["TmWireframe_ReadOnly"],
                Overflow = ActionOverflow.Always,
                Checked = IsReadOnly,
                Group = "view",
                GroupLabel = Loc["TmWireframe_GroupView"],
                OnClick = EventCallback.Factory.Create(this, ToggleReadOnly),
            });
        }

        items.Add(new TmActionItem
        {
            Id = "wd-export-json",
            Label = Loc["TmWireframe_ExportJson"],
            Icon = IconNames.FileText,
            Overflow = ActionOverflow.Always,
            Group = "file",
            GroupLabel = Loc["TmWireframe_GroupFile"],
            OnClick = EventCallback.Factory.Create(this, ExportJson),
        });
        items.Add(new TmActionItem
        {
            Id = "wd-export-svg",
            Label = Loc["TmWireframe_ExportSvg"],
            Icon = IconNames.Image,
            Overflow = ActionOverflow.Always,
            Group = "file",
            GroupLabel = Loc["TmWireframe_GroupFile"],
            OnClick = EventCallback.Factory.Create(this, ExportSvg),
        });
        return items;
    }

    /// <summary>A device preset resizes the active page through the undoable <see cref="ResizeCanvasCommand"/> (the inspector does the same).</summary>
    private async Task ApplyDevicePresetAsync(WireframeDevicePreset preset)
    {
        var page = _document?.ActivePage;
        if (_document is null || page is null || IsReadOnly || preset.IsCustom) return;
        double w = preset.Width!.Value, h = preset.Height!.Value;
        if (Math.Abs(w - page.Width) < 0.5 && Math.Abs(h - page.Height) < 0.5) return;

        _commandStack.Push(new ResizeCanvasCommand(_document, page.Width, page.Height, w, h));
        if (_canvas is not null) await _canvas.UpdateCanvasSize(w, h);
        await RerenderCanvas();
    }

    // ── Focus ─────────────────────────────────────────────────────────────────

    /// <summary>After the sheet unmounted: puts the focus on the first tile of the action bar (a no-op when something else holds it).</summary>
    private async Task MobileAfterRenderAsync()
    {
        if (_mobileScrollResetPending && _mobileOpen)
        {
            _mobileScrollResetPending = false;
            try { await JS.InvokeVoidAsync("tmWireframeDesigner.resetMobileSheetScroll", _editorRoot); }
            catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException) { /* no script */ }
        }

        if (!_mobileFocusBarPending) return;
        _mobileFocusBarPending = false;
        try { await JS.InvokeVoidAsync("tmWireframeDesigner.focusFirstMobileAction", _editorRoot); }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException) { /* no bar / no script */ }
    }

    // ── The layout flipped to / from mobile ───────────────────────────────────

    private void EnterOrLeaveMobile(bool wasMobile)
    {
        if (wasMobile == IsMobile) return;
        // The panels start closed on a phone, and a sheet never survives a flip (the desktop panels are the shell's, the tablet ones ours).
        _mobileOpen = false;
        _mobilePanel = EditorShellPanel.None;
        _mobileSnap = 0;
        _mobileOpenedBySelection = false;
        _mobileFocusBarPending = false;
        // The context menu swaps presentation (anchored <-> sheet) and its anchor is stale after a flip, and the connector tool is
        // tablet+ (the script falls back to select without telling .NET): close the menu, reset the tool.
        if (_ctxMenuOpen) CloseContextMenu();
        if (IsMobile && _toolMode == "connector") _toolMode = "select";
        _contentVersion++;
    }
}
