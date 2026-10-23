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
/// The annotations block of <see cref="TmPdfViewer"/> — the annotation layer/panel state,
/// the text-selection setup, the pinch wiring, the thread CRUD and the comments/thumbnails
/// sheet toggles. Split from TmPdfViewer.razor.cs to keep both under the 1200-line ceiling.
/// </summary>
public partial class TmPdfViewer
{
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

        // The text-layer element must exist in the rendered DOM — during a deferred
        // (or torn-down) layout the ref is stale and the handler would bind to nothing.
        // Continuous mode binds the scroll wrap instead: the per-page text layers are
        // its descendants, and the handler resolves the anchor's page from the wrap
        // that contains it (FZE-13).
        var isContinuous = _viewMode == PdfViewMode.Continuous;
        if (isContinuous)
        {
            if (_continuousRef.Context is null)
            {
                return;
            }
        }
        else if (_textLayerRef.Context is null)
        {
            return;
        }

        try
        {
            if (!isContinuous)
            {
                await JS.InvokeVoidAsync("tmPdfViewer.renderTextLayer", _rootRef, _textLayerRef, _currentPage, _scale, Rotation);
            }
            await JS.InvokeVoidAsync("tmPdfViewer.enableSelection", _rootRef,
                isContinuous ? _continuousRef : _textLayerRef, _dotNetRef);
            _selectionEnabled = true;
        }
        catch { }
    }

    /// <summary>
    /// The FZE-9 pinch element seen by the last successful <see cref="SetupPinchAsync"/> call,
    /// so the one-shot setup does not re-invoke the interop on every render pass. The wrap
    /// element is re-created by a view-mode swap, which re-wires the pinch onto it.
    /// </summary>
    private string? _pinchWiredRefId;

    /// <summary>
    /// Wires the core two-finger pinch/pan zoom (pointer-gesture.js — never a local copy)
    /// onto the active scroll wrap. Idempotent per wrap element: a fresh element (the
    /// view-mode swap, a sheet re-render) re-wires, everything else is a no-op. A no-op
    /// when the JS viewer is inactive — the zoom dropdown and the ± buttons stay the
    /// WCAG 2.5.7 alternative for pointers that cannot pinch.
    /// </summary>
    private async Task SetupPinchAsync()
    {
        if (_useFallback || !_pdfInitialized)
        {
            return;
        }

        var wrap = _viewMode == PdfViewMode.Continuous ? _continuousRef : _canvasWrapRef;
        if (wrap.Context is null || !string.IsNullOrEmpty(_pinchWiredRefId) && _pinchWiredRefId == wrap.Id)
        {
            return;
        }

        try
        {
            await JS.InvokeVoidAsync("tmPdfViewer.enablePinchZoom", _rootRef, wrap, _dotNetRef);
            _pinchWiredRefId = wrap.Id;
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

            // FZE-13: the templated per-page overlay is kept over the canvas the same
            // way (and its sync fires the single-page geometry report).
            if (PageOverlayContent is not null && _pageOverlayRef.Context is not null)
            {
                await JS.InvokeVoidAsync("tmPdfViewer.syncOverlay", _rootRef, _pageOverlayRef);
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

    private async Task EditCommentAsync(DocumentCommentEditRequest request)
    {
        try
        {
            await EffectiveAnnotationProvider.EditAsync(AnnotationDocumentId, request);
            await LoadAnnotationsAsync();
            await RefreshAnnotationOverlayAsync();
        }
        catch { }
    }

    private async Task CloseAnnotationsPanelAsync()
    {
        // A user-driven change: the layout defaults must never overwrite it again.
        _panelsTouched = true;
        if (_resolved == TmLayoutMode.Desktop)
        {
            _annotationsPanelVisible = false;
        }
        else
        {
            _commentsSheetOpen = false;
        }
        await InvokeAsync(StateHasChanged);
        await RefreshAnnotationOverlayAsync();
    }

    private async Task SelectThreadAsync(string? threadId)
    {
        _selectedThreadId = threadId;
        // Selecting a thread anchored on another page navigates to that page.
        var thread = _threads.FirstOrDefault(t => string.Equals(t.Id, threadId, StringComparison.Ordinal));
        var page = thread?.Anchor.PageNumber ?? 0;
        if (page >= 1 && (page != _currentPage || _viewMode == PdfViewMode.Continuous))
        {
            await ScrollToAnchorAsync(page, thread!.Anchor.Y);
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
        // M3: the dismiss also clears the DOM selection (and the report dedupe key) —
        // a stale highlight must not survive the dismissed prompt.
        try { await JS.InvokeVoidAsync("tmPdfViewer.clearSelection", _rootRef); }
        catch { }
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// The sticky selection CTA's action (FZE-9): opens the comments sheet — the tablet side
    /// sheet or the mobile bottom sheet — where the pending selection's prompt and composer
    /// wait. The docked desktop panel shows the prompt without a CTA.
    /// </summary>
    private Task OpenSelectionComposerAsync() => SetCommentsSheetAsync(true);

    private async Task ToggleAnnotationsPanelAsync()
    {
        // A user-driven change: the layout defaults must never overwrite it again.
        _panelsTouched = true;
        if (_resolved == TmLayoutMode.Desktop)
        {
            _annotationsPanelVisible = !_annotationsPanelVisible;
        }
        else
        {
            _commentsSheetOpen = !_commentsSheetOpen;
        }
        await InvokeAsync(StateHasChanged);
        await RefreshAnnotationOverlayAsync();
    }

    /// <summary>Opens or closes the comments sheet (tablet side sheet / mobile bottom sheet)
    /// from the mobile action bar or the toolbar toggle. The docked desktop panel keeps
    /// <see cref="_annotationsPanelVisible"/>.</summary>
    private async Task SetCommentsSheetAsync(bool open)
    {
        if (_commentsSheetOpen == open)
        {
            return;
        }

        // A user-driven change: the layout defaults must never overwrite it again.
        _panelsTouched = true;
        _commentsSheetOpen = open;
        await InvokeAsync(StateHasChanged);
        await RefreshAnnotationOverlayAsync();
    }

    /// <summary>Opens or closes the mobile thumbnails sheet; opening primes the post-render
    /// pass to rebuild the JS-rendered thumbnails in the fresh sheet container.</summary>
    private async Task SetThumbnailsSheetAsync(bool open)
    {
        if (_thumbnailsSheetOpen == open)
        {
            return;
        }

        _thumbnailsSheetOpen = open;
        if (open)
        {
            _lastThumbnailUrl = null;
        }
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// The wrapping <see cref="Layout.TmLayoutObserver"/> reports a measured layout change.
    /// <see cref="ApplyResolvedLayoutAsync"/> owns the flip: an <see cref="PdfZoomMode.Auto"/>
    /// zoom follows the layout (fit starts on mobile, the JS observer detaches when it is
    /// left) and the panel defaults re-apply until the user has touched the panels themselves
    /// (<see cref="_panelsTouched"/>) — so an <see cref="TmLayoutMode.Auto"/> viewer behaves
    /// like a forced one. Only the MEASURED value is recorded in <see cref="_measured"/>; the
    /// resolved mode lives solely in <see cref="_resolved"/>.
    /// </summary>
    private Task OnResolvedLayoutChanged(TmLayoutMode mode)
    {
        _measured = mode;
        var resolved = TmLayout.Resolve(LayoutMode, Parent, _measured, TmLayoutMode.Desktop);
        return ApplyResolvedLayoutAsync(resolved);
    }
}
