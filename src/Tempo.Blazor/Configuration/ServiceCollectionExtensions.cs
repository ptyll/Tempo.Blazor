using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Services;

namespace Tempo.Blazor.Configuration;

/// <summary>
/// Extension methods for registering Tempo.Blazor services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all required Tempo.Blazor services.
    /// <list type="bullet">
    ///   <item><description><see cref="ITmLocalizer"/>: Singleton (stateless, thread-safe, backed by .resx)</description></item>
    ///   <item><description><see cref="ThemeService"/>: Scoped (per-circuit in Server mode, per-tab in WASM)</description></item>
    ///   <item><description><see cref="ToastService"/>: Scoped (per-circuit in Server mode, per-tab in WASM)</description></item>
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
        // Localization — Singleton (stateless, thread-safe, backed by .resx)
        services.AddLocalization();
        services.TryAddSingleton<ITmLocalizer, DefaultTmLocalizer>();

        // ThemeService — Scoped (each circuit/tab gets its own theme state)
        services.TryAddScoped<ThemeService>();

        // ToastService — Scoped (each circuit/tab gets its own toast queue)
        services.TryAddScoped<ToastService>();

        return services;
    }
}
