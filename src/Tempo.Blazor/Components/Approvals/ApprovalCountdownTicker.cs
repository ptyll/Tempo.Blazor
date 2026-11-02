namespace Tempo.Blazor.Components.Approvals;

/// <summary>
/// One-second timer behind the countdown of an approval card.
/// <para>
/// The timer is armed only while there is something to count (a pending deadline or an open undo
/// window) and disarmed the moment there is not, so a decided card never keeps re-rendering. The
/// callback runs on a thread-pool thread with <see cref="TimeProvider.System"/> and on the advancing
/// thread with a fake clock, so the owner marshals to the renderer itself.
/// </para>
/// </summary>
internal sealed class ApprovalCountdownTicker : IDisposable
{
    /// <summary>How often the countdown is refreshed. One second reads as a clock, not as a jitter.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _clock;
    private readonly Func<Task> _onTick;
    private ITimer? _timer;

    /// <summary>Creates the ticker.</summary>
    /// <param name="clock">The clock that owns the timer.</param>
    /// <param name="onTick">Called once per interval; must marshal to the renderer itself.</param>
    public ApprovalCountdownTicker(TimeProvider clock, Func<Task> onTick)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(onTick);
        _clock = clock;
        _onTick = onTick;
    }

    /// <summary>Whether a timer is currently armed.</summary>
    public bool IsRunning => _timer is not null;

    /// <summary>Arms the timer; a second call is a no-op, so a re-render cannot stack timers.</summary>
    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = _clock.CreateTimer(_ => _ = TickAsync(), null, Interval, Interval);
    }

    /// <summary>Disarms the timer; safe to call when nothing is armed.</summary>
    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private async Task TickAsync()
    {
        try
        {
            await _onTick();
        }
        catch (ObjectDisposedException)
        {
            // The owner went away between the timer firing and the hop.
        }
        catch (InvalidOperationException)
        {
            // The renderer has already been torn down.
        }
    }

    /// <inheritdoc />
    public void Dispose() => Stop();
}
