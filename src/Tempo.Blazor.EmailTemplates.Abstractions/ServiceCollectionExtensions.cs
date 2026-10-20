using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Registry;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;
using Tempo.Blazor.EmailTemplates.Abstractions.Validation;

namespace Tempo.Blazor.EmailTemplates.Abstractions;

/// <summary>DI registration for the email template engine (model, generation, rendering, validation).</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the email template engine: template engine, MJML generator/compiler, renderer,
    /// text generator, block registry and request validators. Localization
    /// (<c>IStringLocalizer</c>) must be registered separately by the host (e.g. <c>AddLocalization</c>).
    /// Any <see cref="ITemplateSecurityAuditSink"/> registered in the container is wired into the
    /// template engine automatically, and every registered <see cref="ITemplateFunctionProvider"/> is
    /// validated and exposed under its namespace. Providers must be registered as
    /// <see cref="ServiceLifetime.Singleton"/> (the engine snapshots them once) and their namespaces are
    /// also visible on <see cref="TemplateSecurityOptions.FunctionNamespaces"/>.
    /// </summary>
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
        // Eager validation: a fail-open configuration must not wait for the first resolve.
        var configured = new TemplateSecurityOptions();
        configure?.Invoke(configured);
        configured.Validate();

        services.AddSingleton<TemplateFunctionRegistry>(provider =>
        {
            RejectNonSingletonFunctionProviders(services);
            var logger = provider.GetService<ILogger<ScribanTemplateEngine>>();
            return TemplateFunctionRegistry.Create(
                provider.GetServices<ITemplateFunctionProvider>(),
                logger is null
                    ? null
                    : (name, error) => logger.LogWarning(error, "Template function {Function} failed.", name));
        });

        // The host-visible options are a snapshot of the configured instance enriched with the registered
        // function namespaces. They are built when the options are resolved (not lazily mutated while the
        // engine is being created), so a host can read FunctionNamespaces without resolving the engine.
        services.AddSingleton(provider =>
        {
            var options = configured.Clone();
            foreach (var @namespace in provider.GetRequiredService<TemplateFunctionRegistry>().NamespaceNames)
            {
                options.FunctionNamespaces.Add(@namespace);
            }

            return options;
        });

        services.AddSingleton<ITemplateEngine>(provider => new ScribanTemplateEngine(
            provider.GetRequiredService<TemplateSecurityOptions>(),
            provider.GetServices<ITemplateSecurityAuditSink>(),
            provider.GetService<TimeProvider>(),
            provider.GetRequiredService<TemplateFunctionRegistry>(),
            provider.GetService<ILogger<ScribanTemplateEngine>>()));
        services.AddSingleton<MjmlGenerator>();
        services.AddSingleton<IMjmlCompiler, MjmlNetCompiler>();
        services.AddSingleton<TextVersionGenerator>();
        services.AddSingleton<IEmailTemplateRenderer>(provider => new EmailTemplateRenderer(
            provider.GetRequiredService<ITemplateEngine>(),
            provider.GetRequiredService<MjmlGenerator>(),
            provider.GetRequiredService<IMjmlCompiler>(),
            provider.GetRequiredService<TextVersionGenerator>(),
            provider.GetServices<ITemplateSecurityAuditSink>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<ILogger<EmailTemplateRenderer>>()));
        services.AddSingleton<IBlockRegistry, BlockRegistry>();

        services.AddScoped<IValidator<CreateEmailTemplateRequest>, CreateEmailTemplateRequestValidator>();
        services.AddScoped<IValidator<UpdateEmailTemplateRequest>, UpdateEmailTemplateRequestValidator>();
        services.AddScoped<IValidator<SendEmailRequest>, SendEmailRequestValidator>();

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
