using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Components.Layout;

// The tablet overlay presentation (decision EDITOR-SHELL-TABLET-PRESENTATION): with TabletPanelPresentation.Overlay the
// open panel is an overlay drawer (Left) or a non-modal side sheet (Right) over a full-width canvas, at most one at a
// time, driven by LeftOpen / RightOpen. Kept beside the markup to respect the .razor length ceiling.
public partial class TmEditorShell
{
    private readonly string _overlayTitleId = $"editor-shell-overlay-title-{Guid.NewGuid():N}";

    // The one panel the overlay shows (None / Left / Right), derived from the host's open state in SyncAsync.
    private EditorShellPanel _overlayOpen;
    // Which panel wins when the host opens both: the one that became open most recently (default: the inspector).
    private EditorShellPanel _overlayWinner = EditorShellPanel.Right;
    private bool _prevOverlayLeftOpen;
    private bool _prevOverlayRightOpen;
    // The loser the host has already been told to close: the notice is raised once per conflict, not on every pass.
    private EditorShellPanel _overlayLoserRaised;
    private bool _focusOverlayAfter;
    private ElementReference _canvasRegion;
    private ElementReference _overlayClose;

    /// <summary>The overlay presentation applies to the tablet layout only; desktop and mobile never use it.</summary>
    private bool IsOverlay(TmLayoutMode resolved)
        => resolved == TmLayoutMode.Tablet && TabletPanelPresentation == TabletPanelPresentation.Overlay;

    private string OverlayTestId(EditorShellPanel side) => side == EditorShellPanel.Left
        ? LeftOverlayTestId ?? TestId("editor-shell-left-overlay")
        : RightOverlayTestId ?? TestId("editor-shell-right-overlay");

    private string OverlayCloseId(EditorShellPanel side) => OverlayCloseTestId ?? $"{OverlayTestId(side)}-close";

    /// <summary>
    /// Derives the overlay state from the host's open flags: which panel shows, who lost a "both open" conflict (the host is
    /// told once, through the loser's own Open callback) and whether the focus should move into a panel that just opened.
    /// Tracked on every layout so a desktop → tablet flip keeps the most recently opened panel.
    /// </summary>
    private async Task SyncOverlayAsync(TmLayoutMode resolved, bool flipped)
    {
        var leftOpen = LeftOpen && Left is not null;
        var rightOpen = RightOpen && Right is not null;
        var leftNew = leftOpen && !_prevOverlayLeftOpen;
        var rightNew = rightOpen && !_prevOverlayRightOpen;
        if (leftNew && !rightNew) _overlayWinner = EditorShellPanel.Left;
        else if (rightNew) _overlayWinner = EditorShellPanel.Right;
        _prevOverlayLeftOpen = leftOpen;
        _prevOverlayRightOpen = rightOpen;

        var before = _overlayOpen;
        if (!IsOverlay(resolved))
        {
            _overlayOpen = EditorShellPanel.None;
            _overlayLoserRaised = EditorShellPanel.None;
            _focusOverlayAfter = false;
            return;
        }

        _overlayOpen = leftOpen && rightOpen ? _overlayWinner
            : leftOpen ? EditorShellPanel.Left
            : rightOpen ? EditorShellPanel.Right
            : EditorShellPanel.None;

        // Focus moves into a panel the user (or the host) just opened, never at load and never on a layout flip.
        if (_overlayOpen == EditorShellPanel.None) _focusOverlayAfter = false;
        else if (_stateInitialised && !flipped && _overlayOpen != before) _focusOverlayAfter = true;

        if (leftOpen && rightOpen)
        {
            var loser = _overlayWinner == EditorShellPanel.Left ? EditorShellPanel.Right : EditorShellPanel.Left;
            if (_overlayLoserRaised == loser) return;
            _overlayLoserRaised = loser;
            if (loser == EditorShellPanel.Left) await LeftOpenChanged.InvokeAsync(false);
            else await RightOpenChanged.InvokeAsync(false);
        }
        else
        {
            _overlayLoserRaised = EditorShellPanel.None;
        }
    }

    /// <summary>The × button and Escape: closing is the host's decision, raised through the open panel's own callback.</summary>
    private async Task CloseOverlayAsync()
    {
        switch (_overlayOpen)
        {
            case EditorShellPanel.Left:
                await LeftOpenChanged.InvokeAsync(false);
                break;
            case EditorShellPanel.Right:
                await RightOpenChanged.InvokeAsync(false);
                break;
        }
    }

    /// <summary>
    /// After a panel opened: when the focus sits on a control outside the canvas (the toolbar toggle that opened it) it moves
    /// to the panel's close button, so Escape works from the keyboard. Focus the user keeps on the canvas (a selection opened
    /// the panel) is never taken away.
    /// </summary>
    private async Task FocusOverlayAsync()
    {
        if (!_focusOverlayAfter || _overlayOpen == EditorShellPanel.None) return;
        _focusOverlayAfter = false;
        _focus ??= new FocusIfLostInterop(JS);
        if (_canvasRegion.Id is not null && await _focus.FocusWithinAsync(_canvasRegion)) return;
        try { await _overlayClose.FocusAsync(); }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException) { /* the control is gone */ }
    }

    /// <summary>
    /// The overlay of ONE side: nothing unless that side is the open one. The left overlay renders before the canvas and the
    /// right one after it (DOM order = visual order, so the Tab order follows what the user sees), at fixed positions in the
    /// markup so the canvas keeps its place in the tree and is never remounted by a side switch.
    /// </summary>
    private RenderFragment RenderOverlay(EditorShellPanel side)
    {
        if (side != _overlayOpen) return static _ => { };

        var isLeft = side == EditorShellPanel.Left;
        var lower = isLeft ? "left" : "right";
        var content = isLeft ? Left : Right;
        var panelId = isLeft ? _leftPanelId : _rightPanelId;
        var closeLabel = Loc["TmEditorShell_ClosePanel"];

        return builder =>
        {
            builder.OpenComponent<TmFocusScope>(0);
            builder.AddComponentParameter(1, nameof(TmFocusScope.Active), true);
            builder.AddComponentParameter(2, nameof(TmFocusScope.Modal), false);
            builder.AddComponentParameter(3, nameof(TmFocusScope.CloseOnEscape), true);
            builder.AddComponentParameter(4, nameof(TmFocusScope.OnEscape), EventCallback.Factory.Create(this, CloseOverlayAsync));
            builder.AddComponentParameter(5, nameof(TmFocusScope.AriaLabelledBy), _overlayTitleId);
            builder.AddComponentParameter(6, nameof(TmFocusScope.DataTestId), OverlayTestId(side));
            builder.AddComponentParameter(7, nameof(TmFocusScope.Class),
                isLeft ? "tm-editor-shell__overlay tm-editor-shell__overlay--left" : "tm-editor-shell__overlay tm-editor-shell__overlay--right");
            builder.AddComponentParameter(8, nameof(TmFocusScope.ChildContent), (RenderFragment)(inner =>
            {
                inner.OpenElement(0, "div");
                inner.AddAttribute(1, "class", "tm-editor-shell__overlay-header");
                // A heading (level = OverlayHeadingLevel, default 2): the panel is a named region of the editor and a screen-reader user can reach it by heading.
                inner.OpenElement(2, "div");
                inner.AddAttribute(3, "role", "heading");
                inner.AddAttribute(4, "aria-level", EffectiveOverlayHeadingLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));
                inner.AddAttribute(5, "class", "tm-editor-shell__overlay-title");
                inner.AddAttribute(6, "id", _overlayTitleId);
                inner.AddContent(7, PanelTitle(side));
                inner.CloseElement();
                inner.OpenElement(8, "button");
                inner.AddAttribute(9, "type", "button");
                inner.AddAttribute(10, "class", "tm-editor-shell__overlay-close");
                inner.AddAttribute(11, "aria-label", closeLabel);
                inner.AddAttribute(12, "title", closeLabel);
                inner.AddAttribute(13, "data-testid", OverlayCloseId(side));
                inner.AddAttribute(14, "onclick", EventCallback.Factory.Create(this, CloseOverlayAsync));
                inner.AddElementReferenceCapture(15, e => _overlayClose = e);
                inner.OpenComponent<TmIcon>(16);
                inner.AddComponentParameter(17, nameof(TmIcon.Name), "x");
                inner.AddComponentParameter(18, nameof(TmIcon.Size), IconSize.Sm);
                inner.CloseComponent();
                inner.CloseElement();
                inner.CloseElement();

                inner.OpenElement(19, "div");
                inner.AddAttribute(20, "class", "tm-editor-shell__overlay-body");
                inner.OpenElement(21, "div");
                inner.AddAttribute(22, "id", panelId);
                inner.AddAttribute(23, "data-region", lower);
                inner.AddContent(24, content);
                inner.CloseElement();
                inner.CloseElement();
            }));
            builder.CloseComponent();
        };
    }
}