using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions;

/// <summary>
/// DI registration of the local render engine (§3.0/§3.5, decision D11). This type lives in the
/// <c>Tempo.Blazor.EmailTemplates.Engine</c> assembly but keeps the
/// <c>Tempo.Blazor.EmailTemplates.Abstractions</c> namespace on purpose (D21): adding the package to a
/// host is a <c>PackageReference</c> plus one call, never a <c>using</c> rewrite.
/// </summary>
public static class EmailTemplatesEngineServiceCollectionExtensions
{
    /// <summary>
    /// Registers the local render engine — the sandboxed Scriban template engine, the Mjml.Net compiler,
    /// the AngleSharp HTML sanitizer, the render pipeline and the local preview renderer — on top of
    /// <c>AddTempoEmailTemplateModel()</c>.
    /// <para>
    /// Every engine service REPLACES the baseline the model registration installs
    /// (<see cref="IHtmlContentSanitizer"/>, <see cref="ITemplateVariableExtractor"/>,
    /// <see cref="ITemplateValidator"/>, <see cref="IMjmlCompiler"/>, <see cref="IEmailTemplateRenderer"/>,
    /// <see cref="IEmailPreviewRenderer"/>), so the result does not depend on the order in which the two
    /// registrations are called: <c>Model() → Engine()</c> and <c>Engine() → Model()</c> both end with
    /// <c>AngleSharpHtmlContentSanitizer</c> and the same <see cref="TemplateSecurityOptions"/> instance.
    /// </para>
    /// <para>
    /// Any <see cref="ITemplateSecurityAuditSink"/> registered in the container is wired into the engine
    /// automatically, and every registered <see cref="ITemplateFunctionProvider"/> is validated and
    /// exposed under its namespace. Providers must be singletons (the engine snapshots them once).
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration of the shared sandbox options.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A configured <see cref="TemplateSecurityOptions"/> limit is zero or negative. Thrown immediately.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A registered <see cref="ITemplateFunctionProvider"/> cannot be exposed (invalid or keyword
    /// namespace, a namespace shadowing a builtin or another provider, an async delegate or an
    /// unsupported delegate signature). Thrown when <see cref="ITemplateEngine"/> is resolved.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// An <see cref="ITemplateFunctionProvider"/> is registered with a non-singleton lifetime. Thrown when
    /// <see cref="ITemplateEngine"/> is resolved.
    /// </exception>
    public static IServiceCollection AddTempoEmailTemplateEngine(
        this IServiceCollection services, Action<TemplateSecurityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Baseline first (idempotent): sanitizer, extractor, shared security options, validators, the
        // validators' closed JSON localizer and the MJML/text generators.
        services.AddTempoEmailTemplateModel(configure);

        services.Replace(ServiceDescriptor.Singleton<IHtmlContentSanitizer, AngleSharpHtmlContentSanitizer>());
        services.Replace(ServiceDescriptor.Singleton<ITemplateVariableExtractor, ScribanTemplateVariableExtractor>());
        services.Replace(ServiceDescriptor.Singleton<ITemplateValidator, ScribanTemplateValidator>());
        services.Replace(ServiceDescriptor.Singleton<IMjmlCompiler, MjmlNetCompiler>());

        services.TryAddSingleton<TemplateFunctionRegistry>(provider =>
        {
            RejectNonSingletonFunctionProviders(services);
            var logger = provider.GetService<ILogger<ScribanTemplateEngine>>();
            return TemplateFunctionRegistry.Create(
                provider.GetServices<ITemplateFunctionProvider>(),
                logger is null
                    ? null
                    : (name, error) => logger.LogWarning(error, "Template function {Function} failed.", name));
        });

        // The model registration (Abstractions) enriches the shared TemplateSecurityOptions with the
        // provider namespaces through this internal alias, so the options are complete as soon as they are
        // resolved — without the model taking a dependency on the Engine package.
        services.TryAddSingleton<ITemplateFunctionNamespaceSource>(
            provider => provider.GetRequiredService<TemplateFunctionRegistry>());

        services.TryAddSingleton<ITemplateEngine>(provider =>
        {
            // The host-visible options already carry the provider namespaces (the model registration
            // enriches the shared instance from the registry), and the engine keeps its own clone, so a
            // later mutation of the shared instance cannot weaken a render.
            var options = provider.GetRequiredService<TemplateSecurityOptions>();
            var registry = provider.GetRequiredService<TemplateFunctionRegistry>();

            return new ScribanTemplateEngine(
                options,
                provider.GetServices<ITemplateSecurityAuditSink>(),
                provider.GetService<TimeProvider>(),
                registry,
                provider.GetRequiredService<IHtmlContentSanitizer>(),
                provider.GetService<ILogger<ScribanTemplateEngine>>());
        });

        services.Replace(ServiceDescriptor.Singleton<IEmailTemplateRenderer>(provider => new EmailTemplateRenderer(
            provider.GetRequiredService<ITemplateEngine>(),
            provider.GetRequiredService<MjmlGenerator>(),
            provider.GetRequiredService<IMjmlCompiler>(),
            provider.GetRequiredService<TextVersionGenerator>(),
            provider.GetServices<ITemplateSecurityAuditSink>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<ILogger<EmailTemplateRenderer>>())));

        services.Replace(ServiceDescriptor.Singleton<IEmailPreviewRenderer>(provider => new LocalEmailPreviewRenderer(
            provider.GetRequiredService<IEmailTemplateRenderer>(),
            provider.GetService<ILogger<LocalEmailPreviewRenderer>>())));

        return services;
    }

    /// <summary>
    /// The registry is built once and shared by every render, so a scoped/transient provider would be
    /// captured from the root scope and would silently never see the scoped instance again.
    /// </summary>
    private static void RejectNonSingletonFunctionProviders(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType != typeof(ITemplateFunctionProvider) ||
                descriptor.Lifetime == ServiceLifetime.Singleton)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"ITemplateFunctionProvider must be registered as a singleton: " +
                $"'{descriptor.ImplementationType?.FullName ?? descriptor.ImplementationInstance?.GetType().FullName ?? "<factory>"}' " +
                $"is registered as {descriptor.Lifetime}, but the engine snapshots its functions once. " +
                "Use services.AddSingleton<ITemplateFunctionProvider, MyProvider>().");
        }
    }
}
