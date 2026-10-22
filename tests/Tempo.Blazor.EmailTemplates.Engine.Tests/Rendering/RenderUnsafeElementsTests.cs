using AngleSharp.Html.Parser;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// Fáze 10 review (security 4, U1): the markup guard removes the elements an e-mail never needs and a browser
/// would run — <c>script</c>, <c>iframe</c>, <c>frame</c>, <c>frameset</c>, <c>object</c>, <c>embed</c>,
/// <c>applet</c>, <c>base</c>, <c>form</c>, <c>portal</c> — ALWAYS, including from a raw block (the author's
/// opt-in is "raw HTML", not "run script"), and a raw-text element is removed together with its content, up to
/// its closing tag. The removal is a <see cref="RenderErrorSeverity.Warning"/> and an audit event, so the host
/// still decides whether to send (D23).
/// </summary>
public class RenderUnsafeElementsTests
{
    private sealed class SpySink : ITemplateSecurityAuditSink
    {
        public List<TemplateSecurityEvent> Events { get; } = new();

        public void OnEvent(TemplateSecurityEvent securityEvent) => Events.Add(securityEvent);
    }

    private static IEmailTemplateRenderer Renderer(ITemplateSecurityAuditSink? sink = null)
        => new EmailTemplateRenderer(
            new ScribanTemplateEngine(),
            new MjmlGenerator(AngleSharpHtmlContentSanitizer.Instance),
            new MjmlNetCompiler(),
            new TextVersionGenerator(),
            sink is null ? null : new[] { sink });

    private static EmailTemplateDocument DocWith(EmailBlockBase block)
    {
        var doc = new EmailTemplateDocument { Subject = "S" };
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(block);
        section.Columns.Add(column);
        doc.Sections.Add(section);
        return doc;
    }

    [Theory]
    [InlineData("script")]
    [InlineData("iframe")]
    [InlineData("frame")]
    [InlineData("frameset")]
    [InlineData("object")]
    [InlineData("embed")]
    [InlineData("applet")]
    [InlineData("base")]
    [InlineData("form")]
    [InlineData("portal")]
    public async Task UnsafeElement_InARawBlock_IsRemovedWithAWarning(string element)
    {
        var sink = new SpySink();
        var doc = DocWith(new EmailRawBlock
        {
            Content = $"<{element} data-x=\"1\">payload</{element}><p>keep me</p>",
        });

        var result = await Renderer(sink).RenderAsync(doc, new { });

        var parsed = new HtmlParser().ParseDocument(result.Html);
        parsed.QuerySelectorAll(element).Should().BeEmpty($"a message never needs <{element}>");
        parsed.Body!.TextContent.Should().Contain("keep me", "only the unsafe element goes");

        result.Errors.Should().Contain(e => e.Key == RenderKeys.UnsafeElementRemoved
            && e.Severity == RenderErrorSeverity.Warning);
        sink.Events.Should().Contain(e => e.Kind == TemplateSecurityEventKind.BlockedMember && e.Detail == element);
        result.Success.Should().BeTrue("the rest of the message is still renderable (D23)");
    }

    [Fact]
    public async Task ScriptElement_IsRemovedTogetherWithItsContent()
    {
        // A raw-text element owns its content: removing only the tags would leave the script body as text
        // (and, worse, the browser would still have executed it in the export that carries the markup).
        var doc = DocWith(new EmailRawBlock
        {
            Content = "<script>var s = '</p><img src=x onerror=alert(1)>';</script><p>after</p>",
        });

        var result = await Renderer().RenderAsync(doc, new { });

        result.Html.Should().NotContain("var s");
        result.Html.Should().NotContain("onerror");
        var parsed = new HtmlParser().ParseDocument(result.Html);
        parsed.QuerySelectorAll("script").Should().BeEmpty();
        parsed.Body!.TextContent.Should().Contain("after");
    }
}
