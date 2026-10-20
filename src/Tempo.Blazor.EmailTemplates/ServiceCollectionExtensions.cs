using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Localization;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;
using Tempo.Blazor.EmailTemplates.Localization;
using Tempo.Blazor.EmailTemplates.Resources;

namespace Tempo.Blazor.EmailTemplates;

/// <summary>DI registration for the Tempo.Blazor email template editor UI.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the email template editor: the engine (model, generation, rendering, validation from
    /// <c>Tempo.Blazor.EmailTemplates.Abstractions</c>), localization and the UI localizer. Hosts may
    /// override <see cref="ITmEmailLocalizer"/> after this call.
    /// <para>
    /// Localization is self-contained: the editor resolves its strings from
    /// <c>Resources/TmEmailResources[.&lt;culture&gt;].json</c>, embedded in this package's assembly, through
    /// a CLOSED <c>IStringLocalizer&lt;TmEmailResources&gt;</c> registration (en/cs/sk/fr). This method
    /// therefore does NOT call <c>AddLocalization()</c> and registers no
    /// <see cref="IStringLocalizerFactory"/>: a closed registration wins over an open-generic factory in
    /// the container, so a host that replaces the whole localization pipeline (its own factory, its own
    /// <c>IStringLocalizer&lt;&gt;</c>, no resx lookup at all) still sees real text instead of raw keys.
    /// That is the failure mode this registration exists to prevent: a host that calls
    /// <c>RemoveAll&lt;IStringLocalizerFactory&gt;()</c> to install its own JSON localization would never
    /// find the editor's resx tables again.
    /// </para>
    /// </summary>
    public static IServiceCollection AddTempoEmailTemplates(
        this IServiceCollection services, Action<TemplateSecurityOptions>? configure = null)
    {
        // Closed-generic registration of the editor's own JSON tables. TryAdd so a host that already
        // registered IStringLocalizer<TmEmailResources> (a decorator over the shipped strings) keeps it.
        services.TryAddSingleton<IStringLocalizer<TmEmailResources>, JsonStringLocalizer<TmEmailResources>>();
        services.TryAddSingleton<ITmEmailLocalizer, DefaultTmEmailLocalizer>();
        services.AddTempoEmailTemplateEngine(configure);
        return services;
    }
}
