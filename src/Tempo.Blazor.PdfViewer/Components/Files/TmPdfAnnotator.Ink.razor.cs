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

    private double[]? _surfaceSize;

    /// <summary>The viewer instance the <see cref="TmPdfViewer.PinchStarted"/> subscription
    /// currently belongs to (the ref can be re-set by a re-render; each instance is
    /// subscribed exactly once).</summary>
    private TmPdfViewer? _pinchSubscribedViewer;

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
                await InvokeAsync(StateHasChanged);
                return;
            }
            else
            {
                // A stroke is already live for a pointer that keeps priority (a second pen,
                // a mouse while a pen draws, a palm touch during a pen stroke): the new
                // contact draws nothing.
                return;
            }
        }

        var point = await NormalizePointAsync(e.OffsetX, e.OffsetY, refreshSize: true);
        if (point is null)
        {
            return;
        }

        _activeStroke = [DocumentInkPoint.Create(point.Value.X, point.Value.Y)];
        _strokePointerId = e.PointerId;
        _strokePointerType = e.PointerType;
        await InvokeAsync(StateHasChanged);
    }

    private async Task HandlePointerMoveAsync(PointerEventArgs e)
    {
        if (_activeStroke is null)
        {
            return;
        }

        // Only the pointer that started the stroke may extend it: after a pinch captures
        // both fingers, their moves are retargeted and must not keep drawing.
        if (e.PointerId != _strokePointerId)
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

    private Task HandlePointerUpAsync(PointerEventArgs e) => FinishStrokeAsync(e.PointerId);

    private Task HandlePointerLeaveAsync(PointerEventArgs e) => FinishStrokeAsync(e.PointerId);

    /// <summary>
    /// FZE-9 review (M2): the platform cancelled the pointer (a pinch hand-over, a palm
    /// rejection, an incoming call). The live stroke is discarded — never committed —
    /// exactly like the pinch's own start notification does.
    /// </summary>
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
        _activeStroke = null;
        _strokePointerId = null;
        _strokePointerType = null;
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
}
