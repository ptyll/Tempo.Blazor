using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// Event-handler hardening of template-authored raw markup: a variable interpolated into an
/// <c>on*</c> attribute is decoded by the browser before JavaScript runs it, so escaping alone is not
/// enough — the attribute itself must go.
/// </summary>
public class RenderMarkupHardeningTests
{
    private sealed class SpySink : ITemplateSecurityAuditSink
    {
        public List<TemplateSecurityEvent> Events { get; } = new();

        public void OnEvent(TemplateSecurityEvent securityEvent) => Events.Add(securityEvent);
    }

    private static IEmailTemplateRenderer Renderer(ITemplateSecurityAuditSink? sink = null)
        => new EmailTemplateRenderer(
            sink is null ? new ScribanTemplateEngine() : new ScribanTemplateEngine(auditSinks: new[] { sink }),
            new MjmlGenerator(),
            new MjmlNetCompiler(),
            new TextVersionGenerator(),
            sink is null ? null : new[] { sink });

    private static EmailTemplateDocument Doc(params EmailBlockBase[] blocks)
    {
        var doc = new EmailTemplateDocument { Subject = "S" };
        var section = new EmailSection();
        var col = new EmailColumn();
        foreach (var block in blocks) col.Blocks.Add(block);
        section.Columns.Add(col);
        doc.Sections.Add(section);
        return doc;
    }

    [Fact]
    public async Task RenderAsync_EventHandlerWithVariable_IsRemoved()
    {
        // The browser entity-decodes the attribute value before evaluating it, so an escaped variable
        // still executes: the whole handler has to be dropped.
        var doc = Doc(new EmailRawBlock { Content = "<div onclick=\"f('{{ x }}')\">click</div>" });

        var result = await Renderer().RenderAsync(doc, new { x = "');alert(1);//" });

        result.Html.Should().NotContain("onclick");
        result.Html.Should().NotContain("alert(1)");
        var document = new HtmlParser().ParseDocument(result.Html);
        document.QuerySelectorAll("[onclick]").Should().BeEmpty();
        result.Errors.Should().Contain(e => e.Key == RenderKeys.EventHandlerRemoved);
        result.Errors.Should().OnlyContain(e => e.Severity == RenderErrorSeverity.Warning);
    }

    [Fact]
    public async Task RenderAsync_RemovedHandler_IsAuditedAsBlockedMember()
    {
        var sink = new SpySink();
        var doc = Doc(new EmailRawBlock { Content = "<div onclick=\"steal()\">click</div>" });

        await Renderer(sink).RenderAsync(doc, new { });

        sink.Events.Should().ContainSingle();
        sink.Events[0].Kind.Should().Be(TemplateSecurityEventKind.BlockedMember);
        sink.Events[0].Detail.Should().Be("onclick");
    }

    [Theory]
    [InlineData("<div onmouseover=\"steal()\">x</div>")]
    [InlineData("<div ONCLICK=\"steal()\">x</div>")]
    [InlineData("<div onload=steal()>x</div>")]
    [InlineData("<iframe srcdoc=\"<script>alert(1)</script>\"></iframe>")]
    public async Task RenderAsync_AnyEventHandlerOrSrcdoc_IsRemoved(string raw)
    {
        var doc = Doc(new EmailRawBlock { Content = raw });

        var result = await Renderer().RenderAsync(doc, new { });

        result.Html.Should().NotMatchRegex("\\son[a-z]+\\s*=");
        result.Html.Should().NotContain("srcdoc");
    }

    [Fact]
    public async Task RenderAsync_OrdinaryDocument_GainsNoEventHandlerAttributes()
    {
        // Golden sanity: the MJML compiler itself never emits on* attributes, so a clean render must
        // produce no warning and no handler.
        var doc = Doc(
            new EmailTextBlock { Content = "Hello" },
            new EmailButtonBlock { Text = "Go", Href = "https://example.com" },
            new EmailImageBlock { Src = "https://example.com/a.png", Alt = "a" });

        var result = await Renderer().RenderAsync(doc, new { });

        result.Errors.Should().BeEmpty();
        Regex.IsMatch(result.Html, "\\son[a-z]+\\s*=").Should().BeFalse();
    }
}
