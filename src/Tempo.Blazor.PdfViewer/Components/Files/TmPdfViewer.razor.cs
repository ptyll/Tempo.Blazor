using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.Files;

/// <summary>View mode for the PDF viewer.</summary>
public enum PdfViewMode
{
    /// <summary>Single page mode with navigation.</summary>
    SinglePage,
    /// <summary>Continuous scroll through all pages.</summary>
    Continuous
}

/// <summary>
/// How the viewer decides the zoom scale. <see cref="TmPdfViewer.ZoomMode"/> drives it;
/// <see cref="Auto"/> (the default since the responsive viewer redesign) resolves to
/// fit-width on a mobile layout and to the bound <see cref="TmPdfViewer.Scale"/>
/// everywhere else. Set <see cref="Custom"/> to restore the pre-Auto behavior.
/// </summary>
public enum PdfZoomMode
{
    /// <summary>The scale is controlled by the user (zoom buttons, presets, binding).</summary>
    Custom,
    /// <summary>The scale is computed so the current page fills the container width.</summary>
    FitWidth,
    /// <summary>The scale is computed so the whole current page fits the container.</summary>
    FitPage,
    /// <summary>
    /// Default. Fit-width on a mobile layout, the bound scale everywhere else — a resize
    /// only recomputes the scale while the layout stays mobile, so a zoom preset or button
    /// still hands control back to the user.
    /// </summary>
    Auto
}

/// <summary>
/// How the viewer presents its search UI on desktop and tablet layouts. Driven by
/// <see cref="TmPdfViewer.SearchPresentation"/>; a mobile layout always keeps the
/// in-flow search bar (the sheet-style presentation).
/// </summary>
public enum PdfSearchPresentation
{
    /// <summary>
    /// The pre-2.9.1 in-flow search bar under the toolbar (a full-width row with the
    /// input, the match counter and the prev/next buttons).
    /// </summary>
    Inline,
    /// <summary>
    /// Default. A floating search card anchored top-right over the page body — outside
    /// the scroll area, never covering the toolbar. Escape closes it and returns focus
    /// to the toolbar's search toggle.
    /// </summary>
    Floating
}

/// <summary>
/// A reusable PDF viewer component powered by PDF.js v5.
/// Supports page navigation, zoom, rotation, text layer, thumbnails, search, and continuous scroll.
/// </summary>
public partial class TmPdfViewer : TmComponentBase, IAsyncDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>URL of the PDF document to display.</summary>
    [Parameter] public string? Url { get; set; }

    /// <summary>Current page number (1-based). Supports two-way binding.</summary>
    [Parameter] public int Page { get; set; } = 1;

    /// <summary>Callback when the page changes.</summary>
    [Parameter] public EventCallback<int> PageChanged { get; set; }

    /// <summary>Current zoom scale. Supports two-way binding.</summary>
    [Parameter] public double Scale { get; set; } = 1.0;

    /// <summary>Callback when the scale changes.</summary>
    [Parameter] public EventCallback<double> ScaleChanged { get; set; }

    /// <summary>Whether to show the toolbar. Default is true.</summary>
    [Parameter] public bool ShowToolbar { get; set; } = true;

    /// <summary>Whether to allow downloading the PDF. Default is true.</summary>
    [Parameter] public bool AllowDownload { get; set; } = true;

    /// <summary>Whether to allow rotation. Default is false.</summary>
    [Parameter] public bool AllowRotation { get; set; }

    /// <summary>Whether to render a selectable text layer over the canvas. Default is false.</summary>
    [Parameter] public bool ShowTextLayer { get; set; }

    /// <summary>Whether to show a thumbnails sidebar. Default is false.</summary>
    [Parameter] public bool ShowThumbnails { get; set; }

    /// <summary>Whether to show a search input. Default is false.</summary>
    [Parameter] public bool ShowSearch { get; set; }

    /// <summary>
    /// How the search UI is presented on desktop and tablet layouts. Default
    /// <see cref="PdfSearchPresentation.Floating"/> (since 2.9.1): a card anchored
    /// top-right over the page body, outside the scroll area. Set
    /// <see cref="PdfSearchPresentation.Inline"/> to restore the pre-2.9.1 in-flow bar
    /// under the toolbar. A mobile layout always keeps the in-flow bar.
    /// </summary>
    [Parameter] public PdfSearchPresentation SearchPresentation { get; set; } = PdfSearchPresentation.Floating;

    /// <summary>
    /// Whether the document header's default subtitle appends the formatted file size
    /// ("Pages: N · 1.4 MB"). Only applies when <see cref="ShowHeader"/> is true and
    /// no explicit <see cref="Subtitle"/> is given. Default is true.
    /// </summary>
    [Parameter] public bool ShowFileSize { get; set; } = true;

    /// <summary>
    /// The document's byte size shown in the header. When set, this host-supplied value
    /// wins over the length the viewer obtains from PDF.js
    /// (<c>getDownloadInfo</c> → <see cref="OnDocumentLengthKnown"/>); when omitted and
    /// the size is unknown, the subtitle keeps its pages-only form.
    /// </summary>
    [Parameter] public long? FileSize { get; set; }

    /// <summary>
    /// Whether the viewer renders its own mobile action bar (Pages | Search | Comments).
    /// Default is true. Hosts that fold these entries into their own action bar (such as
    /// <see cref="TmPdfAnnotator"/>) pass false and drive the sheets through the public
    /// <see cref="OpenThumbnailsSheetAsync"/> / <see cref="OpenSearchAsync"/> methods instead.
    /// </summary>
    [Parameter] public bool ShowMobileActionBar { get; set; } = true;

    /// <summary>Whether to show a view mode toggle (single/continuous). Default is false.</summary>
    [Parameter] public bool ShowViewModeToggle { get; set; }

    /// <summary>Current view mode. Default is SinglePage.</summary>
    [Parameter] public PdfViewMode ViewMode { get; set; } = PdfViewMode.SinglePage;

    /// <summary>Callback when the view mode changes.</summary>
    [Parameter] public EventCallback<PdfViewMode> ViewModeChanged { get; set; }

    /// <summary>
    /// How the zoom scale is decided. Default <see cref="PdfZoomMode.Auto"/>: fit-width on a
    /// mobile layout, the bound scale everywhere else. <see cref="PdfZoomMode.FitWidth"/> and
    /// <see cref="PdfZoomMode.FitPage"/> let JavaScript compute the scale from the container
    /// size (and keep recomputing it on resize); any zoom button or preset switches the mode
    /// back to <see cref="PdfZoomMode.Custom"/>. Set <see cref="PdfZoomMode.Custom"/> to
    /// restore the pre-Auto behavior at every width. Supports two-way binding.
    /// </summary>
    [Parameter] public PdfZoomMode ZoomMode { get; set; } = PdfZoomMode.Auto;

    /// <summary>Callback when the zoom mode changes.</summary>
    [Parameter] public EventCallback<PdfZoomMode> ZoomModeChanged { get; set; }

    /// <summary>
    /// Whether the toolbar's icon buttons show their text labels. Default
    /// <see cref="ToolbarLabels.Icons"/> (the icon-only toolbar of the earlier redesign
    /// phases); <see cref="ToolbarLabels.Auto"/> shows the labels except on a toolbar
    /// narrower than the mobile breakpoint, and <see cref="ToolbarLabels.IconsWithText"/>
    /// always shows them.
    /// </summary>
    [Parameter] public ToolbarLabels ToolbarLabels { get; set; } = ToolbarLabels.Icons;

    /// <summary>
    /// How the viewer decides its responsive layout. <see cref="TmLayoutMode.Auto"/> measures
    /// the viewer's own container; a forced value renders that layout without measuring, so a
    /// test — or a host — can pin a mode. Default <see cref="TmLayoutMode.Auto"/>.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>Whether to show a document header above the toolbar. Default is false,
    /// which keeps the toolbar-only layout. When true, the Open and Download links move
    /// from the toolbar into the header.</summary>
    [Parameter] public bool ShowHeader { get; set; }

    /// <summary>Document title rendered in the header. Used only when <see cref="ShowHeader"/> is true.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>
    /// Document subtitle rendered in the header. Used only when <see cref="ShowHeader"/> is true.
    /// When omitted, a neutral page count ("Pages: N") is rendered once the document is loaded.
    /// </summary>
    [Parameter] public string? Subtitle { get; set; }

    /// <summary>Whether to show an "Open in new tab" link. Default is true.</summary>
    [Parameter] public bool ShowOpenLink { get; set; } = true;

    /// <summary>
    /// Whether the thumbnails sidebar is collapsed. Supports two-way binding; the collapse
    /// button in the sidebar header and the toolbar "Pages" toggle both drive it.
    /// Default is false.
    /// </summary>
    [Parameter] public bool ThumbnailsCollapsed { get; set; }

    /// <summary>Callback when the thumbnails collapse state changes.</summary>
    [Parameter] public EventCallback<bool> ThumbnailsCollapsedChanged { get; set; }

    /// <summary>
    /// Content rendered at the very start of the toolbar (before the navigation group),
    /// letting hosts such as TmPdfAnnotator compose their own tool groups into the toolbar.
    /// </summary>
    [Parameter] public RenderFragment? ToolbarStart { get; set; }

    /// <summary>
    /// Content rendered at the very end of the toolbar (after the built-in groups),
    /// letting hosts such as TmPdfAnnotator compose their own tool groups into the toolbar.
    /// </summary>
    [Parameter] public RenderFragment? ToolbarEnd { get; set; }

    /// <summary>Content rendered at the start of the document header. Used only when <see cref="ShowHeader"/> is true.</summary>
    [Parameter] public RenderFragment? HeaderStart { get; set; }

    /// <summary>Content rendered at the end of the document header, after the Open/Download links. Used only when <see cref="ShowHeader"/> is true.</summary>
    [Parameter] public RenderFragment? HeaderActions { get; set; }

    /// <summary>
    /// Content rendered in the viewer's in-flow sticky selection CTA row — OUTSIDE the
    /// scroll area (FZE-9 review M4), so a zoom or a scroll never carries it away. Hosts
    /// (the annotator) supply their own sticky selection actions through this slot; the
    /// viewer's own comment CTA is independent and may render in the same row. The row
    /// renders whenever this slot is supplied.
    /// </summary>
    [Parameter] public RenderFragment? SelectionCtaContent { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Height of the viewer (CSS value). Default is "600px".</summary>
    [Parameter] public string? Height { get; set; }

    /// <summary>Additional HTML attributes passed to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>
    /// The layout context cascaded by an ancestor <see cref="Layout.TmLayoutObserver"/>. A forced
    /// ancestor decides the layout for this viewer too (same resolution as
    /// <see cref="Layout.TmEditorShell"/>): its resolved mode wins over the viewer's own
    /// measurement while <see cref="LayoutMode"/> stays <see cref="TmLayoutMode.Auto"/>.
    /// </summary>
    [CascadingParameter] public TmLayoutContext? Parent { get; set; }

    // ── Annotation parameters ─────────────────────────────────────────────────

    /// <summary>Whether the annotation layer and comments panel are enabled. Default is false.</summary>
    [Parameter] public bool EnableAnnotations { get; set; }

    /// <summary>
    /// Provider used to load and persist annotation threads when <see cref="EnableAnnotations"/> is true.
    /// When omitted, an in-memory provider keeps annotations for the current session.
    /// </summary>
    [Parameter] public IPdfAnnotationProvider? AnnotationProvider { get; set; }

    /// <summary>Stable document identifier used by the annotation provider. Falls back to <see cref="Url"/>.</summary>
    [Parameter] public string? DocumentId { get; set; }

    /// <summary>Author applied to annotations created by the current viewer.</summary>
    [Parameter] public DocumentCommentUser? CurrentUser { get; set; }

    /// <summary>Whether resolved annotation threads are shown by default. Default is false.</summary>
    [Parameter] public bool ShowResolvedAnnotations { get; set; }

    /// <summary>Optional seed threads used when no <see cref="AnnotationProvider"/> is supplied.</summary>
    [Parameter] public IReadOnlyList<DocumentCommentThread>? Annotations { get; set; }

    /// <summary>Callback invoked when the loaded annotation set changes.</summary>
    [Parameter] public EventCallback<IReadOnlyList<DocumentCommentThread>> AnnotationsChanged { get; set; }

    /// <summary>Callback invoked when text is selected in the viewer text layer.</summary>
    [Parameter] public EventCallback<PdfTextSelection> OnTextSelected { get; set; }

    /// <summary>
    /// Optional content rendered in an overlay that is kept aligned with the current page
    /// canvas (used by TmPdfAnnotator to draw its own annotation layer).
    /// </summary>
    [Parameter] public RenderFragment? OverlayContent { get; set; }

    /// <summary>Callback invoked with the total page count once the document is loaded.</summary>
    [Parameter] public EventCallback<int> OnDocumentLoaded { get; set; }

    /// <summary>
    /// Enables text selection events (<see cref="OnTextSelected"/>) without turning on the
    /// built-in annotation layer and panel. Default is false.
    /// </summary>
    [Parameter] public bool EnableTextSelectionEvents { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private ElementReference _rootRef;
    private ElementReference _canvasRef;
    private ElementReference _textLayerRef;
    private ElementReference _thumbnailsRef;
    private ElementReference _continuousRef;
    private ElementReference _searchLayerRef;
    private ElementReference _annotationOverlayRef;
    private ElementReference _customOverlayRef;
    private ElementReference _canvasWrapRef;
    private bool _customOverlaySynced;
    private DotNetObjectReference<TmPdfViewer>? _dotNetRef;
    private bool _pdfInitialized;
    private bool _useFallback;
    private bool _isLoading;
    private int _currentPage = 1;
    private int _totalPages;
    /// <summary>
    /// The byte length reported by JavaScript once the document has loaded
    /// (<see cref="OnDocumentLengthKnown"/>). Null when the length is unknown —
    /// a failed or missing <c>getDownloadInfo</c> reports nothing and the header
    /// keeps its pages-only subtitle.
    /// </summary>
    private long? _reportedLength;
    private double _scale = 1.0;
    /// <summary>
    /// Scale bounds shared with the JS fit computation (pdf-viewer.js clamps to the same
    /// range). A fit mode MAY compute a scale outside <see cref="_zoomSteps"/> — that is by
    /// design so fit-width can use the exact container ratio — but never outside this range.
    /// </summary>
    internal const double MinScale = 0.1;
    internal const double MaxScale = 5.0;
    /// <summary>Current rotation in degrees (0, 90, 180, 270).</summary>
    public int Rotation { get; private set; }
    private PdfViewMode _viewMode = PdfViewMode.SinglePage;
    private PdfZoomMode _zoomMode = PdfZoomMode.Custom;
    /// <summary>Text currently shown in the toolbar page input; kept in sync with <see cref="_currentPage"/>.</summary>
    private string _pageInput = "1";
    private bool _thumbnailsCollapsed;
    private string? _loadError;
    private string? _lastUrl;
    /// <summary>
    /// Incremented on every <see cref="Url"/> change. The OnAfterRenderAsync pass that starts
    /// <c>tmPdfViewer.init</c> captures the current generation and only ends the loading state
    /// if it still owns it — otherwise an older load finishing after a mid-download Url change
    /// would clear the spinner and consume the one-shot flags of the newer load.
    /// </summary>
    private int _loadGeneration;
    /// <summary>The (Url, Scale, Rotation) tuple the continuous scroll was last rendered with,
    /// so OnAfterRenderAsync re-renders only when one of them changed.</summary>
    private (string? Url, double Scale, int Rotation)? _lastContinuousRender;
    /// <summary>The Url the thumbnails sidebar was last rendered with.</summary>
    private string? _lastThumbnailUrl;
    /// <summary>The page the thumbnails sidebar last highlighted.</summary>
    private int _lastHighlightedThumbnailPage;
    /// <summary>
    /// A fit recompute was deferred to the next <see cref="OnAfterRenderAsync"/> pass: a
    /// view-mode switch swaps the observed container (single-page canvas wrap vs
    /// continuous wrap), and its ElementReference is stale — the fresh element exists
    /// only after the switch has been rendered.
    /// </summary>
    private bool _fitPending;
    /// <summary>
    /// A Continuous → SinglePage switch was deferred to the next
    /// <see cref="OnAfterRenderAsync"/> pass: the canvas, text-layer and overlay
    /// ElementReferences still point at the torn-down continuous DOM until the
    /// re-render that follows the switch — rendering or attaching selection against
    /// them would target stale (or missing) nodes. Same pattern as <see cref="_fitPending"/>.
    /// </summary>
    private bool _singlePagePending;

    // Search state
    private bool _searchVisible;
    private string? _searchQuery;
    private string? _lastSearchedQuery;
    private int _searchTotalMatches;
    private int _searchCurrentMatch;
    /// <summary>The last executed search completed with zero matches: the localized
    /// no-matches verdict renders in the live region and the input flags aria-invalid
    /// until a new search runs or the query is cleared.</summary>
    private bool _searchRanWithNoMatches;
    /// <summary>The floating search card's input; focused when the card opens so typing
    /// can start immediately (bUnit has no live element — the focus is best-effort).</summary>
    private ElementReference _searchInputRef;
    /// <summary>The search input must be focused in the next OnAfterRenderAsync pass
    /// (the card has to render first).</summary>
    private bool _pendingSearchInputFocus;

    // Annotation state
    private bool _annotationsLoaded;
    private bool _selectionEnabled;
    private bool _annotationsPanelVisible = true;
    private bool _showResolvedAnnotations;
    private string? _selectedThreadId;
    private PdfTextSelection? _pendingSelection;
    private List<DocumentCommentThread> _threads = [];
    private InMemoryPdfAnnotationProvider? _fallbackProvider;

    // Responsive state (FZE-7)
    /// <summary>
    /// The layout the wrapping <see cref="Tempo.Blazor.Components.Layout.TmLayoutObserver"/> last
    /// measured (reported through <see cref="OnResolvedLayoutChanged"/>). Null until the first
    /// report; combined with <see cref="LayoutMode"/> and <see cref="Parent"/> through
    /// <see cref="TmLayout.Resolve"/>.
    /// </summary>
    private TmLayoutMode? _measured;
    /// <summary>
    /// The resolved layout the chrome branches on: <see cref="TmLayout.Resolve"/> of the
    /// <see cref="LayoutMode"/> parameter, the cascaded <see cref="Parent"/> and
    /// <see cref="_measured"/>. Desktop until the first report.
    /// </summary>
    private TmLayoutMode _resolved = TmLayoutMode.Desktop;
    /// <summary>
    /// Whether the user has touched the panels (thumbnails, comments) since the document was
    /// loaded. While false, a layout flip re-applies the panel defaults; the user's own choice
    /// is never overwritten.
    /// </summary>
    private bool _panelsTouched;
    /// <summary>Whether the comments panel is open as a sheet (tablet side sheet / mobile bottom sheet).</summary>
    private bool _commentsSheetOpen;
    /// <summary>Whether the thumbnails sheet is open on a mobile layout.</summary>
    private bool _thumbnailsSheetOpen;

    private static readonly double[] _zoomSteps = [0.5, 0.75, 1.0, 1.25, 1.5, 2.0];

    private IPdfAnnotationProvider EffectiveAnnotationProvider
    {
        get
        {
            if (AnnotationProvider is not null)
            {
                return AnnotationProvider;
            }

            if (_fallbackProvider is null)
            {
                _fallbackProvider = Annotations is { Count: > 0 }
                    ? new InMemoryPdfAnnotationProvider(new Dictionary<string, IReadOnlyList<DocumentCommentThread>>
                    {
                        [AnnotationDocumentId] = Annotations
                    })
                    : new InMemoryPdfAnnotationProvider();
            }

            return _fallbackProvider;
        }
    }

    private string AnnotationDocumentId
        => !string.IsNullOrEmpty(DocumentId) ? DocumentId : (Url ?? string.Empty);

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // The chrome branches on TmLayout.Resolve: an explicit LayoutMode wins, then a forced
        // cascaded ancestor, then the observer's measurement (kept current by its callback) —
        // the first frame is Desktop. A forced mode also suppresses the measurement, exactly
        // like the TmEditorShell resolution. The resolution is APPLIED through
        // ApplyResolvedLayoutAsync (OnParametersSetAsync / OnResolvedLayoutChanged); only a
        // fresh document takes it directly here, because its from-scratch defaults must not
        // wait for — and must not double-apply behind — the async pass.
        var resolved = TmLayout.Resolve(LayoutMode, Parent, _measured, TmLayoutMode.Desktop);

        if (Url != _lastUrl)
        {
            _lastUrl = Url;
            // A newer generation owns the loading state from here on; the finally of an
            // older init() in flight must not end it.
            _loadGeneration++;
            _pdfInitialized = false;
            _currentPage = Math.Max(1, Page);
            _pageInput = _currentPage.ToString(CultureInfo.InvariantCulture);
            _scale = Scale is > 0 and <= 5 ? Scale : 1.0;
            Rotation = 0;
            _viewMode = ViewMode;
            _zoomMode = ZoomMode;
            _panelsTouched = false;
            // The panel defaults follow the layout (FZE-7): the docked thumbnails start
            // collapsed on a tablet layout, the comments panel opens only on desktop.
            _resolved = resolved;
            _thumbnailsCollapsed = ThumbnailsCollapsed || _resolved == TmLayoutMode.Tablet;
            _annotationsPanelVisible = _resolved == TmLayoutMode.Desktop;
            _commentsSheetOpen = false;
            _thumbnailsSheetOpen = false;
            _useFallback = false;
            _isLoading = true;
            _loadError = null;
            _totalPages = 0;
            _reportedLength = null;
            _singlePagePending = false;
            _fitPending = false;
            // Render keys are per document: a new Url must re-render thumbnails
            // and continuous pages even if scale/rotation are unchanged.
            _lastContinuousRender = null;
            _lastThumbnailUrl = null;
            _customOverlaySynced = false;
            _searchVisible = ShowSearch;
            _searchQuery = null;
            _lastSearchedQuery = null;
            _searchTotalMatches = 0;
            _searchCurrentMatch = 0;
            _annotationsLoaded = false;
            _selectionEnabled = false;
            _pinchWiredRefId = null;
            _showResolvedAnnotations = ShowResolvedAnnotations;
            _selectedThreadId = null;
            _pendingSelection = null;
            _threads = [];
            _fallbackProvider = null;
        }
        else
        {
            if (Page != _currentPage && Page >= 1)
            {
                _currentPage = Page;
                _pageInput = _currentPage.ToString(CultureInfo.InvariantCulture);
                if (_pdfInitialized && !_useFallback)
                {
                    // In continuous mode a host-driven Page change scrolls the wrap —
                    // the lazy renderer paints it when it becomes visible.
                    _ = _viewMode == PdfViewMode.Continuous
                        ? ScrollToPageInContinuousAsync(Page)
                        : RenderCurrentPageAsync();
                }
            }
            if (Math.Abs(Scale - _scale) > 0.001 && Scale is > 0 and <= 5)
            {
                _scale = Scale;
                if (_pdfInitialized && !_useFallback)
                {
                    _ = ApplyZoomAsync();
                }
            }
            if (ZoomMode != _zoomMode)
            {
                _zoomMode = ZoomMode;
                if (_pdfInitialized && !_useFallback)
                {
                    if (_zoomMode is PdfZoomMode.Custom)
                    {
                        // A host-driven switch to Custom must detach the JS fit observer
                        // too — exactly like the user-driven SwitchToCustomZoomAsync.
                        _ = ClearFitAsync();
                    }
                    else if (_totalPages > 0)
                    {
                        _ = ApplyZoomModeAsync();
                    }
                }
            }
            if (ThumbnailsCollapsed != _thumbnailsCollapsed)
            {
                _thumbnailsCollapsed = ThumbnailsCollapsed;
                if (!ThumbnailsCollapsed)
                {
                    // The JS-rendered thumbnails died with the hidden panel; force the
                    // post-render pass to rebuild them in the fresh container (the
                    // user-driven SetThumbnailsCollapsedAsync does the same).
                    _lastThumbnailUrl = null;
                }
            }
            if (ViewMode != _viewMode)
            {
                _viewMode = ViewMode;
                if (_pdfInitialized && !_useFallback)
                {
                    _ = SwitchViewModeAsync();
                }
            }
        }
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        // A layout flip that arrives through a parameter — a forced <see cref="LayoutMode"/>
        // or a cascaded ancestor changing its resolved mode — must re-apply the same defaults
        // an observer-reported flip does. The fresh document was handled synchronously above
        // (its _resolved is already the new value, so this is a guarded no-op).
        if (string.IsNullOrEmpty(Url))
        {
            return;
        }

        var resolved = TmLayout.Resolve(LayoutMode, Parent, _measured, TmLayoutMode.Desktop);
        await ApplyResolvedLayoutAsync(resolved);

        // A fresh document applies the panel defaults synchronously (above), which the
        // no-op ApplyResolvedLayoutAsync call skips — report the guarded default to a bound
        // host here, so the next parameter pass does not override it with a stale value.
        if (!_panelsTouched && _thumbnailsCollapsed != ThumbnailsCollapsed && ThumbnailsCollapsedChanged.HasDelegate)
        {
            await ThumbnailsCollapsedChanged.InvokeAsync(_thumbnailsCollapsed);
        }
    }

    /// <summary>
    /// The single mutation point for a resolved-layout change: re-applies the guarded panel
    /// defaults (until the user has touched the panels themselves, <see cref="_panelsTouched"/>),
    /// starts the <see cref="PdfZoomMode.Auto"/> fit computation on mobile — or detaches the JS
    /// fit observer when the layout leaves mobile — and records <paramref name="next"/> as the
    /// resolved layout the chrome branches on. A no-op when the resolved mode did not change,
    /// so every caller may invoke it unconditionally.
    /// </summary>
    private async Task ApplyResolvedLayoutAsync(TmLayoutMode next)
    {
        if (_resolved == next)
        {
            return;
        }

        if (!_panelsTouched)
        {
            var reopenThumbnails = _thumbnailsCollapsed && !(ThumbnailsCollapsed || next == TmLayoutMode.Tablet);
            _thumbnailsCollapsed = ThumbnailsCollapsed || next == TmLayoutMode.Tablet;
            _annotationsPanelVisible = next == TmLayoutMode.Desktop;
            if (reopenThumbnails)
            {
                // The docked thumbnails reappear in a fresh container: the post-render pass
                // must rebuild the JS-rendered thumbnails (same as SetThumbnailsCollapsedAsync).
                _lastThumbnailUrl = null;
            }

            // Report the layout default to a bound host: the annotator forwards the callback
            // into its own two-way shadow, so the next parameter pass carries the same value
            // back instead of overriding the guarded default with a stale parameter (phase 8
            // review 8 — the annotator must not fight the viewer's tablet default).
            if (_thumbnailsCollapsed != ThumbnailsCollapsed && ThumbnailsCollapsedChanged.HasDelegate)
            {
                await ThumbnailsCollapsedChanged.InvokeAsync(_thumbnailsCollapsed);
            }
        }

        _resolved = next;

        // Read AFTER the assignment: the effective mode follows the new layout (Auto resolves
        // to fit-width on mobile, to the bound scale everywhere else).
        if (_pdfInitialized && !_useFallback)
        {
            if (next == TmLayoutMode.Mobile)
            {
                if (EffectiveZoomMode is not PdfZoomMode.Custom && _totalPages > 0)
                {
                    _ = ApplyZoomModeAsync();
                }
            }
            else if (_zoomMode == PdfZoomMode.Auto)
            {
                _ = ClearFitAsync();
            }
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (string.IsNullOrEmpty(Url)) return;

        if (!_pdfInitialized)
        {
            _pdfInitialized = true;
            _isLoading = true;
            StateHasChanged();
            _dotNetRef?.Dispose();
            _dotNetRef = DotNetObjectReference.Create(this);
            var loadGeneration = _loadGeneration;
            try
            {
                var available = await JS.InvokeAsync<bool>("tmPdfViewer.isAvailable");
                if (available)
                {
                    await JS.InvokeVoidAsync("tmPdfViewer.init", _rootRef, Url, _dotNetRef);
                }
                else
                {
                    _useFallback = true;
                }
            }
            catch
            {
                _useFallback = true;
            }
            finally
            {
                // A Url change mid-load started a newer generation; only the generation that
                // owns the current load may end its loading state.
                if (_loadGeneration == loadGeneration)
                {
                    _isLoading = false;
                }
                StateHasChanged();
            }
        }

        // Thumbnails are re-rendered only when the document changed; the active-page
        // highlight is refreshed separately whenever the page changes.
        if (_totalPages > 0 && ShowThumbnails && !_useFallback && _thumbnailsRef.Context is not null)
        {
            if (_lastThumbnailUrl != Url)
            {
                _lastThumbnailUrl = Url;
                _lastHighlightedThumbnailPage = 0; // a re-render wipes the highlight
                try
                {
                    await JS.InvokeVoidAsync("tmPdfViewer.renderThumbnails", _rootRef, _thumbnailsRef, 0.3, _dotNetRef);
                }
                catch { }
            }

            if (_lastHighlightedThumbnailPage != _currentPage)
            {
                _lastHighlightedThumbnailPage = _currentPage;
                try { await JS.InvokeVoidAsync("tmPdfViewer.highlightThumbnail", _rootRef, _currentPage); }
                catch { }
            }
        }

        // Continuous pages are re-rendered only when the render key changed; explicit
        // zoom/rotate/view-mode calls update the key themselves.
        if (_totalPages > 0 && _viewMode == PdfViewMode.Continuous && _continuousRef.Context is not null
            && _lastContinuousRender != (Url, _scale, Rotation))
        {
            // The render key is recorded inside RenderContinuousPagesAsync only after
            // the interop call succeeded — a failed/no-op call stays unrecorded and is
            // retried by the next OnAfterRender pass.
            await RenderContinuousPagesAsync();
        }

        // A fit recompute deferred by a view-mode switch runs here: the swapped
        // container exists only after the switch was rendered, so the render above has
        // already put the fresh pages into it before the fit math observes it.
        if (_fitPending)
        {
            _fitPending = false;
            if (EffectiveZoomMode is not PdfZoomMode.Custom && !_useFallback && _totalPages > 0)
            {
                await ApplyZoomModeAsync();
            }
        }

        // The floating search card just opened: move the caret into its input. Runs
        // BEFORE the loading short-circuit — the card renders while the document is
        // still downloading and focusing must not wait for the load. Best-effort: bUnit
        // (no live element) and a card disposed before the pass both throw harmlessly.
        if (_pendingSearchInputFocus)
        {
            _pendingSearchInputFocus = false;
            if (_searchInputRef.Context is not null)
            {
                try { await _searchInputRef.FocusAsync(); }
                catch { /* no live element — skip the focus move */ }
            }
        }

        // The one-shot blocks below need the JS instance state that init() creates.
        // A re-entrant OnAfterRenderAsync pass (fired by the StateHasChanged() above)
        // runs while the init await is still pending, and must not consume their
        // flags — otherwise the overlay/selection is never synced at all.
        if (_isLoading) return;

        // A deferred Continuous → SinglePage switch (the toggle or the PD1 thread
        // select): the single-page canvas/text-layer/overlay refs are fresh only
        // after the swap rendered, so the render + selection/overlay setup runs here.
        if (_singlePagePending)
        {
            if (_canvasRef.Context is not null)
            {
                _singlePagePending = false;
                await RenderCurrentPageAsync();
                if (!_useFallback && (EnableAnnotations || EnableTextSelectionEvents))
                {
                    await SetupSelectionAsync();
                    await RefreshAnnotationOverlayAsync();
                }
            }
        }

        if (EnableAnnotations && !_useFallback && !_annotationsLoaded)
        {
            _annotationsLoaded = true;
            await LoadAnnotationsAsync();
            await SetupSelectionAsync();
            await RefreshAnnotationOverlayAsync();
        }

        if (!EnableAnnotations && EnableTextSelectionEvents && !_useFallback && !_selectionEnabled)
        {
            await SetupSelectionAsync();
            await RefreshAnnotationOverlayAsync();
        }

        // A custom overlay must be sized even when annotations/text selection are off —
        // the ResizeObserver attached by syncOverlay keeps it aligned afterwards.
        if (OverlayContent is not null && !_useFallback && !_customOverlaySynced && _customOverlayRef.Context is not null)
        {
            _customOverlaySynced = true;
            await RefreshAnnotationOverlayAsync();
        }

        // The pinch zoom binds to the active wrap; a fresh wrap element (the view-mode
        // swap, a re-created sheet container) re-wires it, everything else is a no-op.
        await SetupPinchAsync();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Navigates to a specific page. In continuous mode the page wrap is
    /// scrolled into view instead of re-rendering a single canvas. A fit mode recomputes
    /// inside <see cref="RenderCurrentPageAsync"/> for the new page size.</summary>
    public Task GoToPageAsync(int page) => NavigateToPageAsync(page);

    /// <summary>
    /// Opens the mobile thumbnails sheet. Public API for hosts that fold the viewer's
    /// Pages entry into their own mobile action bar (<see cref="ShowMobileActionBar"/> false);
    /// a no-op on desktop/tablet layouts and without <see cref="ShowThumbnails"/>.
    /// </summary>
    public Task OpenThumbnailsSheetAsync()
        => !ShowThumbnails || _useFallback ? Task.CompletedTask : SetThumbnailsSheetAsync(true);

    /// <summary>
    /// Opens the search input. Public API for hosts that fold the viewer's Search entry
    /// into their own mobile action bar (<see cref="ShowMobileActionBar"/> false); a no-op
    /// when the search is not offered (<see cref="ShowSearch"/> false) or already open.
    /// </summary>
    public Task OpenSearchAsync()
        => !ShowSearch || _searchVisible || _useFallback ? Task.CompletedTask : ToggleSearchAsync();

    /// <summary>Sets the zoom scale directly. Switches an active fit mode back to
    /// <see cref="PdfZoomMode.Custom"/> — an explicit scale is a user decision.</summary>
    public async Task SetScaleAsync(double scale)
    {
        if (scale is <= 0 or > 5) return;
        await SwitchToCustomZoomAsync();
        _scale = scale;
        await ScaleChanged.InvokeAsync(_scale);
        await ApplyZoomAsync();
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_pdfInitialized && !_useFallback)
        {
            try { await JS.InvokeVoidAsync("tmPdfViewer.destroy", _rootRef); }
            catch { }
        }
        _dotNetRef?.Dispose();
    }
}
