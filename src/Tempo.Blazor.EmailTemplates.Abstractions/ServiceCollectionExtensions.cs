using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Localization;
using Tempo.Blazor.EmailTemplates.Abstractions.Registry;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Resources;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;
using Tempo.Blazor.EmailTemplates.Abstractions.Validation;

namespace Tempo.Blazor.EmailTemplates.Abstractions;

/// <summary>
/// DI registration of the email template model — everything the editor needs that has no Scriban,
/// Mjml.Net or AngleSharp dependency (§3.0, decision D11).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the model side of the email template editor: block registry, MJML and plain-text
    /// generators, the request validators with their closed JSON localizer, the conservative BCL HTML
    /// sanitizer, the regex variable extractor and the one shared
    /// <see cref="TemplateSecurityOptions"/> instance.
    /// <para>
    /// This registration is <b>idempotent</b> (every service is added with <c>TryAdd</c>, and the
    /// security options instance is created once and reused), so
    /// <c>AddTempoEmailTemplateModel()</c>, <c>AddTempoEmailTemplateEngine()</c> and
    /// <c>AddTempoEmailTemplateEditor()</c> can be combined in any order. <paramref name="configure"/>
    /// always applies to that one instance.
    /// </para>
    /// <para>
    /// It never registers an engine type: no <c>ITemplateEngine</c>, no <c>IMjmlCompiler</c>, no
    /// <c>ITemplateValidator</c>, no <c>IEmailTemplateRenderer</c> and no
    /// <see cref="IEmailPreviewRenderer"/>. Add <c>Tempo.Blazor.EmailTemplates.Engine</c> (and call
    /// <c>AddTempoEmailTemplateEngine()</c>) for the local Scriban/MJML pipeline, or supply a host
    /// <see cref="IEmailPreviewRenderer"/> / <see cref="ITemplateValidator"/> yourself.
    /// </para>
    /// <para>
    /// The validators' messages come from a CLOSED <c>IStringLocalizer&lt;EmailTemplateValidationResources&gt;</c>
    /// over embedded JSON (en/cs/sk/fr), so the host needs neither <c>AddLocalization()</c> nor a working
    /// <see cref="IStringLocalizerFactory"/> — they are translated here, next to the validators, not in
    /// the Engine package.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration of the shared sandbox options.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A configured <see cref="TemplateSecurityOptions"/> limit is zero or negative. Thrown immediately, so
    /// a fail-open configuration cannot wait for the first resolve.
    /// </exception>
    public static IServiceCollection AddTempoEmailTemplateModel(
        this IServiceCollection services, Action<TemplateSecurityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = SharedSecurityOptions(services);
        configure?.Invoke(options);
        options.Validate();

        services.TryAddSingleton<IStringLocalizer<EmailTemplateValidationResources>,
            JsonStringLocalizer<EmailTemplateValidationResources>>();
        services.TryAddSingleton<IHtmlContentSanitizer>(ConservativeHtmlContentSanitizer.Instance);
        services.TryAddSingleton<ITemplateVariableExtractor>(RegexTemplateVariableExtractor.Instance);
        services.TryAddSingleton(provider => new MjmlGenerator(provider.GetRequiredService<IHtmlContentSanitizer>()));
        services.TryAddSingleton<TextVersionGenerator>();
        services.TryAddSingleton<IBlockRegistry, BlockRegistry>();

        services.TryAddScoped<IValidator<CreateEmailTemplateRequest>, CreateEmailTemplateRequestValidator>();
        services.TryAddScoped<IValidator<UpdateEmailTemplateRequest>, UpdateEmailTemplateRequestValidator>();
        services.TryAddScoped<IValidator<SendEmailRequest>, SendEmailRequestValidator>();

        return services;
    }

    /// <summary>
    /// Returns the one <see cref="TemplateSecurityOptions"/> instance of this service collection,
    /// creating and registering it on first use. The service is resolved through a factory so the options
    /// carry the function namespaces of the registered <see cref="ITemplateFunctionProvider"/>s — the
    /// model does not know the engine package, so it reads them through the optional internal
    /// <see cref="ITemplateFunctionNamespaceSource"/> the registry implements. With the engine absent there
    /// is no source and nothing to add.
    /// </summary>
    private static TemplateSecurityOptions SharedSecurityOptions(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(TemplateSecurityOptionsHolder) &&
                descriptor.ImplementationInstance is TemplateSecurityOptionsHolder existing)
            {
                return existing.Options;
            }
        }

        var created = new TemplateSecurityOptions();
        services.AddSingleton(new TemplateSecurityOptionsHolder(created));
        services.AddSingleton(provider =>
        {
            foreach (var source in provider.GetServices<ITemplateFunctionNamespaceSource>())
            {
                foreach (var @namespace in source.NamespaceNames)
                {
                    created.FunctionNamespaces.Add(@namespace);
                }
            }

            return created;
        });

        return created;
    }
}
