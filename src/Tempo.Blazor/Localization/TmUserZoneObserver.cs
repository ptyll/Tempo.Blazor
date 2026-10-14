namespace Tempo.Blazor.Localization;

/// <summary>
/// Wires one consumer (a relative-time component, the Scheduler, the Gantt) to
/// <see cref="ITmUserTimeZone"/>: subscribes to <see cref="ITmUserTimeZone.Changed"/> and re-renders
/// through the renderer's <c>InvokeAsync</c> (the stamp is computed at render time; the zone event
/// only triggers that render), and triggers the one-time browser probe after the first interactive
/// render. A no-op when the service is not registered (a bare test context).
/// </summary>
internal sealed class TmUserZoneObserver : IDisposable
{
    private readonly Func<Task> _refresh;
    private readonly ITmUserTimeZone _zone;

    private TmUserZoneObserver(Func<Task> refresh, ITmUserTimeZone zone)
    {
        _refresh = refresh;
        _zone = zone;
    }

    /// <summary>
    /// Subscribes <paramref name="refresh"/> to the registered <see cref="ITmUserTimeZone"/>, or
    /// returns null when none is registered.
    /// </summary>
    /// <param name="services">The component's service provider (<c>Services</c>).</param>
    /// <param name="refresh">Re-renders the component, e.g. <c>() =&gt; InvokeAsync(StateHasChanged)</c>.</param>
    public static TmUserZoneObserver? Subscribe(IServiceProvider services, Func<Task> refresh)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(refresh);
        if (services.GetService(typeof(ITmUserTimeZone)) is not ITmUserTimeZone zone)
        {
            return null;
        }

        var observer = new TmUserZoneObserver(refresh, zone);
        zone.Changed += observer.OnZoneChanged;
        return observer;
    }

    /// <summary>
    /// Triggers the one-time browser zone probe (the built-in Server implementation only). Call from
    /// <c>OnAfterRenderAsync(firstRender: true)</c>, never earlier — before the first interactive
    /// render JS interop is unavailable.
    /// </summary>
    public Task ProbeAsync()
        => _zone is TmUserTimeZone concrete ? concrete.EnsureResolvedAsync() : Task.CompletedTask;

    private void OnZoneChanged(object? sender, EventArgs e) => _ = _refresh();

    /// <summary>Unsubscribes; the component no longer refreshes when the zone changes.</summary>
    public void Dispose() => _zone.Changed -= OnZoneChanged;
}
