namespace Tempo.Blazor.AI.Models;

/// <summary>
/// The render gate every live agent visualization shares: at most one paint per <see cref="Interval"/>, measured on
/// an injected <see cref="TimeProvider"/>.
/// <para>
/// A hub that pushes ten events per second would otherwise paint ten renders per second per component, and on
/// Blazor Server every one of them is a circuit round trip on top of the DOM work. The gate collapses that burst into
/// one paint per window while the component's state keeps absorbing every event — the state is never throttled, only
/// the paint, so a snapshot read at any moment is complete.
/// </para>
/// <para>
/// An interval of <see cref="TimeSpan.Zero"/> (or a negative one, which is normalised to zero) is the documented way
/// for a host to opt out of throttling entirely.
/// </para>
/// <para>
/// The class owns one timestamp and no clock of its own: nothing reads the wall clock, so a test drives it with
/// <c>FakeTimeProvider</c> and a host may pin it to the same clock its data uses.
/// </para>
/// </summary>
/// <remarks>
/// NOT thread-safe, unlike <see cref="AgentEventStream{TEvent}"/>: one timestamp is read and written with no gate, so
/// two threads calling <see cref="ShouldRender"/> at the same moment can both be answered true for one window. An
/// instance therefore belongs to the component that owns it and is called from that component's renderer context
/// (inside <c>InvokeAsync</c> on Blazor Server, or on the dispatcher in WASM) — which is where a render is decided
/// anyway. A host that feeds one instance from two threads needs its own lock; the class stays allocation- and
/// lock-free on the hot path.
/// </remarks>
public sealed class AgentRenderThrottle
{
    /// <summary>The shared default window: ten events per second collapse into one paint.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(100);

    private readonly TimeProvider _timeProvider;
    private DateTimeOffset? _lastRender;

    /// <summary>Creates a throttle on the system clock with the shared default window.</summary>
    public AgentRenderThrottle()
        : this(TimeProvider.System, null)
    {
    }

    /// <summary>Creates a throttle on a host clock.</summary>
    /// <param name="timeProvider">The clock the window is measured on; null uses the system clock.</param>
    /// <param name="interval">
    /// The minimum time between two paints. Null uses <see cref="DefaultInterval"/>; zero (or a negative value) means
    /// "never throttle".
    /// </param>
    public AgentRenderThrottle(TimeProvider? timeProvider = null, TimeSpan? interval = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        var window = interval ?? DefaultInterval;
        Interval = window < TimeSpan.Zero ? TimeSpan.Zero : window;
    }

    /// <summary>The effective window. <see cref="TimeSpan.Zero"/> means every call paints.</summary>
    public TimeSpan Interval { get; }

    /// <summary>
    /// Whether the component may paint now. A true answer OPENS a new window, so a caller must not ask twice for one
    /// paint (ask once per event, paint when the answer is true).
    /// </summary>
    /// <returns><c>true</c> when at least <see cref="Interval"/> has passed since the last true answer.</returns>
    public bool ShouldRender()
    {
        var now = _timeProvider.GetUtcNow();
        if (_lastRender is { } last && now - last < Interval)
        {
            return false;
        }

        _lastRender = now;
        return true;
    }

    /// <summary>
    /// How long a caller scheduling a trailing paint has to wait for the current window to close.
    /// <see cref="TimeSpan.Zero"/> means the stream may paint immediately; the value never goes negative, so a clock
    /// that jumped cannot produce a negative delay.
    /// </summary>
    /// <returns>The remaining window, or zero.</returns>
    public TimeSpan DelayUntilNextRender()
    {
        if (_lastRender is not { } last)
        {
            return TimeSpan.Zero;
        }

        var remaining = Interval - (_timeProvider.GetUtcNow() - last);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// Forgets the last paint, so the next <see cref="ShouldRender"/> answers true. A host that repainted for its own
    /// reason (a snapshot reload) calls this instead of leaving its next event held back by the previous window.
    /// </summary>
    public void Reset() => _lastRender = null;
}
