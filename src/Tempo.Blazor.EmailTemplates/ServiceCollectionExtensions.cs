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
    /// Registers the email template editor — UI plus the model side (block registry, generators, request
    /// validators, variable extraction, HTML sanitizer) — and NOT the local render engine (§3.0/D11).
    /// <para>
    /// The host supplies the two cross-process seams itself:
    /// <see cref="Abstractions.Rendering.IEmailPreviewRenderer"/> for the preview and the HTML export (the
    /// typical WASM host posts it to its server) and, optionally,
    /// <see cref="ITemplateValidator"/> for the syntax check. When the process genuinely renders locally,
    /// add the <c>Tempo.Blazor.EmailTemplates.Engine</c> package and call
    /// <c>AddTempoEmailTemplateEngine()</c> — the order of the two calls does not matter.
    /// </para>
    /// <para>
    /// Localization is self-contained: the editor resolves its strings from
    /// <c>Resources/TmEmailResources[.&lt;culture&gt;].json</c>, embedded in this package's assembly, through
    /// a CLOSED <c>IStringLocalizer&lt;TmEmailResources&gt;</c> registration (en/cs/sk/fr). This method
    /// therefore does NOT call <c>AddLocalization()</c> and registers no
    /// <see cref="IStringLocalizerFactory"/>: a closed registration wins over an open-generic factory in
    /// the container, so a host that replaces the whole localization pipeline still sees real text instead
    /// of raw keys.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional editor options (the shared sandbox limits).</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A configured <see cref="TemplateSecurityOptions"/> limit is zero or negative. Thrown immediately.
    /// </exception>
    public static IServiceCollection AddTempoEmailTemplateEditor(
        this IServiceCollection services, Action<EmailTemplateEditorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new EmailTemplateEditorOptions();
        configure?.Invoke(options);

        // Closed-generic registration of the editor's own JSON tables. TryAdd so a host that already
        // registered IStringLocalizer<TmEmailResources> (a decorator over the shipped strings) keeps it.
        services.TryAddSingleton<IStringLocalizer<TmEmailResources>, JsonStringLocalizer<TmEmailResources>>();
        services.TryAddSingleton<ITmEmailLocalizer, DefaultTmEmailLocalizer>();

        // The model side, without any engine: conservative sanitizer, regex variable extractor, validators
        // with their own closed JSON localizer, and the one shared TemplateSecurityOptions instance.
        services.AddTempoEmailTemplateModel(options.ApplyTo);

        return services;
    }

    /// <summary>
    /// Registers the editor through the pre-2.10 shortcut. The engine is NOT registered any more: the UI
    /// package no longer references Scriban/Mjml.Net/AngleSharp, so a host that wants a local render must
    /// add the <c>Tempo.Blazor.EmailTemplates.Engine</c> package and call <c>AddTempoEmailTemplateEngine()</c>.
    /// Without a registered <see cref="Abstractions.Rendering.IEmailPreviewRenderer"/> the preview shows an
    /// <c>Unavailable</c> state that names the missing registration (never an empty iframe).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration of the shared sandbox limits.</param>
    [Obsolete("Use AddTempoEmailTemplateEditor() and, for local rendering, AddTempoEmailTemplateEngine() " +
              "from Tempo.Blazor.EmailTemplates.Engine")]
    public static IServiceCollection AddTempoEmailTemplates(
        this IServiceCollection services, Action<TemplateSecurityOptions>? configure = null)
        => services.AddTempoEmailTemplateEditor(o => configure?.Invoke(o.Security));
}
