using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Engine.Tests;

/// <summary>
/// The registration split of §3.0/§3.5 (decision D11): <c>AddTempoEmailTemplateModel</c> is the baseline,
/// <c>AddTempoEmailTemplateEngine</c> REPLACES the engine services on top of it, and the result must not
/// depend on the order in which a host calls them.
/// </summary>
public class RegistrationSplitTests
{
    private static IServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        return services;
    }

    [Fact]
    public void ModelThenEngine_ReplacesEveryBaselineWithTheEngineImplementation()
    {
        var services = NewServices()
            .AddTempoEmailTemplateModel()
            .AddTempoEmailTemplateEngine();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IHtmlContentSanitizer>().Should().BeOfType<AngleSharpHtmlContentSanitizer>();
        provider.GetRequiredService<ITemplateVariableExtractor>().Should().BeOfType<ScribanTemplateVariableExtractor>();
        provider.GetRequiredService<ITemplateValidator>().Should().BeOfType<ScribanTemplateValidator>();
        provider.GetRequiredService<IMjmlCompiler>().Should().BeOfType<MjmlNetCompiler>();
        provider.GetRequiredService<IEmailTemplateRenderer>().Should().BeOfType<EmailTemplateRenderer>();
        provider.GetRequiredService<IEmailPreviewRenderer>().Should().BeOfType<LocalEmailPreviewRenderer>();
        provider.GetRequiredService<ITemplateEngine>().Should().BeOfType<ScribanTemplateEngine>();
    }

    [Fact]
    public void EngineThenModel_ProducesTheSameRegistrations()
    {
        var services = NewServices()
            .AddTempoEmailTemplateEngine()
            .AddTempoEmailTemplateModel();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IHtmlContentSanitizer>().Should().BeOfType<AngleSharpHtmlContentSanitizer>(
            "Replace + TryAdd must make the result independent of the call order");
        provider.GetRequiredService<ITemplateVariableExtractor>().Should().BeOfType<ScribanTemplateVariableExtractor>();
        provider.GetRequiredService<ITemplateValidator>().Should().BeOfType<ScribanTemplateValidator>();
        provider.GetRequiredService<IEmailPreviewRenderer>().Should().BeOfType<LocalEmailPreviewRenderer>();
        provider.GetRequiredService<MjmlGenerator>().Should().NotBeNull();
    }

    [Fact]
    public void BothOrders_ShareTheSameSecurityOptionsInstance()
    {
        var first = NewServices().AddTempoEmailTemplateModel(o => o.LoopLimit = 11).AddTempoEmailTemplateEngine();
        var second = NewServices().AddTempoEmailTemplateEngine().AddTempoEmailTemplateModel(o => o.LoopLimit = 11);

        using var firstProvider = first.BuildServiceProvider();
        using var secondProvider = second.BuildServiceProvider();

        var firstOptions = firstProvider.GetRequiredService<TemplateSecurityOptions>();
        var secondOptions = secondProvider.GetRequiredService<TemplateSecurityOptions>();

        firstOptions.LoopLimit.Should().Be(11);
        secondOptions.LoopLimit.Should().Be(11);
        firstProvider.GetRequiredService<TemplateSecurityOptions>().Should().BeSameAs(firstOptions,
            "one shared instance, whatever the order");
        secondProvider.GetRequiredService<TemplateSecurityOptions>().Should().BeSameAs(secondOptions);
    }

    [Fact]
    public void Engine_CalledTwice_IsIdempotent()
    {
        var services = NewServices()
            .AddTempoEmailTemplateEngine()
            .AddTempoEmailTemplateEngine();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IHtmlContentSanitizer>().Should().BeOfType<AngleSharpHtmlContentSanitizer>();
        services.Count(d => d.ServiceType == typeof(IEmailPreviewRenderer)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(ITemplateEngine)).Should().Be(1);
    }

    [Fact]
    public void ModelOnly_HasNoEngineService()
    {
        using var provider = NewServices().AddTempoEmailTemplateModel().BuildServiceProvider();

        provider.GetService<ITemplateEngine>().Should().BeNull();
        provider.GetService<IMjmlCompiler>().Should().BeNull();
        provider.GetService<ITemplateValidator>().Should().BeNull();
        provider.GetService<IEmailPreviewRenderer>().Should().BeNull();
        provider.GetRequiredService<IHtmlContentSanitizer>().Should().BeOfType<ConservativeHtmlContentSanitizer>();
        provider.GetRequiredService<ITemplateVariableExtractor>().Should().BeOfType<RegexTemplateVariableExtractor>();
    }

    [Fact]
    public void Engine_KeepsAHostSuppliedSanitizerReplacement()
    {
        var hostSanitizer = Substitute.For<IHtmlContentSanitizer>();
        var services = NewServices();
        services.AddTempoEmailTemplateEngine();
        services.Replace(ServiceDescriptor.Singleton(hostSanitizer));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IHtmlContentSanitizer>().Should().BeSameAs(hostSanitizer,
            "a host registration made after the engine wins (last registration wins)");
    }

    [Fact]
    public async Task ResolvedPreviewRenderer_RendersThroughTheEngine()
    {
        using var provider = NewServices().AddTempoEmailTemplateEngine().BuildServiceProvider();
        var renderer = provider.GetRequiredService<IEmailPreviewRenderer>();

        var request = new EmailPreviewRequest
        {
            Document = Document("Hi {{ name }}", "Hello {{ name }}"),
            VariablesJson = """{"name":"World"}""",
        };

        var result = await renderer.RenderAsync(request);

        result.Status.Should().Be(EmailPreviewStatus.Rendered);
        result.Subject.Should().Be("Hi World");
        result.Html.Should().Contain("Hello World");
        result.Text.Should().Contain("Hello World");
    }

    [Fact]
    public void Validators_AreScopedAndTranslatedWithoutHostLocalization()
    {
        using var provider = NewServices().AddTempoEmailTemplateEngine().BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IStringLocalizer<
            Abstractions.Resources.EmailTemplateValidationResources>>().Should().NotBeNull();
        scope.ServiceProvider.GetService<FluentValidation.IValidator<Abstractions.Dtos.CreateEmailTemplateRequest>>()
            .Should().NotBeNull();
    }

    internal static Abstractions.Model.EmailTemplateDocument Document(string subject, string text)
    {
        var document = new Abstractions.Model.EmailTemplateDocument { Subject = subject };
        var column = new Abstractions.Model.EmailColumn();
        column.Blocks.Add(new Abstractions.Model.Blocks.EmailTextBlock { Content = text });
        var section = new Abstractions.Model.EmailSection();
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }
}
