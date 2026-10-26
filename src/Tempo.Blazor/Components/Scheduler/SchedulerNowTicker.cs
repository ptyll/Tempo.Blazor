using Microsoft.AspNetCore.Components;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// One minute timer for a whole grid. The first tick lands on the next minute boundary, then every
/// minute, and every indicator of that grid listens to the same timer. Disposing it is safe from
/// inside its own callback.
/// </summary>
internal sealed class SchedulerNowTicker : IDisposable
{
    private readonly TimeProvider _clock;
    private readonly Action _onTick;
    private ITimer? _timer;
    private bool _disposed;

    public SchedulerNowTicker(TimeProvider clock, Action onTick)
    {
        _clock = clock;
        _onTick = onTick;
        
    }

    /// <summary>Starts the timer unless one is already running.</summary>
    public void Start()
    {
        if (_timer is not null || _disposed) return;
        var now = _clock.GetUtcNow();
        var next = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Offset).AddMinutes(1);
        var due = next - now;
        if (due <= TimeSpan.Zero) due = TimeSpan.FromMinutes(1);
        _timer = _clock.CreateTimer(OnTick, null, due, TimeSpan.FromMinutes(1));
    }

    private void OnTick(object? state)
    {
        if (_disposed) return;
        try
        {
            _onTick();
        }
        catch (ObjectDisposedException)
        {
            // The renderer went away between the tick and the dispatch. The next dispose is a no-op.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }
}
