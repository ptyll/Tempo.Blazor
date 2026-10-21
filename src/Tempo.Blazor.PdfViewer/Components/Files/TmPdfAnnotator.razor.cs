using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Text.Json;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Toolbar;

namespace Tempo.Blazor.Components.Files;

/// <summary>Active tool of the PDF annotator.</summary>
public enum PdfAnnotatorMode
{
    /// <summary>Select and browse existing annotations.</summary>
    Browse,

    /// <summary>Select text to create a highlight annotation.</summary>
    Highlight,

    /// <summary>Click a page location to start a comment thread.</summary>
    Comment,

    /// <summary>Click a page location to place a stamp.</summary>
    Stamp,

    /// <summary>Draw freehand ink strokes on the page.</summary>
    Draw
}

/// <summary>Pending, not-yet-saved annotation created by an annotator tool.</summary>
public sealed class PdfAnnotatorDraft
{
    /// <summary>Annotation kind of the draft.</summary>
    public DocumentAnnotationKind Kind { get; init; }

    /// <summary>Anchor of the draft, when already known.</summary>
    public DocumentCommentAnchor? Anchor { get; set; }

    /// <summary>Completed ink strokes of a drawing draft.</summary>
    public List<DocumentInkStroke> Strokes { get; } = [];
}

/// <summary>
/// Standalone PDF annotation component built on top of <see cref="TmPdfViewer"/>.
/// Provides highlight, comment, stamp, and freehand drawing tools, a comment thread
/// panel with resolve/reopen, per-author or per-role annotation colors, export of
/// annotations into the PDF (optionally flattened), and a print variant.
/// Persistence is pluggable through <see cref="IPdfAnnotationProvider"/>.
/// </summary>
public partial class TmPdfAnnotator : TmComponentBase
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>URL of the PDF document to annotate.</summary>
    [Parameter] public string? Url { get; set; }

    /// <summary>Stable document identifier used by the annotation provider. Falls back to <see cref="Url"/>.</summary>
    [Parameter] public string? DocumentId { get; set; }

    /// <summary>Height of the annotator (CSS value). Default is "600px". The root owns the
    /// height contract; the embedded viewer fills it and shrinks inside it on a mobile layout.</summary>
    [Parameter] public string? Height { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Provider used to load and persist annotation threads. When omitted, an in-memory
    /// provider keeps annotations for the lifetime of the component.
    /// </summary>
    [Parameter] public IPdfAnnotationProvider? AnnotationProvider { get; set; }

    /// <summary>Author applied to annotations created in this annotator.</summary>
    [Parameter] public DocumentCommentUser? CurrentUser { get; set; }

    /// <summary>Known users, used to resolve author roles for role-based colors.</summary>
    [Parameter] public IReadOnlyList<DocumentCommentUser>? Users { get; set; }

    /// <summary>Annotation colors per author, keyed by user id.</summary>
    [Parameter] public IReadOnlyDictionary<string, string>? AuthorColors { get; set; }

    /// <summary>Annotation colors per role, keyed by role.</summary>
    [Parameter] public IReadOnlyDictionary<string, string>? RoleColors { get; set; }

    /// <summary>
    /// Explicit color (CSS value) stored with annotations created in this annotator.
    /// When null (default) colors resolve per author/role at display time.
    /// </summary>
    [Parameter] public string? AnnotationColor { get; set; }

    /// <summary>Active tool. Supports two-way binding. Default is <see cref="PdfAnnotatorMode.Browse"/>.</summary>
    [Parameter] public PdfAnnotatorMode Mode { get; set; } = PdfAnnotatorMode.Browse;

    /// <summary>Callback invoked when the active tool changes.</summary>
    [Parameter] public EventCallback<PdfAnnotatorMode> ModeChanged { get; set; }

    /// <summary>Stamp texts offered in stamp mode. Defaults to localized Approved/Rejected/Draft/Confidential.</summary>
    [Parameter] public IReadOnlyList<string>? StampOptions { get; set; }

    /// <summary>Whether resolved annotation threads are shown by default. Default is false.</summary>
    [Parameter] public bool ShowResolved { get; set; }

    /// <summary>Callback invoked when the resolved filter toggles.</summary>
    [Parameter] public EventCallback<bool> ShowResolvedChanged { get; set; }

    /// <summary>Whether the thread panel is rendered. Default is true.</summary>
    [Parameter] public bool ShowPanel { get; set; } = true;

    /// <summary>Callback invoked when the panel's close button requests to hide the panel
    /// (two-way <see cref="ShowPanel"/> binding).</summary>
    [Parameter] public EventCallback<bool> ShowPanelChanged { get; set; }

    /// <summary>
    /// Kept for backward compatibility. When true (default) the annotator has no toolbar of
    /// its own: its tool groups merge into the embedded viewer's single toolbar through the
    /// viewer's <c>ToolbarStart</c>/<c>ToolbarEnd</c> slots, and the export group lives in the
    /// header row when <see cref="ShowHeader"/> is true. When false the viewer's chrome
    /// (navigation, zoom, rotation) is hidden and the annotator renders a compact tools row
    /// of its own instead.
    /// </summary>
    [Parameter] public bool ShowViewerToolbar { get; set; } = true;

    /// <summary>Whether the "Open in new tab" link is rendered (in the merged toolbar, or in
    /// the header row when <see cref="ShowHeader"/> is true). Default is true.</summary>
    [Parameter] public bool ShowOpenLink { get; set; } = true;

    /// <summary>Whether the viewer's thumbnails sidebar is offered. Default is false; passed through to the embedded viewer.</summary>
    [Parameter] public bool ShowThumbnails { get; set; }

    /// <summary>Whether the viewer's search is offered. Default is false; passed through to the embedded viewer.</summary>
    [Parameter] public bool ShowSearch { get; set; }

    /// <summary>Whether the viewer's rotation button is offered. Default is false; passed through to the embedded viewer.</summary>
    [Parameter] public bool AllowRotation { get; set; }

    /// <summary>Whether the thumbnails sidebar is collapsed. Supports two-way binding; the annotator tracks the state so an unbound host keeps the user's choice. Default is false.</summary>
    [Parameter] public bool ThumbnailsCollapsed { get; set; }

    /// <summary>Callback invoked when the thumbnails collapse state changes in the viewer.</summary>
    [Parameter] public EventCallback<bool> ThumbnailsCollapsedChanged { get; set; }

    /// <summary>How the viewer's zoom scale is decided. Supports two-way binding; passed through to the embedded viewer. Default is <see cref="PdfZoomMode.Auto"/>, consistent with the viewer (fit-width on a mobile layout, the bound scale everywhere else).</summary>
    [Parameter] public PdfZoomMode ZoomMode { get; set; } = PdfZoomMode.Auto;

    /// <summary>Callback when the zoom mode changes in the viewer.</summary>
    [Parameter] public EventCallback<PdfZoomMode> ZoomModeChanged { get; set; }

    /// <summary>Whether a header row (title, subtitle, export group and host actions) is rendered above the merged toolbar. Default is false.</summary>
    [Parameter] public bool ShowHeader { get; set; }

    /// <summary>Document title rendered in the header row. Used only when <see cref="ShowHeader"/> is true.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Document subtitle rendered in the header row. Used only when <see cref="ShowHeader"/> is true; when omitted the viewer shows a neutral page count.</summary>
    [Parameter] public string? Subtitle { get; set; }

    /// <summary>Content rendered at the start of the header row. Used only when <see cref="ShowHeader"/> is true.</summary>
    [Parameter] public RenderFragment? HeaderStart { get; set; }

    /// <summary>Extra host actions rendered at the end of the header row, after the export group. Used only when <see cref="ShowHeader"/> is true.</summary>
    [Parameter] public RenderFragment? HeaderActions { get; set; }

    /// <summary>Whether export and print actions are offered. Default is true.</summary>
    [Parameter] public bool AllowExport { get; set; } = true;

    /// <summary>File name used for the exported PDF. Defaults to the source name with an "-annotated" suffix.</summary>
    [Parameter] public string? ExportFileName { get; set; }

    /// <summary>Callback invoked when the loaded annotation set changes.</summary>
    [Parameter] public EventCallback<IReadOnlyList<DocumentCommentThread>> AnnotationsChanged { get; set; }

    /// <summary>
    /// How the annotator decides its responsive layout. <see cref="TmLayoutMode.Auto"/> measures
    /// the annotator's own container through the embedded <see cref="Layout.TmEditorShell"/>; a
    /// forced value renders that layout without measuring, so a test — or a host — can pin a
    /// mode. Default <see cref="TmLayoutMode.Auto"/>. The mobile layout is the G2 presentation:
    /// viewing plus light edits only.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>
    /// Whether the toolbar's icon buttons show their text labels. Default
    /// <see cref="ToolbarLabels.Icons"/> (the icon-only tools of the earlier phases);
    /// <see cref="ToolbarLabels.Auto"/> and <see cref="ToolbarLabels.IconsWithText"/> show the
    /// labels — on a tablet layout under the icons (the ribbon style of the tablet reference).
    /// The value is passed through to the embedded viewer's toolbar as well.
    /// </summary>
    [Parameter] public ToolbarLabels ToolbarLabels { get; set; } = ToolbarLabels.Icons;

    // ── State ────────────────────────────────────────────────────────────────

    private ElementReference _surfaceRef;

    /// <summary>The annotator root — the scope of the JS focus helpers (opener restore, the
    /// sheet composer focus, the selected-card scroll) for the mobile sheet.</summary>
    private ElementReference _annotatorRef;

    /// <summary>The embedded viewer instance: the annotator folds the viewer's Pages/Search
    /// entries into its own mobile action bar and drives them through the viewer's public API.</summary>
    private TmPdfViewer? _viewer;

    private List<DocumentCommentThread> _threads = [];
    private InMemoryPdfAnnotationProvider? _fallbackProvider;
    private string? _loadedDocumentId;
    private string? _selectedThreadId;
    private bool _showResolved;
    private bool _showResolvedInitialized;
    private bool _panelVisible = true;

    /// <summary>The <see cref="ShowPanel"/> value seen by the previous parameter pass: the internal
    /// panel state syncs from the parameter only when it actually changes, so a close from the
    /// panel's ✕ (or the toolbar toggle) is not undone by an unrelated re-render of an unbound host.</summary>
    private bool? _lastShowPanelParam;

    /// <summary>Whether the host explicitly supplied <see cref="ShowPanel"/> in the current
    /// parameter set (detected in <see cref="SetParametersAsync"/>). When it did, the mobile
    /// sheet default (start closed) does not apply — the host owns the state.</summary>
    private bool _showPanelSupplied;

    /// <summary>Whether the shell's right panel is collapsed to its rail. Two-way shadow of the
    /// shell's <see cref="TmEditorShell.CollapsedPanels"/> (phase 8 review 2 — the
    /// collapse chevron was dead without the binding).</summary>
    private EditorShellPanel _collapsedPanels;

    private PdfAnnotatorMode _mode;
    private PdfAnnotatorMode _lastModeParam;
    private int _currentPage = 1;
    private int _totalPages;
    private int _selectedStampIndex;
    private PdfAnnotatorDraft? _draft;
    private List<DocumentInkPoint>? _activeStroke;
    private double[]? _surfaceSize;

    /// <summary>The thumbnails collapse state handed to the viewer; synced from the
    /// <see cref="ThumbnailsCollapsed"/> parameter only when it actually changes, so an
    /// unbound host keeps the user's choice (same pattern as <see cref="_panelVisible"/>).</summary>
    private bool _thumbnailsCollapsed;

    /// <summary>The <see cref="ThumbnailsCollapsed"/> value seen by the previous parameter pass.</summary>
    private bool? _lastThumbnailsCollapsedParam;

    /// <summary>The zoom mode handed to the viewer; synced from <see cref="ZoomMode"/> only when
    /// it actually changes, so an unbound host keeps the mode the viewer reported back.</summary>
    private PdfZoomMode _zoomMode = PdfZoomMode.Auto;

    /// <summary>The <see cref="ZoomMode"/> value seen by the previous parameter pass.</summary>
    private PdfZoomMode _lastZoomModeParam = PdfZoomMode.Auto;

    // ── Responsive layout (FZE-8) ────────────────────────────────────────────

    /// <summary>Peek/half/full snap fractions of the shell stage (the diagram-editor G2 sheet).</summary>
    private static readonly double[] MobileSheetSnapPoints = [0.35, 0.7, 1.0];

    /// <summary>The snap the sheet lands on when it opens from a marker or a draft (phase 8
    /// review 12): the HALF snap (0.7) — the composer's input and its Reply + Resolve row must
    /// sit fully inside the sheet's visible band (the peek's band was too short and the
    /// reply/resolve pair ended below the fold).</summary>
    private const int MobileOpenSnapIndex = MobileHalfSnapIndex;

    /// <summary>The snap for the explicit "Comments" (threads) entry: half — the whole thread
    /// list is readable for browsing.</summary>
    private const int MobileHalfSnapIndex = 1;

    /// <summary>The sheet snap, recorded from the shell (a two-way shadow, never re-raised).</summary>
    private int _mobileSnapIndex = MobileHalfSnapIndex;

    /// <summary>The resolved layout. Under a forced <see cref="LayoutMode"/> the parameter is the
    /// resolution (applied in <see cref="OnParametersSetAsync"/>); under <see cref="TmLayoutMode.Auto"/>
    /// the embedded shell's callback owns it.</summary>
    private TmLayoutMode _resolved = TmLayoutMode.Desktop;

    /// <summary>Armed when the mobile sheet closes: after the render the JS focus helper returns
    /// focus to the element that opened the sheet (marker / Comments tile / draft), falling back
    /// to the first action-bar tile only when that element is gone (phase 8 review 1).</summary>
    private bool _focusOpenerAfter;

    /// <summary>Armed when the mobile sheet opens from a marker, the Comments tile or a draft:
    /// after the render the draft textarea (or the selected thread's reply input) is focused
    /// (phase 8 review 12).</summary>
    private bool _focusComposerAfter;

    /// <summary>Armed when the mobile sheet opens on a selected thread: the selected card is
    /// scrolled into view with <c>scrollIntoView({block:'nearest'})</c> (phase 8 review 11).</summary>
    private bool _scrollSelectedCardAfter;

    /// <summary>Whether the resolved layout is mobile (the G2 presentation).</summary>
    private bool IsMobileLayout => _resolved == TmLayoutMode.Mobile;

    /// <summary>Whether the resolved layout is tablet (labels below the icons, docked side panel).</summary>
    private bool IsTabletLayout => _resolved == TmLayoutMode.Tablet;

    /// <summary>The <c>data-editor-layout</c> mirror of the resolved layout (diagram-editor convention).</summary>
    private string LayoutName => _resolved.ToString().ToLowerInvariant();

    /// <summary>Whether the annotator's groups merge into the viewer toolbar: on a DESKTOP layout
    /// only — a tablet layout renders the full-width labelled tools row above the canvas (phase 8
    /// review 7) and a mobile layout renders the G2 palette and the action bar; neither injects
    /// anything into the viewer's toolbar.</summary>
    private bool InjectToolbarGroups => UseMergedToolbar && !IsMobileLayout && !IsTabletLayout;

    /// <summary>Where the annotator's tool labels sit: under the icon on a tablet layout, inline
    /// everywhere else — the exact rule of the embedded viewer's <c>ResolvedLabelPosition</c>
    /// (phase 8 review 5). <see cref="ToolbarLabels.Icons"/> keeps the buttons icon-only: inside
    /// the merged toolbar the enclosing <c>TmToolbar</c> decides that; the fallback/tablet rows
    /// render outside one, so the same visibility rule is mirrored here.</summary>
    private ToolbarLabelPosition ToolLabelPosition
        => ToolbarLabels == ToolbarLabels.Icons
            ? ToolbarLabelPosition.Hidden
            : IsTabletLayout
                ? ToolbarLabelPosition.Below
                : ToolbarLabelPosition.Inline;

    /// <summary>
    /// The annotator root height. The root OWNS the height contract (the shell's mobile stage
    /// needs a definite height to lay out against) and the embedded viewer flexes inside it —
    /// its own <see cref="Height"/> value is the preferred height it shrinks from on a phone.
    /// </summary>
    private string RootStyle => $"height:{(string.IsNullOrEmpty(Height) ? "600px" : Height)}";

    /// <summary>The viewer height: it fills the annotator root and shrinks on a mobile layout
    /// (the flex chain of the shell's canvas region), so the bound value stays the preferred one.</summary>
    private string ViewerHeight => "100%";

    private IPdfAnnotationProvider EffectiveProvider
        => AnnotationProvider ?? (_fallbackProvider ??= new InMemoryPdfAnnotationProvider());

    private string EffectiveDocumentId
        => !string.IsNullOrEmpty(DocumentId) ? DocumentId : (Url ?? string.Empty);

    /// <summary>
    /// Whether the annotator merges its groups into the viewer's single toolbar. False when
    /// the host disabled the viewer chrome (<see cref="ShowViewerToolbar"/>) or there is no
    /// document to annotate yet — then the compact fallback row renders instead.
    /// </summary>
    private bool UseMergedToolbar => ShowViewerToolbar && !string.IsNullOrEmpty(Url);

    private IReadOnlyList<string> EffectiveStampOptions
        => StampOptions is { Count: > 0 }
            ? StampOptions
            :
            [
                Loc["TmPdfAnnotator_StampApproved"],
                Loc["TmPdfAnnotator_StampRejected"],
                Loc["TmPdfAnnotator_StampDraft"],
                Loc["TmPdfAnnotator_StampConfidential"]
            ];

    private IReadOnlyList<DocumentCommentUser> _knownUsers = [];
    private Func<DocumentCommentThread, string>? _colorResolver;

    private IReadOnlyList<DocumentCommentUser> KnownUsers => _knownUsers;

    private bool SurfaceActive
        => _mode is PdfAnnotatorMode.Comment or PdfAnnotatorMode.Stamp or PdfAnnotatorMode.Draw;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public override async Task SetParametersAsync(ParameterView parameters)
    {
        // Detect an explicit host ShowPanel BEFORE the sync below: the mobile sheet default
        // (start closed) must not fight a host that binds the panel state itself (phase 8
        // review 3).
        _showPanelSupplied = parameters.TryGetValue<bool>(nameof(ShowPanel), out _);
        await base.SetParametersAsync(parameters);
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // The panel state syncs from the parameter first: a layout change applied below must
        // win over it (the mobile default closes the sheet), never the other way round.
        if (ShowPanel != _lastShowPanelParam)
        {
            _lastShowPanelParam = ShowPanel;
            _panelVisible = ShowPanel;
        }

        if (Mode != _lastModeParam)
        {
            _lastModeParam = Mode;
            _mode = Mode;
        }

        ApplyMobileToolFallback();

        if (ThumbnailsCollapsed != _lastThumbnailsCollapsedParam)
        {
            _lastThumbnailsCollapsedParam = ThumbnailsCollapsed;
            _thumbnailsCollapsed = ThumbnailsCollapsed;
        }

        if (ZoomMode != _lastZoomModeParam)
        {
            _lastZoomModeParam = ZoomMode;
            _zoomMode = ZoomMode;
        }

        if (!_showResolvedInitialized)
        {
            _showResolvedInitialized = true;
            _showResolved = ShowResolved;
        }

        // Recomputed once per parameter set; ResolveThreadColor runs per thread per render.
        _knownUsers = Users is null
            ? (CurrentUser is null ? [] : [CurrentUser])
            : (CurrentUser is null ? Users : [.. Users, CurrentUser]);
        _colorResolver ??= ResolveThreadColor;
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        // A FORCED layout is the resolution: the embedded shell reports changes only, and the
        // initial resolution is not a change — so a host (or test) that pins a mode must see
        // that presentation from the first frame. Under Auto the shell's callback owns this
        // state (it fires on the first container measurement). Awaited, so the mobile sheet
        // default lands before the first frame renders.
        if (LayoutMode != TmLayoutMode.Auto && LayoutMode != _resolved)
        {
            await ApplyResolvedLayoutAsync(LayoutMode);
        }

        var documentId = EffectiveDocumentId;
        if (!string.Equals(documentId, _loadedDocumentId, StringComparison.Ordinal))
        {
            _loadedDocumentId = documentId;
            _threads = [];
            _selectedThreadId = null;
            _draft = null;
            _activeStroke = null;
            _currentPage = 1;
            _totalPages = 0;
            _surfaceSize = null;
            await LoadThreadsAsync();
        }
    }

    private async Task LoadThreadsAsync()
    {
        try
        {
            _threads = (await EffectiveProvider.GetThreadsAsync(EffectiveDocumentId)).ToList();
        }
        catch
        {
            _threads = [];
        }

        if (AnnotationsChanged.HasDelegate)
        {
            // Hand out a snapshot: handlers may mutate their own collections while we render.
            await AnnotationsChanged.InvokeAsync(_threads.ToArray());
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Reloads annotation threads from the provider (e.g. after an external change).</summary>
    public Task RefreshAsync() => LoadThreadsAsync();

    // ── Responsive layout (FZE-8) ────────────────────────────────────────────

    /// <summary>
    /// Applies a resolved layout change: the G2 rules ride along — the editing tools do
    /// not exist on a phone, so an active edit mode falls back to Browse, and the mobile
    /// sheet STARTS CLOSED on the first resolution to mobile and on every switch from
    /// desktop/tablet to mobile, unless the host explicitly supplied <see cref="ShowPanel"/>
    /// (phase 8 review 3).
    /// </summary>
    private async Task ApplyResolvedLayoutAsync(TmLayoutMode mode)
    {
        if (_resolved == mode)
        {
            return;
        }

        var previous = _resolved;
        _resolved = mode;
        ApplyMobileToolFallback();

        if (mode == TmLayoutMode.Mobile
            && previous != TmLayoutMode.Mobile
            && !_showPanelSupplied
            && _panelVisible)
        {
            _panelVisible = false;
            await ShowPanelChanged.InvokeAsync(false);
        }
    }

    /// <summary>
    /// G2: highlight, stamp and draw are tablet+ tools. When the resolved layout is mobile and
    /// the active mode is one of them, the internal state falls back to Browse (an in-flight
    /// stroke is dropped). The host's <see cref="Mode"/> parameter keeps its value — the
    /// fallback is a layout adaptation, not a user action, so no callback is raised.
    /// </summary>
    private void ApplyMobileToolFallback()
    {
        if (!IsMobileLayout
            || _mode is not (PdfAnnotatorMode.Highlight or PdfAnnotatorMode.Stamp or PdfAnnotatorMode.Draw))
        {
            return;
        }

        _mode = PdfAnnotatorMode.Browse;
        _activeStroke = null;
    }

    /// <summary>Receives the embedded shell's resolved layout (Auto mode) and re-renders.</summary>
    private async Task OnShellResolvedLayoutChanged(TmLayoutMode mode)
    {
        await ApplyResolvedLayoutAsync(mode);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>The F5 mobile action bar. The annotator's own entries (Export PDF | Export
    /// flattened | Print | the threads entry in the shared "More" menu) are PREPENDED by the
    /// viewer's Pages/Search entries when those features are enabled (phase 8 review 4): the
    /// embedded viewer renders no bar of its own (<c>ShowMobileActionBar=false</c>) and the
    /// entries drive it through its public <c>OpenThumbnailsSheetAsync</c>/<c>OpenSearchAsync</c>
    /// API. The bar's default budget of three tiles pushes the rest into "More".</summary>
    private List<TmActionItem> MobileBarItems
    {
        get
        {
            var items = new List<TmActionItem>(6);
            if (ShowThumbnails)
            {
                items.Add(new TmActionItem
                {
                    Id = "pages",
                    Label = Loc["TmPdfViewer_OpenPages"],
                    Icon = "panel-left",
                    OnClick = EventCallback.Factory.Create(this, () => _viewer?.OpenThumbnailsSheetAsync() ?? Task.CompletedTask),
                });
            }

            if (ShowSearch)
            {
                items.Add(new TmActionItem
                {
                    Id = "search",
                    Label = Loc["TmPdfViewer_Search"],
                    Icon = "search",
                    OnClick = EventCallback.Factory.Create(this, () => _viewer?.OpenSearchAsync() ?? Task.CompletedTask),
                });
            }

            if (AllowExport)
            {
                items.Add(new TmActionItem
                {
                    Id = "export",
                    Label = Loc["TmPdfAnnotator_Export"],
                    Icon = "file-down",
                    OnClick = EventCallback.Factory.Create(this, () => ExportAsync(flatten: false)),
                });
                items.Add(new TmActionItem
                {
                    Id = "export-flat",
                    Label = Loc["TmPdfAnnotator_ExportFlattened"],
                    Icon = "layers",
                    OnClick = EventCallback.Factory.Create(this, () => ExportAsync(flatten: true)),
                });
            }

            items.Add(new TmActionItem
            {
                Id = "print",
                Label = Loc["TmPdfAnnotator_Print"],
                Icon = "printer",
                OnClick = EventCallback.Factory.Create(this, PrintAsync),
            });
            items.Add(new TmActionItem
            {
                Id = "threads",
                Label = Loc["TmPdfAnnotator_OpenThreads"],
                Icon = "message-square",
                OnClick = EventCallback.Factory.Create(this, OpenThreadsSheetAsync),
            });
            return items;
        }
    }

    /// <summary>Opens the threads bottom sheet (from the action bar's More entry): half snap so
    /// the whole thread list is readable for browsing, the opener is remembered for the close
    /// restore, and the two-way <see cref="ShowPanel"/> callback learns the state like every
    /// other open path.</summary>
    private async Task OpenThreadsSheetAsync()
    {
        await RememberOpenerAsync();
        _panelVisible = true;
        _mobileSnapIndex = MobileHalfSnapIndex;
        await ShowPanelChanged.InvokeAsync(true);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Remembers the element that is opening the mobile sheet (a thread marker, the Comments
    /// tile or a palette tile arming a draft): the JS helper captures
    /// <c>document.activeElement</c> inside the annotator root, and
    /// <c>tmPdfAnnotator.focusOpener</c> returns focus there after the sheet closes — falling
    /// back to the first action-bar tile only when the opener is gone (phase 8 review 1).
    /// A no-op where JS is unavailable.
    /// </summary>
    private async Task RememberOpenerAsync()
    {
        if (!IsMobileLayout)
        {
            return;
        }

        try { await JS.InvokeAsync<bool>("tmPdfAnnotator.rememberFocus", _annotatorRef); }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
    }

    /// <summary>The shell's RightOpen two-way bridge: internal state first (an unbound host never
    /// echoes the callback), then the <see cref="ShowPanelChanged"/> callback. A mobile close
    /// arms the opener focus restore (the sheet's close button unmounted with the sheet).</summary>
    private async Task HandleRightOpenChangedAsync(bool open)
    {
        _panelVisible = open;
        if (!open && IsMobileLayout)
        {
            _focusOpenerAfter = true;
        }

        await ShowPanelChanged.InvokeAsync(open);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// The shell's CollapsedPanels two-way bridge (phase 8 review 2): the collapse chevron was
    /// dead because the annotator never echoed the set back. A collapse of the right panel maps
    /// onto <see cref="HandleRightOpenChangedAsync"/> (false) — the panel hides and the shell's
    /// slim reopen toggle (the "rail") brings it back through the RightOpen callback.
    /// </summary>
    private async Task HandleCollapsedPanelsChangedAsync(EditorShellPanel collapsed)
    {
        var rightWasCollapsed = _collapsedPanels.HasFlag(EditorShellPanel.Right);
        _collapsedPanels = collapsed;

        var rightNowCollapsed = collapsed.HasFlag(EditorShellPanel.Right);
        if (!rightWasCollapsed && rightNowCollapsed)
        {
            await HandleRightOpenChangedAsync(false);
        }
        else
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>Records the sheet snap the user dragged to; never re-raised (the shell owns it).</summary>
    private Task SilentSnapChangedAsync(int index)
    {
        _mobileSnapIndex = index;
        return Task.CompletedTask;
    }

    /// <summary>The active-state class of a mobile palette tile.</summary>
    private string PaletteTileClass(PdfAnnotatorMode mode)
        => _mode == mode
            ? "tm-pdf-annotator__tile tm-pdf-annotator__tile--active"
            : "tm-pdf-annotator__tile";

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusOpenerAfter)
        {
            _focusOpenerAfter = false;
            try { await JS.InvokeAsync<bool>("tmPdfAnnotator.focusOpener", _annotatorRef); }
            catch (JSException) { }
            catch (JSDisconnectedException) { }
        }

        if (_focusComposerAfter)
        {
            _focusComposerAfter = false;
            await FocusSheetComposerAsync();
        }

        if (_scrollSelectedCardAfter)
        {
            _scrollSelectedCardAfter = false;
            try
            {
                await JS.InvokeAsync<bool>("tmPdfAnnotator.scrollSelectedCardIntoView", _annotatorRef);
            }
            catch (JSException) { }
            catch (JSDisconnectedException) { }
        }
    }

    /// <summary>
    /// Focuses the sheet composer after the mobile sheet renders: the draft textarea when a
    /// draft is pending, otherwise the selected thread's reply input (phase 8 review 12).
    /// Tolerates a missing element and missing JS.
    /// </summary>
    private async Task FocusSheetComposerAsync()
    {
        try
        {
            var selector = _draft is not null
                ? "[data-testid='pdf-annotator-new-input']"
                : "[data-testid='pdf-annotator-reply-input']";
            await JS.InvokeAsync<bool>("tmPdfAnnotator.focusDescendant", _annotatorRef, selector);
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
    }

    // ── Mode & toolbar ───────────────────────────────────────────────────────

    private async Task SetModeAsync(PdfAnnotatorMode mode)
    {
        if (_mode == mode)
        {
            return;
        }

        _mode = mode;
        _draft = null;
        _activeStroke = null;
        await ModeChanged.InvokeAsync(mode);
        await InvokeAsync(StateHasChanged);
    }

    private string ModeClass(PdfAnnotatorMode mode)
        => _mode == mode
            ? "tm-pdf-annotator__mode tm-pdf-annotator__mode--active"
            : "tm-pdf-annotator__mode";

    private void HandleStampSelectionChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var index)
            && index >= 0 && index < EffectiveStampOptions.Count)
        {
            _selectedStampIndex = index;
        }
    }

    // ── Surface interaction ──────────────────────────────────────────────────

    private async Task HandleSurfaceClickAsync(MouseEventArgs e)
    {
        if (_mode is not (PdfAnnotatorMode.Comment or PdfAnnotatorMode.Stamp))
        {
            return;
        }

        var point = await NormalizePointAsync(e.OffsetX, e.OffsetY, refreshSize: true);
        if (point is null)
        {
            return;
        }

        if (_mode == PdfAnnotatorMode.Comment)
        {
            _draft = new PdfAnnotatorDraft
            {
                Kind = DocumentAnnotationKind.Comment,
                Anchor = DocumentCommentAnchor.Point(_currentPage, point.Value.X, point.Value.Y)
            };

            // G2: the draft composer lives in the bottom sheet on a phone — opening the sheet
            // with the draft is part of the tool's action (the tiles are the only entry). The
            // sheet opens at the HALF snap with the composer pinned to its bottom and focused.
            if (IsMobileLayout && !_panelVisible)
            {
                await RememberOpenerAsync();
                _panelVisible = true;
                _mobileSnapIndex = MobileOpenSnapIndex;
                _focusComposerAfter = true;
                await ShowPanelChanged.InvokeAsync(true);
            }
            else if (IsMobileLayout)
            {
                _focusComposerAfter = true;
            }

            await InvokeAsync(StateHasChanged);
        }
        else
        {
            await CreateStampAsync(point.Value.X, point.Value.Y);
        }
    }

    private async Task HandlePointerDownAsync(PointerEventArgs e)
    {
        if (_mode != PdfAnnotatorMode.Draw)
        {
            return;
        }

        var point = await NormalizePointAsync(e.OffsetX, e.OffsetY, refreshSize: true);
        if (point is null)
        {
            return;
        }

        _activeStroke = [DocumentInkPoint.Create(point.Value.X, point.Value.Y)];
        await InvokeAsync(StateHasChanged);
    }

    private async Task HandlePointerMoveAsync(PointerEventArgs e)
    {
        if (_activeStroke is null)
        {
            return;
        }

        // Reuse the size measured on pointer-down: no JS round-trip per move event.
        var point = await NormalizePointAsync(e.OffsetX, e.OffsetY, refreshSize: false);
        if (point is null)
        {
            return;
        }

        _activeStroke.Add(DocumentInkPoint.Create(point.Value.X, point.Value.Y));
        await InvokeAsync(StateHasChanged);
    }

    private Task HandlePointerUpAsync(PointerEventArgs e) => FinishStrokeAsync();

    private Task HandlePointerLeaveAsync(PointerEventArgs e) => FinishStrokeAsync();

    private async Task FinishStrokeAsync()
    {
        if (_activeStroke is null)
        {
            return;
        }

        var stroke = _activeStroke;
        _activeStroke = null;
        if (stroke.Count >= 2)
        {
            _draft ??= new PdfAnnotatorDraft
            {
                Kind = DocumentAnnotationKind.Drawing,
                Anchor = DocumentCommentAnchor.Point(_currentPage, stroke[0].X, stroke[0].Y)
            };
            _draft.Strokes.Add(new DocumentInkStroke { Points = stroke });
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task<(double X, double Y)?> NormalizePointAsync(double offsetX, double offsetY, bool refreshSize)
    {
        if (refreshSize || _surfaceSize is null)
        {
            try
            {
                _surfaceSize = await JS.InvokeAsync<double[]>("tmPdfAnnotator.measure", _surfaceRef);
            }
            catch
            {
                _surfaceSize = null;
            }
        }

        if (_surfaceSize is not { Length: >= 2 } size || size[0] <= 0 || size[1] <= 0)
        {
            return null;
        }

        return (offsetX / size[0], offsetY / size[1]);
    }

    // ── Annotation creation ──────────────────────────────────────────────────

    private DocumentCommentUser ResolveAuthor()
        => CurrentUser ?? new DocumentCommentUser
        {
            UserId = "anonymous",
            DisplayName = Loc["TmPdfAnnotator_AnonymousUser"]
        };

    private async Task HandleTextSelectedAsync(PdfTextSelection selection)
    {
        if (_mode != PdfAnnotatorMode.Highlight || !selection.IsValid)
        {
            return;
        }

        _draft = new PdfAnnotatorDraft
        {
            Kind = DocumentAnnotationKind.Highlight,
            Anchor = selection.ToAnchor()
        };
        await InvokeAsync(StateHasChanged);
    }

    private async Task CreateStampAsync(double x, double y)
    {
        var stampText = EffectiveStampOptions[Math.Min(_selectedStampIndex, EffectiveStampOptions.Count - 1)];
        const double stampWidth = 0.18;
        const double stampHeight = 0.05;
        var request = new DocumentCommentThreadCreateRequest
        {
            Anchor = DocumentCommentAnchor.Area(
                _currentPage,
                Math.Clamp(x - stampWidth / 2, 0, 1 - stampWidth),
                Math.Clamp(y - stampHeight / 2, 0, 1 - stampHeight),
                stampWidth,
                stampHeight),
            Body = stampText,
            Kind = DocumentAnnotationKind.Stamp,
            Color = AnnotationColor,
            StampText = stampText
        };

        await CreateThreadAsync(request);
    }

    private async Task CreateFromDraftAsync(string body)
    {
        if (_draft?.Anchor is null)
        {
            return;
        }

        var request = new DocumentCommentThreadCreateRequest
        {
            Anchor = _draft.Anchor,
            Body = body ?? string.Empty,
            Kind = _draft.Kind,
            Color = AnnotationColor,
            InkStrokes = [.. _draft.Strokes]
        };

        _draft = null;
        await CreateThreadAsync(request);
    }

    private async Task CreateThreadAsync(DocumentCommentThreadCreateRequest request)
    {
        try
        {
            var created = await EffectiveProvider.CreateThreadAsync(EffectiveDocumentId, request, ResolveAuthor());
            _selectedThreadId = created.Id;
            await LoadThreadsAsync();
        }
        catch { }
    }

    private async Task DismissDraftAsync()
    {
        _draft = null;
        _activeStroke = null;
        await InvokeAsync(StateHasChanged);
    }

    // ── Panel callbacks ──────────────────────────────────────────────────────

    private async Task ReplyToThreadAsync(DocumentCommentReplyRequest request)
    {
        try
        {
            await EffectiveProvider.ReplyAsync(EffectiveDocumentId, request, ResolveAuthor());
            await LoadThreadsAsync();
        }
        catch { }
    }

    private async Task ResolveThreadAsync(string threadId)
    {
        try
        {
            await EffectiveProvider.ResolveAsync(EffectiveDocumentId, threadId, ResolveAuthor());
            await LoadThreadsAsync();
        }
        catch { }
    }

    private async Task ReopenThreadAsync(string threadId)
    {
        try
        {
            await EffectiveProvider.ReopenAsync(EffectiveDocumentId, threadId, ResolveAuthor());
            await LoadThreadsAsync();
        }
        catch { }
    }

    private async Task DeleteCommentAsync(DocumentCommentDeleteRequest request)
    {
        try
        {
            var target = _threads.FirstOrDefault(thread => string.Equals(thread.Id, request.ThreadId, StringComparison.Ordinal));
            var threadWillBeRemoved = target is null || target.Comments.Count <= 1;

            await EffectiveProvider.DeleteAsync(EffectiveDocumentId, request);

            if (threadWillBeRemoved && string.Equals(_selectedThreadId, request.ThreadId, StringComparison.Ordinal))
            {
                _selectedThreadId = null;
            }

            await LoadThreadsAsync();
        }
        catch { }
    }

    private async Task EditCommentAsync(DocumentCommentEditRequest request)
    {
        try
        {
            await EffectiveProvider.EditAsync(EffectiveDocumentId, request);
            await LoadThreadsAsync();
        }
        catch { }
    }

    private async Task ClosePanelAsync()
    {
        // Internal state first: an unbound host never echoes ShowPanel back, so the panel must
        // disappear (and be re-openable by the toolbar toggle) without the parameter changing.
        _panelVisible = false;
        if (IsMobileLayout)
        {
            // The sheet's close unmounted the opener's click target context: restore focus.
            _focusOpenerAfter = true;
        }

        await ShowPanelChanged.InvokeAsync(false);
        await InvokeAsync(StateHasChanged);
    }

    private async Task TogglePanelAsync()
    {
        _panelVisible = !_panelVisible;
        await ShowPanelChanged.InvokeAsync(_panelVisible);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Forwards the viewer's thumbnails collapse change: internal state first (an
    /// unbound host never echoes the parameter), then the two-way callback.</summary>
    private async Task HandleThumbnailsCollapsedChangedAsync(bool value)
    {
        _thumbnailsCollapsed = value;
        await ThumbnailsCollapsedChanged.InvokeAsync(value);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Forwards the viewer's zoom mode change: internal state first, then the
    /// two-way callback (a fit mode abandoned by a zoom button reports Custom).</summary>
    private async Task HandleZoomModeChangedAsync(PdfZoomMode value)
    {
        _zoomMode = value;
        await ZoomModeChanged.InvokeAsync(value);
        await InvokeAsync(StateHasChanged);
    }

    private async Task SelectThreadAsync(string? threadId)
    {
        _selectedThreadId = threadId;
        // Selecting a thread anchored on another page navigates there: the current page
        // is handed to the embedded viewer through its Page parameter.
        var page = _threads.FirstOrDefault(t => string.Equals(t.Id, threadId, StringComparison.Ordinal))?.Anchor.PageNumber ?? 0;
        if (page >= 1 && page != _currentPage)
        {
            _currentPage = page;
            ResetPageState();
        }

        // G2: on a phone the panel is the bottom sheet — a tapped thread marker opens it on
        // that thread (the HALF snap, the selected card scrolled into view and the reply
        // composer focused), like the diagram editor's selection-driven sheet.
        if (IsMobileLayout && threadId is not null)
        {
            if (!_panelVisible)
            {
                await RememberOpenerAsync();
                _panelVisible = true;
                _mobileSnapIndex = MobileOpenSnapIndex;
                await ShowPanelChanged.InvokeAsync(true);
            }

            _focusComposerAfter = true;
            _scrollSelectedCardAfter = true;
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task SetShowResolvedAsync(bool value)
    {
        _showResolved = value;
        await ShowResolvedChanged.InvokeAsync(value);
        await InvokeAsync(StateHasChanged);
    }

    // ── Colors ───────────────────────────────────────────────────────────────

    private string ResolveThreadColor(DocumentCommentThread thread)
        => PdfAnnotationColorHelper.ResolveColor(thread, AuthorColors, RoleColors, KnownUsers);

    private string CurrentAuthorColor
        => AnnotationColor ?? PdfAnnotationColorHelper.ResolveForAuthor(
            CurrentUser?.UserId ?? "anonymous", AuthorColors, RoleColors, KnownUsers);

    // ── Viewer callbacks ─────────────────────────────────────────────────────

    private async Task HandlePageChangedAsync(int page)
    {
        _currentPage = page;
        ResetPageState();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Drops page-scoped interaction state (an in-progress ink stroke and the cached
    /// surface size); used whenever the visible page changes, from page navigation
    /// (<see cref="HandlePageChangedAsync"/>) and thread selection
    /// (<see cref="SelectThreadAsync"/>) alike.
    /// </summary>
    private void ResetPageState()
    {
        _activeStroke = null;
        _surfaceSize = null;
    }

    private void HandleDocumentLoaded(int totalPages)
    {
        _totalPages = totalPages;
        if (totalPages > 0 && _currentPage > totalPages)
        {
            _currentPage = 1;
        }
    }

    // ── Export & print ───────────────────────────────────────────────────────

    /// <summary>Exports the document with its annotations embedded as PDF annotations,
    /// or drawn into the page content when <paramref name="flatten"/> is true.</summary>
    /// <param name="flatten">Whether annotations are flattened into the page content.</param>
    public async Task ExportAsync(bool flatten = false)
    {
        if (string.IsNullOrEmpty(Url))
        {
            return;
        }

        var payload = PdfAnnotationExportPayloadBuilder.Build(
            _threads.ToArray(), includeResolved: true, AuthorColors, RoleColors, KnownUsers);
        var options = JsonSerializer.Serialize(new { flatten, fileName = ResolveExportFileName() });

        try { await JS.InvokeVoidAsync("tmPdfAnnotator.exportPdf", Url, payload, options); }
        catch { }
    }

    /// <summary>Opens a print variant of the document with annotations rendered on the pages
    /// followed by a comment summary.</summary>
    public async Task PrintAsync()
    {
        if (string.IsNullOrEmpty(Url))
        {
            return;
        }

        var payload = PdfAnnotationExportPayloadBuilder.Build(
            _threads.ToArray(), includeResolved: _showResolved, AuthorColors, RoleColors, KnownUsers);

        try { await JS.InvokeVoidAsync("tmPdfAnnotator.printWithAnnotations", Url, payload); }
        catch { }
    }

    private string ResolveExportFileName()
    {
        if (!string.IsNullOrWhiteSpace(ExportFileName))
        {
            return ExportFileName!;
        }

        var name = Url ?? "document.pdf";
        var slash = name.LastIndexOfAny(['/', '\\']);
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        var query = name.IndexOfAny(['?', '#']);
        if (query >= 0)
        {
            name = name[..query];
        }

        if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return (string.IsNullOrWhiteSpace(name) ? "document" : name) + "-annotated.pdf";
    }
}
