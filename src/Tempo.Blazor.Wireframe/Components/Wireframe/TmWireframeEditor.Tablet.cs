using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Layout;

namespace Tempo.Blazor.Components.Wireframe;

// Tablet layout (wireframe redesign plan ce19aa8c, phase 6): in the editor container's 768–1199 px band the docked panels
// give way to ONE non-modal side sheet at a time - the left panel (Components / Pages / Layers) as an overlay drawer, the
// inspector as a side sheet with a close button. Opening or closing a sheet asks the canvas to re-measure its viewport by
// script (no canvas re-render).
public partial class TmWireframeEditor
{
    // Tablet: the left panel (Components / Pages / Layers) is an overlay drawer, the inspector a side sheet; at most ONE is open.
    private bool _leftSheetOpen;
    private bool _inspectorSheetOpen;
    private bool _relayoutPending;
    private bool _focusSheetAfter;
    // Shared by both sheets: only one sheet is ever open (opening one closes the other), so a single ref is never ambiguous.
    private ElementReference _sheetCloseRef;
    // Names the open sheet (aria-labelledby); unique per editor instance so two editors on a page never share an id.
    private readonly string _sheetTitleId = "tm-wd-sheet-title-" + Guid.NewGuid().ToString("N")[..8];

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

        // Entering the tablet layout: the shell still held both panels when it measured (it rails one of them and raises
        // CollapsedPanelsChanged BEFORE this callback). That rail is not a user choice and the tablet has no docked panels, so
        // it must not survive the trip back to desktop (a host-bound collapse set stays the truth for the shell).
        if (IsTablet) _collapsedPanels = EditorShellPanel.None;

        // The canvas box changed with the layout: re-measure it by script, without re-rendering the canvas.
        _relayoutPending = true;
    }

    // ── Tablet sheets ─────────────────────────────────────────────────────────

    private bool IsTablet => _resolvedLayout == TmLayoutMode.Tablet;

    private bool LeftSheetOpen => IsTablet && _leftSheetOpen && HasLeftContent;

    private bool InspectorSheetOpen => IsTablet && _inspectorSheetOpen && ShowPropertiesPanel;

    private ElementReference? CanvasFocusTarget => _canvas is null ? null : _canvas.WrapElement;

    private void ToggleLeftSheet()
    {
        var open = !LeftSheetOpen;
        _leftSheetOpen = open;
        _inspectorSheetOpen = false;
        _focusSheetAfter = open;
        _relayoutPending = true;
    }

    private void ToggleInspectorSheet()
    {
        var open = !InspectorSheetOpen;
        _inspectorSheetOpen = open;
        _leftSheetOpen = false;
        _focusSheetAfter = open;
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
        if (_focusSheetAfter && (LeftSheetOpen || InspectorSheetOpen))
        {
            // The toolbar toggle opened the sheet: the non-modal focus scope leaves the focus where it is, so move it in
            // (Escape closes a non-modal sheet only while it holds the focus; the scope restores it to the canvas).
            _focusSheetAfter = false;
            try { await _sheetCloseRef.FocusAsync(); }
            catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException) { /* the control is gone */ }
        }

        await MobileAfterRenderAsync();

        if (_relayoutPending && _canvas is not null)
        {
            _relayoutPending = false;
            await _canvas.NotifyViewportChangedAsync();
        }
    }
}
