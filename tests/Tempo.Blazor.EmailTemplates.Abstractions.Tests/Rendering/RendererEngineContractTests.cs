using Microsoft.Extensions.Logging;
using NSubstitute;
using Scriban;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// Review fixes 5 and U2: the asynchronous write path escapes exactly like the synchronous one, and a
/// host-supplied <see cref="ITemplateEngine"/> that is not the Tempo engine (so cannot be asked for a
/// raw pass) is called out with a warning instead of silently weakening the body escaping.
/// </summary>
public class RendererEngineContractTests
{
    [Fact]
    public async Task WriteAsync_AlsoEscapesExpressionOutput()
    {
        // Scriban's async rendering writes expression results through WriteAsync; it must encode them
        // exactly like the synchronous path, otherwise an async host render would be unescaped.
        var options = new TemplateSecurityOptions();
        using var context = new SandboxedTemplateContext(options, TemplateEscapeMode.Html);
        context.PushGlobal(ObjectToScriptObjectConverter.ToScriptObject(new { name = "<script>alert(1)</script>" }));
        var output = new SandboxedScriptOutput(options.MaxOutputLength, options.Timeout);
        context.PushOutput(output);

        await Template.Parse("{{ name }}").RenderAsync(context);

        output.ToString().Should().Be("&lt;script&gt;alert(1)&lt;/script&gt;");
    }

    [Fact]
    public async Task Renderer_WithCustomEngine_LogsOneWarning_AndStillRenders()
    {
        var logger = Substitute.For<ILogger<EmailTemplateRenderer>>();
        var engine = Substitute.For<ITemplateEngine>();
        engine.Render(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<TemplateRenderOptions?>())
            .Returns(call => Result<string>.Success("rendered " + call.ArgAt<string>(0).Length));

        var renderer = new EmailTemplateRenderer(
            engine, new MjmlGenerator(), new MjmlNetCompiler(), new TextVersionGenerator(), logger: logger);

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());

        var doc = new EmailTemplateDocument { Subject = "S" };
        doc.Sections.Add(new EmailSection
        {
            Columns = { new EmailColumn { Blocks = { new EmailTextBlock { Content = "x" } } } },
        });

        var result = await renderer.RenderAsync(doc, new { });

        result.Should().NotBeNull();
        engine.Received().Render(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<TemplateRenderOptions?>());
    }

    [Fact]
    public void Renderer_WithTempoEngine_DoesNotWarn()
    {
        var logger = Substitute.For<ILogger<EmailTemplateRenderer>>();

        _ = new EmailTemplateRenderer(
            new ScribanTemplateEngine(), new MjmlGenerator(), new MjmlNetCompiler(), new TextVersionGenerator(),
            logger: logger);

        logger.DidNotReceive().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
