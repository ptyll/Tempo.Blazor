namespace Tempo.Blazor.AI.Formatting;

/// <summary>
/// Drives the live elapsed timer of a running tool call and of a running reasoning panel: a repeating
/// timer on the injected <see cref="TimeProvider"/> that re-renders the owner once a second and stops the
/// moment the work finishes.
/// </summary>
/// <remarks>
/// <para>
/// The timer exists only while there is something to count. A finished call keeps its recorded duration,
/// so leaving a timer armed would either keep the UI re-rendering forever or (worse) overwrite a fact with
/// an estimate.
/// </para>
/// <para>
/// The callback runs on a thread-pool thread with <see cref="TimeProvider.System"/> (and on the advancing
/// thread with a fake clock), so the owner passes an <c>InvokeAsync</c> hop — the ticker never touches
/// component state itself.
/// </para>
/// </remarks>
internal sealed class AiElapsedTicker : IDisposable
{
    /// <summary>How often the elapsed label is refreshed. One second reads as a clock, not as a jitter.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _clock;
    private readonly Func<Task> _onTick;
    private ITimer? _timer;

    /// <summary>Creates the ticker.</summary>
    /// <param name="clock">The clock that owns the timer.</param>
    /// <param name="onTick">Called once per interval; must marshal to the renderer itself.</param>
    public AiElapsedTicker(TimeProvider clock, Func<Task> onTick)
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
