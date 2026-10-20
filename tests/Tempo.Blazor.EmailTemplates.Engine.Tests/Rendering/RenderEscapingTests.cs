using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// Escaping of expression output (B3): a variable value can never inject markup into the HTML body,
/// while the subject, preheader and text version stay raw. PostrionCore parity:
/// <c>tests/PostrionCore.EmailTemplates.Tests/Templating/HtmlEscapingTests.cs</c>.
/// </summary>
public class RenderEscapingTests
{
    private static ScribanTemplateEngine Engine() => new();

    private static IEmailTemplateRenderer Renderer()
        => new EmailTemplateRenderer(
            new ScribanTemplateEngine(), new MjmlGenerator(AngleSharpHtmlContentSanitizer.Instance), new MjmlNetCompiler(), new TextVersionGenerator());

    private static EmailTemplateDocument DocWith(string subject, string? preheader, string textContent)
    {
        var doc = new EmailTemplateDocument { Subject = subject, Preheader = preheader };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = textContent });
        section.Columns.Add(col);
        doc.Sections.Add(section);
        return doc;
    }

    // ── PostrionCore HtmlEscapingTests parity ──────────────────────────────────────────────────

    [Fact]
    public void Engine_Render_HtmlInVariable_EscapesByDefault()
    {
        // PostrionCore: HtmlEscapingTests.Render_HtmlInVariable_EscapesByDefault
        var result = Engine().Render("{{ content }}", new { content = "<script>alert('xss')</script>" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain("<script>");
        result.Value.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void Engine_Render_WithHtmlTrustedFilter_DoesNotEscape()
    {
        // PostrionCore: HtmlEscapingTests.Render_WithHtmlFilter_DoesNotEscape (there skipped as
        // "html.raw filter requires custom implementation"; the Tempo opt-out is html.trusted)
        var result = Engine().Render("{{ content | html.trusted }}", new { content = "<b>Bold</b>" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("<b>Bold</b>");
    }

    [Fact]
    public void Engine_Render_Ampersand_EscapesToEntity()
    {
        // PostrionCore: HtmlEscapingTests.Render_Ampersand_EscapesToEntity
        var result = Engine().Render("{{ content }}", new { content = "A & B" });

        result.Value.Should().Be("A &amp; B");
    }

    [Fact]
    public void Engine_Render_Quotes_EscapesToEntities()
    {
        // PostrionCore: HtmlEscapingTests.Render_Quotes_EscapesToEntities
        var result = Engine().Render("{{ content }}", new { content = "He said \"Hello\"" });

        result.Value.Should().Contain("&quot;");
    }

    // ── The opt-out never bypasses the sanitizer ───────────────────────────────────────────────

    [Fact]
    public void Engine_Render_HtmlTrustedFilter_SanitizesTheValue()
    {
        var result = Engine().Render(
            "{{ content | html.trusted }}",
            new { content = "<b onclick=\"evil()\">Bold</b><script>alert(1)</script><a href=\"javascript:alert(1)\">x</a>" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("<b>Bold</b>");
        result.Value.Should().NotContain("onclick");
        result.Value.Should().NotContain("<script");
        result.Value.Should().NotContain("javascript:");
    }

    // ── Filters run on the raw value; only the output is escaped ───────────────────────────────

    [Fact]
    public void Engine_Render_StringFilter_SeesTheUnescapedValue()
    {
        // "A & B" is 5 characters raw and 9 once encoded; the filter must measure the raw value.
        Engine().Render("{{ name | string.size }}", new { name = "A & B" }).Value.Should().Be("5");

        var replaced = Engine().Render("{{ name | string.replace \"&\" \"and\" }}", new { name = "A & B" });

        replaced.Value.Should().Be("A and B");
    }

    [Fact]
    public void Engine_Render_ArraySizeFilter_IsNotAffectedByEscaping()
    {
        var result = Engine().Render("{{ items | array.size }}", new { items = new[] { "a", "<b>", "&" } });

        result.Value.Should().Be("3");
    }

    [Fact]
    public void Engine_Render_HtmlEscapeFilter_IsNotEscapedTwice()
    {
        var result = Engine().Render("{{ \"a<b\" | html.escape }}", new { });

        result.Value.Should().Be("a&lt;b");
    }

    [Fact]
    public void Engine_Render_LiteralTemplateText_IsNotTouched()
    {
        var result = Engine().Render("<p class=\"x\">A &amp; B</p>", new { });

        result.Value.Should().Be("<p class=\"x\">A &amp; B</p>");
    }

    [Fact]
    public void Engine_Render_VisibleWhenAndLoops_StillEvaluate()
    {
        var template = "{{ if flag }}[{{ for item in items }}{{ item }}{{ end }}]{{ end }}";

        var result = Engine().Render(template, new { flag = true, items = new[] { "<a>", "B" } });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("[&lt;a&gt;B]");
    }

    // ── The renderer picks the mode per pass ───────────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_BodyEscapes_SubjectPreheaderAndTextVersionStayRaw()
    {
        var doc = DocWith("Hi {{ name }}", "Preview {{ name }}", "<p>Hi {{ name }}</p>");

        var result = await Renderer().RenderAsync(doc, new { name = "A & B" });

        result.Success.Should().BeTrue();
        result.Html.Should().Contain("A &amp; B");
        result.Subject.Should().Be("Hi A & B");
        result.Preheader.Should().Be("Preview A & B");
        result.TextVersion.Should().Contain("A & B");
    }

    [Fact]
    public async Task RenderAsync_ScriptInVariable_IsEscapedInBody()
    {
        var doc = DocWith("S", null, "<p>{{ name }}</p>");

        var result = await Renderer().RenderAsync(doc, new { name = "<script>alert(1)</script>" });

        result.Html.Should().NotContain("<script");
        result.Html.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
    }

    [Fact]
    public async Task RenderAsync_TrustedHtmlVariable_RendersSanitizedMarkup()
    {
        var doc = DocWith("S", null, "{{ content | html.trusted }}");

        var result = await Renderer().RenderAsync(doc, new
        {
            content = "<b>Bold</b><script>alert(1)</script><a href=\"javascript:alert(1)\">x</a>",
        });

        result.Html.Should().Contain("<b>Bold</b>");
        result.Html.Should().NotContain("<script");
        result.Html.Should().NotContain("javascript:");
    }

    [Fact]
    public async Task RenderAsync_AttributeValue_IsEscapedNotInjected()
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailButtonBlock { Text = "Go", Href = "{{ url }}" });
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var result = await Renderer().RenderAsync(doc, new { url = "?a=1&b=2\"onmouseover=\"alert(1)" });

        // The quote the value carries must stay inside the href value: no element may gain an event
        // handler attribute, and the ampersand must be encoded.
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(result.Html);
        document.QuerySelectorAll("[onmouseover], [onclick], [onerror]").Should().BeEmpty();
        var anchor = document.QuerySelector("a[href]");
        anchor.Should().NotBeNull();
        anchor!.GetAttribute("href").Should().Be("?a=1&b=2\"onmouseover=\"alert(1)");
        result.Html.Should().Contain("&amp;");
    }

    // ── html.trusted in an attribute position must not break out (review fix 1) ────────────────

    [Theory]
    [InlineData("\" onmouseover=\"alert(1)")]
    [InlineData("' onmouseover='alert(1)")]
    public async Task RenderAsync_TrustedHtmlInRawAttribute_CannotBreakOutOfTheAttribute(string payload)
    {
        var doc = new EmailTemplateDocument { Subject = "S" };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailRawBlock { Content = "<a title=\"{{ x | html.trusted }}\" href=\"https://ok\">link</a>" });
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var result = await Renderer().RenderAsync(doc, new { x = payload });

        // The payload stays inside the quoted value (as entities); what must not happen is a new
        // onmouseover *attribute* appearing on the element.
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(result.Html);
        document.QuerySelectorAll("[onmouseover]").Should().BeEmpty();
        document.QuerySelector("a")!.GetAttribute("title").Should().Contain("onmouseover=");
    }

    [Fact]
    public async Task RenderAsync_TrustedHtmlInButtonHref_CannotBreakOutOfTheAttribute()
    {
        var doc = new EmailTemplateDocument { Subject = "S" };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailButtonBlock { Text = "Go", Href = "https://ok/{{ x | html.trusted }}" });
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var result = await Renderer().RenderAsync(doc, new { x = "\" onmouseover=\"alert(1)" });

        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(result.Html);
        document.QuerySelectorAll("[onmouseover]").Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAsync_EscapedThenTrusted_CannotBreakOutOfTheAttribute()
    {
        var doc = new EmailTemplateDocument { Subject = "S" };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailRawBlock { Content = "<a title=\"{{ x | html.escape | html.trusted }}\" href=\"https://ok\">link</a>" });
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var result = await Renderer().RenderAsync(doc, new { x = "\" onmouseover=\"alert(1)" });

        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(result.Html);
        document.QuerySelectorAll("[onmouseover]").Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAsync_TrustedHtmlMarkup_IsUnchangedByTheAttributeHardening()
    {
        var doc = DocWith("S", null, "{{ content | html.trusted }}");

        var result = await Renderer().RenderAsync(doc, new { content = "<b>Bold</b> and <i>italic</i>" });

        result.Html.Should().Contain("<b>Bold</b>");
        result.Html.Should().Contain("<i>italic</i>");
    }

    // ── Filter order matters (review fix 11) ──────────────────────────────────────────────────

    [Fact]
    public void Engine_Render_EscapeFollowedByAStringFilter_IsDocumentedNotSupported()
    {
        // html.escape / html.trusted are terminal filters: their marker is converted back to plain
        // text by a following string filter, so the value is encoded a second time. Pinned so a future
        // change of this behaviour is a conscious decision (documented in TEMPLATE_SYNTAX.md).
        var result = Engine().Render("{{ x | html.escape | string.upcase }}", new { x = "<b>" });

        result.Value.Should().Be("&amp;LT;B&amp;GT;");
    }

    [Fact]
    public async Task RenderAsync_VisibleWhen_IsUnaffectedByEscaping()
    {
        var doc = new EmailTemplateDocument { Subject = "S" };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = "members {{ name }}", VisibleWhen = "is_member" });
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var shown = await Renderer().RenderAsync(doc, new { IsMember = true, Name = "<b>x</b>" });
        var hidden = await Renderer().RenderAsync(doc, new { IsMember = false, Name = "<b>x</b>" });

        shown.Html.Should().Contain("&lt;b&gt;x&lt;/b&gt;");
        hidden.Html.Should().NotContain("members");
    }
}
