using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.Testing;

namespace Tempo.Blazor.EmailTemplates.Engine.Tests.Rendering;

/// <summary>
/// D12/D22: the authoritative AngleSharp sanitizer and the BCL
/// <see cref="ConservativeHtmlContentSanitizer"/> are held to the SAME XSS / mutation-XSS set, and the
/// conservative output may only differ towards something stricter — never towards something that runs.
/// </summary>
public class SanitizerParityTests
{
    private static readonly IHtmlContentSanitizer AngleSharp = AngleSharpHtmlContentSanitizer.Instance;
    private static readonly IHtmlContentSanitizer Conservative = ConservativeHtmlContentSanitizer.Instance;

    [Fact]
    public void AngleSharp_SurvivesTheSharedXssSet()
    {
        foreach (var payload in SanitizerXssVectors.Payloads)
        {
            var output = AngleSharp.Sanitize(payload);

            SanitizerXssVectors.AssertNoExecutableMarkup(output, $"payload '{payload}' must not produce executable markup");
            SanitizerXssVectors.AssertOutputWithinPolicy(output, conservative: false);
        }
    }

    [Fact]
    public void BothImplementations_RefuseEveryExecutableVectorTheSameWay()
    {
        foreach (var payload in SanitizerXssVectors.Payloads)
        {
            var angle = AngleSharp.Sanitize(payload);
            var conservative = Conservative.Sanitize(payload);

            // The arbiter is the shared policy check (no live tag, no event-handler attribute, no
            // executable URL in an attribute) — a naive "javascript:" substring would also flag the
            // INERT escaped text the conservative implementation emits for an unknown tag.
            SanitizerXssVectors.AssertNoExecutableMarkup(angle, $"AngleSharp let '{payload}' through as '{angle}'");
            SanitizerXssVectors.AssertNoExecutableMarkup(conservative,
                $"the conservative sanitizer let '{payload}' through as '{conservative}'");
        }
    }

    [Fact]
    public void AngleSharp_Fuzz_OutputNeverEscapesThePolicy()
    {
        foreach (var fuzz in SanitizerXssVectors.Fuzz(400, seed: 424242))
        {
            var output = AngleSharp.Sanitize(fuzz);

            SanitizerXssVectors.AssertOutputWithinPolicy(output, conservative: false);
            SanitizerXssVectors.AssertNoExecutableMarkup(output, $"fuzz '{fuzz}' must not produce executable markup");
        }
    }

    [Fact]
    public void BothImplementations_MaskCodeSpansIdentically()
    {
        const string content = "<b>{{ price | math.format \"N2\" }}</b>{% if active %}yes{% end %}";

        AngleSharp.SanitizeTemplateContent(content).Should().Be(Conservative.SanitizeTemplateContent(content));
    }

    [Fact]
    public void BothImplementations_SurviveTheSharedTemplateXssSet()
    {
        foreach (var payload in SanitizerXssVectors.TemplatePayloads)
        {
            var angle = AngleSharp.SanitizeTemplateContent(payload);
            var conservative = Conservative.SanitizeTemplateContent(payload);

            SanitizerXssVectors.AssertNoExecutableMarkup(angle,
                $"AngleSharp let '{payload}' through SanitizeTemplateContent as '{angle}'");
            SanitizerXssVectors.AssertNoExecutableMarkup(conservative,
                $"the conservative sanitizer let '{payload}' through SanitizeTemplateContent as '{conservative}'");
            SanitizerXssVectors.AssertOutputWithinPolicy(angle, conservative: false);
            SanitizerXssVectors.AssertOutputWithinPolicy(conservative, conservative: true);
            SanitizerXssVectors.AssertNoMaskArtifacts(angle, $"AngleSharp left a placeholder of '{payload}' behind");
            SanitizerXssVectors.AssertNoMaskArtifacts(conservative,
                $"the conservative sanitizer left a placeholder of '{payload}' behind");
            angle.Should().NotContainEquivalentOf("\"javascript:", "splicing '{0}' together must fail closed", payload);
            conservative.Should().NotContainEquivalentOf("\"javascript:",
                "splicing '{0}' together must fail closed", payload);
            angle.Should().NotContainEquivalentOf("\"//", "splicing '{0}' together must fail closed", payload);
            conservative.Should().NotContainEquivalentOf("\"//", "splicing '{0}' together must fail closed", payload);
        }
    }

    [Fact]
    public void BothImplementations_TemplateFuzz_OutputNeverEscapesThePolicy()
    {
        foreach (var fuzz in SanitizerXssVectors.Fuzz(600))
        {
            var angle = AngleSharp.SanitizeTemplateContent(fuzz);
            var conservative = Conservative.SanitizeTemplateContent(fuzz);

            SanitizerXssVectors.AssertOutputWithinPolicy(angle, conservative: false);
            SanitizerXssVectors.AssertOutputWithinPolicy(conservative, conservative: true);
            SanitizerXssVectors.AssertNoExecutableMarkup(angle,
                $"AngleSharp fuzz '{fuzz}' must not produce executable markup through the masker");
            SanitizerXssVectors.AssertNoExecutableMarkup(conservative,
                $"conservative fuzz '{fuzz}' must not produce executable markup through the masker");
            SanitizerXssVectors.AssertNoMaskArtifacts(angle, $"AngleSharp fuzz '{fuzz}' left a placeholder behind");
            SanitizerXssVectors.AssertNoMaskArtifacts(conservative,
                $"conservative fuzz '{fuzz}' left a placeholder behind");
        }
    }

    [Fact]
    public void ConservativeIsStricterOnFormatting_NotLooserOnSafety()
    {
        // The documented UX difference (D22): AngleSharp unwraps an unknown container and keeps its text,
        // the conservative implementation escapes the tag as visible text. Both are inert.
        const string content = "<div>hello</div>";

        AngleSharp.Sanitize(content).Should().Be("hello");
        Conservative.Sanitize(content).Should().Be("&lt;div&gt;hello&lt;/div&gt;");
    }

    [Fact]
    public void BothImplementations_KeepTheAllowListedFormatting()
    {
        const string content = "<b>bold</b><i>it</i><a href=\"https://x/y\">link</a><br>";

        AngleSharp.Sanitize(content).Should().Contain("<b>bold</b>").And.Contain("<i>it</i>");
        Conservative.Sanitize(content).Should().Be(content);
    }

    /// <summary>
    /// D28: <c>h1</c>–<c>h6</c> are allow-listed as a formatting element (the WYSIWYG canvas needs the
    /// author's typography) and behave exactly like <c>b</c>/<c>i</c>: the tag and its text survive, every
    /// attribute — <c>onclick</c>, <c>style</c> — is dropped by BOTH implementations.
    /// </summary>
    [Fact]
    public void BothImplementations_AllowHeadingsWithoutAnyAttribute()
    {
        const string heading = "<h1 onclick=x style=\"background:url(javascript:alert(1))\">Ahoj</h1>";

        AngleSharp.Sanitize(heading).Should().Be("<h1>Ahoj</h1>");
        Conservative.Sanitize(heading).Should().Be("<h1>Ahoj</h1>");
    }

    /// <summary>
    /// D28: an allow-listed heading is not a hole — markup nested inside it is judged exactly as it would be
    /// at the top level, so <c>&lt;h2&gt;&lt;img onerror&gt;&lt;/h2&gt;</c> keeps the heading and never keeps
    /// the image.
    /// </summary>
    [Fact]
    public void BothImplementations_DoNotLetAHeadingSmuggleMarkupThrough()
    {
        const string payload = "<h2><img src=x onerror=alert(1)></h2>";

        var angle = AngleSharp.Sanitize(payload);
        var conservative = Conservative.Sanitize(payload);

        angle.Should().StartWith("<h2>").And.EndWith("</h2>");
        conservative.Should().StartWith("<h2>").And.EndWith("</h2>");
        foreach (var output in new[] { angle, conservative })
        {
            output.Should().NotContainEquivalentOf("<img", "a heading must not smuggle an element through");
            SanitizerXssVectors.AssertNoExecutableMarkup(output, $"'{payload}' must stay inert");
            SanitizerXssVectors.AssertOutputWithinPolicy(output, output == conservative);
        }
    }

    [Fact]
    public void BothImplementations_BlockTheSameUrlSchemes()
    {
        foreach (var scheme in new[] { "javascript:alert(1)", "vbscript:x", "data:text/html,x", "//evil/x", "java\tscript:alert(1)" })
        {
            var markup = $"<a href=\"{scheme}\">x</a>";

            foreach (var output in new[] { AngleSharp.Sanitize(markup), Conservative.Sanitize(markup) })
            {
                output.Should().NotContainEquivalentOf("javascript:", "must refuse '{0}'", scheme);
                output.Should().NotContainEquivalentOf("vbscript:", "must refuse '{0}'", scheme);
                output.Should().NotContainEquivalentOf("data:text", "must refuse '{0}'", scheme);
                output.Should().NotContainEquivalentOf("//evil", "must refuse '{0}'", scheme);
            }
        }
    }
}
