using System.Globalization;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// Scriban code inside block content (<c>mj-text</c>, <c>mj-accordion-text</c>) must survive the content
/// sanitizer: the sanitizer entity-encodes quotes and operators, which used to destroy the template
/// (<c>{{ price | math.format "N2" }}</c> → "Invalid token found &amp;" and an empty render). Code spans
/// are masked before sanitizing and restored afterwards; everything outside a span is still sanitized,
/// so the masking must not open a way to smuggle markup in.
/// </summary>
public class RenderBlockContentTests
{
    private static IEmailTemplateRenderer Renderer()
        => new EmailTemplateRenderer(
            new ScribanTemplateEngine(), new MjmlGenerator(), new MjmlNetCompiler(), new TextVersionGenerator());

    private static EmailTemplateDocument DocWithContent(string content)
    {
        var document = new EmailTemplateDocument { Subject = "S" };
        document.Sections.Add(new EmailSection
        {
            Columns = { new EmailColumn { Blocks = { new EmailTextBlock { Content = content } } } },
        });
        return document;
    }

    private static EmailTemplateDocument DocWithAccordionContent(string content)
    {
        var accordion = new EmailAccordionBlock();
        accordion.Items.Add(new EmailAccordionItem { Title = "T", Content = content });
        var document = new EmailTemplateDocument { Subject = "S" };
        document.Sections.Add(new EmailSection
        {
            Columns = { new EmailColumn { Blocks = { accordion } } },
        });
        return document;
    }

    private static readonly object Model = new
    {
        price = 1234.5m,
        text = "abcdefghij",
        created = new DateTime(2026, 10, 20, 13, 5, 0),
        a = true,
        b = true,
        x = string.Empty,
    };

    // ── Benign: filters with string literals and comparison operators must render ───────────────

    [Fact]
    public async Task Text_FilterWithStringLiteral_RendersInvariant()
    {
        var result = await Renderer().RenderAsync(DocWithContent("{{ price | math.format \"N2\" }}"), Model);

        result.Success.Should().BeTrue(result.FirstError());
        result.Html.Should().Contain("1,234.50");
    }

    [Fact]
    public async Task Text_FilterWithStringLiteral_FollowsTheRenderCulture()
    {
        var result = await Renderer().RenderAsync(
            DocWithContent("{{ price | math.format \"N2\" }}"),
            Model,
            new TemplateRenderOptions { Culture = new CultureInfo("cs-CZ") });

        result.Success.Should().BeTrue(result.FirstError());
        result.Html.Should().Contain("1\u00A0234,50");
    }

    [Fact]
    public async Task Text_TruncateWithEllipsisLiteral_Renders()
    {
        var result = await Renderer().RenderAsync(DocWithContent("{{ text | string.truncate 5 \"...\" }}"), Model);

        result.Success.Should().BeTrue(result.FirstError());
        // Scriban's truncate keeps the total length: 5 characters with a three-character ellipsis.
        result.Html.Should().Contain("ab...");
    }

    [Fact]
    public async Task Text_DateFilterWithFormatLiteral_Renders()
    {
        var result = await Renderer().RenderAsync(DocWithContent("{{ created | date.to_string \"%d.%m.%Y\" }}"), Model);

        result.Success.Should().BeTrue(result.FirstError());
        result.Html.Should().Contain("20.10.2026");
    }

    [Fact]
    public async Task Text_ComparisonOperatorInsideIf_Renders()
    {
        var result = await Renderer().RenderAsync(DocWithContent("{{ if price > 100 }}big{{ end }}"), Model);

        result.Success.Should().BeTrue(result.FirstError());
        result.Html.Should().Contain("big");
    }

    [Fact]
    public async Task Text_LogicalAndOperator_Renders()
    {
        var result = await Renderer().RenderAsync(DocWithContent("{{ if a && b }}yes{{ end }}"), Model);

        result.Success.Should().BeTrue(result.FirstError());
        result.Html.Should().Contain("yes");
    }

    [Fact]
    public async Task Text_FilterInsideAnAttribute_Renders()
    {
        var result = await Renderer().RenderAsync(
            DocWithContent("<a href=\"https://x.cz/{{ 'a' | string.append 'b' }}\">link</a>"), Model);

        result.Success.Should().BeTrue(result.FirstError());
        result.Html.Should().Contain("https://x.cz/ab");
    }

    [Fact]
    public async Task AccordionText_FilterWithStringLiteral_Renders()
    {
        var result = await Renderer().RenderAsync(
            DocWithAccordionContent("{{ price | math.format \"N2\" }}"), Model);

        result.Success.Should().BeTrue(result.FirstError());
        result.Html.Should().Contain("1,234.50");
    }

    // ── Adversarial: masking code spans must not let markup through ────────────────────────────

    public static TheoryData<string> AttackPayloads => new()
    {
        // A variable smuggled into an attribute (the value carries a quote and a handler).
        "<a href=\"https://x.cz/?q={{ x | html.trusted }}\">link</a>",
        "<span style=\"{{ x }}\">styled</span>",
        // A template expression that *produces* the attribute-breaking quote itself.
        "<a href=\"{{ '\" onmouseover=alert(1) x=\"' }}\">link</a>",
        // An element/attribute name assembled from an expression.
        "<a {{ 'onclick' }}=\"alert(1)\">link</a>",
        "<{{ 'script' }}>alert(1)</{{ 'script' }}>",
        // Block/expression spans that try to escape the mask or the sanitizer.
        "{%{ {{ '<script>' }} }%}",
        "{%{ <script> }%}",
        "{{ \"}}\" }}<img src=x onerror=alert(1)>",
        "<!-- {{ x }} -->",
        // Raw block whose payload carries markup between the block tags.
        "{% raw %}<script>alert(1)</script>{% endraw %}",
        "{% raw %}<img src=x onerror=alert(1)>{% endraw %}",
    };

    [Theory]
    [MemberData(nameof(AttackPayloads))]
    public async Task Text_CodeSpansCannotSmuggleMarkup(string content)
    {
        var model = new
        {
            price = 1m,
            text = "t",
            created = DateTime.UtcNow,
            a = true,
            b = true,
            x = "\" onmouseover=\"alert(1)",
        };

        var result = await Renderer().RenderAsync(DocWithContent(content), model);

        var html = result.Html;
        // The escaped *text* of a payload may legitimately appear inside a value ("onmouseover=&quot;…"),
        // so the security property is asserted on the parsed DOM: no event-handler attribute anywhere, no
        // script element, no javascript: target — plus the raw injection signature.
        html.Should().NotContain("<script");
        html.Should().NotContain("javascript:");
        html.Should().NotContain("onmouseover=\"alert(1)");
        html.Should().NotContain("onclick=\"alert(1)");
        html.Should().NotContain("onerror=");

        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);
        foreach (var element in document.All)
        {
            element.LocalName.Should().NotBe("script");
            foreach (var attribute in element.Attributes)
            {
                attribute.Name.Should().NotStartWith("on");
                attribute.Value.Should().NotContain("javascript:");
            }
        }
    }

    [Fact]
    public async Task Text_TrustedMarkupInContent_IsStillAllowed()
    {
        var model = new { price = 1m, text = "t", created = DateTime.UtcNow, a = true, b = true, x = "<b>bold</b>" };

        var result = await Renderer().RenderAsync(DocWithContent("<span>{{ x | html.trusted }}</span>"), model);

        result.Success.Should().BeTrue(result.FirstError());
        result.Html.Should().Contain("<b>bold</b>");
    }

    [Fact]
    public async Task Text_TrustedMarkupInAnAttribute_CannotBecomeMarkup()
    {
        var model = new { price = 1m, text = "t", created = DateTime.UtcNow, a = true, b = true, x = "<b>bold</b>" };

        // `title` is not an allow-listed attribute, so the whole value (and its span) disappears; the
        // point is that trusted markup can never be promoted by an attribute.
        var result = await Renderer().RenderAsync(
            DocWithContent("<a title=\"{{ x | html.trusted }}\" href=\"https://x.cz\">link</a>"), model);

        result.Html.Should().NotContain("<b>bold</b>");
        result.Html.Should().Contain("href=\"https://x.cz\"");
    }
}

internal static class RenderResultAssertions
{
    /// <summary>A readable failure message: the first error instead of a bare "expected true".</summary>
    public static string FirstError(this RenderResult result)
        => result.Errors.Count == 0 ? "no render error" : string.Join("; ", result.Errors.Select(e => e.Message));
}
