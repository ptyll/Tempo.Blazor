using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.Files;

/// <summary>
/// JS interop callbacks of <see cref="TmPdfViewer"/> — every <c>[JSInvokable]</c> entry point
/// the pdf-viewer.js module calls back into (load results, fit/pinch zoom, thumbnails,
/// search, scroll tracking and text selection). Split from TmPdfViewer.razor.cs to keep
/// both under the 1200-line source ceiling.
/// </summary>
public partial class TmPdfViewer
{
    // ── JS invokable ─────────────────────────────────────────────────────────

    /// <summary>Receives the total page count after the JavaScript PDF loader finishes.</summary>
    [JSInvokable]
    public void OnPdfLoaded(int totalPages)
    {
        _totalPages = totalPages;
        _isLoading = false;
        _loadError = null;
        _pageInput = _currentPage.ToString(CultureInfo.InvariantCulture);
        if (OnDocumentLoaded.HasDelegate)
        {
            _ = InvokeAsync(() => OnDocumentLoaded.InvokeAsync(totalPages));
        }

        // A fit mode requested before the document existed can only be computed now.
        if (EffectiveZoomMode is not PdfZoomMode.Custom && !_useFallback)
        {
            _ = ApplyZoomModeAsync();
        }

        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Raised when the core two-finger pinch gesture starts on the viewer's scroll wrap
    /// (pdf-viewer.js <c>onStart</c>). Hosts — the annotator — use it to discard an
    /// in-progress ink stroke: the pinch owns the surface from here on, so the stroke is
    /// cancelled, never committed. An internal extension point on purpose: it is not a
    /// <see cref="Parameter"/> (the annotator subscribes through its viewer reference).
    /// </summary>
    internal event Action? PinchStarted;

    /// <summary>Receives the pinch-start notification from JavaScript.</summary>
    [JSInvokable]
    public void OnPinchStarted()
    {
        PinchStarted?.Invoke();
    }

    /// <summary>Receives a fit-mode scale computed by JavaScript — either the initial fit request or
    /// a recompute after the container was resized. Ignored when the effective mode has since
    /// returned to <see cref="PdfZoomMode.Custom"/> (a zoom button or preset won the race) or when the
    /// value is outside the documented clamp range <see cref="MinScale"/>..<see cref="MaxScale"/>
    /// (the JS side clamps to the same range; this is the defensive second guard).</summary>
    [JSInvokable]
    public async Task OnFitScaleComputed(double scale)
    {
        if (EffectiveZoomMode is PdfZoomMode.Custom || scale is < MinScale or > MaxScale)
        {
            return;
        }

        if (Math.Abs(scale - _scale) <= 0.001)
        {
            return;
        }

        _scale = scale;
        await ScaleChanged.InvokeAsync(_scale);
        await ApplyZoomAsync();
        // The toolbar zoom trigger (percentage / fit label) renders from _scale — a
        // resize-driven recompute arrives from JS and must refresh the UI.
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Receives the absolute scale a two-finger pinch (or ctrl+wheel) gesture committed by the
    /// core pointer-gesture module. The gesture is a user zoom: the mode hands over to
    /// <see cref="PdfZoomMode.Custom"/> (detaching any active fit observer) and the viewer
    /// re-renders at the clamped scale. The JS side already clamps to
    /// <see cref="MinScale"/>..<see cref="MaxScale"/> — this is the defensive second guard.
    /// A NaN/∞ scale, an unchanged scale, or an inactive viewer commits nothing.
    /// </summary>
    /// <param name="scale">The absolute (document) scale after the gesture.</param>
    [JSInvokable]
    public async Task OnPinchZoomEnded(double scale)
    {
        if (!_pdfInitialized || _useFallback || !double.IsFinite(scale))
        {
            return;
        }

        // A pinch is a user zoom: the fit observer is abandoned exactly like a zoom button
        // press would (the non-drag WCAG 2.5.7 alternative shares the same commit path).
        if (EffectiveZoomMode is not PdfZoomMode.Custom)
        {
            _zoomMode = PdfZoomMode.Custom;
            await ZoomModeChanged.InvokeAsync(_zoomMode);
            await ClearFitAsync();
        }

        var next = Math.Clamp(scale, MinScale, MaxScale);
        if (Math.Abs(next - _scale) <= 0.001)
        {
            await InvokeAsync(StateHasChanged);
            return;
        }

        _scale = next;
        await ScaleChanged.InvokeAsync(_scale);
        await ApplyZoomAsync();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Receives the document's byte length from JavaScript (PDF.js <c>getDownloadInfo</c>)
    /// once the document has loaded (FZE-11). JavaScript reports <em>nothing</em> when the
    /// length is unknown — a rejected or field-less <c>getDownloadInfo</c> leaves
    /// <see cref="_reportedLength"/> null, so the header keeps its pages-only subtitle.
    /// A host-supplied <see cref="FileSize"/> wins over this value (see
    /// <see cref="EffectiveFileSize"/>).
    /// </summary>
    [JSInvokable]
    public void OnDocumentLengthKnown(long length)
    {
        if (length < 0)
        {
            return;
        }

        _reportedLength = length;
        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// The effective header file size: the host-supplied <see cref="FileSize"/> when set,
    /// otherwise the JS-reported length; null when neither is known.
    /// </summary>
    internal long? EffectiveFileSize => FileSize ?? _reportedLength;

    /// <summary>Receives a PDF load failure notification from JavaScript and switches to fallback rendering.</summary>
    [JSInvokable]
    public void OnPdfLoadError(string message)
    {
        _useFallback = true;
        _isLoading = false;
        _loadError = Loc["TmPdfViewer_LoadError"];
        InvokeAsync(StateHasChanged);
    }

    /// <summary>Handles thumbnail page selection from the JavaScript thumbnail renderer.
    /// On a mobile layout the thumbnails live in a bottom sheet, so the selection closes it.</summary>
    [JSInvokable]
    public async Task OnThumbnailClicked(int pageNum)
    {
        if (pageNum < 1 || pageNum > _totalPages) return;
        await NavigateToPageAsync(pageNum);
        if (_thumbnailsSheetOpen)
        {
            await SetThumbnailsSheetAsync(false);
        }
        try { await JS.InvokeVoidAsync("tmPdfViewer.highlightThumbnail", _rootRef, _currentPage); }
        catch { }
    }

    /// <summary>Receives search result counts from the JavaScript search implementation.</summary>
    [JSInvokable]
    public void OnSearchResults(int totalMatches, int[] matchesPerPage)
    {
        _searchTotalMatches = totalMatches;
        _searchCurrentMatch = totalMatches > 0 ? 1 : 0;
        // A completed search with zero matches announces the localized no-matches verdict
        // (the live region) and flags the input aria-invalid until a new search/clear.
        _searchRanWithNoMatches = totalMatches == 0;
        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Receives the page the user scrolled to in continuous mode (reported by the JS
    /// scroll tracker): updates the current page, the toolbar input and the two-way
    /// <see cref="Page"/> binding, and refreshes the active thumbnail. Rendering is
    /// not needed — the lazy continuous renderer paints what becomes visible.
    /// </summary>
    [JSInvokable]
    public void OnVisiblePageChanged(int pageNumber)
    {
        if (pageNumber < 1 || pageNumber > _totalPages || pageNumber == _currentPage)
        {
            return;
        }

        _currentPage = pageNumber;
        _pageInput = pageNumber.ToString(CultureInfo.InvariantCulture);
        if (PageChanged.HasDelegate)
        {
            _ = InvokeAsync(() => PageChanged.InvokeAsync(_currentPage));
        }

        InvokeAsync(StateHasChanged);
    }

    /// <summary>Receives the active search match position (1-based) and its page from JavaScript.</summary>
    [JSInvokable]
    public async Task OnSearchActiveChanged(int activeMatch, int pageNumber)
    {
        _searchCurrentMatch = activeMatch;
        if (pageNumber >= 1 && pageNumber <= _totalPages && pageNumber != _currentPage)
        {
            await SetPageAsync(pageNumber);
            if (_viewMode == PdfViewMode.Continuous)
            {
                // The JS search already scrolled the wrap to the match's page; the
                // lazy renderer paints it when it becomes visible. Rendering into the
                // single-page canvas would target a node that does not exist here.
                try { await JS.InvokeVoidAsync("tmPdfViewer.highlightThumbnail", _rootRef, _currentPage); }
                catch { }
            }
            else
            {
                // A match on another page must actually show that page: the full render
                // (canvas + text layer + overlay + search redraw) and, in a fit mode, the
                // recompute for the new page size.
                await RenderCurrentPageAsync();
            }
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Receives the per-page overlay geometry from JavaScript (FZE-13). In continuous mode
    /// the payload carries ONLY the pages inside the ±50% mount band — exactly the pages
    /// that get a Blazor overlay slot, so the lazy-render contract and the painted-page
    /// limit keep holding; in single-page mode it carries the current canvas box. The
    /// arrays are position-parallel; entries for pages outside the loaded document are
    /// ignored (annotations on a nonexistent page never render).
    /// </summary>
    /// <param name="pages">One-based page numbers of the mounted pages, ascending.</param>
    /// <param name="tops">Page box tops in scroll-content coordinates.</param>
    /// <param name="lefts">Page box lefts in scroll-content coordinates.</param>
    /// <param name="widths">Page box widths in CSS pixels.</param>
    /// <param name="heights">Page box heights in CSS pixels.</param>
    /// <param name="rotation">Document rotation the geometry was measured at.</param>
    [JSInvokable]
    public void OnContinuousPageGeometry(
        int[]? pages, double[]? tops, double[]? lefts, double[]? widths, double[]? heights, int rotation)
    {
        var count = Math.Min(pages?.Length ?? 0,
            Math.Min(tops?.Length ?? 0, Math.Min(lefts?.Length ?? 0,
                Math.Min(widths?.Length ?? 0, heights?.Length ?? 0))));
        var boxes = new List<PdfPageOverlayBox>(count);
        for (var i = 0; i < count; i++)
        {
            var page = pages![i];
            if (page < 1 || (_totalPages > 0 && page > _totalPages))
            {
                continue; // a page outside the document is not a mountable slot
            }
            boxes.Add(new PdfPageOverlayBox(page, tops![i], lefts![i], widths![i], heights![i]));
        }

        _overlayPages = boxes;
        _overlayRotation = rotation;
        if (_viewMode == PdfViewMode.Continuous)
        {
            // M2: the slots live in their own child — it re-renders ITSELF, so a
            // geometry report never costs a root-viewer diff.
            _continuousOverlaysRef?.SetGeometry(boxes, rotation);
        }
        else
        {
            // Single-page: the report is the canvas box behind PageOverlayContent —
            // the root re-render refreshes the templated context.
            InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>Receives a text selection captured from the JavaScript text layer.</summary>
    /// <param name="text">Selected text.</param>
    /// <param name="page">One-based page the selection belongs to.</param>
    /// <param name="rects">Flat list of normalized rectangles: x, y, width, height per rectangle.</param>
    [JSInvokable]
    public async Task OnTextSelectionChanged(string? text, int page, double[]? rects)
    {        var normalized = BuildRects(rects);
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
}
