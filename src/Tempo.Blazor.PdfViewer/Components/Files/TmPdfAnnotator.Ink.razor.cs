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

/// <summary>
/// Surface interaction of <see cref="TmPdfAnnotator"/> — the ink stroke fields and the
/// pointer handlers that capture strokes, stamps and comment drops on the canvas.
/// Split from TmPdfAnnotator.razor.cs to keep both under the 1200-line source ceiling.
/// </summary>
public partial class TmPdfAnnotator
{
    private List<DocumentInkPoint>? _activeStroke;

    /// <summary>The pointer that owns the active stroke (FZE-9): a second contact never
    /// extends or commits it, and the pinch's captured fingers cannot keep drawing.</summary>
    private long? _strokePointerId;

    /// <summary>The pointer type of the active stroke — the pen's priority drops a live
    /// finger stroke when the stylus lands.</summary>
    private string? _strokePointerType;

    /// <summary>The page the active stroke belongs to (FZE-14): the stroke is confined to
    /// the page where it started — points normalized against another page's slot never
    /// join it, and the committed anchor lands on the start page. 0 while no stroke lives.</summary>
    private int _activeStrokePage;

    private double[]? _surfaceSize;

    /// <summary>The viewer instance the <see cref="TmPdfViewer.PinchStarted"/> subscription
    /// currently belongs to (the ref can be re-set by a re-render; each instance is
    /// subscribed exactly once).</summary>
    private TmPdfViewer? _pinchSubscribedViewer;

    // ── Surface interaction (single-page overlay: one surface, JS-measured) ──

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

        await DropPointAsync(_currentPage, point.Value.X, point.Value.Y);
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

        StartStrokeAt(_currentPage, point.Value, e);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Single-page move: the one surface IS the current page, so the stroke keeps
    /// normalizing against the JS-measured surface size (FZE-9 — no JS round-trip per move
    /// event, the size from pointer-down is reused) and only the owning pointer extends it.</summary>
    private async Task HandlePointerMoveAsync(PointerEventArgs e)
    {
        if (_activeStroke is null || e.PointerId != _strokePointerId)
        {
            return;
        }

        var point = await NormalizePointAsync(e.OffsetX, e.OffsetY, refreshSize: false);
        if (point is null)
        {
            return;
        }

        _activeStroke.Add(DocumentInkPoint.Create(point.Value.X, point.Value.Y));
        await InvokeAsync(StateHasChanged);
    }

    private Task HandlePointerUpAsync(PointerEventArgs e) => FinishStrokeAsync(e.PointerId);

    private Task HandlePointerLeaveAsync(PointerEventArgs e) => FinishStrokeAsync(e.PointerId);

    // ── Surface interaction (continuous: per-page slots, slot-box normalized) ──

    /// <summary>
    /// FZE-14: a click on one page's slot. The slot context carries the page number and the
    /// page box (the geometry report's CSS-pixel size — the slot IS that box, so the offset
    /// normalizes against it without a JS measure), so the tool anchors on THE page clicked,
    /// never on the annotator's current page.
    /// </summary>
    private async Task HandleSurfaceClickAsync(PdfPageOverlayContext page, MouseEventArgs e)
    {
        if (_mode is not (PdfAnnotatorMode.Comment or PdfAnnotatorMode.Stamp))
        {
            return;
        }

        var point = NormalizeOnPage(page, e.OffsetX, e.OffsetY);
        if (point is null)
        {
            return;
        }

        await DropPointAsync(page.PageNumber, point.Value.X, point.Value.Y);
    }

    private async Task HandlePointerDownAsync(PdfPageOverlayContext page, PointerEventArgs e)
    {
        if (_mode != PdfAnnotatorMode.Draw)
        {
            return;
        }

        var point = NormalizeOnPage(page, e.OffsetX, e.OffsetY);
        if (point is null)
        {
            return;
        }

        // A second contact is resolved by StartStrokeAt: the pen's priority drops a live
        // touch stroke, another touch contact is the pinch hand-over (cancel), and any
        // further contact draws nothing.
        if (!StartStrokeAt(page.PageNumber, point.Value, e))
        {
            await InvokeAsync(StateHasChanged);
            return;
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task HandlePointerMoveAsync(PdfPageOverlayContext page, PointerEventArgs e)
    {
        if (_activeStroke is null)
        {
            return;
        }

        // Only the pointer that started the stroke may extend it (the pinch's retargeted
        // fingers must not keep drawing), and only on the page the stroke started on —
        // a move reported by another page's slot is ignored (the stroke is confined).
        if (e.PointerId != _strokePointerId || page.PageNumber != _activeStrokePage)
        {
            return;
        }

        var point = NormalizeOnPage(page, e.OffsetX, e.OffsetY);
        if (point is null)
        {
            return;
        }

        _activeStroke.Add(DocumentInkPoint.Create(point.Value.X, point.Value.Y));
        await InvokeAsync(StateHasChanged);
    }

    private Task HandlePointerUpAsync(PdfPageOverlayContext page, PointerEventArgs e)
        => FinishStrokeAsync(e.PointerId);

    private Task HandlePointerLeaveAsync(PdfPageOverlayContext page, PointerEventArgs e)
        => FinishStrokeAsync(e.PointerId);

    // ── Shared tool drop (comment draft / stamp) ─────────────────────────────

    /// <summary>Applies a click point to the active tool: a comment draft anchored on
    /// <paramref name="page"/> (with the G2 mobile sheet flow) or an immediate stamp.</summary>
    private async Task DropPointAsync(int page, double x, double y)
    {
        if (_mode == PdfAnnotatorMode.Comment)
        {
            _draft = new PdfAnnotatorDraft
            {
                Kind = DocumentAnnotationKind.Comment,
                Anchor = DocumentCommentAnchor.Point(page, x, y)
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
            await CreateStampAsync(page, x, y);
        }
    }

    // ── Ink stroke bookkeeping ───────────────────────────────────────────────

    /// <summary>
    /// Starts a stroke on <paramref name="page"/>. Returns false when the contact must not
    /// draw (a second finger is the pinch hand-over, a second pen/mouse contact is ignored,
    /// or the page holds a drawing draft anchored on a different page); the pen's priority
    /// (FZE-9) is unchanged — a stylus landing replaces a live finger stroke, which is
    /// cancelled (never committed).
    /// </summary>
    private bool StartStrokeAt(int page, (double X, double Y) point, PointerEventArgs e)
    {
        // A completed stroke joins the open drawing draft — but only on the draft's own
        // page: a contact on another page must not extend a draft anchored elsewhere
        // (FZE-14), so it draws nothing and the draft stays confined to its page.
        if (_draft is { Kind: DocumentAnnotationKind.Drawing, Anchor: { } draftAnchor }
            && draftAnchor.PageNumber != page)
        {
            return false;
        }

        if (_activeStroke is not null)
        {
            if (e.PointerType == "pen" && _strokePointerType == "touch")
            {
                // FZE-9: the pen has priority — it replaces the finger's stroke, which is
                // cancelled (never committed), and the pen starts its own below.
                _activeStroke = null;
                _strokePointerId = null;
                _strokePointerType = null;
            }
            else if (e.PointerType == "touch" && _strokePointerType == "touch")
            {
                // A second finger is the pinch hand-over — the same event that starts the
                // core pinch — so the stroke is cancelled, never committed (the
                // pointer-drag convention: the pinch's start cancels the one-finger drag).
                // A palm touch during a PEN stroke does NOT match: the pen keeps drawing.
                _activeStroke = null;
                _strokePointerId = null;
                _strokePointerType = null;
                _activeStrokePage = 0;
                return false;
            }
            else
            {
                // A stroke is already live for a pointer that keeps priority (a second pen,
                // a mouse while a pen draws, a palm touch during a pen stroke): the new
                // contact draws nothing.
                return false;
            }
        }

        _activeStroke = [DocumentInkPoint.Create(point.X, point.Y)];
        _strokePointerId = e.PointerId;
        _strokePointerType = e.PointerType;
        _activeStrokePage = page;
        return true;
    }

    private void HandlePointerCancel(PointerEventArgs e) => DiscardActiveStroke();

    /// <summary>
    /// Discards the active ink stroke without committing it. Raised by the embedded
    /// viewer's <see cref="TmPdfViewer.PinchStarted"/> (the pinch owns the surface from
    /// the moment its two fingers land) and by <c>pointercancel</c> on the surface.
    /// Void on purpose: it is an <c>Action</c> event handler.
    /// </summary>
    private void DiscardActiveStroke()
    {
        if (_activeStroke is null)
        {
            return;
        }

        _activeStroke = null;
        _strokePointerId = null;
        _strokePointerType = null;
        _activeStrokePage = 0;
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Subscribes the stroke discard to the embedded viewer's pinch start. Called from
    /// <see cref="OnAfterRenderAsync"/> (the viewer reference exists only after a render);
    /// idempotent per viewer instance.
    /// </summary>
    private void SubscribeToViewerPinch()
    {
        if (_viewer is null || ReferenceEquals(_pinchSubscribedViewer, _viewer))
        {
            return;
        }

        if (_pinchSubscribedViewer is not null)
        {
            _pinchSubscribedViewer.PinchStarted -= DiscardActiveStroke;
        }

        _pinchSubscribedViewer = _viewer;
        _pinchSubscribedViewer.PinchStarted += DiscardActiveStroke;
    }

    private void UnsubscribeFromViewerPinch()
    {
        if (_pinchSubscribedViewer is null)
        {
            return;
        }

        _pinchSubscribedViewer.PinchStarted -= DiscardActiveStroke;
        _pinchSubscribedViewer = null;
    }

    private async Task FinishStrokeAsync(long? pointerId = null)
    {
        if (_activeStroke is null)
        {
            return;
        }

        // A lifting finger other than the stroke's own (e.g. the pinch's captured second
        // finger) must not commit the stroke.
        if (pointerId is { } id && id != _strokePointerId)
        {
            return;
        }

        var stroke = _activeStroke;
        var strokePage = _activeStrokePage;
        _activeStroke = null;
        _strokePointerId = null;
        _strokePointerType = null;
        _activeStrokePage = 0;
        if (stroke.Count >= 2)
        {
            _draft ??= new PdfAnnotatorDraft
            {
                Kind = DocumentAnnotationKind.Drawing,
                Anchor = DocumentCommentAnchor.Point(strokePage, stroke[0].X, stroke[0].Y)
            };
            _draft.Strokes.Add(new DocumentInkStroke { Points = stroke });
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Normalizes a pointer offset against a continuous page slot: the slot's box IS the
    /// reported page geometry (FZE-13), so no JS measure is needed. Returns null when the
    /// geometry is not (yet) known — the slot does not exist before the report anyway.
    /// </summary>
    private static (double X, double Y)? NormalizeOnPage(PdfPageOverlayContext page, double offsetX, double offsetY)
        => page.Width > 0 && page.Height > 0
            ? (offsetX / page.Width, offsetY / page.Height)
            : null;

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
}
