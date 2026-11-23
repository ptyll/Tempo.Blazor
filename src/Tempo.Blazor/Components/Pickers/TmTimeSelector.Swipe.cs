using Microsoft.JSInterop;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Pickers;

public partial class TmTimeSelector
{
    private bool _swipeActive;
    private bool _swipeHadValue;
    private TimeOnly? _swipeOrigin;
    private bool _suppressUnchangedRender;
    private string? _renderedFingerprint;
    private bool _hasRendered;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        base.OnInitialized();
        _renderedFingerprint = VisualFingerprint();
        _hasRendered = true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Auto-repeat and swipe ticks set <see cref="_suppressUnchangedRender"/> so a clamped
    /// step that does not change the readout skips the Blazor render (Fáze 8).
    /// </remarks>
    protected override bool ShouldRender()
    {
        var next = VisualFingerprint();
        if (_suppressUnchangedRender && _hasRendered && next == _renderedFingerprint)
        {
            _suppressUnchangedRender = false;
            return false;
        }

        _suppressUnchangedRender = false;
        _renderedFingerprint = next;
        _hasRendered = true;
        return true;
    }

    /// <summary>Called by <c>tm-time-selector.js</c> when an opt-in swipe crosses the 6 px threshold.</summary>
    [JSInvokable]
    public Task OnSwipeStartAsync(string segment)
        => InvokeAsync(() =>
        {
            if (Disabled || ReadOnly || !EnableSwipe)
            {
                return;
            }

            // A fast pointermove can deliver OnSwipeStepAsync first (JS invoke is fire-and-forget).
            // Do not overwrite an origin the lazy step already captured.
            if (_swipeActive)
            {
                return;
            }

            CaptureSwipeOrigin();
        });

    /// <summary>Called for every 24 px of vertical travel (swipe up = +1).</summary>
    [JSInvokable]
    public Task OnSwipeStepAsync(string segment, int delta)
        => InvokeAsync(async () =>
        {
            if (Disabled || ReadOnly || !EnableSwipe)
            {
                return;
            }

            if (!_swipeActive)
            {
                CaptureSwipeOrigin();
            }

            _suppressUnchangedRender = true;
            await StepAsync(ParseSegment(segment), delta, announce: false);
        });

    /// <summary>Called on pointerup: announces the result and fires <see cref="OnCommitted"/>.</summary>
    [JSInvokable]
    public Task OnSwipeEndAsync(string segment)
        => InvokeAsync(async () =>
        {
            if (!_swipeActive)
            {
                return;
            }

            _swipeActive = false;
            _swipeHadValue = false;
            _swipeOrigin = null;
            if (Disabled || ReadOnly)
            {
                return;
            }

            await StepAsync(ParseSegment(segment), 0, announce: true);
        });

    /// <summary>
    /// Called on pointercancel / Escape: restores the gesture-start value. An empty origin
    /// restores <c>null</c> (the readout goes back to <c>--</c>) and never fires
    /// <see cref="OnCommitted"/>. <see cref="ValueChanged"/> carries the restored time so a
    /// two-way bind is not left mid-gesture; <c>EditContext.NotifyFieldChanged</c> follows so
    /// an <c>EditForm</c> re-validates after the mid-gesture tick.
    /// </summary>
    [JSInvokable]
    public Task OnSwipeCancelAsync(string segment)
        => InvokeAsync(async () =>
        {
            if (!_swipeActive)
            {
                return;
            }

            var origin = _swipeOrigin;
            var hadValue = _swipeHadValue;
            _swipeActive = false;
            _swipeHadValue = false;
            _swipeOrigin = null;
            if (Disabled || ReadOnly)
            {
                return;
            }

            if (hadValue)
            {
                if (origin is { } restored)
                {
                    await CommitAsync(restored, announce: false);
                }

                return;
            }

            _hasDraft = false;
            _draft = null;
            await ValueChanged.InvokeAsync(null);
            CascadedEditContext?.NotifyFieldChanged(FieldIdentifier);
            StateHasChanged();
        });

    private void CaptureSwipeOrigin()
    {
        _swipeActive = true;
        _swipeHadValue = ShownValue.HasValue;
        _swipeOrigin = ShownValue;
    }

    private string VisualFingerprint()
        => string.Join(
            '\u001f',
            ShownValue?.ToString("HH:mm:ss") ?? "-",
            LiveText,
            EnableSwipe ? "1" : "0",
            Disabled ? "1" : "0",
            ReadOnly ? "1" : "0",
            _typing ? "1" : "0",
            _keyboardOpen ? "1" : "0",
            FitAttr,
            LayoutAttr,
            GridAttr);
}
