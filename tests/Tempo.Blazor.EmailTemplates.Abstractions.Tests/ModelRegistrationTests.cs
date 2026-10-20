using FluentValidation;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Localization;
using Tempo.Blazor.EmailTemplates.Abstractions.Registry;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Resources;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests;

/// <summary>
/// <c>AddTempoEmailTemplateModel</c> is the baseline registration the editor gets without the Engine
/// package (§3.0/§3.5): model, registry, validators, the conservative sanitizer, the regex variable
/// extractor, one shared <see cref="TemplateSecurityOptions"/> instance and the closed JSON localizer
/// of the validators. It is idempotent and must never pull an engine type into the container.
/// </summary>
public class ModelRegistrationTests
{
    private static IServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        return services;
    }

    private static ServiceProvider Build(Action<TemplateSecurityOptions>? configure = null)
        => NewServices().AddTempoEmailTemplateModel(configure).BuildServiceProvider();

    [Fact]
    public void RegistersTheBaselineImplementations()
    {
        using var provider = Build();

        provider.GetRequiredService<IHtmlContentSanitizer>().Should().BeOfType<ConservativeHtmlContentSanitizer>();
        provider.GetRequiredService<ITemplateVariableExtractor>().Should().BeOfType<RegexTemplateVariableExtractor>();
        provider.GetRequiredService<MjmlGenerator>().Should().NotBeNull();
        provider.GetRequiredService<TextVersionGenerator>().Should().NotBeNull();
        provider.GetRequiredService<IBlockRegistry>().Should().BeOfType<BlockRegistry>();
        provider.GetRequiredService<IStringLocalizer<EmailTemplateValidationResources>>().Should()
            .BeOfType<JsonStringLocalizer<EmailTemplateValidationResources>>();
    }

    [Fact]
    public void RegistersTheRequestValidators()
    {
        using var provider = Build();

        provider.GetService<IValidator<CreateEmailTemplateRequest>>().Should().NotBeNull();
        provider.GetService<IValidator<UpdateEmailTemplateRequest>>().Should().NotBeNull();
        provider.GetService<IValidator<SendEmailRequest>>().Should().NotBeNull();
    }

    [Fact]
    public void DoesNotRegisterAnyEngineType()
    {
        using var provider = Build();

        provider.GetService<IMjmlCompiler>().Should().BeNull("the MJML compiler is an Engine type");
        provider.GetService<ITemplateEngine>().Should().BeNull("the Scriban engine lives in the Engine package");
        provider.GetService<ITemplateValidator>().Should().BeNull("no baseline syntax validator exists (graceful degradation)");
        provider.GetService<IEmailTemplateRenderer>().Should().BeNull();
        provider.GetService<IEmailPreviewRenderer>().Should().BeNull();
    }

    [Fact]
    public void IsIdempotent_AndSharesOneSecurityOptionsInstance()
    {
        var services = NewServices()
            .AddTempoEmailTemplateModel()
            .AddTempoEmailTemplateModel();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<TemplateSecurityOptions>();
        var second = provider.GetRequiredService<TemplateSecurityOptions>();

        first.Should().BeSameAs(second);
        services.Count(d => d.ServiceType == typeof(TemplateSecurityOptions)).Should().Be(1,
            "one factory descriptor over one shared instance");
        services.Count(d => d.ServiceType == typeof(IHtmlContentSanitizer)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(MjmlGenerator)).Should().Be(1);
    }

    [Fact]
    public void Configure_IsAppliedToTheOneSharedInstance_EvenWhenCalledTwice()
    {
        var services = NewServices()
            .AddTempoEmailTemplateModel(o => o.LoopLimit = 7)
            .AddTempoEmailTemplateModel(o => o.RecursiveLimit = 3);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<TemplateSecurityOptions>();

        options.LoopLimit.Should().Be(7);
        options.RecursiveLimit.Should().Be(3);
    }

    [Fact]
    public void KeepsAHostProvidedSanitizerRegistration()
    {
        var services = NewServices();
        services.AddSingleton<IHtmlContentSanitizer>(Substitute.For<IHtmlContentSanitizer>());
        services.AddTempoEmailTemplateModel();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHtmlContentSanitizer>().Should().NotBeOfType<ConservativeHtmlContentSanitizer>();
    }

    [Fact]
    public void KeepsAHostProvidedLocalizer()
    {
        var hostLocalizer = Substitute.For<IStringLocalizer<EmailTemplateValidationResources>>();
        var services = NewServices();
        services.AddSingleton<IStringLocalizer<EmailTemplateValidationResources>>(hostLocalizer);
        services.AddTempoEmailTemplateModel();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStringLocalizer<EmailTemplateValidationResources>>().Should().BeSameAs(hostLocalizer);
    }

    [Fact]
    public void InvalidLimits_ThrowAtRegistrationTime()
    {
        var act = () => NewServices().AddTempoEmailTemplateModel(o => o.LoopLimit = 0);

        act.Should().Throw<ArgumentOutOfRangeException>("a fail-open configuration must not reach a render");
    }

    [Fact]
    public void ValidatorMessages_AreLocalizedWithoutAnyHostLocalizationPipeline()
    {
        var services = NewServices();
        services.AddTempoEmailTemplateModel();

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IValidator<CreateEmailTemplateRequest>>();

        var result = validator.Validate(new CreateEmailTemplateRequest { Name = string.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().OnlyContain(e => !e.ErrorMessage.StartsWith("validation.", StringComparison.Ordinal));
    }
}
