using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Toolbar;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// Phase-7 mobile chrome of <see cref="TmDiagramEditor"/> (and the shared floating minimap
/// overlay), kept beside the markup to respect the .razor length ceiling: the mobile "⋯" menu
/// groups, the floating zoom pill and the minimap overlay/reopen markup render as
/// <see cref="RenderTreeBuilder"/> fragments here, mirroring
/// <c>TmEditorShell.Extra.razor.cs</c>.
/// </summary>
public partial class TmDiagramEditor
{
    /// <summary>
    /// The mobile-only groups of the header "⋯" menu: the Search toggle, the minimap toggle
    /// (X1 — the map starts hidden and this is its only switch) and the Read-only toggle (the
    /// status bar does not exist on a mobile layout). A tap closes the menu and returns focus to
    /// the trigger, so the sheet/panel it opened never strands the pointer.
    /// </summary>
    private RenderFragment MobileViewMenuGroups => builder =>
    {
        var seq = 0;

        // Phase 7 (mobile): the compact header drops the search toggle, so the "⋯" menu carries it.
        if (Shows(DiagramToolbarGroups.Search))
        {
            builder.OpenElement(seq++, "div");
            builder.AddAttribute(seq++, "class", "tm-diagram-editor__toolbar-group");
            builder.AddAttribute(seq++, "data-tm-group", "search");
            builder.OpenComponent<TmToolbarButton>(seq++);
            builder.AddComponentParameter(seq++, nameof(TmToolbarButton.Icon), IconNames.Search);
            builder.AddComponentParameter(seq++, nameof(TmToolbarButton.Text), Loc["TmDiagramEditor_Search"]);
            builder.AddComponentParameter(seq++, nameof(TmToolbarButton.Tooltip), Loc["TmDiagramEditor_Search"]);
            builder.AddComponentParameter(seq++, nameof(TmToolbarButton.Pressed), _showSearchPanel);
            builder.AddComponentParameter(seq++, nameof(TmToolbarButton.OnClick),
                EventCallback.Factory.Create(this, OnMobileSearchFromMenu));
            builder.CloseComponent();
            builder.CloseElement();
        }

        // X1: the minimap starts hidden on mobile — this is its only toggle. It lives under the
        // frozen "panels" group semantics (it surfaces a panel-like view), never a new
        // data-tm-group. Rendered whenever the toggle is AVAILABLE (Panels or View group), so a
        // group set without either never strands a hidden map.
        if (MobileMinimapToggleAvailable)
        {
            builder.OpenElement(seq++, "div");
            builder.AddAttribute(seq++, "class", "tm-diagram-editor__toolbar-group");
            builder.AddAttribute(seq++, "data-tm-group", "panels");
            builder.OpenComponent<TmToolbarButton>(seq++);
            builder.AddComponentParameter(seq++, nameof(TmToolbarButton.Icon), IconNames.Map);
            builder.AddComponentParameter(seq++, nameof(TmToolbarButton.Text), Loc["TmDiagramEditor_PanelMinimap"]);
            builder.AddComponentParameter(seq++, nameof(TmToolbarButton.Tooltip),
                _minimapCollapsed ? Loc["TmDiagramEditor_ShowMinimap"] : Loc["TmDiagramEditor_HideMinimap"]);
            builder.AddComponentParameter(seq++, nameof(TmToolbarButton.Pressed), !_minimapCollapsed);
            builder.AddComponentParameter(seq++, nameof(TmToolbarButton.OnClick),
                EventCallback.Factory.Create(this, OnMobileMinimapToggleFromMenu));
            builder.CloseComponent();
            builder.CloseElement();
        }

        if (ShowsReadOnlyToggle)
        {
            // The VISIBLE label is the constant "Read-only" (the control's identity); the STATE
            // travels on aria-pressed, the lock/pencil icon and the tooltip wording. The aria-label
            // repeats the visible label (WCAG 2.5.3) — TmToolbarButton would otherwise name the
            // button after the tooltip.
            builder.OpenElement(seq++, "div");
            builder.AddAttribute(seq++, "class", "tm-diagram-editor__toolbar-group");
            builder.AddAttribute(seq++, "data-tm-group", "readonly");
            builder.OpenComponent<TmToolbarButton>(seq++);
            builder.AddComponentParameter(seq++, nameof(TmToolbarButton.Icon), IsReadOnly ? IconNames.Lock : IconNames.Pencil);
            builder.AddComponentParameter(seq++, nameof(TmToolbarButton.Text), Loc["TmDiagramEditor_ReadOnly"]);
            builder.AddComponentParameter(seq++, nameof(TmToolbarButton.Tooltip),
                IsReadOnly ? Loc["TmDiagramEditor_ReadOnly"] : Loc["TmDiagramEditor_Editing"]);
            builder.AddComponentParameter(seq++, "aria-label", Loc["TmDiagramEditor_ReadOnly"]);
            builder.AddComponentParameter(seq++, nameof(TmToolbarButton.Pressed), IsReadOnly);
            builder.AddComponentParameter(seq++, nameof(TmToolbarButton.OnClick),
                EventCallback.Factory.Create(this, ToggleReadOnly));
            builder.CloseComponent();
            builder.CloseElement();
        }
    };

    /// <summary>
    /// The Search toggle inside the mobile "⋯" menu: opens the panel, closes the menu and
    /// returns focus to the "⋯" trigger (the panel owns focus from here).
    /// </summary>
    private void OnMobileSearchFromMenu()
    {
        OnSearchClicked();
        CloseViewMenuAndFocusTrigger();
    }

    /// <summary>
    /// The minimap toggle inside the mobile "⋯" menu: flips the map, closes the menu and returns
    /// focus to the "⋯" trigger (the reopened overlay owns its own controls from here).
    /// </summary>
    private void OnMobileMinimapToggleFromMenu()
    {
        _minimapCollapsed = !_minimapCollapsed;
        CloseViewMenuAndFocusTrigger();
    }

    /// <summary>Closes the "⋯" menu (mobile) and re-asserts focus on its trigger after the render.</summary>
    private void CloseViewMenuAndFocusTrigger()
    {
        if (!IsMobileLayout) return;
        _viewMenuOpen = false;
        _viewMenuTriggerFocusPending = true;
    }

    /// <summary>
    /// The floating zoom pill (phase 7, mobile): the status bar does not render on a mobile
    /// layout, so the − | % | + | fit pill floats over the canvas corner (the mockup's left-side
    /// pill), outside the SVG.
    /// </summary>
    private RenderFragment MobileZoomOverlaySlot => builder =>
    {
        var seq = 0;
        builder.OpenElement(seq++, "div");
        builder.AddAttribute(seq++, "class", "tm-diagram-editor__zoom-overlay");
        builder.AddAttribute(seq++, "role", "group");
        builder.AddAttribute(seq++, "aria-label", Loc["TmDiagramEditor_ZoomControls"]);
        builder.AddAttribute(seq++, "data-testid", TestId("diagram-editor-zoom-overlay"));

        // UX review: the pill reads − | % | + | fit (zoom-out first, matching the desktop status
        // bar order) so the "make it smaller" control is never between the label and "+".
        builder.OpenElement(seq++, "button");
        builder.AddAttribute(seq++, "type", "button");
        builder.AddAttribute(seq++, "class", "tm-diagram-editor__zoom-overlay-btn");
        builder.AddAttribute(seq++, "aria-label", Loc["TmDiagramEditor_ZoomOut"]);
        builder.AddAttribute(seq++, "title", Loc["TmDiagramEditor_ZoomOut"]);
        builder.AddAttribute(seq++, "disabled", !CanZoomOut);
        builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, ZoomOut));
        builder.OpenComponent<TmIcon>(seq++);
        builder.AddComponentParameter(seq++, nameof(TmIcon.Name), IconNames.Minus);
        builder.AddComponentParameter(seq++, nameof(TmIcon.Size), IconSize.Sm);
        builder.CloseComponent();
        builder.CloseElement();

        builder.OpenElement(seq++, "span");
        builder.AddAttribute(seq++, "class", "tm-diagram-editor__zoom-label");
        builder.AddAttribute(seq++, "role", "status");
        builder.AddAttribute(seq++, "aria-live", "polite");
        builder.AddAttribute(seq++, "aria-label", string.Format(Loc["TmDiagramEditor_ZoomAria"], _zoomLabel));
        builder.AddContent(seq++, _zoomLabel);
        builder.CloseElement();

        builder.OpenElement(seq++, "button");
        builder.AddAttribute(seq++, "type", "button");
        builder.AddAttribute(seq++, "class", "tm-diagram-editor__zoom-overlay-btn");
        builder.AddAttribute(seq++, "aria-label", Loc["TmDiagramEditor_ZoomIn"]);
        builder.AddAttribute(seq++, "title", Loc["TmDiagramEditor_ZoomIn"]);
        builder.AddAttribute(seq++, "disabled", !CanZoomIn);
        builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, ZoomIn));
        builder.OpenComponent<TmIcon>(seq++);
        builder.AddComponentParameter(seq++, nameof(TmIcon.Name), IconNames.Plus);
        builder.AddComponentParameter(seq++, nameof(TmIcon.Size), IconSize.Sm);
        builder.CloseComponent();
        builder.CloseElement();

        builder.OpenElement(seq++, "button");
        builder.AddAttribute(seq++, "type", "button");
        builder.AddAttribute(seq++, "class", "tm-diagram-editor__zoom-overlay-btn");
        builder.AddAttribute(seq++, "aria-label", Loc["TmDiagramEditor_FitToView"]);
        builder.AddAttribute(seq++, "title", Loc["TmDiagramEditor_FitToView"]);
        builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, FitToView));
        builder.OpenComponent<TmIcon>(seq++);
        builder.AddComponentParameter(seq++, nameof(TmIcon.Name), IconNames.Maximize2);
        builder.AddComponentParameter(seq++, nameof(TmIcon.Size), IconSize.Sm);
        builder.CloseComponent();
        builder.CloseElement();

        builder.CloseElement();
    };

    /// <summary>
    /// The floating minimap overlay (DI3, phase 6): from tablet down the minimap floats over the
    /// canvas corner instead of taking a column of its own; a tap or drag on it navigates the
    /// canvas. A small header strip names the map and hosts the collapse button (UX review: the
    /// old absolutely-positioned button covered the map's corner); the overlay collapses in place
    /// (X4) and reopens from a floating corner button (tablet/desktop — on mobile the "⋯" menu
    /// is the reopen path). The width follows the resolved layout so the tap mapping always
    /// matches the render.
    /// </summary>
    private RenderFragment MinimapOverlaySlot => builder =>
    {
        var seq = 0;
        if (ShowMinimap && MinimapAsOverlay && !_minimapCollapsed)
        {
            builder.OpenElement(seq++, "div");
            builder.AddAttribute(seq++, "id", _minimapOverlayId);
            builder.AddAttribute(seq++, "class", "tm-diagram-editor__minimap-overlay");
            builder.AddAttribute(seq++, "data-testid", TestId("diagram-editor-minimap-overlay"));

            // UX review: the collapse affordance lives in a header strip (small title + button)
            // so it never covers the map; aria-expanded names the overlay it controls.
            builder.OpenElement(seq++, "div");
            builder.AddAttribute(seq++, "class", "tm-diagram-editor__minimap-overlay-header");
            builder.OpenElement(seq++, "span");
            builder.AddAttribute(seq++, "class", "tm-diagram-editor__minimap-overlay-title");
            builder.AddContent(seq++, Loc["TmDiagramMinimap_Title"]);
            builder.CloseElement();
            builder.OpenElement(seq++, "button");
            builder.AddAttribute(seq++, "type", "button");
            builder.AddAttribute(seq++, "class", "tm-diagram-editor__minimap-overlay-collapse");
            builder.AddAttribute(seq++, "aria-expanded", "true");
            builder.AddAttribute(seq++, "aria-controls", _minimapOverlayId);
            builder.AddAttribute(seq++, "aria-label", Loc["TmDiagramEditor_HideMinimap"]);
            builder.AddAttribute(seq++, "title", Loc["TmDiagramEditor_HideMinimap"]);
            builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, CollapseMinimap));
            builder.AddElementReferenceCapture(seq++, e => _minimapCollapseBtn = e);
            builder.OpenComponent<TmIcon>(seq++);
            builder.AddComponentParameter(seq++, nameof(TmIcon.Name), IconNames.ChevronDown);
            builder.AddComponentParameter(seq++, nameof(TmIcon.Size), IconSize.Xs);
            builder.CloseComponent();
            builder.CloseElement();
            builder.CloseElement();

            builder.OpenComponent<TmDiagramMinimap>(seq++);
            builder.AddComponentParameter(seq++, nameof(TmDiagramMinimap.Document), _document);
            builder.AddComponentParameter(seq++, nameof(TmDiagramMinimap.SelectedIds), _selectedIds);
            builder.AddComponentParameter(seq++, nameof(TmDiagramMinimap.Viewport), _minimapViewport);
            builder.AddComponentParameter(seq++, nameof(TmDiagramMinimap.Width), MinimapWidth);
            builder.AddComponentParameter(seq++, nameof(TmDiagramMinimap.NavigateRequested),
                EventCallback.Factory.Create<DiagramMinimapNavigateArgs>(this, OnMinimapNavigate));
            builder.AddComponentParameter(seq++, nameof(TmDiagramMinimap.Class), "tm-diagram-editor__minimap");
            builder.CloseComponent();

            builder.CloseElement();
        }
        else if (ShowMinimap && MinimapAsOverlay && !IsMobileLayout)
        {
            builder.OpenElement(seq++, "button");
            builder.AddAttribute(seq++, "type", "button");
            builder.AddAttribute(seq++, "class", "tm-diagram-editor__minimap-overlay-reopen");
            builder.AddAttribute(seq++, "aria-expanded", "false");
            builder.AddAttribute(seq++, "aria-controls", _minimapOverlayId);
            builder.AddAttribute(seq++, "aria-label", Loc["TmDiagramEditor_ShowMinimap"]);
            builder.AddAttribute(seq++, "title", Loc["TmDiagramEditor_ShowMinimap"]);
            builder.AddAttribute(seq++, "data-testid", TestId("diagram-editor-minimap-reopen"));
            builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, ExpandMinimap));
            builder.AddElementReferenceCapture(seq++, e => _minimapReopenBtn = e);
            builder.OpenComponent<TmIcon>(seq++);
            builder.AddComponentParameter(seq++, nameof(TmIcon.Name), IconNames.Map);
            builder.AddComponentParameter(seq++, nameof(TmIcon.Size), IconSize.Sm);
            builder.CloseComponent();
            builder.CloseElement();
        }
    };
}
