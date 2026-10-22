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
/// Navigation, zoom, rotation, view-mode, thumbnails and search behaviors of
/// <see cref="TmPdfViewer"/>. Split from TmPdfViewer.razor.cs to keep both under the
/// 1200-line source ceiling.
/// </summary>
public partial class TmPdfViewer
{
    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Single mutation point for the current page: updates the state, the toolbar page
    /// input and the two-way <see cref="Page"/> binding together, so every navigation path
    /// (buttons, thumbnails, search matches, thread anchors, the page input) stays consistent.
    /// </summary>
    private async Task SetPageAsync(int page)
    {
        _currentPage = page;
        _pageInput = page.ToString(CultureInfo.InvariantCulture);
        await PageChanged.InvokeAsync(_currentPage);
    }

    /// <summary>
    /// User-driven navigation shared by the prev/next buttons, the page input,
    /// thumbnails, search matches and thread anchors. In continuous mode it scrolls
    /// the wrap to the page — the lazy renderer paints it when it becomes visible —
    /// instead of rendering into an invisible single-page canvas.
    /// </summary>
    private async Task NavigateToPageAsync(int page)
    {
        if (page < 1 || page > _totalPages) return;
        await SetPageAsync(page);
        if (_viewMode == PdfViewMode.Continuous)
        {
            await ScrollToPageInContinuousAsync(page);
            return;
        }
        await RenderCurrentPageAsync();
    }

    /// <summary>Scrolls the continuous wrap to a page. The lazy renderer paints the
    /// page when it scrolls into view; nothing is rendered into an invisible canvas.</summary>
    private async Task ScrollToPageInContinuousAsync(int page)
    {
        try { await JS.InvokeVoidAsync("tmPdfViewer.scrollToPage", _rootRef, page); }
        catch { }
    }

    private async Task GoToPreviousPageAsync()
    {
        if (_currentPage <= 1) return;
        await NavigateToPageAsync(_currentPage - 1);
    }

    private async Task GoToNextPageAsync()
    {
        if (_currentPage >= _totalPages) return;
        await NavigateToPageAsync(_currentPage + 1);
    }

    /// <summary>Commits the toolbar page input: a valid in-range value navigates, anything
    /// else (unparsable, out of 1..N) reverts the input to the current page.</summary>
    private async Task CommitPageInputAsync()
    {
        var parsed = int.TryParse(_pageInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page);
        if (!parsed || page < 1 || page > _totalPages || page == _currentPage)
        {
            // Revert an invalid entry; nothing to do when the value already matches.
            if (_pageInput != _currentPage.ToString(CultureInfo.InvariantCulture))
            {
                _pageInput = _currentPage.ToString(CultureInfo.InvariantCulture);
                await InvokeAsync(StateHasChanged);
            }
            return;
        }

        await NavigateToPageAsync(page);
    }

    private async Task HandlePageInputKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await CommitPageInputAsync();
        }
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

        // A fit mode must recompute for the new page: a different page size changes the
        // container ratio. Runs after renderPage so the JS fit math already sees the new
        // page (it reads the JS-side currentPage) — this covers prev/next, the page
        // input, thumbnail clicks and search-match navigation. On a view-mode switch the
        // container ref is still stale here; that pass is harmless (a detached element
        // has no usable box) and the _fitPending pass in OnAfterRenderAsync does the
        // real recompute once the fresh container exists.
        if (EffectiveZoomMode is not PdfZoomMode.Custom && _pdfInitialized && !_useFallback)
        {
            await ApplyZoomModeAsync();
        }
    }

    // ── Zoom ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The zoom mode the chrome acts on: an explicit fit mode passes through;
    /// <see cref="PdfZoomMode.Auto"/> is fit-width on a mobile layout and the bound scale
    /// (Custom behavior) everywhere else.
    /// </summary>
    private PdfZoomMode EffectiveZoomMode => _zoomMode switch
    {
        PdfZoomMode.Auto => _resolved == TmLayoutMode.Mobile ? PdfZoomMode.FitWidth : PdfZoomMode.Custom,
        var mode => mode,
    };

    private async Task ZoomInAsync()
    {
        await SwitchToCustomZoomAsync();
        var next = _zoomSteps.FirstOrDefault(z => z > _scale);
        if (next == 0) return;
        _scale = next;
        await ScaleChanged.InvokeAsync(_scale);
        await ApplyZoomAsync();
    }

    private async Task ZoomOutAsync()
    {
        await SwitchToCustomZoomAsync();
        var prev = _zoomSteps.LastOrDefault(z => z < _scale);
        if (prev == 0) return;
        _scale = prev;
        await ScaleChanged.InvokeAsync(_scale);
        await ApplyZoomAsync();
    }

    /// <summary>
    /// The ± buttons and the zoom presets hand control back to the user: a pending fit mode
    /// is abandoned (its JS ResizeObserver detached via <see cref="ClearFitAsync"/>) and the
    /// mode reports <see cref="PdfZoomMode.Custom"/> through <see cref="ZoomModeChanged"/>.
    /// On a desktop/tablet layout <see cref="PdfZoomMode.Auto"/> already behaves like Custom,
    /// so nothing is demoted there — Auto only demotes once a fit is actually active.
    /// </summary>
    private async Task SwitchToCustomZoomAsync()
    {
        if (EffectiveZoomMode is PdfZoomMode.Custom)
        {
            return;
        }

        _zoomMode = PdfZoomMode.Custom;
        await ZoomModeChanged.InvokeAsync(_zoomMode);
        await ClearFitAsync();
    }

    /// <summary>Detaches the JS fit observer and abandons the active fit mode, so a
    /// container resize stops recomputing the scale. Shared by the user-driven
    /// <see cref="SwitchToCustomZoomAsync"/> and the host-driven ZoomMode=Custom
    /// parameter change.</summary>
    private async Task ClearFitAsync()
    {
        try { await JS.InvokeVoidAsync("tmPdfViewer.clearFit", _rootRef); }
        catch { }
    }

    /// <summary>Applies a zoom preset (0.5, 0.75, 1.0, 1.25, 1.5, 2.0) or a fit mode chosen
    /// in the zoom dropdown. Preset values outside the known set are ignored.</summary>
    private async Task HandleZoomSelectAsync(string value)
    {
        switch (value)
        {
            case "fit-width":
                await SetZoomModeAsync(PdfZoomMode.FitWidth);
                break;
            case "fit-page":
                await SetZoomModeAsync(PdfZoomMode.FitPage);
                break;
            default:
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)
                    && Array.Exists(_zoomSteps, step => Math.Abs(step - scale) < 0.0001))
                {
                    await SwitchToCustomZoomAsync();
                    await SetScaleAsync(scale);
                }
                break;
        }
    }

    /// <summary>Text shown in the zoom dropdown trigger: the fit-mode label while a fit mode
    /// is active, otherwise the current percentage. On a mobile layout the trigger is the bare
    /// percentage — the compact single row has no room for the fit label (the "%" glyph is the
    /// narrow-width contract; the tooltip/aria-label still name the control).</summary>
    private string ZoomTriggerText
    {
        get
        {
            if (IsMobileLayout)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(_scale * 100)}%");
            }

            return EffectiveZoomMode switch
            {
                PdfZoomMode.FitWidth => Loc["TmPdfViewer_FitWidth"],
                PdfZoomMode.FitPage => Loc["TmPdfViewer_FitPage"],
                _ => string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(_scale * 100)}%")
            };
        }
    }

    /// <summary>Number of annotation threads that still need attention (shown on the toolbar toggle).</summary>
    private int OpenThreadCount => _threads.Count(thread => thread.Status == DocumentCommentThreadStatus.Open);

    private async Task SetZoomModeAsync(PdfZoomMode mode)
    {
        if (_zoomMode == mode) return;
        _zoomMode = mode;
        await ZoomModeChanged.InvokeAsync(_zoomMode);
        if (_pdfInitialized && !_useFallback && _totalPages > 0)
        {
            await ApplyZoomModeAsync();
        }
    }

    /// <summary>
    /// Asks JavaScript to compute the scale for the active fit mode. The JS side clamps the
    /// result to <see cref="MinScale"/>..<see cref="MaxScale"/> — a fit scale is ALLOWED to
    /// fall outside <see cref="_zoomSteps"/> (the exact container ratio beats the steps), it
    /// just never leaves the absolute bounds — and keeps a ResizeObserver on the scroll
    /// container so the scale follows resizes through <see cref="OnFitScaleComputed"/>.
    /// </summary>
    private async Task ApplyZoomModeAsync()
    {
        if (EffectiveZoomMode is PdfZoomMode.Custom)
        {
            return;
        }

        var mode = EffectiveZoomMode == PdfZoomMode.FitWidth ? "fit-width" : "fit-page";
        try
        {
            var container = _viewMode == PdfViewMode.Continuous ? _continuousRef : _canvasWrapRef;
            var scale = await JS.InvokeAsync<double>("tmPdfViewer.fitScale", _rootRef, container, mode, _dotNetRef);
            if (scale is >= MinScale and <= MaxScale && Math.Abs(scale - _scale) > 0.001)
            {
                _scale = scale;
                await ScaleChanged.InvokeAsync(_scale);
                await ApplyZoomAsync();
            }
        }
        catch { }
    }

    private async Task ApplyZoomAsync()
    {
        try
        {
            if (_viewMode == PdfViewMode.Continuous && _continuousRef.Context is not null)
            {
                if (_lastContinuousRender is { } last && last.Url == Url && last.Rotation == Rotation
                    && _totalPages > 0)
                {
                    // Scale-only change: the per-page DOM, its layers and the
                    // observer/scroll bindings are still valid — update the
                    // placeholders in place instead of rebuilding the wrap.
                    await JS.InvokeVoidAsync("tmPdfViewer.updateContinuousScale", _rootRef, _continuousRef, _scale);
                    MarkContinuousRendered();
                }
                else
                {
                    // First continuous render for this Url/rotation: full rebuild.
                    await RenderContinuousPagesAsync();
                }
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
        await ApplyRotationAsync();
    }

    /// <summary>
    /// Rotate the document 90 degrees counter-clockwise (FZE-11) — the inverse of
    /// <see cref="RotateAsync"/>: 0 → 270 → 180 → 90 → 0. Continuous mode rebuilds the
    /// placeholders and preserves the scroll position through the same path as the
    /// clockwise rotation; the text layer follows the rotation attribute PDF.js writes
    /// (<c>data-main-rotation</c>).
    /// </summary>
    public async Task RotateCounterClockwiseAsync()
    {
        Rotation = (Rotation + 270) % 360;
        await ApplyRotationAsync();
    }

    /// <summary>
    /// The shared rotation apply path: in Continuous mode the pages re-render (the JS
    /// side recomputes the placeholders and preserves the scroll position) and an active
    /// fit mode recomputes for the flipped aspect ratio; single-page re-renders the
    /// current page (which refits inside <see cref="RenderCurrentPageAsync"/>).
    /// </summary>
    private async Task ApplyRotationAsync()
    {
        if (_viewMode == PdfViewMode.Continuous && _continuousRef.Context is not null)
        {
            await RenderContinuousPagesAsync();

            // The rotation changes the page aspect ratio, so an active fit mode must
            // recompute (renderAllPages above synced the JS-side rotation the fit math
            // reads). The single-page path refits inside RenderCurrentPageAsync.
            if (EffectiveZoomMode is not PdfZoomMode.Custom && _pdfInitialized && !_useFallback)
            {
                await ApplyZoomModeAsync();
            }
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
        // The fit container (canvas wrap vs continuous wrap) is swapped by the
        // re-render that follows this call — its ElementReference is stale right now,
        // so the recompute is deferred to OnAfterRenderAsync via _fitPending.
        if (EffectiveZoomMode is not PdfZoomMode.Custom && _pdfInitialized && !_useFallback)
        {
            _fitPending = true;
        }

        if (_viewMode == PdfViewMode.Continuous)
        {
            _singlePagePending = false;
            await RenderContinuousPagesAsync();
        }
        else
        {
            // PD1: the annotation overlay and the text-selection setup live in the
            // canvas-wrap layout only. The canvas/text-layer refs still point at the
            // torn-down continuous DOM until the swap has been rendered — defer the
            // render + selection/overlay setup to OnAfterRenderAsync
            // (_singlePagePending), exactly like the deferred fit recompute.
            _singlePagePending = true;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Rebuilds the continuous pages at the current scale/rotation. The JS side keeps
    /// placeholders for every page and paints lazily; the flags decide which per-page
    /// layers the wraps carry. PD1: annotations never render in continuous mode, so
    /// the text layer follows only the viewer's own <see cref="ShowTextLayer"/> and the
    /// search layer follows <see cref="ShowSearch"/> — annotation-driven selection
    /// (EnableAnnotations / EnableTextSelectionEvents) stays single-page.
    /// </summary>
    private async Task RenderContinuousPagesAsync()
    {
        var textLayer = ShowTextLayer;
        var searchLayer = ShowSearch;
        try
        {
            // The label template rides along even without text/search layers — the
            // per-page aria-labels are always rendered (A11y). currentPage lets the
            // JS side restore the scroll to this page when no anchor exists (a first
            // build into an empty container — e.g. a mode switch while page N was
            // open must not jump back to page 1).
            await JS.InvokeVoidAsync("tmPdfViewer.renderAllPages", _rootRef, _continuousRef, _scale, Rotation,
                new { textLayer, searchLayer, pageLabelTemplate = Loc["TmPdfViewer_PageOfTotal"], currentPage = _currentPage });
            MarkContinuousRendered();
        }
        catch { }
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

    // ── Thumbnails ────────────────────────────────────────────────────────────

    /// <summary>Collapses the thumbnails sidebar through its header button and reports
    /// the state through <see cref="ThumbnailsCollapsedChanged"/>.</summary>
    private async Task CollapseThumbnailsAsync()
    {
        await SetThumbnailsCollapsedAsync(true);
    }

    /// <summary>Toggles the thumbnails sidebar from the toolbar "Pages" switch.</summary>
    private async Task ToggleThumbnailsCollapsedAsync()
    {
        await SetThumbnailsCollapsedAsync(!_thumbnailsCollapsed);
    }

    private async Task SetThumbnailsCollapsedAsync(bool collapsed)
    {
        if (_thumbnailsCollapsed == collapsed)
        {
            return;
        }

        // A user-driven change: the layout defaults must never overwrite it again.
        _panelsTouched = true;
        _thumbnailsCollapsed = collapsed;
        await ThumbnailsCollapsedChanged.InvokeAsync(collapsed);
        if (!collapsed)
        {
            // The JS-rendered thumbnails died with the hidden container; force the
            // post-render pass to rebuild them in the fresh one.
            _lastThumbnailUrl = null;
        }
        await InvokeAsync(StateHasChanged);
    }

    private async Task ToggleSearchAsync()
    {
        _searchVisible = !_searchVisible;
        // The floating card is focus-managed: once it renders, its input takes the caret
        // (OnAfterRenderAsync consumes the flag). The Inline bar and the mobile sheet keep
        // their own flow — no focus move there.
        _pendingSearchInputFocus = _searchVisible && !_useFallback
            && SearchPresentation == PdfSearchPresentation.Floating
            && !IsMobileLayout;
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Closes the floating search card and returns focus to the toolbar's search toggle
    /// (FZE-11) — both the Escape path and the card's ✕ button share it, so a keyboard
    /// user never drops to <c>document.body</c> when the card disappears. The restore is
    /// best-effort: without the JS module (the fallback embed) the card still closes.
    /// </summary>
    private async Task CloseSearchCardAsync()
    {
        _searchVisible = false;
        await InvokeAsync(StateHasChanged);
        try { await JS.InvokeVoidAsync("tmPdfViewer.focusSearchToggle", _rootRef); }
        catch { }
    }

    /// <summary>Escape inside the floating search card closes it and restores the focus.</summary>
    private Task HandleSearchCardKeyDownAsync(KeyboardEventArgs e)
        => e.Key == "Escape" ? CloseSearchCardAsync() : Task.CompletedTask;

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
        _searchRanWithNoMatches = false;
        try { await JS.InvokeVoidAsync("tmPdfViewer.clearSearch", _rootRef, _searchLayerRef); }
        catch { }
        await InvokeAsync(StateHasChanged);
    }
}
