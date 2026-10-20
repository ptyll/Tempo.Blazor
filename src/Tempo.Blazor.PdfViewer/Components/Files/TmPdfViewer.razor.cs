using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Models;

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

    /// <summary>Whether to show a view mode toggle (single/continuous). Default is false.</summary>
    [Parameter] public bool ShowViewModeToggle { get; set; }

    /// <summary>Current view mode. Default is SinglePage.</summary>
    [Parameter] public PdfViewMode ViewMode { get; set; } = PdfViewMode.SinglePage;

    /// <summary>Callback when the view mode changes.</summary>
    [Parameter] public EventCallback<PdfViewMode> ViewModeChanged { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Height of the viewer (CSS value). Default is "600px".</summary>
    [Parameter] public string? Height { get; set; }

    /// <summary>Additional HTML attributes passed to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

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
    private bool _customOverlaySynced;
    private DotNetObjectReference<TmPdfViewer>? _dotNetRef;
    private bool _pdfInitialized;
    private bool _useFallback;
    private bool _isLoading;
    private int _currentPage = 1;
    private int _totalPages;
    private double _scale = 1.0;
    /// <summary>Current rotation in degrees (0, 90, 180, 270).</summary>
    public int Rotation { get; private set; }
    private PdfViewMode _viewMode = PdfViewMode.SinglePage;
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

    // Search state
    private bool _searchVisible;
    private string? _searchQuery;
    private string? _lastSearchedQuery;
    private int _searchTotalMatches;
    private int _searchCurrentMatch;

    // Annotation state
    private bool _annotationsLoaded;
    private bool _selectionEnabled;
    private bool _annotationsPanelVisible = true;
    private bool _showResolvedAnnotations;
    private string? _selectedThreadId;
    private PdfTextSelection? _pendingSelection;
    private List<DocumentCommentThread> _threads = [];
    private InMemoryPdfAnnotationProvider? _fallbackProvider;

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
        if (Url != _lastUrl)
        {
            _lastUrl = Url;
            // A newer generation owns the loading state from here on; the finally of an
            // older init() in flight must not end it.
            _loadGeneration++;
            _pdfInitialized = false;
            _currentPage = Math.Max(1, Page);
            _scale = Scale is > 0 and <= 5 ? Scale : 1.0;
            Rotation = 0;
            _viewMode = ViewMode;
            _useFallback = false;
            _isLoading = true;
            _loadError = null;
            _totalPages = 0;
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
                if (_pdfInitialized && !_useFallback)
                {
                    _ = RenderCurrentPageAsync();
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
            _lastContinuousRender = (Url, _scale, Rotation);
            try
            {
                await JS.InvokeVoidAsync("tmPdfViewer.renderAllPages", _rootRef, _continuousRef, _scale, Rotation);
            }
            catch { }
        }

        // The one-shot blocks below need the JS instance state that init() creates.
        // A re-entrant OnAfterRenderAsync pass (fired by the StateHasChanged() above)
        // runs while the init await is still pending, and must not consume their
        // flags — otherwise the overlay/selection is never synced at all.
        if (_isLoading) return;

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
    }

    // ── JS invokable ─────────────────────────────────────────────────────────

    /// <summary>Receives the total page count after the JavaScript PDF loader finishes.</summary>
    [JSInvokable]
    public void OnPdfLoaded(int totalPages)
    {
        _totalPages = totalPages;
        _isLoading = false;
        _loadError = null;
        if (OnDocumentLoaded.HasDelegate)
        {
            _ = InvokeAsync(() => OnDocumentLoaded.InvokeAsync(totalPages));
        }

        InvokeAsync(StateHasChanged);
    }

    /// <summary>Receives a PDF load failure notification from JavaScript and switches to fallback rendering.</summary>
    [JSInvokable]
    public void OnPdfLoadError(string message)
    {
        _useFallback = true;
        _isLoading = false;
        _loadError = Loc["TmPdfViewer_LoadError"];
        InvokeAsync(StateHasChanged);
    }

    /// <summary>Handles thumbnail page selection from the JavaScript thumbnail renderer.</summary>
    [JSInvokable]
    public async Task OnThumbnailClicked(int pageNum)
    {
        if (pageNum < 1 || pageNum > _totalPages) return;
        _currentPage = pageNum;
        await PageChanged.InvokeAsync(_currentPage);
        await RenderCurrentPageAsync();
        try { await JS.InvokeVoidAsync("tmPdfViewer.highlightThumbnail", _rootRef, _currentPage); }
        catch { }
    }

    /// <summary>Receives search result counts from the JavaScript search implementation.</summary>
    [JSInvokable]
    public void OnSearchResults(int totalMatches, int[] matchesPerPage)
    {
        _searchTotalMatches = totalMatches;
        _searchCurrentMatch = totalMatches > 0 ? 1 : 0;
        InvokeAsync(StateHasChanged);
    }

    /// <summary>Receives the active search match position (1-based) and its page from JavaScript.</summary>
    [JSInvokable]
    public async Task OnSearchActiveChanged(int activeMatch, int pageNumber)
    {
        _searchCurrentMatch = activeMatch;
        if (pageNumber >= 1 && pageNumber <= _totalPages && pageNumber != _currentPage)
        {
            _currentPage = pageNumber;
            await PageChanged.InvokeAsync(_currentPage);
            if (ShowTextLayer || EnableAnnotations || EnableTextSelectionEvents)
            {
                try { await JS.InvokeVoidAsync("tmPdfViewer.renderTextLayer", _rootRef, _textLayerRef, _currentPage, _scale, Rotation); }
                catch { }
            }
            await RefreshAnnotationOverlayAsync();
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Receives a text selection captured from the JavaScript text layer.</summary>
    /// <param name="text">Selected text.</param>
    /// <param name="page">One-based page the selection belongs to.</param>
    /// <param name="rects">Flat list of normalized rectangles: x, y, width, height per rectangle.</param>
    [JSInvokable]
    public async Task OnTextSelectionChanged(string? text, int page, double[]? rects)
    {
        var normalized = BuildRects(rects);
        if (string.IsNullOrWhiteSpace(text) || normalized.Count == 0)
        {
            _pendingSelection = null;
        }
        else
        {
            var pageNumber = page >= 1 ? page : _currentPage;
            _pendingSelection = new PdfTextSelection(text!, pageNumber, normalized);
            if (OnTextSelected.HasDelegate)
            {
                await OnTextSelected.InvokeAsync(_pendingSelection);
            }
        }

        await InvokeAsync(StateHasChanged);
    }

    private static List<DocumentCommentRect> BuildRects(double[]? flat)
    {
        var result = new List<DocumentCommentRect>();
        if (flat is null)
        {
            return result;
        }

        for (var i = 0; i + 3 < flat.Length; i += 4)
        {
            var rect = DocumentCommentRect.Create(flat[i], flat[i + 1], flat[i + 2], flat[i + 3]);
            if (rect.IsValid)
            {
                result.Add(rect);
            }
        }

        return result;
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private async Task GoToPreviousPageAsync()
    {
        if (_currentPage <= 1) return;
        _currentPage--;
        await PageChanged.InvokeAsync(_currentPage);
        await RenderCurrentPageAsync();
    }

    private async Task GoToNextPageAsync()
    {
        if (_currentPage >= _totalPages) return;
        _currentPage++;
        await PageChanged.InvokeAsync(_currentPage);
        await RenderCurrentPageAsync();
    }

    private async Task RenderCurrentPageAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("tmPdfViewer.renderPage", _rootRef, _currentPage, _scale, Rotation);
            if (ShowTextLayer || EnableAnnotations || EnableTextSelectionEvents)
            {
                await JS.InvokeVoidAsync("tmPdfViewer.renderTextLayer", _rootRef, _textLayerRef, _currentPage, _scale, Rotation);
            }
            await RefreshAnnotationOverlayAsync();
            await RedrawSearchHighlightsAsync();
        }
        catch { }
    }

    // ── Zoom ──────────────────────────────────────────────────────────────────

    private async Task ZoomInAsync()
    {
        var next = _zoomSteps.FirstOrDefault(z => z > _scale);
        if (next == 0) return;
        _scale = next;
        await ScaleChanged.InvokeAsync(_scale);
        await ApplyZoomAsync();
    }

    private async Task ZoomOutAsync()
    {
        var prev = _zoomSteps.LastOrDefault(z => z < _scale);
        if (prev == 0) return;
        _scale = prev;
        await ScaleChanged.InvokeAsync(_scale);
        await ApplyZoomAsync();
    }

    private async Task ApplyZoomAsync()
    {
        try
        {
            if (_viewMode == PdfViewMode.Continuous && _continuousRef.Context is not null)
            {
                await JS.InvokeVoidAsync("tmPdfViewer.renderAllPages", _rootRef, _continuousRef, _scale, Rotation);
                MarkContinuousRendered();
            }
            else
            {
                await JS.InvokeVoidAsync("tmPdfViewer.setScale", _rootRef, _scale);
                if (ShowTextLayer || EnableAnnotations || EnableTextSelectionEvents)
                {
                    await JS.InvokeVoidAsync("tmPdfViewer.renderTextLayer", _rootRef, _textLayerRef, _currentPage, _scale, Rotation);
                }
                await RefreshAnnotationOverlayAsync();
                await RedrawSearchHighlightsAsync();
            }
        }
        catch { }
    }

    // ── Rotation ──────────────────────────────────────────────────────────────

    /// <summary>Rotate the document 90 degrees clockwise.</summary>
    public async Task RotateAsync()
    {
        Rotation = (Rotation + 90) % 360;
        if (_viewMode == PdfViewMode.Continuous && _continuousRef.Context is not null)
        {
            try
            {
                await JS.InvokeVoidAsync("tmPdfViewer.renderAllPages", _rootRef, _continuousRef, _scale, Rotation);
                MarkContinuousRendered();
            }
            catch { }
        }
        else
        {
            await RenderCurrentPageAsync();
        }
    }

    // ── View Mode ─────────────────────────────────────────────────────────────

    private async Task SetViewModeAsync(PdfViewMode mode)
    {
        _viewMode = mode;
        await ViewModeChanged.InvokeAsync(mode);
        await SwitchViewModeAsync();
    }

    private async Task SwitchViewModeAsync()
    {
        if (_viewMode == PdfViewMode.Continuous)
        {
            try
            {
                await JS.InvokeVoidAsync("tmPdfViewer.renderAllPages", _rootRef, _continuousRef, _scale, Rotation);
                MarkContinuousRendered();
            }
            catch { }
        }
        else
        {
            await RenderCurrentPageAsync();
        }
    }

    /// <summary>
    /// Records the render key after an explicit continuous render (view-mode switch,
    /// zoom, rotate) so <see cref="OnAfterRenderAsync"/> does not repeat the work.
    /// A call before the document is loaded is a JS no-op and must not be recorded —
    /// the post-load <see cref="OnAfterRenderAsync"/> pass still has to render.
    /// </summary>
    private void MarkContinuousRendered()
    {
        if (_totalPages > 0)
        {
            _lastContinuousRender = (Url, _scale, Rotation);
        }
    }

    private async Task ToggleSearchAsync()
    {
        _searchVisible = !_searchVisible;
        await InvokeAsync(StateHasChanged);
    }

    // ── Search ────────────────────────────────────────────────────────────────

    private async Task HandleSearchKeyUpAsync(KeyboardEventArgs e)
    {
        if (e.Key != "Enter" || string.IsNullOrEmpty(_searchQuery))
        {
            return;
        }

        var queryChanged = !string.Equals(_searchQuery, _lastSearchedQuery, StringComparison.Ordinal);
        if (queryChanged || _searchTotalMatches == 0)
        {
            await PerformSearchAsync();
        }
        else if (e.ShiftKey)
        {
            await PreviousMatchAsync();
        }
        else
        {
            await NextMatchAsync();
        }
    }

    private async Task PerformSearchAsync()
    {
        if (string.IsNullOrEmpty(_searchQuery))
        {
            await ClearSearchAsync();
            return;
        }

        _lastSearchedQuery = _searchQuery;
        try
        {
            await JS.InvokeVoidAsync("tmPdfViewer.search", _rootRef, _searchLayerRef, _searchQuery, _dotNetRef);
        }
        catch { }
    }

    /// <summary>Moves the search selection to the next match and scrolls it into view.</summary>
    public async Task NextMatchAsync()
    {
        if (_searchTotalMatches == 0) return;
        try { await JS.InvokeVoidAsync("tmPdfViewer.searchNext", _rootRef, _searchLayerRef); }
        catch { }
    }

    /// <summary>Moves the search selection to the previous match and scrolls it into view.</summary>
    public async Task PreviousMatchAsync()
    {
        if (_searchTotalMatches == 0) return;
        try { await JS.InvokeVoidAsync("tmPdfViewer.searchPrev", _rootRef, _searchLayerRef); }
        catch { }
    }

    private async Task RedrawSearchHighlightsAsync()
    {
        if (!_searchVisible || _useFallback || string.IsNullOrEmpty(_searchQuery) || _searchLayerRef.Context is null)
        {
            return;
        }

        try { await JS.InvokeVoidAsync("tmPdfViewer.redrawSearch", _rootRef, _searchLayerRef, _currentPage); }
        catch { }
    }

    private async Task ClearSearchAsync()
    {
        _searchQuery = null;
        _lastSearchedQuery = null;
        _searchTotalMatches = 0;
        _searchCurrentMatch = 0;
        try { await JS.InvokeVoidAsync("tmPdfViewer.clearSearch", _rootRef, _searchLayerRef); }
        catch { }
        await InvokeAsync(StateHasChanged);
    }

    // ── Annotations ─────────────────────────────────────────────────────────

    private async Task LoadAnnotationsAsync()
    {
        try
        {
            _threads = (await EffectiveAnnotationProvider.GetThreadsAsync(AnnotationDocumentId)).ToList();
            await AnnotationsChanged.InvokeAsync(_threads);
        }
        catch
        {
            _threads = [];
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task SetupSelectionAsync()
    {
        if (_selectionEnabled || _useFallback)
        {
            return;
        }

        try
        {
            await JS.InvokeVoidAsync("tmPdfViewer.renderTextLayer", _rootRef, _textLayerRef, _currentPage, _scale, Rotation);
            await JS.InvokeVoidAsync("tmPdfViewer.enableSelection", _rootRef, _textLayerRef, _dotNetRef);
            _selectionEnabled = true;
        }
        catch { }
    }

    private async Task RefreshAnnotationOverlayAsync()
    {
        if (_useFallback)
        {
            return;
        }

        try
        {
            if (EnableAnnotations && _annotationOverlayRef.Context is not null)
            {
                await JS.InvokeVoidAsync("tmPdfViewer.syncOverlay", _rootRef, _annotationOverlayRef);
            }

            if (OverlayContent is not null && _customOverlayRef.Context is not null)
            {
                await JS.InvokeVoidAsync("tmPdfViewer.syncOverlay", _rootRef, _customOverlayRef);
            }
        }
        catch { }
    }

    private DocumentCommentUser ResolveAuthor()
        => CurrentUser ?? new DocumentCommentUser
        {
            UserId = "anonymous",
            DisplayName = Loc["TmPdfViewer_AnonymousUser"]
        };

    private async Task CreateThreadFromSelectionAsync(string body)
    {
        if (_pendingSelection is null || !_pendingSelection.IsValid || string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        var request = new DocumentCommentThreadCreateRequest
        {
            Anchor = _pendingSelection.ToAnchor(),
            Body = body
        };

        try
        {
            var created = await EffectiveAnnotationProvider.CreateThreadAsync(AnnotationDocumentId, request, ResolveAuthor());
            _pendingSelection = null;
            _selectedThreadId = created.Id;
            await LoadAnnotationsAsync();
            await RefreshAnnotationOverlayAsync();
        }
        catch { }
    }

    private async Task ReplyToThreadAsync(DocumentCommentReplyRequest request)
    {
        try
        {
            await EffectiveAnnotationProvider.ReplyAsync(AnnotationDocumentId, request, ResolveAuthor());
            await LoadAnnotationsAsync();
        }
        catch { }
    }

    private async Task ResolveThreadAsync(string threadId)
    {
        try
        {
            await EffectiveAnnotationProvider.ResolveAsync(AnnotationDocumentId, threadId, ResolveAuthor());
            await LoadAnnotationsAsync();
            await RefreshAnnotationOverlayAsync();
        }
        catch { }
    }

    private async Task ReopenThreadAsync(string threadId)
    {
        try
        {
            await EffectiveAnnotationProvider.ReopenAsync(AnnotationDocumentId, threadId, ResolveAuthor());
            await LoadAnnotationsAsync();
            await RefreshAnnotationOverlayAsync();
        }
        catch { }
    }

    private async Task DeleteCommentAsync(DocumentCommentDeleteRequest request)
    {
        try
        {
            // Decide selection clearing from the pre-delete state: an in-memory provider may
            // alias the same thread instances, so inspect the count before the mutation.
            var target = _threads.FirstOrDefault(thread => string.Equals(thread.Id, request.ThreadId, StringComparison.Ordinal));
            var threadWillBeRemoved = target is null || target.Comments.Count <= 1;

            await EffectiveAnnotationProvider.DeleteAsync(AnnotationDocumentId, request);

            if (threadWillBeRemoved && string.Equals(_selectedThreadId, request.ThreadId, StringComparison.Ordinal))
            {
                _selectedThreadId = null;
            }

            await LoadAnnotationsAsync();
            await RefreshAnnotationOverlayAsync();
        }
        catch { }
    }

    private async Task SelectThreadAsync(string? threadId)
    {
        _selectedThreadId = threadId;
        // Selecting a thread anchored on another page navigates to that page.
        var page = _threads.FirstOrDefault(t => string.Equals(t.Id, threadId, StringComparison.Ordinal))?.Anchor.PageNumber ?? 0;
        if (page >= 1 && page != _currentPage)
        {
            await GoToPageAsync(page);
        }
        await InvokeAsync(StateHasChanged);
    }

    private async Task SetShowResolvedAnnotationsAsync(bool value)
    {
        _showResolvedAnnotations = value;
        await InvokeAsync(StateHasChanged);
        await RefreshAnnotationOverlayAsync();
    }

    private async Task DismissSelectionAsync()
    {
        _pendingSelection = null;
        await InvokeAsync(StateHasChanged);
    }

    private async Task ToggleAnnotationsPanelAsync()
    {
        _annotationsPanelVisible = !_annotationsPanelVisible;
        await InvokeAsync(StateHasChanged);
        await RefreshAnnotationOverlayAsync();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Navigates to a specific page. In continuous mode the page wrap is
    /// scrolled into view instead of re-rendering a single canvas.</summary>
    public async Task GoToPageAsync(int page)
    {
        if (page < 1 || page > _totalPages) return;
        _currentPage = page;
        await PageChanged.InvokeAsync(_currentPage);
        if (_viewMode == PdfViewMode.Continuous && !_useFallback)
        {
            try { await JS.InvokeVoidAsync("tmPdfViewer.scrollToPage", _rootRef, page); }
            catch { }
        }
        await RenderCurrentPageAsync();
    }

    /// <summary>Sets the zoom scale directly.</summary>
    public async Task SetScaleAsync(double scale)
    {
        if (scale is <= 0 or > 5) return;
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
