using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;

namespace Tempo.Blazor.Components.Layout;

// The public parameter surface of the shell. It lives in its own partial (N270/F13) so the markup
// file stays under the 1200-line source ceiling; the class is one type either way.
public partial class TmEditorShell
{
    /// <summary>Content of the top header region (a title, breadcrumbs, the primary save action).</summary>
    [Parameter] public RenderFragment? Header { get; set; }

    /// <summary>Content of the toolbar region below the header (undo/redo, preview, validate). It is rendered inside the shell's layout cascade, so it can read the cascaded <see cref="TmLayoutContext"/> and adapt. Not rendered on a mobile layout when <see cref="MobileActions"/> is set — the action bar replaces it.</summary>
    [Parameter] public RenderFragment? Toolbar { get; set; }

    /// <summary>The left panel (a toolbox, a block list). A docked column on desktop and tablet (a rail when collapsed) and part of the mobile panels sheet or tabs.</summary>
    [Parameter] public RenderFragment? Left { get; set; }

    /// <summary>The central canvas. Rendered at every layout.</summary>
    [Parameter] public RenderFragment? Canvas { get; set; }

    /// <summary>The right panel (an inspector, properties). A docked column on desktop and tablet (a rail when collapsed) and part of the mobile panels sheet or tabs.</summary>
    [Parameter] public RenderFragment? Right { get; set; }

    /// <summary>Optional status bar content, rendered below the main region (inside it, above the
    /// action bar, on mobile).</summary>
    [Parameter] public RenderFragment? StatusBar { get; set; }

    /// <summary>
    /// The shell's mobile actions, rendered through <see cref="TmMobileActionBar"/> at the bottom
    /// of a mobile layout (hidden automatically above the mobile breakpoint). When set, the
    /// <see cref="Toolbar"/> slot is not rendered on a mobile layout: the actions replace it.
    /// </summary>
    [Parameter] public IReadOnlyList<TmActionItem>? MobileActions { get; set; }

    /// <summary>
    /// How many of the <see cref="MobileActions"/> stay on the mobile action bar before the rest
    /// overflow into the bar's own "More" menu. Default 3 — the bar's own default, so a host that
    /// never sets it keeps exactly the behaviour of earlier versions. A host whose action set is the
    /// editor's primary navigation (the email template editor's Back / Redo / Preview / Validate)
    /// raises it to keep those commands one tap away instead of behind "More".
    /// </summary>
    /// <remarks>A value below 1 is rejected with <see cref="ArgumentOutOfRangeException"/>.</remarks>
    [Parameter] public int MobileActionsMaxVisible { get; set; } = 3;

    /// <summary>Whether the left panel is open. Two-way with <see cref="LeftOpenChanged"/>. Means the same on desktop and tablet; the mobile Tabs presentation ignores it.</summary>
    [Parameter] public bool LeftOpen { get; set; } = true;

    /// <summary>Raised when the left panel should open or close.</summary>
    [Parameter] public EventCallback<bool> LeftOpenChanged { get; set; }

    /// <summary>Whether the right panel is open. Two-way with <see cref="RightOpenChanged"/>. Means the same on desktop and tablet; the mobile Tabs presentation ignores it.</summary>
    [Parameter] public bool RightOpen { get; set; } = true;

    /// <summary>Raised when the right panel should open or close.</summary>
    [Parameter] public EventCallback<bool> RightOpenChanged { get; set; }

    /// <summary>
    /// Panels rendered collapsed to a slim rail instead of their full width. Two-way with
    /// <see cref="CollapsedPanelsChanged"/>: the shell's own collapse/expand buttons raise it. On a
    /// tablet layout at most ONE panel is expanded — when two panels would be, the shell rails the
    /// other and raises this callback with the new set.
    /// </summary>
    [Parameter] public EditorShellPanel CollapsedPanels { get; set; }

    /// <summary>Raised when a panel is collapsed to, or expanded from, its rail.</summary>
    [Parameter] public EventCallback<EditorShellPanel> CollapsedPanelsChanged { get; set; }

    /// <summary>How the open panels present on a mobile layout. <see cref="MobilePanelPresentation.Sheet"/> (default) keeps the canvas visible with a bottom sheet — a Left/Right tab strip inside it when both panels are open; <see cref="MobilePanelPresentation.Tabs"/> is a full-region Left | Canvas | Right tablist with no sheet.</summary>
    [Parameter] public MobilePanelPresentation MobilePanelPresentation { get; set; } = MobilePanelPresentation.Sheet;

    /// <summary>
    /// How the open panels present on a TABLET layout, one value for both sides.
    /// <see cref="TabletPanelPresentation.Docked"/> (default) keeps the docked columns with rails (at most one
    /// expanded). <see cref="TabletPanelPresentation.Overlay"/> turns the Left panel into an overlay drawer and the
    /// Right panel into a non-modal side sheet over a full-width canvas: at most one is open (opening one closes the
    /// other and raises its <see cref="LeftOpenChanged"/> / <see cref="RightOpenChanged"/> with <c>false</c>), they are
    /// driven by <see cref="LeftOpen"/> / <see cref="RightOpen"/> (the host renders the toggles), have no rail and no
    /// resize separator, and Escape or the close button closes the open one and returns the focus to its trigger.
    /// Desktop and mobile layouts ignore it.
    /// </summary>
    [Parameter] public TabletPanelPresentation TabletPanelPresentation { get; set; } = TabletPanelPresentation.Docked;
    /// <summary>
    /// The <c>data-testid</c> of the Left overlay drawer root (<see cref="TabletPanelPresentation.Overlay"/> only). Null uses
    /// <c>editor-shell-left-overlay</c>; either way <see cref="TmComponentBase.TestIdPrefix"/> namespaces the default, while
    /// an explicit value is used as given (the host namespaces it itself).
    /// </summary>
    [Parameter] public string? LeftOverlayTestId { get; set; }

    /// <summary>
    /// The <c>aria-level</c> of the overlay panel titles (<see cref="TabletPanelPresentation.Overlay"/> only; they are <c>role="heading"</c>). Default <c>2</c>:
    /// a host whose page nests the editor under an <c>h2</c> section passes <c>3</c> so the outline stays consistent. Values outside 1-6 fall back to 2.
    /// </summary>
    [Parameter] public int OverlayHeadingLevel { get; set; } = 2;

    private int EffectiveOverlayHeadingLevel => OverlayHeadingLevel is >= 1 and <= 6 ? OverlayHeadingLevel : 2;

    /// <summary>The <c>data-testid</c> of the Right overlay side sheet root (<see cref="TabletPanelPresentation.Overlay"/> only). Null uses <c>editor-shell-right-overlay</c>.</summary>
    [Parameter] public string? RightOverlayTestId { get; set; }

    /// <summary>The <c>data-testid</c> of the overlay's close button. Null uses the open overlay's test id with a <c>-close</c> suffix.</summary>
    [Parameter] public string? OverlayCloseTestId { get; set; }
    /// <summary>The left panel title — the dock's accessible name, the mobile tab label and the mobile closed-state label.</summary>
    [Parameter] public string? LeftTitle { get; set; }

    /// <summary>The right panel title — the dock's accessible name, the mobile tab label and the mobile closed-state label.</summary>
    [Parameter] public string? RightTitle { get; set; }

    /// <summary>Width of the left panel (a docked column on desktop and tablet). A CSS pixel length, default 280px. Two-way with <see cref="LeftWidthChanged"/>: a user resize raises the callback with the new "NNNpx" value.</summary>
    [Parameter] public string LeftWidth { get; set; } = "280px";

    /// <summary>Raised when the user resizes the left panel (pointer drag or keyboard on its separator) and when a persisted width is restored on load.</summary>
    [Parameter] public EventCallback<string> LeftWidthChanged { get; set; }

    /// <summary>Width of the right panel (a docked column on desktop and tablet). A CSS pixel length, default 320px. Two-way with <see cref="RightWidthChanged"/>.</summary>
    [Parameter] public string RightWidth { get; set; } = "320px";

    /// <summary>Raised when the user resizes the right panel (pointer drag or keyboard on its separator) and when a persisted width is restored on load.</summary>
    [Parameter] public EventCallback<string> RightWidthChanged { get; set; }

    /// <summary>Smallest width, in CSS pixels, the left panel can be resized to. Default 200.</summary>
    [Parameter] public int MinLeftWidth { get; set; } = 200;

    /// <summary>Largest width, in CSS pixels, the left panel can be resized to. Default 480.</summary>
    [Parameter] public int MaxLeftWidth { get; set; } = 480;

    /// <summary>Smallest width, in CSS pixels, the right panel can be resized to. Default 240.</summary>
    [Parameter] public int MinRightWidth { get; set; } = 240;

    /// <summary>Largest width, in CSS pixels, the right panel can be resized to. Default 560.</summary>
    [Parameter] public int MaxRightWidth { get; set; } = 560;

    /// <summary>The canvas width, in CSS pixels, a resize never takes away: dragging a panel stops where the canvas would drop below it. Default 400.</summary>
    [Parameter] public int MinCanvasWidth { get; set; } = 400;

    /// <summary>
    /// Per-instance layout thresholds for this shell. Null (default) uses <see cref="TmLayoutBreakpoints.Default"/>
    /// (mobile below 640, desktop from 1024). An editor whose content needs different steps — say
    /// desktop from 1200 and mobile below 768 — passes <c>new TmLayoutBreakpoints(768, 1200)</c>.
    /// </summary>
    [Parameter] public TmLayoutBreakpoints? Breakpoints { get; set; }

    /// <summary>The panel a mobile layout shows: the selected tab of the sheet (<see cref="EditorShellPanel.Left"/>,
    /// <see cref="EditorShellPanel.Right"/> or the optional <see cref="EditorShellPanel.Extra"/> sheet tab) or of
    /// the Tabs presentation (<see cref="EditorShellPanel.None"/> is the canvas tab). Two-way with
    /// <see cref="ActiveMobilePanelChanged"/>, so a host can switch to the properties when a block is selected.</summary>
    [Parameter] public EditorShellPanel ActiveMobilePanel { get; set; }

    /// <summary>Raised when the user selects another mobile tab.</summary>
    [Parameter] public EventCallback<EditorShellPanel> ActiveMobilePanelChanged { get; set; }

    /// <summary>The snap of the mobile panels sheet: an index into <see cref="MobileSheetSnapPoints"/> (0 = the lowest snap). Two-way with <see cref="MobileSheetSnapIndexChanged"/>, so a host can collapse the sheet on an "add block" action.</summary>
    [Parameter] public int MobileSheetSnapIndex { get; set; }

    /// <summary>Raised when a gesture or the keyboard moves the mobile sheet to another snap, and when a layout flip resets it.</summary>
    [Parameter] public EventCallback<int> MobileSheetSnapIndexChanged { get; set; }

    /// <summary>
    /// Fractions (0–1] of the shell's stage the mobile panels sheet snaps to, ascending;
    /// <see cref="MobileSheetSnapIndex"/> indexes this list (0 = the lowest). Default
    /// <c>[0.5, 1.0]</c>. Invalid values are dropped and the list is sorted (the TmDrawer rules);
    /// an empty result falls back to the default. The lowest snap is also the fraction of the
    /// stage the canvas reserves while the sheet is open.
    /// </summary>
    [Parameter] public IReadOnlyList<double>? MobileSheetSnapPoints { get; set; }

    /// <summary>The label of the canvas tab in the <see cref="MobilePanelPresentation.Tabs"/> presentation. Null uses the localized default.</summary>
    [Parameter] public string? CanvasTitle { get; set; }

    /// <summary>
    /// Whether the closed-state mobile panels toggle (the labelled bar naming the hidden panels)
    /// renders while the sheet is closed. Default <c>true</c>. A host that renders its own mobile
    /// action bar (the F5 pattern — its tiles reopen the panels) sets this to <c>false</c> so the
    /// sheet has exactly ONE reopen path and the bar is the focus target after the sheet closes.
    /// </summary>
    [Parameter] public bool ShowMobilePanelsToggle { get; set; } = true;

    /// <summary>Content rendered inside the left panel's collapsed rail, below the expand button (an icon strip of the toolbox).</summary>
    [Parameter] public RenderFragment? LeftRail { get; set; }

    /// <summary>Content rendered inside the right panel's collapsed rail, below the expand button.</summary>
    [Parameter] public RenderFragment? RightRail { get; set; }

    /// <summary>
    /// When set, panel widths the USER resized persist to (and restore from) localStorage under this
    /// key, so a returning user keeps the layout they left. Only user-resized widths are stored,
    /// together with the host width they were resized from; a stored value is validated against the
    /// min/max widths and applies only while the host still supplies that same width, and an explicit
    /// <see cref="LeftWidth"/>/<see cref="RightWidth"/> the host sets after a user resize wins. A
    /// restored width is reported through <see cref="LeftWidthChanged"/>/<see cref="RightWidthChanged"/>
    /// (a restore counts as a user value). A key set after the first render, or changed later, is
    /// loaded when it appears. Null (default) disables persistence.
    /// </summary>
    [Parameter] public string? PersistWidthsKey { get; set; }

    /// <summary>
    /// The layout the shell renders. <see cref="TmLayoutMode.Auto"/> measures the shell's own
    /// root; a forced value never measures, so a test can render a mode without a DOM.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>The layout rendered before the first measurement. Desktop unless the host says otherwise.</summary>
    [Parameter] public TmLayoutMode InitialMode { get; set; } = TmLayoutMode.Desktop;

    /// <summary>
    /// Raised when the resolved layout changes. The initial value is not a change. Never pair
    /// <see cref="LayoutMode"/> with this callback: it reports the resolved mode, and a two-way
    /// bind would pin that mode.
    /// </summary>
    [Parameter] public EventCallback<TmLayoutMode> ResolvedLayoutChanged { get; set; }

    /// <summary>Extra CSS classes for the shell root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the shell root.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The cascaded layout of an ancestor observer, when the shell is nested in one (the same input its own observer resolves with).</summary>
    [CascadingParameter] public TmLayoutContext? Parent { get; set; }
}
