using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Registry;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests;

public class DependencyInjectionTests
{
    private static IServiceProvider BuildProvider(Action<TemplateSecurityOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddLocalization();
        services.AddTempoEmailTemplateEngine(configure);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddTempoEmailTemplateEngine_RegistersCoreServices()
    {
        using var scope = BuildProvider().CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetService<ITemplateEngine>().Should().NotBeNull();
        sp.GetService<MjmlGenerator>().Should().NotBeNull();
        sp.GetService<IMjmlCompiler>().Should().BeOfType<MjmlNetCompiler>();
        sp.GetService<TextVersionGenerator>().Should().NotBeNull();
        sp.GetService<IEmailTemplateRenderer>().Should().BeOfType<EmailTemplateRenderer>();
        sp.GetService<IBlockRegistry>().Should().BeOfType<BlockRegistry>();
    }

    [Fact]
    public void AddTempoEmailTemplateEngine_RegistersValidators()
    {
        using var scope = BuildProvider().CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetService<IValidator<CreateEmailTemplateRequest>>().Should().NotBeNull();
        sp.GetService<IValidator<UpdateEmailTemplateRequest>>().Should().NotBeNull();
        sp.GetService<IValidator<SendEmailRequest>>().Should().NotBeNull();
    }

    [Fact]
    public async Task ResolvedRenderer_CanRenderADocument()
    {
        using var scope = BuildProvider().CreateScope();
        var renderer = scope.ServiceProvider.GetRequiredService<IEmailTemplateRenderer>();

        var doc = new EmailTemplateDocument { Subject = "Hi {{ name }}" };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = "Hello {{ name }}" });
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var result = await renderer.RenderAsync(doc, new { Name = "World" });

        result.Success.Should().BeTrue();
        result.Subject.Should().Be("Hi World");
        result.Html.Should().Contain("Hello World");
    }

    [Fact]
    public void PlainEngineAndTheDiEngine_SanitizeTrustedHtmlIdentically()
    {
        // The Engine's own default must be the authoritative AngleSharp sanitizer, exactly what
        // AddTempoEmailTemplateEngine() registers (it was the conservative BCL one, so a plain
        // `new ScribanTemplateEngine()` silently produced different markup than the DI registration —
        // an "unchecked-looking" difference between two hosts of the same package).
        using var scope = BuildProvider().CreateScope();
        var di = scope.ServiceProvider.GetRequiredService<ITemplateEngine>();
        var plain = new ScribanTemplateEngine();
        var trusted = new { X = "<div>a</div>" };

        var plainOutput = plain.Render("{{ x | html.trusted }}", trusted).Value;
        var diOutput = di.Render("{{ x | html.trusted }}", trusted).Value;

        diOutput.Should().Be("a", "AngleSharp unwraps the unknown <div> and keeps its text");
        plainOutput.Should().Be(diOutput);
    }

    [Fact]
    public void Configure_AppliesCustomSecurityOptions()
    {
        using var scope = BuildProvider(o => o.LoopLimit = 7).CreateScope();
        scope.ServiceProvider.GetRequiredService<TemplateSecurityOptions>().LoopLimit.Should().Be(7);
    }
}
