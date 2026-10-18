namespace Tempo.Blazor.Components.Diagram.Services;

/// <summary>
/// Shared 300 ms debouncer for the diagram import dialogs' live preview (SQL and CSV): each
/// input re-arms a single-shot timer and the callback drains through the Blazor dispatcher once
/// typing pauses. Centralising the timer keeps the staleness check (a superseded timer never
/// fires) and the disposal guard in one place.
/// </summary>
internal sealed class DiagramPreviewDebouncer : IDisposable
{
    internal const int DebounceMs = 300;

    private readonly Func<Func<Task>, Task> _invokeAsync;
    private readonly TimeProvider _clock;
    private ITimer? _timer;
    private bool _disposed;

    /// <summary>
    /// Creates the debouncer. <paramref name="invokeAsync"/> is the owner's ComponentBase
    /// InvokeAsync (marshals the callback onto the renderer's synchronization context);
    /// <paramref name="clock"/> drives the timer (tests substitute a fake one).
    /// </summary>
    public DiagramPreviewDebouncer(Func<Func<Task>, Task> invokeAsync, TimeProvider? clock = null)
    {
        _invokeAsync = invokeAsync;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Re-arms the debounce; <paramref name="onElapsed"/> runs once, on the dispatcher, after the quiet period.</summary>
    public void Schedule(Func<Task> onElapsed)
    {
        Cancel();
        if (_disposed) return;
        ITimer? timer = null;
        timer = _clock.CreateTimer(_ => _ = OnElapsedAsync(timer!, onElapsed), null,
            TimeSpan.FromMilliseconds(DebounceMs), Timeout.InfiniteTimeSpan);
        _timer = timer;
    }

    /// <summary>Drops a pending timer without running the callback.</summary>
    public void Cancel()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose()
    {
        _disposed = true;
        Cancel();
    }

    private async Task OnElapsedAsync(ITimer timer, Func<Task> onElapsed)
    {
        if (_disposed || !ReferenceEquals(_timer, timer)) return;
        try
        {
            await _invokeAsync(async () =>
            {
                if (_disposed || !ReferenceEquals(_timer, timer)) return;
                _timer = null;
                timer.Dispose();
                await onElapsed();
            });
        }
        catch (ObjectDisposedException)
        {
            // The component was disposed while the debounced callback was in flight — nothing to update.
        }
    }
}
