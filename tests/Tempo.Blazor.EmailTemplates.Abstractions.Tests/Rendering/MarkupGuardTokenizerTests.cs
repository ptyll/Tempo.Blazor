using System.Diagnostics;
using System.Text.RegularExpressions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// Third-review hardening of the markup guard: the tag scan is a linear WHATWG-style tokenizer (no
/// backtracking, no quadratic rescan), it survives mismatched quotes, it drops SVG animation elements,
/// meta refresh and CSS escapes, and it never rewrites text that merely looks like an attribute.
/// </summary>
public class MarkupGuardTokenizerTests
{
    private static IEmailTemplateRenderer Renderer()
        => new EmailTemplateRenderer(
            new ScribanTemplateEngine(), new MjmlGenerator(), new MjmlNetCompiler(), new TextVersionGenerator());

    private static EmailTemplateDocument DocWithRaw(string raw)
    {
        var doc = new EmailTemplateDocument { Subject = "S" };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailRawBlock { Content = raw });
        section.Columns.Add(col);
        doc.Sections.Add(section);
        return doc;
    }

    // ── 1. Performance: the linear tokenizer ──────────────────────────────────────────────────

    [Theory]
    [InlineData("<a", 500_000)]
    [InlineData("<a \"", 250_000)]
    public void ApplyToHtml_UnterminatedTagAtEof_IsLinear(string unit, int count)
    {
        // A regex that backtracks on every '<' is quadratic here (100 KB used to take minutes). The
        // tokenizer walks the string once and, because the browser drops an unterminated tag, the rest
        // of the input is discarded (fail-closed).
        var markup = string.Concat(Enumerable.Repeat(unit, count));
        var stopwatch = Stopwatch.StartNew();

        var result = MarkupGuard.ApplyToHtml(markup);

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        result.Output.Should().BeEmpty();
    }

    [Fact]
    public void ApplyToHtml_LargeWellFormedMarkup_StaysLinear()
    {
        var markup = string.Concat(Enumerable.Repeat("<p>hello</p>", 100_000));
        var stopwatch = Stopwatch.StartNew();

        var result = MarkupGuard.ApplyToHtml(markup);

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        result.Output.Should().StartWith("<p>hello</p>");
    }

    // ── 1b. The tokenizer never skips content a browser may parse as tags ───────────────────

    [Theory]
    [InlineData("<!--><img src=x onerror=alert(1)>-->")]
    [InlineData("<!---><img src=x onerror=alert(1)>-->")]
    [InlineData("<!-- x --!><img src=x onerror=alert(1)>")]
    [InlineData("<svg><style><img src=x onerror=alert(1)></style></svg>")]
    [InlineData("<svg><title><a href=\"javascript:alert(1)\">x</a></title></svg>")]
    [InlineData("<textarea><img src=x onerror=alert(1)></textarea>")]
    [InlineData("<script><img src=x onerror=alert(1)></script>")]
    public void ApplyToHtml_CommentOrRawTextContent_IsStillScanned(string markup)
    {
        // A browser ends an abrupt comment at the first '>' and does not switch to raw text inside
        // foreign (svg/math) content, so content before that '>' or inside <style>/<title> can become
        // live markup. The guard therefore never skips content.
        var result = MarkupGuard.ApplyToHtml(markup);

        result.Output.Should().NotContain("onerror");
        result.Output.Should().NotContain("javascript:");
    }

    [Theory]
    [InlineData("<!--><img src=x onerror=alert(1)>-->")]
    [InlineData("<svg><style><img src=x onerror=alert(1)></style></svg>")]
    [InlineData("<svg><title><a href=\"javascript:alert(1)\">x</a></title></svg>")]
    [InlineData("<textarea><img src=x onerror=alert(1)></textarea>")]
    public async Task RenderAsync_CommentOrRawTextContent_IsSanitized(string raw)
    {
        var result = await Renderer().RenderAsync(DocWithRaw(raw), new { });

        result.Html.Should().NotContain("onerror");
        result.Html.Should().NotContain("javascript:");
    }

    [Fact]
    public void ApplyToHtml_OutlookConditionalComment_IsUnchanged()
    {
        // Conditional comments are how the MJML compiler targets Outlook, so scanning their payload
        // must not rewrite a legit VML background.
        const string markup =
            "<!--[if mso | IE]><table><v:fill src=\"https://example.com/bg.png\" /><![endif]-->" +
            "<!--[if !mso]><!--><p>hi</p><!--<![endif]-->";

        var result = MarkupGuard.ApplyToHtml(markup);

        result.Output.Should().Be(markup);
        result.BlockedSchemes.Should().BeEmpty();
        result.RemovedEventHandlers.Should().BeEmpty();
        result.RemovedElements.Should().BeEmpty();
    }

    // ── 2. Mismatched quotes must not hide an attribute ──────────────────────────────────────

    [Theory]
    [InlineData("<img src=x onerror=alert(16) x'>", "onerror")]
    [InlineData("<a x\"y href=javascript:alert(17)>link</a>", "javascript:")]
    [InlineData("<img src=x onerror=alert(32) title=a\"b>", "onerror")]
    [InlineData("<img src=x onerror=alert(1) title=a'b>", "onerror")]
    [InlineData("<img src=x onerror=alert(1) x`y>", "onerror")]
    public void ApplyToHtml_MismatchedQuotes_StillRemovesHandlersAndUrls(string markup, string forbidden)
    {
        var result = MarkupGuard.ApplyToHtml(markup);

        result.Output.Should().NotContain(forbidden);
    }

    [Theory]
    [InlineData("<img src=x onerror=alert(16) x'>")]
    [InlineData("<a x\"y href=javascript:alert(17)>link</a>")]
    public async Task RenderAsync_RawMarkupWithMismatchedQuotes_IsSanitized(string raw)
    {
        var result = await Renderer().RenderAsync(DocWithRaw(raw), new { });

        result.Html.Should().NotContain("onerror");
        result.Html.Should().NotContain("javascript:");
    }

    // ── 3. SVG SMIL animation elements ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("<svg><a><animate attributeName=\"href\" values=\"javascript:alert(1)\"/></a></svg>", "animate")]
    [InlineData("<svg><a><set attributeName=\"href\" to=\"javascript:alert(1)\"/></a></svg>", "set")]
    [InlineData("<svg><animateMotion from=\"0\" by=\"javascript:alert(1)\"/></svg>", "animateMotion")]
    [InlineData("<svg><animateTransform from=\"0\" by=\"javascript:alert(1)\"/></svg>", "animateTransform")]
    [InlineData("<svg><discard begin=\"0s\"/></svg>", "discard")]
    public void ApplyToHtml_SmilElement_IsDropped(string markup, string element)
    {
        var result = MarkupGuard.ApplyToHtml(markup);

        result.Output.Should().NotContain(element, "an SVG animation element can rewrite a link at runtime");
        result.Output.Should().NotContain("javascript:");
        result.RemovedElements.Should().Contain(element.ToLowerInvariant());
    }

    // ── 4. meta refresh ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url='javascript:alert(1)'\">")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0; javascript:alert(1)\">")]
    [InlineData("<meta http-equiv=refresh content=\"0;url=javascript:alert(1)\">")]
    [InlineData("<meta HTTP-EQUIV='Refresh' content='0;url=javascript:alert(1)'>")]
    public void ApplyToHtml_MetaRefresh_IsDroppedEntirely(string markup)
    {
        // Even a harmless-looking content is hard to judge (quotes, missing url=), so the whole element
        // goes — a template has no legitimate use for a redirect in an e-mail.
        var result = MarkupGuard.ApplyToHtml(markup);

        result.Output.Should().NotContain("javascript:");
        result.Output.Should().NotContain("http-equiv");
        result.RemovedElements.Should().Contain("meta-refresh");
    }

    // ── 5. CSS escapes, image-set and @import ───────────────────────────────────────────────

    [Theory]
    [InlineData("<div style=\"background:u\\72l(javascript:alert(1))\">x</div>")]
    [InlineData("<style>@import \"//evil.example.com/x.css\";</style>")]
    [InlineData("<style>@import 'data:text/css,x';</style>")]
    [InlineData("<div style=\"background:image-set('javascript:alert(1)' 1x)\">x</div>")]
    public void ApplyToHtml_CssEscapeOrImport_IsNeutralised(string markup)
    {
        var result = MarkupGuard.ApplyToHtml(markup);

        result.Output.Should().NotContain("javascript:");
        result.Output.Should().NotContain("data:");
        result.Output.Should().NotContain("//evil.example.com");
    }

    [Fact]
    public void ApplyToHtml_CssEscapedUrl_ReportsAWarning()
    {
        var result = MarkupGuard.ApplyToHtml("<div style=\"background:u\\72l(javascript:alert(1))\">x</div>");

        result.BlockedSchemes.Should().Contain("javascript");
    }

    // ── 6. No false positives in quoted values ─────────────────────────────────────────────

    [Theory]
    [InlineData("<img alt=\"a onerror=b\">")]
    [InlineData("<a title=\"href=javascript:x\">l</a>")]
    [InlineData("<div data-onclick=\"kept\">x</div>")]
    public void ApplyToHtml_TextThatLooksLikeMarkup_IsUntouched(string markup)
    {
        var result = MarkupGuard.ApplyToHtml(markup);

        result.Output.Should().Be(markup);
        result.RemovedEventHandlers.Should().BeEmpty();
        result.BlockedSchemes.Should().BeEmpty();
        result.RemovedElements.Should().BeEmpty();
    }

    // ── 7. Fail-closed Html on an error ─────────────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_CompileError_EmptiesTheHtml()
    {
        var doc = DocWithRaw("<mj-column>");

        var result = await Renderer().RenderAsync(doc, new { });

        result.Success.Should().BeFalse();
        result.Html.Should().BeEmpty("a message whose markup could not be built must not be sent half-built");
    }

    [Fact]
    public void MarkupGuardFailure_MapsToATooComplexError()
    {
        var timeout = new RegexMatchTimeoutException("markup", "pattern", TimeSpan.FromSeconds(2));

        var error = MarkupGuard.FailureToError(timeout);

        error.Severity.Should().Be(RenderErrorSeverity.Error);
        error.Key.Should().Be(RenderKeys.MarkupTooComplex);
        error.Message.Should().NotBeNullOrWhiteSpace();
    }
}
