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
            var wanted = _resolved != TmLayoutMode.Mobile && IsExpanded(side, collapsed);
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
    /// the width the user can really reach. Re-renders only when the measurement changes.
    /// </summary>
    private async Task SyncCanvasWidthAsync()
    {
        if (_disposed) return;
        var docked = _resolved != TmLayoutMode.Mobile && _attachedResizers.Count > 0;
        if (!docked)
        {
            _canvasWidth = double.NaN;
            return;
        }
        _shellJs ??= new EditorShellInterop(JS);
        var measured = await _shellJs.MeasureCanvasAsync(_main);
        if (_disposed || measured.Equals(_canvasWidth)) return;
        _canvasWidth = measured;
        StateHasChanged();
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
        var clamped = (int)EditorShellResize.Clamp(width, CurrentWidthPx(panel), MinWidth(panel), MaxWidth(panel), double.NaN, MinCanvasWidth);
        return InvokeAsync(() => CommitWidthAsync(panel, clamped));
    }

    private async Task CommitWidthAsync(EditorShellPanel side, int width)
    {
        if (_disposed) return;
        // A non-pixel host width has no pixel value to compare with: any committed width is a change.
        var effective = side == EditorShellPanel.Left ? EffectiveLeftWidth : EffectiveRightWidth;
        if (EditorShellResize.TryParsePx(effective, out var currentPx) && width == currentPx) return;

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
