using FluentAssertions;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.Testing;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// The conservative BCL sanitizer that ships in Abstractions (D12/D22) is the default whenever the
/// Engine package is not registered (WASM editor). It must be a tokenizer with HTML-encode — never a
/// regex strip — and it must survive the same XSS / mutation-XSS set as the AngleSharp implementation,
/// differing only towards stricter output.
/// </summary>
public class ConservativeSanitizerTests
{
    private static IHtmlContentSanitizer Sanitizer => ConservativeHtmlContentSanitizer.Instance;

    [Fact]
    public void Instance_IsTheDefaultAndIsReusable()
    {
        ConservativeHtmlContentSanitizer.Instance.Should().BeSameAs(ConservativeHtmlContentSanitizer.Instance);
        Sanitizer.Sanitize("<b>x</b>").Should().Be("<b>x</b>");
    }

    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("<b>bold</b>", "<b>bold</b>")]
    [InlineData("<strong>a</strong><em>b</em>", "<strong>a</strong><em>b</em>")]
    [InlineData("<ul><li>one</li></ul>", "<ul><li>one</li></ul>")]
    [InlineData("a<br>b", "a<br>b")]
    [InlineData("<b>bold", "<b>bold</b>")]
    [InlineData("<b><i>x</i>", "<b><i>x</i></b>")]
    [InlineData("</b>", "&lt;/b&gt;")]
    [InlineData("</mj-text>", "&lt;/mj-text&gt;")]
    [InlineData("<b>x</b></mj-text>", "<b>x</b>&lt;/mj-text&gt;")]
    [InlineData("<b>x<i>y</b>z</i>", "<b>x<i>y&lt;/b&gt;z</i></b>")]
    [InlineData("<script>alert(1)</script>", "")]
    [InlineData("<script>alert(1)", "")]
    [InlineData("<style>b{}</style>x", "x")]
    [InlineData("<!-- hidden -->x", "x")]
    [InlineData("<!-- unterminated", "")]
    [InlineData("<img src=x onerror=alert(1)>", "&lt;img src=x onerror=alert(1)&gt;")]
    [InlineData("<div>x</div>", "&lt;div&gt;x&lt;/div&gt;")]
    [InlineData("<b style=\"color:red\">x</b>", "<b>x</b>")]
    [InlineData("<b class=\"x\" id=\"y\">x</b>", "<b>x</b>")]
    // D28: a heading is allow-listed as a FORMATTING element and keeps text only — its attributes die.
    [InlineData("<h1 onclick=x style=\"background:url(javascript:alert(1))\">Ahoj</h1>", "<h1>Ahoj</h1>")]
    [InlineData("<h2><img src=x onerror=alert(1)></h2>", "<h2>&lt;img src=x onerror=alert(1)&gt;</h2>")]
    [InlineData("<h3>t</h3><h4>u</h4><h5>v</h5><h6>w</h6>", "<h3>t</h3><h4>u</h4><h5>v</h5><h6>w</h6>")]
    [InlineData("<a href=\"https://x/y\">l</a>", "<a href=\"https://x/y\">l</a>")]
    [InlineData("<a href=\"javascript:alert(1)\">l</a>", "<a href=\"#\">l</a>")]
    [InlineData("<a href='javascript:alert(1)'>l</a>", "<a href=\"#\">l</a>")]
    [InlineData("<a href=javascript:alert(1)>l</a>", "<a href=\"#\">l</a>")]
    [InlineData("<a href=\"&#106;avascript:alert(1)\">l</a>", "<a href=\"#\">l</a>")]
    [InlineData("<a href=\"mailto:x@y.z\">l</a>", "<a href=\"mailto:x@y.z\">l</a>")]
    [InlineData("<a href=\"#anchor\">l</a>", "<a href=\"#anchor\">l</a>")]
    [InlineData("<a href=\"//evil.example/x\">l</a>", "<a href=\"#\">l</a>")]
    [InlineData("<a onclick=\"alert(1)\" href=\"https://x/y\">l</a>", "<a href=\"https://x/y\">l</a>")]
    [InlineData("<a href=\"https://x?a=1&amp;b=2\">l</a>", "<a href=\"https://x?a=1&amp;b=2\">l</a>")]
    [InlineData("a & b", "a &amp; b")]
    [InlineData("a &amp; b", "a &amp; b")]
    [InlineData("&lt;script&gt;", "&lt;script&gt;")]
    [InlineData("&#60;b&#62;", "&lt;b&gt;")]
    [InlineData("5 > 3", "5 &gt; 3")]
    [InlineData("\"quoted\"", "&quot;quoted&quot;")]
    public void Sanitize_ProducesTheDocumentedOutput(string input, string expected)
    {
        Sanitizer.Sanitize(input).Should().Be(expected);
    }

    [Fact]
    public void Sanitize_UnterminatedTag_DegradesToEscapedText()
    {
        Sanitizer.Sanitize("<b").Should().Be("&lt;b");
        Sanitizer.Sanitize("<a href=\"https://ok/x").Should().Be("&lt;a href=&quot;https://ok/x");
        Sanitizer.Sanitize("<b>open<i").Should().Be("<b>open&lt;i</b>");
    }

    [Fact]
    public void Sanitize_SelfClosingAndVoidTags_AreBalanced()
    {
        Sanitizer.Sanitize("<br/>").Should().Be("<br>");
        Sanitizer.Sanitize("<b/>").Should().Be("<b></b>");
        Sanitizer.Sanitize("</br>").Should().Be("&lt;/br&gt;");
    }

    [Fact]
    public void Sanitize_SurvivesTheSharedXssSet()
    {
        foreach (var payload in SanitizerXssVectors.Payloads)
        {
            var output = Sanitizer.Sanitize(payload);

            SanitizerXssVectors.AssertNoExecutableMarkup(output, $"payload '{payload}' must not produce executable markup");
            SanitizerXssVectors.AssertOutputWithinPolicy(output, conservative: true);
        }
    }

    [Fact]
    public void Sanitize_Fuzz_OutputNeverEscapesThePolicy()
    {
        foreach (var fuzz in SanitizerXssVectors.Fuzz(600))
        {
            var output = Sanitizer.Sanitize(fuzz);

            SanitizerXssVectors.AssertOutputWithinPolicy(output, conservative: true);
            SanitizerXssVectors.AssertNoExecutableMarkup(output, $"fuzz '{fuzz}' must not produce executable markup");
        }
    }

    [Fact]
    public void Sanitize_IsIdempotentOnItsOwnOutput()
    {
        foreach (var fuzz in SanitizerXssVectors.Fuzz(200, seed: 7))
        {
            var once = Sanitizer.Sanitize(fuzz);
            Sanitizer.Sanitize(once).Should().Be(once, "sanitizing safe markup must be a no-op for '{0}'", fuzz);
        }
    }

    [Fact]
    public void Sanitize_DoesNotDisproportionallySlowDownOnAdversarialInput()
    {
        var adversarial = new string('<', 100_000);

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var output = Sanitizer.Sanitize(adversarial);
        elapsed.Stop();

        output.Should().NotContain("<");
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "the tokenizer is linear, not backtracking");
    }

    [Fact]
    public void SanitizeTemplateContent_KeepsCodeSpansVerbatim()
    {
        const string content = "<b>{{ price | math.format \"N2\" }}</b>{% if active %}yes{% end %}";

        var sanitized = Sanitizer.SanitizeTemplateContent(content);

        sanitized.Should().Contain("{{ price | math.format \"N2\" }}");
        sanitized.Should().Contain("{% if active %}").And.Contain("{% end %}");
        sanitized.Should().Contain("<b>").And.Contain("</b>");
    }

    [Fact]
    public void SanitizeTemplateContent_BlockTagCarryingMarkup_StaysWithTheSanitizer()
    {
        // F4 rule: a block tag that could smuggle markup on its own is not masked, so its comparison
        // operator is entity-encoded — a template author must use a variable or a filter instead.
        Sanitizer.SanitizeTemplateContent("{% if a > b %}x{% end %}").Should().Be("{% if a &gt; b %}x{% end %}");
    }

    [Fact]
    public void SanitizeTemplateContent_StillEncodesMarkupOutsideCodeSpans()
    {
        Sanitizer.SanitizeTemplateContent("{{ a }}<script>alert(1)</script>")
            .Should().Be("{{ a }}");

        Sanitizer.SanitizeTemplateContent("<div>{{ a }}</div>")
            .Should().Be("&lt;div&gt;{{ a }}&lt;/div&gt;");
    }

    [Fact]
    public void SanitizeTemplateContent_FailsClosedOnForgedPlaceholders()
    {
        // A content that already carries the private-use placeholder characters or a suspicious
        // "{%{" construct is not masked — it goes through the plain sanitizer, so a forged placeholder can
        // never be expanded into a "restored" span. The private-use character itself is neutralised to
        // U+FFFD: it can never reach the generated markup.
        var forged = "\uE000" + new string('a', 32) + "_0\uE001";
        Sanitizer.SanitizeTemplateContent(forged).Should().Be("\uFFFD" + new string('a', 32) + "_0\uFFFD");
        Sanitizer.SanitizeTemplateContent("{%{ a }}").Should().Be("{%{ a }}");
    }

    [Fact]
    public void SanitizeTemplateContent_ForgedPlaceholderThroughEntities_CannotSpliceMarkup()
    {
        // The pre-check cannot see a placeholder written as a character reference; the sanitizer decodes
        // it to U+E000/U+E001 and the restore step used to delete it, turning
        // "java&#xE000;…_0&#xE001;script:alert(1)" into a live "javascript:alert(1)" href.
        var output = Sanitizer.SanitizeTemplateContent(
            "<a href=\"java&#xE000;0123456789abcdef0123456789abcdef_0&#xE001;script:alert(1)\">{{ x }}</a>");

        // The forged placeholder is *not* removed (removing it is the attack); the private-use characters
        // become U+FFFD, so the scheme can never be assembled and the value stays a relative, inert URL.
        output.Should().NotContainEquivalentOf("\"javascript:");
        SanitizerXssVectors.AssertNoMaskArtifacts(output, "the forged placeholder must be neutralised");
        SanitizerXssVectors.AssertNoExecutableMarkup(output, "no executable href may be spliced together");
        SanitizerXssVectors.AssertOutputWithinPolicy(output, conservative: true);
    }

    [Fact]
    public void SanitizeTemplateContent_SurvivesTheSharedTemplateXssSet()
    {
        foreach (var payload in SanitizerXssVectors.TemplatePayloads)
        {
            var output = Sanitizer.SanitizeTemplateContent(payload);

            SanitizerXssVectors.AssertNoExecutableMarkup(output,
                $"payload '{payload}' must not produce executable markup through the masker");
            SanitizerXssVectors.AssertOutputWithinPolicy(output, conservative: true);
            SanitizerXssVectors.AssertNoMaskArtifacts(output, $"payload '{payload}' left a placeholder behind");
            output.Should().NotContainEquivalentOf("\"javascript:",
                "the placeholder removal must never assemble the scheme of '{0}'", payload);
            output.Should().NotContainEquivalentOf("\"//",
                "the placeholder removal must never assemble a protocol-relative URL of '{0}'", payload);
        }
    }

    [Fact]
    public void SanitizeTemplateContent_Fuzz_OutputNeverEscapesThePolicy()
    {
        foreach (var fuzz in SanitizerXssVectors.Fuzz(600))
        {
            var output = Sanitizer.SanitizeTemplateContent(fuzz);

            SanitizerXssVectors.AssertOutputWithinPolicy(output, conservative: true);
            SanitizerXssVectors.AssertNoExecutableMarkup(output,
                $"fuzz '{fuzz}' must not produce executable markup through the masker");
            SanitizerXssVectors.AssertNoMaskArtifacts(output, $"fuzz '{fuzz}' left a placeholder behind");
        }
    }

    [Fact]
    public void HtmlSanitizerPolicy_IsTheSingleSourceOfTheAllowLists()
    {
        HtmlSanitizerPolicy.AllowedTags.Should().Contain(["b", "i", "u", "strong", "em", "a", "br", "span", "p", "ul", "ol", "li"]);
        HtmlSanitizerPolicy.AllowedTags.Should().Contain(["h1", "h2", "h3", "h4", "h5", "h6"],
            "D28: headings are an allow-listed formatting element (typography on the WYSIWYG canvas)");
        HtmlSanitizerPolicy.AllowedTags.Should().NotContain(["script", "style", "iframe", "svg", "mj-text", "mj-raw"]);
        HtmlSanitizerPolicy.AllowedAttributes.Should().NotContain(["onclick", "onerror", "srcset"]);
        HtmlSanitizerPolicy.DroppedWithContent.Should().Contain(["script", "style", "iframe", "object", "embed", "link", "meta", "base", "title"]);
        HtmlSanitizerPolicy.IsHrefAllowed("https://x").Should().BeTrue();
        HtmlSanitizerPolicy.IsHrefAllowed("javascript:alert(1)").Should().BeFalse();
        HtmlSanitizerPolicy.IsSafeStyle("color:red").Should().BeTrue();
        HtmlSanitizerPolicy.IsSafeStyle("background:url(javascript:alert(1))").Should().BeFalse();
    }

    [Fact]
    public void Sanitize_NeutralisesC0ControlsAndLoneSurrogates()
    {
        // A NUL or another C0 control (tab/LF/CR excepted) and a lone surrogate are not text a browser keeps
        // verbatim: AttributeValue/Text normalisation drops them, and a dropped byte inside "java\0script:"
        // is exactly the smuggling a URL check must not lose to. Replace them with U+FFFD so the value the
        // policy judged is the value that is emitted.
        Sanitizer.Sanitize("a\0b").Should().Be("a\uFFFDb");
        Sanitizer.Sanitize("a\u0001b").Should().Be("a\uFFFDb");
        Sanitizer.Sanitize("a\u001Fb").Should().Be("a\uFFFDb");
        Sanitizer.Sanitize("a\uD800b").Should().Be("a\uFFFDb");
        Sanitizer.Sanitize("a\uDC00b").Should().Be("a\uFFFDb");

        // Whitespace that is legitimate in markup is untouched.
        Sanitizer.Sanitize("a\tb\nc\rd").Should().Be("a\tb\nc\rd");

        // The same normalisation applies to an attribute value that ends up in the output.
        Sanitizer.Sanitize("<a href=\"https://x/\0y\">l</a>").Should().Be("<a href=\"https://x/\uFFFDy\">l</a>");
    }

    [Fact]
    public void Sanitize_SelfClosingOnlyOnTheSlashGreaterThanPair()
    {
        // WHATWG: only "/>" closes a start tag. A stray "/" is a parse error that is ignored, so
        // "<b/ >x" opens a <b> (its content is inside) instead of emitting an empty <b></b> before x.
        Sanitizer.Sanitize("<b/ >x").Should().Be("<b>x</b>");
        Sanitizer.Sanitize("<b /x>y").Should().Be("<b>y</b>");
        Sanitizer.Sanitize("<b/>x").Should().Be("<b></b>x");
    }

    [Fact]
    public void Sanitize_EmitsOnlyThePolicyAttributes()
    {
        // The conservative implementation used to hardcode "href": the allow-list is
        // HtmlSanitizerPolicy.ConservativeAttributes, so the two cannot drift.
        HtmlSanitizerPolicy.ConservativeAttributes.Should().BeEquivalentTo(["href"]);

        var output = Sanitizer.Sanitize("<a href=\"https://x\" title=\"t\" target=\"_blank\">l</a>");

        output.Should().Be("<a href=\"https://x\">l</a>");
        SanitizerXssVectors.AssertOutputWithinPolicy(output, conservative: true);
    }

    [Fact]
    public void Sanitize_EmptyInput_ReturnsEmpty()
    {
        Sanitizer.Sanitize(string.Empty).Should().BeEmpty();
        Sanitizer.SanitizeTemplateContent(string.Empty).Should().BeEmpty();
    }
}
