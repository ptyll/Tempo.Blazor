using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Layout;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// Phase 6 (tablet and touch, DI3): the editor drives the core <see cref="TmEditorShell"/> tablet
/// presentation. The shell resolves the layout from its own container width — tablet spans the
/// 768–1199px band (host-overridable thresholds) — and keeps at most one docked panel expanded,
/// rendering the other as the 44px rail. On a tablet layout the palette rails by default, the
/// minimap floats over the canvas as an overlay, and selecting a node raises the inspector back
/// up. Expanding or collapsing a panel asks the canvas to re-measure (JS <c>relayout</c>) without
/// a Blazor re-render of the canvas.
/// </summary>
public partial class TmDiagramEditor
{
    // ── Phase-6 layout parameters ────────────────────────────────────────────

    /// <summary>
    /// How the editor resolves its layout: <see cref="TmLayoutMode.Auto"/> measures the editor's
    /// own container width against <see cref="LayoutBreakpoints"/> (tablet 768–1199, desktop from
    /// 1200); a forced <see cref="TmLayoutMode.Tablet"/> / <see cref="TmLayoutMode.Desktop"/> /
    /// <see cref="TmLayoutMode.Mobile"/> pins the presentation for testing and host layouts.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>
    /// Per-instance layout thresholds for the editor container. Null uses the editor default —
    /// tablet from 768 and desktop from 1200, the phase-6 band. A host may pass its own pair
    /// (e.g. <c>new TmLayoutBreakpoints(640, 1024)</c> to follow the shared TmBreakpoints).
    /// </summary>
    [Parameter] public TmLayoutBreakpoints? LayoutBreakpoints { get; set; }

    /// <summary>
    /// Where the minimap renders. <see cref="DiagramMinimapPlacement.Auto"/> shows the right-column
    /// card on a desktop layout and the canvas overlay from tablet down (DI3); the other values
    /// force the choice for every layout.
    /// </summary>
    [Parameter] public DiagramMinimapPlacement MinimapPlacement { get; set; } = DiagramMinimapPlacement.Auto;

    /// <summary>The editor's own tablet/desktop thresholds (the phase-6 band).</summary>
    private static readonly TmLayoutBreakpoints EditorBreakpoints = new(768, 1200);

    // ── Resolved layout state ────────────────────────────────────────────────

    /// <summary>The layout the shell currently renders (reported through <see cref="ResolvedLayoutChanged"/>).</summary>
    private TmLayoutMode _resolvedLayout = TmLayoutMode.Desktop;

    /// <summary>The collapse set in effect, two-way bound with the shell. The shell raises it when
    /// it rails a side (at most one expanded panel on a tablet layout) and when its toggles run.</summary>
    private EditorShellPanel _shellCollapsed;

    /// <summary>The user's collapse choice before the layout turned tablet; restored when it turns back.</summary>
    private EditorShellPanel _preTabletCollapsed;

    /// <summary>Whether the minimap renders as the canvas overlay (DI3).</summary>
    private bool MinimapAsOverlay => MinimapPlacement switch
    {
        DiagramMinimapPlacement.Overlay => true,
        DiagramMinimapPlacement.Panel => false,
        _ => _resolvedLayout is TmLayoutMode.Tablet or TmLayoutMode.Mobile,
    };

    private Task OnShellResolvedLayoutChanged(TmLayoutMode mode)
    {
        if (_resolvedLayout == mode) return Task.CompletedTask;
        ApplyResolvedLayout(mode);
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>Enters/exits the tablet presentation around the resolved layout.</summary>
    private void ApplyResolvedLayout(TmLayoutMode mode)
    {
        var previous = _resolvedLayout;
        _resolvedLayout = mode;

        if (mode == TmLayoutMode.Tablet && previous != TmLayoutMode.Tablet)
        {
            // Entering the tablet band: the palette yields to the inspector (the shell enforces
            // the same outcome on its own, this keeps the editor's panel state in step). The
            // shell may have raised the rail BEFORE reporting the layout — its SyncAsync runs
            // first, and the desktop mirror pass then hid the palette — so the entry re-claims
            // it: on a tablet layout the rail is the palette's collapsed representation, and
            // LeftOpen must stay true for the rail to render at all.
            _preTabletCollapsed = _shellCollapsed;
            if (ShowToolbox && RightDockOpen)
            {
                _toolboxCollapsed = false;
                _shellCollapsed |= EditorShellPanel.Left;
            }
        }
        else if (previous == TmLayoutMode.Tablet && mode != TmLayoutMode.Tablet)
        {
            // Back to desktop/mobile: give the user their pre-tablet choice back. The Left bit
            // the shell imposed at tablet entry (captured before the layout was known) is not a
            // user choice — strip it and re-derive the desktop mirror for the panel flags.
            _shellCollapsed = _preTabletCollapsed & ~EditorShellPanel.Left;
            if (ShowToolbox) _toolboxCollapsed = _shellCollapsed.HasFlag(EditorShellPanel.Left);
            if (_shellCollapsed.HasFlag(EditorShellPanel.Right))
            {
                _propsCollapsed = true;
                _layersCollapsed = true;
            }
        }
    }

    /// <summary>
    /// The shell railed a side (tablet swap or its own toggles). On a tablet layout the flags
    /// mean RAIL — the panel behind a rail stays "open" or the rail would degrade into the
    /// closed-state toggle — so the editor's own collapsed flags are only mirrored on the
    /// desktop layout, where the set really is the user's hide/show choice.
    /// </summary>
    private Task OnShellCollapsedPanelsChanged(EditorShellPanel collapsed)
    {
        _shellCollapsed = collapsed;
        if (_resolvedLayout != TmLayoutMode.Tablet)
        {
            if (ShowToolbox) _toolboxCollapsed = collapsed.HasFlag(EditorShellPanel.Left);
            if (collapsed.HasFlag(EditorShellPanel.Right))
            {
                _propsCollapsed = true;
                _layersCollapsed = true;
            }
        }
        return ScheduleCanvasRelayoutAsync();
    }

    /// <summary>Selection is an editing intent: on a tablet layout a node selection raises the
    /// inspector even when the palette took the single expanded dock slot.</summary>
    private void ExpandInspectorForSelectionOnTablet()
    {
        if (_resolvedLayout != TmLayoutMode.Tablet) return;
        if (_selectedIds.Length == 0) return;
        if (!_shellCollapsed.HasFlag(EditorShellPanel.Right)) return;
        if (!InspectorVisible && !ShowMinimap) return;

        _shellCollapsed &= ~EditorShellPanel.Right;
        if (_propsCollapsed && ShowPropertiesPanel)
        {
            _propsCollapsed = false;
            _inspectorTab = "properties";
        }
        else if (_layersCollapsed && ShowLayersPanel)
        {
            _layersCollapsed = false;
            _inspectorTab = "layers";
        }
    }

    // ── Canvas re-measure ────────────────────────────────────────────────────

    /// <summary>After the chrome around the canvas changed, the canvas must re-measure its
    /// viewport and report the new zoom — through JS, without a Blazor re-render of the canvas.</summary>
    private Task ScheduleCanvasRelayoutAsync()
    {
        if (_canvas is not null) return _canvas.NotifyViewportChangedAsync();
        return Task.CompletedTask;
    }

    private Task OnShellLeftWidthChanged(string width) => ScheduleCanvasRelayoutAsync();

    private Task OnShellRightWidthChanged(string width) => ScheduleCanvasRelayoutAsync();
}
