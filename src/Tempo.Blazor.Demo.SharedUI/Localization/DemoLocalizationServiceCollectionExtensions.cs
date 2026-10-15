using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Resources;

namespace Tempo.Blazor.Demo.SharedUI.Localization;

/// <summary>
/// Registration for the demo-owned localization tables.
/// </summary>
public static class DemoLocalizationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="DemoTmLocalizer"/> as the resolved <see cref="ITmLocalizer"/>:
    /// demo-only keys come from this assembly's embedded <c>DemoResources</c> tables, every
    /// library key falls through to the built-in <c>TmResources</c>. Call it AFTER
    /// <c>AddTempoBlazor()</c> (it consumes the <c>IStringLocalizer&lt;TmResources&gt;</c> that
    /// call registers) in every demo host — both sides of InteractiveAuto included, so server
    /// and WebAssembly renderings resolve the same demo strings.
    /// </summary>
    public static IServiceCollection AddTempoDemoLocalizer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddSingleton<ITmLocalizer>(sp =>
            new DemoTmLocalizer(sp.GetRequiredService<IStringLocalizer<TmResources>>()));
    }
}
