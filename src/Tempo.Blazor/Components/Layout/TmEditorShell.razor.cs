using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Components.Layout;

// The resize half of the shell (separators, the pointer-drag registration, the keyboard path and the
// width commit) lives beside the markup file to keep the .razor under the source-file length ceiling.
public partial class TmEditorShell
{
    // ── Resize separators ───────────────────────────────────────────────────

    /// <summary>
    /// Keeps the pointer-drag registrations in step with what is rendered: one per EXPANDED docked
    /// panel, re-attached only when its clamp inputs change (so a re-render never tears down a drag
    /// in progress), detached when the panel collapses, hides or the layout turns mobile.
    /// </summary>
    private async Task SyncResizersAsync()
    {
        if (_disposed) return;
        var collapsed = EffectiveCollapsed(_resolved);
        foreach (var side in new[] { EditorShellPanel.Left, EditorShellPanel.Right })
        {
            var wanted = _resolved != TmLayoutMode.Mobile && !IsOverlay(_resolved) && IsExpanded(side, collapsed);
            var id = ResizerId(side);
            if (!wanted)
            {
                if (_attachedResizers.Remove(side) && _shellJs is not null) await _shellJs.DetachResizeAsync(id);
                continue;
            }

            var key = $"{MinWidth(side)}:{MaxWidth(side)}:{MinCanvasWidth}";
            if (_attachedResizers.TryGetValue(side, out var attached) && attached == key) continue;
            _shellJs ??= new EditorShellInterop(JS);
            _dotNetRef ??= DotNetObjectReference.Create(this);
            _attachedResizers[side] = key;
            await _shellJs.AttachResizeAsync(id,
                side == EditorShellPanel.Left ? _leftHandle : _rightHandle,
                side == EditorShellPanel.Left ? _leftAside : _rightAside,
                _main, _dotNetRef, side == EditorShellPanel.Left ? "left" : "right", MinWidth(side), MaxWidth(side), MinCanvasWidth);
        }
    }

    /// <summary>
    /// Measures the canvas after each render while panels are docked, so the separators can announce
    /// the width the user can really reach and the tablet fit overlay can keep the canvas at
    /// <see cref="TmEditorShell.MinCanvasWidth"/>. Re-renders only when the measurement or a fit width
    /// changes.
    /// </summary>
    private async Task SyncCanvasWidthAsync()
    {
        if (_disposed) return;
        var collapsed = EffectiveCollapsed(_resolved);
        var docked = _resolved != TmLayoutMode.Mobile && _attachedResizers.Count > 0;
        if (!docked)
        {
            _canvasWidth = double.NaN;
            _tabletFitLeft = null;
            _tabletFitRight = null;
            _fitComputed = false;
            if (_shellJs is not null) await _shellJs.DisconnectCanvasAsync();
            return;
        }
        _shellJs ??= new EditorShellInterop(JS);
        // A container resize can change the canvas width without a Blazor render (a window resize,
        // a sibling pane): the module watches the canvas region and calls back, so the tablet fit
        // recomputes in that case too.
        _dotNetRef ??= DotNetObjectReference.Create(this);
        await _shellJs.ObserveCanvasAsync(_main, _dotNetRef);
        var measured = await _shellJs.MeasureCanvasAsync(_main);
        if (_disposed) return;
        _fitComputed = true;
        var fitChanged = SyncTabletFit(measured, collapsed);
        if (!fitChanged && measured.Equals(_canvasWidth)) return;
        _canvasWidth = measured;
        StateHasChanged();
    }

    /// <summary>
    /// Called by the module's canvas ResizeObserver: the canvas region changed size without a
    /// Blazor render (a container or window resize), so the tablet fit must be recomputed.
    /// </summary>
    [JSInvokable("OnCanvasResized")]
    public Task OnCanvasResized()
        => _disposed ? Task.CompletedTask : InvokeAsync(SyncCanvasWidthAsync);

    /// <summary>
    /// Recomputes the tablet fit overlay for both sides. Returns true when a fit width changed (the
    /// caller re-renders even when the canvas measurement itself is unchanged).
    /// </summary>
    private bool SyncTabletFit(double measured, EditorShellPanel collapsed)
    {
        var left = NextFit(EditorShellPanel.Left, measured, collapsed);
        var right = NextFit(EditorShellPanel.Right, measured, collapsed);
        if (left == _tabletFitLeft && right == _tabletFitRight) return false;
        _tabletFitLeft = left;
        _tabletFitRight = right;
        return true;
    }

    /// <summary>
    /// The fit width of one side: on a tablet layout, an expanded panel narrows — from the width it
    /// renders at, by the measured canvas deficit below <see cref="TmEditorShell.MinCanvasWidth"/> —
    /// clamped to the side's minimum and never beyond the width in effect (user or host). Reaching
    /// the width in effect clears the overlay, so a grown container relaxes back to the host width.
    /// Converges in one extra render: rendered at the fit, the canvas measures exactly
    /// MinCanvasWidth (unless the minimum clamped the fit).
    /// </summary>
    private int? NextFit(EditorShellPanel side, double measured, EditorShellPanel collapsed)
    {
        if (_resolved != TmLayoutMode.Tablet || !IsExpanded(side, collapsed) || double.IsNaN(measured))
            return null;
        var ceiling = CurrentWidthPx(side);
        var active = TabletFit(side);
        var current = active ?? ceiling;
        var deficit = (int)Math.Floor(measured) - MinCanvasWidth;
        // Hysteresis: a sliver of surplus (a fractional relayout, a scrollbar coming and going)
        // must not relax the fit — relaxing and re-applying on every pass would oscillate the
        // rendered width and churn scrollbars. Only a real surplus (24px, a classic scrollbar)
        // gives the width back.
        if (active is { } && deficit is > 0 and < 24) return active;
        var next = Math.Clamp(current + deficit, MinWidth(side), ceiling);
        return next >= ceiling ? null : next;
    }

    // One shell instance, one registration per side: the module keeps a single map, so a shared
    // "left"/"right" key would make the second shell on a page detach the first one's drag.
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private string ResizerId(EditorShellPanel side) => $"{_instanceId}-{(side == EditorShellPanel.Left ? "left" : "right")}";

    private async Task OnSeparatorKeyAsync(EditorShellPanel side, KeyboardEventArgs args)
    {
        var step = args.ShiftKey ? 64 : 16;
        // The left panel's separator is on its right edge: ArrowRight grows it. The right panel's
        // separator is on its left edge: ArrowLeft grows it.
        var grow = side == EditorShellPanel.Left ? "ArrowRight" : "ArrowLeft";
        var shrink = side == EditorShellPanel.Left ? "ArrowLeft" : "ArrowRight";
        _shellJs ??= new EditorShellInterop(JS);
        var current = await CurrentWidthMeasuredAsync(side);
        double requested;
        if (args.Key == grow) requested = current + step;
        else if (args.Key == shrink) requested = current - step;
        else if (args.Key == "Home") requested = MinWidth(side);
        else if (args.Key == "End") requested = MaxWidth(side);
        else return;

        var canvas = await _shellJs.MeasureCanvasAsync(_main);
        var width = (int)EditorShellResize.Clamp(requested, current, MinWidth(side), MaxWidth(side), canvas, MinCanvasWidth);
        await CommitWidthAsync(side, width);
    }

    /// <summary>Commits a width the pointer drag in <c>tm-editor-shell.js</c> settled on.</summary>
    /// <param name="side"><c>left</c> or <c>right</c>.</param>
    /// <param name="width">The width in CSS pixels; clamped and validated again here.</param>
    [JSInvokable("OnResizeCommitted")]
    public Task HandleResizeCommitted(string side, int width)
    {
        if (_disposed) return Task.CompletedTask;
        var panel = side switch
        {
            "left" => EditorShellPanel.Left,
            "right" => EditorShellPanel.Right,
            _ => EditorShellPanel.None,
        };
        if (panel == EditorShellPanel.None) return Task.CompletedTask;
        // The drag started from the RENDERED width (the tablet fit included), so the clamp must
        // too: growth is measured against what is on screen, not the wider host width.
        var clamped = (int)EditorShellResize.Clamp(width, RenderedWidthPx(panel), MinWidth(panel), MaxWidth(panel), double.NaN, MinCanvasWidth);
        return InvokeAsync(() => CommitWidthAsync(panel, clamped));
    }

    private async Task CommitWidthAsync(EditorShellPanel side, int width)
    {
        if (_disposed) return;
        // A user resize is a choice of width: it wins over the tablet fit overlay (the fit may
        // recompute on the next measurement, but never overrides a width the user set). Cleared
        // BEFORE the early return below: a commit of exactly the host width while a fit is active
        // is the user taking the panel back — the fit must not survive it.
        var hadFit = TabletFit(side) is not null;
        if (side == EditorShellPanel.Left) _tabletFitLeft = null; else _tabletFitRight = null;

        // A non-pixel host width has no pixel value to compare with: any committed width is a change.
        var effective = side == EditorShellPanel.Left ? EffectiveLeftWidth : EffectiveRightWidth;
        if (EditorShellResize.TryParsePx(effective, out _)
            && width == RenderedWidthPx(side) && TabletFit(side) is null)
        {
            // The fit was dropped: re-render so the panel shows the committed width, not the fit.
            if (hadFit) StateHasChanged();
            return;
        }

        if (side == EditorShellPanel.Left)
        {
            _userBaseLeft ??= LeftWidth;
            // Back on the width the host supplied: the override is gone, so nothing stays stored.
            _userLeft = EditorShellResize.TryParsePx(_userBaseLeft, out var baseLeft) && baseLeft == width ? null : width;
            if (_userLeft is null) _userBaseLeft = null;
            await LeftWidthChanged.InvokeAsync($"{width}px");
        }
        else
        {
            _userBaseRight ??= RightWidth;
            _userRight = EditorShellResize.TryParsePx(_userBaseRight, out var baseRight) && baseRight == width ? null : width;
            if (_userRight is null) _userBaseRight = null;
            await RightWidthChanged.InvokeAsync($"{width}px");
        }

        await PersistWidthsAsync();
        StateHasChanged();
    }
}
