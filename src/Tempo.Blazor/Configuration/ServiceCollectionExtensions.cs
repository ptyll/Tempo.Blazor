using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
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
    ///   <item><description><see cref="ITmLocalizer"/>: Singleton (stateless, thread-safe, backed by embedded JSON)</description></item>
    ///   <item><description><see cref="ITmRelativeTimeFormatter"/>: Scoped relative-time formatter. It resolves <see cref="ITmLocalizer"/> and <see cref="TimeProvider"/> from the current scope, so a scoped localizer (language, tenant) is not captured once for every circuit. The default clock is <see cref="TimeProvider.System"/>, whose <see cref="TimeProvider.LocalTimeZone"/> is the server's zone — wrong for a Blazor Server circuit. Give the formatter its own user clock by replacing this registration with a scoped <see cref="ITmRelativeTimeFormatter"/> over a <see cref="TimeProvider"/> that reads the circuit's zone lazily (IANA id from the browser once per circuit, <c>TimeZoneInfo.FindSystemTimeZoneById</c> with a fallback to UTC, re-render once it arrives). Never replace the app-wide <see cref="TimeProvider"/>: Kestrel and cookie authentication are singletons that consume it, so a scoped replacement fails the host under <c>ValidateScopes</c>. No component takes a time-zone parameter for this.</description></item>
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
    /// To override the built-in localization with your own strings, register
    /// a custom ITmLocalizer AFTER this call:
    /// <code>
    /// builder.Services.AddTempoBlazor();
    /// builder.Services.AddSingleton&lt;ITmLocalizer, MyCustomTmLocalizer&gt;();
    /// </code>
    ///
    /// To support Czech localization, configure culture in Program.cs:
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

        // Relative time ("just now", "5 min ago", "yesterday at 4:20 PM"): one formatter instead of a private
        // copy per component, resolved per scope. A singleton would capture the first ITmLocalizer (wrong
        // language/tenant, and a crash under ValidateScopes when that localizer is scoped) and the first
        // TimeProvider. The host's TimeProvider, when one is registered, supplies the clock and the zone
        // "yesterday" is judged in; tests register a FakeTimeProvider. A Blazor Server host that wants the
        // browser's zone replaces THIS registration with a scoped formatter over its own user TimeProvider
        // (see docs/localization-plurals.md). It must not register a scoped TimeProvider: that replaces the
        // app-wide clock the host's singletons consume.
        services.TryAddScoped<ITmRelativeTimeFormatter>(sp =>
            new TmRelativeTimeFormatter(sp.GetRequiredService<ITmLocalizer>(), sp.GetService<TimeProvider>()));

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
