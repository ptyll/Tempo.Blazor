using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
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
    /// template engine automatically.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A configured <see cref="TemplateSecurityOptions"/> limit is zero or negative.
    /// </exception>
    public static IServiceCollection AddTempoEmailTemplateEngine(
        this IServiceCollection services, Action<TemplateSecurityOptions>? configure = null)
    {
        var options = new TemplateSecurityOptions();
        configure?.Invoke(options);
        options.Validate();
        services.AddSingleton(options);

        services.AddSingleton<ITemplateEngine>(provider => new ScribanTemplateEngine(
            provider.GetRequiredService<TemplateSecurityOptions>(),
            provider.GetServices<ITemplateSecurityAuditSink>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<ILogger<ScribanTemplateEngine>>()));
        services.AddSingleton<MjmlGenerator>();
        services.AddSingleton<IMjmlCompiler, MjmlNetCompiler>();
        services.AddSingleton<TextVersionGenerator>();
        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();
        services.AddSingleton<IBlockRegistry, BlockRegistry>();

        services.AddScoped<IValidator<CreateEmailTemplateRequest>, CreateEmailTemplateRequestValidator>();
        services.AddScoped<IValidator<UpdateEmailTemplateRequest>, UpdateEmailTemplateRequestValidator>();
        services.AddScoped<IValidator<SendEmailRequest>, SendEmailRequestValidator>();

        return services;
    }
}
