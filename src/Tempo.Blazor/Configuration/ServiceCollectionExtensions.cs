using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Resources;
using Tempo.Blazor.Services;

namespace Tempo.Blazor.Configuration;

/// <summary>
/// Extension methods for registering Tempo.Blazor services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core Tempo.Blazor services shared by all component groups.
    /// <list type="bullet">
    ///   <item><description><see cref="ITmLocalizer"/>: Singleton (stateless, thread-safe, backed by embedded JSON tables for en/cs/sk/fr)</description></item>
    ///   <item><description><see cref="ITmRelativeTimeFormatter"/>: Scoped relative-time formatter over a private <see cref="TimeProvider"/> that reads <see cref="ITmUserTimeZone"/> — the time comes from the host's <see cref="TimeProvider"/> (the system clock when none is registered), while the calendar day ("yesterday at 4:20 PM") is judged in the user's zone: the runtime's zone on WebAssembly, the browser's IANA zone on Blazor Server (UTC until the probe reports after the first interactive render, then the browser zone). The scoped <see cref="ITmUserTimeZone"/> instance is shared (the browser is read once per circuit); relative-time components re-render when the zone arrives and <c>ITmUserTimeZone.SetZone</c> lets a host profile setting win. Never replace the app-wide <see cref="TimeProvider"/>: Kestrel and cookie authentication are singletons that consume it, so a scoped replacement fails the host under <c>ValidateScopes</c>. No component takes a time-zone parameter for this.</description></item>
    ///   <item><description><see cref="ThemeService"/>: Scoped (per-circuit in Server mode, per-tab in WASM)</description></item>
    ///   <item><description><see cref="ToastService"/>: Scoped (per-circuit in Server mode, per-tab in WASM)</description></item>
    ///   <item><description><see cref="DragDropService"/>: Scoped drag/drop state shared by core components</description></item>
    /// </list>
    ///
    /// Add this call to your Blazor Program.cs:
    /// <code>
    /// builder.Services.AddTempoBlazor();
    /// </code>
    ///
    /// To reword shipped translations in your application, register a decorator
    /// ITmLocalizer AFTER this call (it injects the built-in IStringLocalizer&lt;TmResources&gt;
    /// and overrides only the keys you list):
    /// <code>
    /// builder.Services.AddTempoBlazor();
    /// builder.Services.AddSingleton&lt;ITmLocalizer, MyCustomTmLocalizer&gt;();
    /// </code>
    ///
    /// To support e.g. Czech or Slovak localization, configure culture in Program.cs:
    /// <code>
    /// var culture = new System.Globalization.CultureInfo("cs");
    /// System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
    /// System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
    /// </code>
    /// </summary>
    public static IServiceCollection AddTempoBlazor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Localization: singleton, stateless, backed by embedded JSON (Resources/TmResources*.json).
        // The JSON IStringLocalizer resolves identically under Server and WebAssembly, unlike the
        // resx/ResourceManager pipeline (which returns raw keys under WASM). The closed-generic
        // registration takes precedence over AddLocalization's open-generic factory for TmResources.
        services.AddLocalization();
        services.TryAddSingleton<IStringLocalizer<TmResources>, JsonStringLocalizer<TmResources>>();
        services.TryAddSingleton<ITmLocalizer, DefaultTmLocalizer>();

        // The end user's time zone, resolved once per circuit (WASM: the runtime zone immediately;
        // Server: the browser's IANA id after the first interactive render — UTC until then). The
        // relative-time formatter below and the Scheduler/Gantt "today" read it. GetService, not
        // GetRequiredService: a scope with no IJSRuntime at all (a pure backend resolving the
        // formatter) must still resolve this service — with no browser to ask, the machine-local
        // zone IS the right zone, so TmUserTimeZone starts Local/resolved and never probes.
        services.TryAddScoped<ITmUserTimeZone>(sp => new TmUserTimeZone(sp.GetService<IJSRuntime>()));

        // Relative time ("just now", "5 min ago", "yesterday at 4:20 PM"): one formatter instead of a private
        // copy per component, resolved per scope. A singleton would capture the first ITmLocalizer (wrong
        // language/tenant, and a crash under ValidateScopes when that localizer is scoped) and the first
        // TimeProvider. The formatter's private clock is a TmUserTimeProvider: the time comes from the
        // host's TimeProvider when one is registered (the system clock otherwise; tests register a
        // FakeTimeProvider), while "yesterday" is judged in ITmUserTimeZone.Zone — the browser's zone
        // (UTC on Blazor Server until the probe reports, then the browser zone). A host with no
        // IJSRuntime at all gets the same shape over a Local/resolved zone service — the day boundary
        // equals the clock's own zone, never a fault from the missing interop. A host that wants the
        // 2.9.0 server-zone behaviour (or its own zone plumbing) registers a scoped
        // ITmRelativeTimeFormatter AFTER this call, or a scoped ITmUserTimeZone of its own (see
        // docs/localization-plurals.md). It must not register a scoped TimeProvider as the ZONE
        // source: that replaces the app-wide clock the host's singletons consume.
        services.TryAddScoped<ITmRelativeTimeFormatter>(sp =>
        {
            var localizer = sp.GetRequiredService<ITmLocalizer>();
            var clock = sp.GetService<TimeProvider>();
            // Always over TmUserTimeProvider: with no IJSRuntime the zone service above is
            // Local/resolved, so the day boundary equals the clock's own zone — the plain
            // formatter's behaviour — while a host-registered ITmUserTimeZone still wins.
            return new TmRelativeTimeFormatter(
                localizer,
                new TmUserTimeProvider(sp.GetRequiredService<ITmUserTimeZone>(), clock));
        });

        // ThemeService: scoped so each circuit/tab gets its own theme state.
        services.TryAddScoped<ThemeService>();

        // ToastService: scoped so each circuit/tab gets its own toast queue.
        services.TryAddScoped<ToastService>();

        // DragDropService carries dragged IDs between sibling components.
        services.TryAddScoped<DragDropService>();

        // Notification system.
        services.TryAddSingleton<ITmNotificationService, NoOpNotificationService>();

        return services;
    }

    /// <summary>
    /// Obsolete no-op alias. Since 2.9.1 <see cref="AddTempoBlazor"/> itself registers the relative-time
    /// formatter over a private <see cref="TimeProvider"/> whose <see cref="TimeProvider.LocalTimeZone"/>
    /// reads the scoped <see cref="ITmUserTimeZone"/> lazily — the browser's zone on Blazor Server
    /// (UTC until the probe reports after the first interactive render), the runtime's zone on
    /// WebAssembly. Calling this method changes nothing; it stays so existing hosts compile. The
    /// app-wide <see cref="TimeProvider"/> is untouched, and a host profile setting can still win via
    /// <c>ITmUserTimeZone.SetZone</c>. Hosts that want the 2.9.0 behaviour (days judged in the
    /// server's zone) register a scoped <see cref="ITmRelativeTimeFormatter"/> over the clock of their
    /// choice after <see cref="AddTempoBlazor"/> — see docs/localization-plurals.md.
    /// </summary>
    [Obsolete("AddTempoBlazor() opts the relative-time formatter into the user's time zone by default since 2.9.1; this call is a no-op kept for source compatibility and will be removed in a future release.")]
    public static IServiceCollection AddTempoUserTimeZone(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The default formatter already runs over TmUserTimeProvider. Deliberately empty: replacing
        // the formatter registration here would clobber a host's own registration made in between.
        return services;
    }

    /// <summary>
    /// Replaces the default <see cref="NoOpNotificationService"/> with
    /// <see cref="InMemoryNotificationStore"/> so notifications are kept in memory.
    ///
    /// Use this in demo / test applications where you want to see notifications
    /// without a real backend.
    /// </summary>
    public static IServiceCollection AddInMemoryNotifications(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<InMemoryNotificationStore>();
        services.AddSingleton<ITmNotificationService>(sp => sp.GetRequiredService<InMemoryNotificationStore>());
        return services;
    }
}
