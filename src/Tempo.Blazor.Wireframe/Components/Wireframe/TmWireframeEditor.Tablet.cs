using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Layout;

namespace Tempo.Blazor.Components.Wireframe;

// Tablet layout (wireframe redesign plan ce19aa8c, phase 6; core presentation since plan 643bba06, phase 5): in the editor
// container's 768–1199 px band the docked panels give way to ONE non-modal side sheet at a time - the left panel (Components /
// Pages / Layers) as an overlay drawer, the inspector as a side sheet with a close button. Both are the core TmEditorShell's
// TabletPanelPresentation.Overlay (decision EDITOR-SHELL-TABLET-PRESENTATION): the editor owns WHICH sheet is open (the flags
// below, two-way with the shell's LeftOpen / RightOpen), the shell renders it, traps Escape and restores the focus. Opening or
// closing a sheet asks the canvas to re-measure its viewport by script (no canvas re-render).
public partial class TmWireframeEditor
{
    // Tablet: the left panel (Components / Pages / Layers) is an overlay drawer, the inspector a side sheet; at most ONE is open.
    private bool _leftSheetOpen;
    private bool _inspectorSheetOpen;
    private bool _relayoutPending;

    private void ApplyResolvedLayout(TmLayoutMode mode)
    {
        if (_resolvedLayout == mode) return;
        var wasTablet = IsTablet;
        var wasMobile = IsMobile;
        _resolvedLayout = mode;
        EnterOrLeaveMobile(wasMobile);
        // Leaving the tablet layout drops its sheets (desktop and mobile have their own panel state).
        if (wasTablet && !IsTablet && (_leftSheetOpen || _inspectorSheetOpen))
        {
            _leftSheetOpen = false;
            _inspectorSheetOpen = false;
        }

        // The canvas box changed with the layout: re-measure it by script, without re-rendering the canvas.
        _relayoutPending = true;
    }

    // ── Tablet sheets ─────────────────────────────────────────────────────────

    private bool IsTablet => _resolvedLayout == TmLayoutMode.Tablet;

    private bool LeftSheetOpen => IsTablet && _leftSheetOpen && HasLeftContent;

    private bool InspectorSheetOpen => IsTablet && _inspectorSheetOpen && ShowPropertiesPanel;

    private void ToggleLeftSheet()
    {
        _leftSheetOpen = !LeftSheetOpen;
        _inspectorSheetOpen = false;
        _relayoutPending = true;
    }

    private void ToggleInspectorSheet()
    {
        _inspectorSheetOpen = !InspectorSheetOpen;
        _leftSheetOpen = false;
        _relayoutPending = true;
    }

    /// <summary>The shell closed the drawer (its × or Escape) or opened it: the flag follows, and the canvas re-measures.</summary>
    private void OnTabletLeftOpenChanged(bool open)
    {
        if (_leftSheetOpen == open) return;
        _leftSheetOpen = open;
        if (open) _inspectorSheetOpen = false;
        _relayoutPending = true;
    }

    /// <summary>The shell closed the inspector sheet (its × or Escape) or opened it.</summary>
    private void OnTabletRightOpenChanged(bool open)
    {
        if (_inspectorSheetOpen == open) return;
        _inspectorSheetOpen = open;
        if (open) _leftSheetOpen = false;
        _relayoutPending = true;
    }

    private void CloseSheet()
    {
        if (!_leftSheetOpen && !_inspectorSheetOpen) return;
        _leftSheetOpen = false;
        _inspectorSheetOpen = false;
        _relayoutPending = true;
    }

    /// <summary>Selecting something on a tablet is an editing intent: the inspector sheet opens (and the left drawer yields).</summary>
    private void RaiseInspectorOnTablet()
    {
        if (!IsTablet || !ShowPropertiesPanel || _inspectorSheetOpen) return;
        _inspectorSheetOpen = true;
        _leftSheetOpen = false;
        _relayoutPending = true;
    }

    /// <summary>The selection became empty: the inspector sheet has nothing to show, so it closes (the canvas gets its room back).</summary>
    private void CloseInspectorSheetOnTablet()
    {
        if (!IsTablet || !_inspectorSheetOpen) return;
        _inspectorSheetOpen = false;
        _relayoutPending = true;
    }

    /// <summary>A press on the canvas while the left drawer is open dismisses it (the drawer is a transient picker over the canvas).</summary>
    private void OnCanvasPointerDown()
    {
        if (LeftSheetOpen) CloseSheet();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await MobileAfterRenderAsync();

        if (_relayoutPending && _canvas is not null)
        {
            _relayoutPending = false;
            await _canvas.NotifyViewportChangedAsync();
        }
    }
}
