namespace Tempo.Blazor.Localization;

/// <summary>
/// The relative-time formatter's private clock: <see cref="LocalTimeZone"/> reads
/// <see cref="ITmUserTimeZone.Zone"/> lazily on every access (the browser reports the zone only
/// after the first render), while the time itself — <see cref="GetUtcNow"/>, timestamps, timers —
/// delegates to the host's <see cref="TimeProvider"/> (the system clock when none is registered),
/// so a host <c>FakeTimeProvider</c> keeps working.
/// <para>
/// NEVER registered as <see cref="TimeProvider"/>: Kestrel and cookie authentication are singletons
/// that consume the app-wide clock, so a scoped replacement fails the host under
/// <c>ValidateScopes</c> and, without validation, drives the server's clocks with the user's zone.
/// </para>
/// </summary>
internal sealed class TmUserTimeProvider : TimeProvider
{
    private readonly ITmUserTimeZone _zone;
    private readonly TimeProvider _inner;

    public TmUserTimeProvider(ITmUserTimeZone zone, TimeProvider? inner = null)
    {
        ArgumentNullException.ThrowIfNull(zone);
        _zone = zone;
        _inner = inner ?? TimeProvider.System;
    }

    /// <summary>Read lazily on every access: the zone arrives from JS after the first render.</summary>
    public override TimeZoneInfo LocalTimeZone => _zone.Zone;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();

    /// <inheritdoc />
    public override long GetTimestamp() => _inner.GetTimestamp();

    /// <inheritdoc />
    public override long TimestampFrequency => _inner.TimestampFrequency;

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => _inner.CreateTimer(callback, state, dueTime, period);
}
