namespace Tempo.Blazor.Localization;

/// <summary>
/// The end user's time zone, resolved once per circuit. Registering <c>AddTempoUserTimeZone()</c>
/// opts the relative-time formatter into this zone; until then the default formatter keeps judging
/// "yesterday" in the clock's local zone, so nothing changes for hosts that do not opt in. The
/// scoped instance can be shared by other consumers (e.g. Scheduler, Gantt), so the browser is
/// read only once per circuit.
/// <see cref="Zone"/> is UTC on Blazor Server until the browser reports its zone after the first
/// interactive render; on WebAssembly it is the runtime's local zone, which already is the
/// browser's zone.
/// </summary>
public interface ITmUserTimeZone
{
    /// <summary>The user's zone. Before the browser reported it: <see cref="TimeZoneInfo.Local"/> on WebAssembly, UTC on Server and prerender.</summary>
    TimeZoneInfo Zone { get; }

    /// <summary>True once the zone came from the browser (WebAssembly: immediately) or from <see cref="SetZone"/>.</summary>
    bool IsResolved { get; }

    /// <summary>Raised on the thread that applied the change (the renderer's context for the browser probe); handlers must marshal UI work via <c>InvokeAsync</c>. Fires when the browser value arrived or <see cref="SetZone"/> was called. Consumers re-render their stamps; nothing ticks them.</summary>
    event EventHandler? Changed;

    /// <summary>Host override (e.g. a profile setting). Wins over the browser value.</summary>
    /// <param name="zone">The zone to use from now on; also marks the zone resolved.</param>
    /// <exception cref="ArgumentNullException"><paramref name="zone"/> is null.</exception>
    void SetZone(TimeZoneInfo zone);
}
