using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// URL scheme allow-list applied after substitution (§3.7a, D15): <c>http</c>, <c>https</c>,
/// <c>mailto</c>, <c>tel</c>, relative paths and <c>#</c> pass; every other scheme is replaced with
/// <c>#</c>, reported as a <see cref="RenderError"/> and audited as
/// <see cref="TemplateSecurityEventKind.BlockedUrlScheme"/>. The checks cover every link the model
/// can put into the message: button, image <c>src</c>/<c>href</c>, navbar, social, carousel,
/// hero/wrapper/section background, and template-authored raw markup.
/// </summary>
public class RenderUrlAllowListTests
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

    // ── Blocked schemes, per link kind ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_ButtonHref_JavascriptScheme_IsReplacedWithHash()
    {
        var doc = Doc(new EmailButtonBlock { Text = "Go", Href = "{{ url }}" });

        var result = await Renderer().RenderAsync(doc, new { url = "javascript:alert(1)" });

        result.Html.Should().NotContain("javascript:");
        result.Html.Should().Contain("href=\"#\"");
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RenderAsync_ImageSrcAndHref_JavascriptScheme_IsReplacedWithHash()
    {
        var doc = Doc(new EmailImageBlock
        {
            Src = "{{ src }}",
            Href = "{{ url }}",
            Alt = "alt",
        });

        var result = await Renderer().RenderAsync(doc, new { src = "javascript:alert(2)", url = "javascript:alert(3)" });

        result.Html.Should().NotContain("javascript:");
        result.Html.Should().Contain("src=\"#\"");
        result.Html.Should().Contain("href=\"#\"");
    }

    [Fact]
    public async Task RenderAsync_NavbarLink_JavascriptScheme_IsReplacedWithHash()
    {
        var navbar = new EmailNavbarBlock();
        navbar.Links.Add(new EmailNavbarLink { Text = "Home", Href = "{{ url }}" });
        var doc = Doc(navbar);

        var result = await Renderer().RenderAsync(doc, new { url = "javascript:alert(4)" });

        result.Html.Should().NotContain("javascript:");
        result.Html.Should().Contain("href=\"#\"");
    }

    [Fact]
    public async Task RenderAsync_SocialHref_JavascriptScheme_IsReplacedWithHash()
    {
        var social = new EmailSocialBlock();
        social.Elements.Add(new EmailSocialElement { Name = "facebook", Href = "{{ url }}" });
        var doc = Doc(social);

        var result = await Renderer().RenderAsync(doc, new { url = "javascript:alert(5)" });

        // Mjml.Net rewrites a named social link to its share URL (<prefix>?u=<href>), so the refused
        // value shows up as a '#' inside that URL rather than as a bare href="#".
        result.Html.Should().NotContain("javascript:");
        result.Html.Should().Contain("#");
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RenderAsync_CarouselImage_JavascriptScheme_IsReplacedWithHash()
    {
        var carousel = new EmailCarouselBlock();
        carousel.Images.Add(new EmailCarouselImage { Src = "{{ src }}", Href = "{{ url }}", Alt = "a" });
        var doc = Doc(carousel);

        var result = await Renderer().RenderAsync(doc, new { src = "javascript:alert(6)", url = "javascript:alert(7)" });

        result.Html.Should().NotContain("javascript:");
    }

    [Fact]
    public async Task RenderAsync_HeroBackground_JavascriptScheme_IsReplacedWithHash()
    {
        var doc = Doc(new EmailHeroBlock { BackgroundUrl = "{{ url }}" });

        var result = await Renderer().RenderAsync(doc, new { url = "javascript:alert(8)" });

        // The MJML compiler copies background-url into a CSS url(...), a background attribute and a
        // VML fill inside a conditional comment — none of them may keep the scheme.
        result.Html.Should().NotContain("javascript:");
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RenderAsync_WrapperBackground_JavascriptScheme_IsReplacedWithHash()
    {
        var wrapper = new EmailWrapperBlock { BackgroundUrl = "{{ url }}" };
        wrapper.Sections.Add(new EmailSection());
        var doc = Doc(wrapper);

        var result = await Renderer().RenderAsync(doc, new { url = "javascript:alert(9)" });

        result.Html.Should().NotContain("javascript:");
    }

    [Fact]
    public async Task RenderAsync_SectionBackground_JavascriptScheme_IsReplacedWithHash()
    {
        var doc = new EmailTemplateDocument { Subject = "S" };
        doc.Sections.Add(new EmailSection
        {
            BackgroundUrl = "{{ url }}",
            Columns = { new EmailColumn { Blocks = { new EmailTextBlock { Content = "x" } } } },
        });

        var result = await Renderer().RenderAsync(doc, new { url = "javascript:alert(10)" });

        result.Html.Should().NotContain("javascript:");
    }

    [Fact]
    public async Task RenderAsync_RawMarkup_JavascriptScheme_IsReplacedWithHash()
    {
        // mj-raw is template-authored markup: the DOM pass has to catch it as well.
        var doc = Doc(new EmailRawBlock { Content = "<a href=\"{{ url }}\">raw</a>" });

        var result = await Renderer().RenderAsync(doc, new { url = "javascript:alert(11)" });

        result.Html.Should().NotContain("javascript:");
        result.Html.Should().Contain("href=\"#\"");
    }

    // ── Smuggling vectors a code review found beyond the plain scheme check ──────────────────

    [Theory]
    [InlineData("<a href=\"javascript&amp;colon;alert(1)\">x</a>")]
    [InlineData("<a href=\"javascript&#58;alert(1)\">x</a>")]
    [InlineData("<a href=\"javascript&#x3a;alert(1)\">x</a>")]
    public async Task RenderAsync_RawMarkup_EntityEncodedColon_IsBlocked(string raw)
    {
        // The browser decodes the character reference before it decides what to run, so the check has
        // to as well.
        var doc = Doc(new EmailRawBlock { Content = raw });

        var result = await Renderer().RenderAsync(doc, new { });

        result.Html.Should().NotContain("alert(1)", "the refused URL must not survive in any form");
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RenderAsync_RawMarkup_CssUrlWithParentheses_IsBlocked()
    {
        var doc = Doc(new EmailRawBlock
        {
            Content = "<div style=\"background:url(javascript:alert(1))\">x</div>",
        });

        var result = await Renderer().RenderAsync(doc, new { });

        result.Html.Should().NotContain("javascript:");
    }

    [Fact]
    public async Task RenderAsync_RawMarkup_UnquotedAttribute_IsBlocked()
    {
        var doc = Doc(new EmailRawBlock { Content = "<a href=javascript:alert(1)>x</a>" });

        var result = await Renderer().RenderAsync(doc, new { });

        result.Html.Should().NotContain("javascript:");
    }

    [Fact]
    public async Task RenderAsync_RawMarkup_XlinkHref_IsBlocked()
    {
        var doc = Doc(new EmailRawBlock
        {
            Content = "<svg><a xlink:href=\"javascript:alert(1)\">x</a></svg>",
        });

        var result = await Renderer().RenderAsync(doc, new { });

        result.Html.Should().NotContain("javascript:");
    }

    [Theory]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("blob:https://example.com/1234")]
    [InlineData("ftp://example.com/file")]
    public async Task RenderAsync_OtherDisallowedSchemes_AreReplacedWithHash(string url)
    {
        var doc = Doc(new EmailButtonBlock { Text = "Go", Href = "{{ url }}" });

        var result = await Renderer().RenderAsync(doc, new { url });

        result.Html.Should().Contain("href=\"#\"");
        result.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData(" javascript:alert(1)")]
    [InlineData("\tjavascript:alert(1)")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("java\nscript:alert(1)")]
    [InlineData("\u0001javascript:alert(1)")]
    public async Task RenderAsync_SchemeSmuggling_IsBlocked(string url)
    {
        var doc = Doc(new EmailButtonBlock { Text = "Go", Href = "{{ url }}" });

        var result = await Renderer().RenderAsync(doc, new { url });

        result.Html.Should().Contain("href=\"#\"");
    }

    // ── Allowed schemes stay untouched ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://example.com/a?b=1#c")]
    [InlineData("http://example.com")]
    [InlineData("mailto:info@example.com")]
    [InlineData("tel:+420123456789")]
    [InlineData("/relative/path")]
    [InlineData("relative/path")]
    [InlineData("./relative")]
    [InlineData("../relative")]
    [InlineData("#anchor")]
    [InlineData("")]
    public async Task RenderAsync_AllowedSchemes_AreKept(string url)
    {
        var doc = Doc(new EmailButtonBlock { Text = "Go", Href = "{{ url }}" });

        var result = await Renderer().RenderAsync(doc, new { url });

        result.Errors.Should().BeEmpty();
        if (url.Length > 0)
        {
            result.Html.Should().Contain("href=\"" + url.Replace("&", "&amp;", StringComparison.Ordinal) + "\"");
        }
    }

    // ── Raw-markup parsing and scheme-normalisation vectors (review fixes 2 + 3) ──────────────

    [Fact]
    public async Task RenderAsync_RawMarkup_GreaterThanInsideAttributeValue_IsStillChecked()
    {
        // A naive start-tag scan stops at the '>' inside title="", so the javascript: href after it
        // would never be looked at.
        var doc = Doc(new EmailRawBlock { Content = "<a title=\">\" href=\"javascript:alert(7)\">x</a>" });

        var result = await Renderer().RenderAsync(doc, new { });

        result.Html.Should().NotContain("javascript:");
        result.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("javascript&#58alert(1)")]
    [InlineData("javascript&#0000000058;alert(1)")]
    [InlineData("&#x0000006A;avascript:alert(1)")]
    [InlineData("&#106avascript:")]
    public async Task RenderAsync_CharacterReferenceForms_OfAColon_AreBlocked(string url)
    {
        // The browser decodes all of these to ':' before deciding what to run; an entity parser that
        // only understands "&#58;" and requires the semicolon lets them through.
        var doc = Doc(new EmailRawBlock { Content = $"<a href=\"{url}\">x</a>" });

        var result = await Renderer().RenderAsync(doc, new { });

        result.Html.Should().NotContain("javascript", "no decoding form of the scheme may survive");
        result.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("//evil.example.com/steal")]
    [InlineData("\\\\evil.example.com\\share")]
    [InlineData("/\\evil.example.com")]
    [InlineData("\t//evil.example.com")]
    public async Task RenderAsync_NetworkPath_IsBlocked(string url)
    {
        // A protocol-relative or UNC path makes the mail client resolve a remote (SMB) authority —
        // neither is a relative path in the "same origin" sense the allow-list promises.
        var doc = Doc(new EmailButtonBlock { Text = "Go", Href = "{{ url }}" });

        var result = await Renderer().RenderAsync(doc, new { url });

        result.Html.Should().Contain("href=\"#\"");
        result.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("<img srcset=\"javascript:alert(1) 1x, https://example.com/a.png 2x\">")]
    [InlineData("<object data=\"javascript:alert(1)\"></object>")]
    [InlineData("<a href=\"https://example.com\" ping=\"javascript:alert(1)\">x</a>")]
    [InlineData("<img lowsrc=\"javascript:alert(1)\">")]
    [InlineData("<img dynsrc=\"javascript:alert(1)\">")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=javascript:alert(1)\">")]
    public async Task RenderAsync_FurtherUrlBearingMarkup_IsBlocked(string raw)
    {
        var doc = Doc(new EmailRawBlock { Content = raw });

        var result = await Renderer().RenderAsync(doc, new { });

        result.Html.Should().NotContain("javascript:");
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RenderAsync_StyleSheetCss_WithBlockedUrl_IsNeutralised()
    {
        var doc = new EmailTemplateDocument { Subject = "S" };
        doc.Styles.Styles.Add(new EmailStyle { Css = ".x{background:url(javascript:alert(1))}" });
        doc.Sections.Add(new EmailSection
        {
            Columns = { new EmailColumn { Blocks = { new EmailTextBlock { Content = "x" } } } },
        });

        var result = await Renderer().RenderAsync(doc, new { });

        result.Html.Should().NotContain("javascript:");
    }

    [Fact]
    public async Task RenderAsync_ModelLinkWithoutTemplate_IsCheckedToo()
    {
        var doc = Doc(new EmailButtonBlock { Text = "Go", Href = "javascript:alert(12)" });

        var result = await Renderer().RenderAsync(doc, new { });

        result.Html.Should().NotContain("javascript:");
        result.Html.Should().Contain("href=\"#\"");
    }

    // ── Audit and error reporting ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_BlockedScheme_IsAuditedOncePerScheme()
    {
        var sink = new SpySink();
        var doc = Doc(
            new EmailButtonBlock { Text = "A", Href = "javascript:alert(1)" },
            new EmailButtonBlock { Text = "B", Href = "javascript:alert(2)" });

        var result = await Renderer(sink).RenderAsync(doc, new { });

        sink.Events.Should().ContainSingle();
        sink.Events[0].Kind.Should().Be(TemplateSecurityEventKind.BlockedUrlScheme);
        sink.Events[0].Detail.Should().Be("javascript");
        result.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task RenderAsync_BlockedScheme_ErrorCarriesLocalizationKey()
    {
        var doc = Doc(new EmailButtonBlock { Text = "Go", Href = "javascript:alert(1)" });

        var result = await Renderer().RenderAsync(doc, new { });

        result.Errors.Should().ContainSingle();
        result.Errors[0].Key.Should().Be(RenderKeys.BlockedUrlScheme);
        result.Errors[0].Message.Should().NotContain("alert(1)", "a diagnostic must never echo the value");
    }

    [Fact]
    public async Task RenderAsync_TextThatLooksLikeAMarkupAttribute_IsNotRewritten()
    {
        // A body that merely talks about a URL is text, not an attribute: the allow-list must not
        // corrupt authored content while it scans for real tags.
        var doc = Doc(new EmailTextBlock { Content = "Write href=\"javascript:alert(1)\" to trigger it." });

        var result = await Renderer().RenderAsync(doc, new { });

        // The text survives as text (quotes are entity-encoded by the sanitizer, which renders the same
        // characters); what must not happen is the attribute inside it being rewritten to '#'.
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(result.Html);
        document.Body!.TextContent.Should().Contain("Write href=\"javascript:alert(1)\" to trigger it.");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAsync_AllowedOnly_ProducesNoAuditEvents()
    {
        var sink = new SpySink();
        var doc = Doc(new EmailButtonBlock { Text = "Go", Href = "https://example.com" });

        var result = await Renderer(sink).RenderAsync(doc, new { });

        result.Success.Should().BeTrue();
        sink.Events.Should().BeEmpty();
    }
}
