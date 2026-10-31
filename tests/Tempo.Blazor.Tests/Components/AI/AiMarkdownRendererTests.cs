using FluentAssertions;
using Tempo.Blazor.AI.Markdown;
using Tempo.Blazor.Testing;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Security and formatting contract of <see cref="AiMarkdownRenderer"/> — the pipeline every AI answer
/// is rendered through. Model output is untrusted, so the assertions are deliberately paranoid: no raw
/// HTML survives, only http/https/mailto/relative link targets survive (everything else collapses to
/// <c>#</c>), images are limited to http(s), every link carries
/// <c>target="_blank" rel="noopener noreferrer nofollow"</c>, and the whole shared XSS vector set from
/// <c>tests/Shared/XssPayloads.cs</c> produces no active content.
/// </summary>
public sealed class AiMarkdownRendererTests
{
    private static readonly AiMarkdownRenderOptions Options = new()
    {
        CopyCodeLabel = "Copy code",
        CopyCodeWithLanguageLabel = "Copy {0} code",
        CopiedLabel = "Copied",
        CitationLabelFormat = "Source {0}",
        TableLabel = "Table",
    };

    private static string Render(string? markdown, AiMarkdownRenderOptions? options = null)
        => new AiMarkdownRenderer().Render(markdown, options ?? Options);

    // ── Formatting ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Headings_And_Lists_Render()
    {
        var html = Render("# Title\n\n- one\n- two\n\n1. first\n2. second");

        html.Should().Contain("<h1");
        html.Should().Contain(">Title</h1>");
        html.Should().Contain("<ul>").And.Contain("<li>one</li>").And.Contain("<li>two</li>");
        html.Should().Contain("<ol>").And.Contain("<li>first</li>");
    }

    [Fact]
    public void Tables_RenderInsideAScrollContainer()
    {
        var html = Render("| Name | Value |\n| --- | ---: |\n| a | 1 |");

        html.TrimEnd().Should().StartWith("<div class=\"tm-markdown-view__table\" tabindex=\"0\"");
        html.Should().Contain("<table>");
        html.Should().Contain("<th>Name</th>");
        html.Should().Contain("<td>a</td>");
        html.TrimEnd().Should().EndWith("</table>\n</div>",
            "the scroll container must wrap the whole table, not sit inside it");
    }

    [Fact]
    public void TableWrapper_IsAKeyboardReachableNamedRegion()
    {
        var html = Render("| Name | Value |\n| --- | --- |\n| a | 1 |");

        html.Should().Contain("class=\"tm-markdown-view__table\" tabindex=\"0\" role=\"region\"",
            "a horizontally scrolling container whose content a mouse can reach must be reachable by "
            + "keyboard too (WCAG 2.1.1), and a region needs a name (WCAG 1.3.1 / 4.1.2)");
        html.Should().Contain("aria-label=\"Table\"");
    }

    [Fact]
    public void TableWrapper_WithoutALabel_KeepsTheFocusStopButNotANamelessRegion()
    {
        var html = Render("| a |\n| --- |\n| 1 |", new AiMarkdownRenderOptions());

        html.Should().Contain("class=\"tm-markdown-view__table\" tabindex=\"0\"");
        html.Should().NotContain("role=\"region\"",
            "a region without an accessible name is a landmark the screen-reader user cannot identify");
        html.Should().NotContain("aria-label");
    }

    [Fact]
    public void FencedCodeBlock_CarriesLanguageAndAnEscapedCodeFence()
    {
        var html = Render("```csharp\nvar x = \"<script>\";\n```");

        html.Should().Contain("class=\"tm-markdown-view__code\"");
        html.Should().Contain("<code class=\"language-csharp\">");
        html.Should().Contain("&lt;script&gt;");
        html.Should().NotContain("<script");
        html.Should().Contain("tm-markdown-view__code-language");
        html.Should().Contain(">csharp</span>");
    }

    [Fact]
    public void CodeBlock_HasACopyAffordanceWithTheSuppliedLocalizedLabels()
    {
        var html = Render("```cs\nvar x = 1;\n```");

        html.Should().Contain("data-copy-code");
        html.Should().Contain("aria-label=\"Copy cs code\"");
        html.Should().Contain("data-copied-label=\"Copied\"");

        var withoutLanguage = Render("```\nplain\n```");
        withoutLanguage.Should().Contain("aria-label=\"Copy code\"");
        withoutLanguage.Should().NotContain("tm-markdown-view__code-language");
    }

    [Fact]
    public void CodeBlockPre_IsAKeyboardScrollableRegion()
    {
        var html = Render("```cs\nvar x = 1;\n```");

        html.Should().Contain("<pre tabindex=\"0\">",
            "a long code line scrolls inside <pre>, so the scroller has to be keyboard reachable");
    }

    [Fact]
    public void CopyButton_KeepsItsAccessibleNameAndOwnsALiveRegionBesideIt()
    {
        var html = Render("```cs\nvar x = 1;\n```");

        var header = html.IndexOf("tm-markdown-view__code-header", StringComparison.Ordinal);
        var button = html.IndexOf("<button", header, StringComparison.Ordinal);
        var buttonEnd = html.IndexOf("</button>", button, StringComparison.Ordinal);
        var status = html.IndexOf("<span class=\"tm-sr-only\" role=\"status\"></span>", buttonEnd, StringComparison.Ordinal);
        var headerEnd = html.IndexOf("</div>", header, StringComparison.Ordinal);

        status.Should().BeGreaterThan(buttonEnd,
            "the live region is a SIBLING of the button: a role=status nested inside the control is not "
            + "reliably announced, and it would also join the button's accessible name");
        status.Should().BeLessThan(headerEnd, "the live region belongs to the code header row");
        html.Substring(button, buttonEnd - button).Should().Contain("aria-label=\"Copy cs code\"",
            "the accessible name stays the action's name; the copied state is announced, not renamed");
    }

    [Fact]
    public void InlineCode_Renders()
    {
        Render("Use `dotnet test` here.").Should().Contain("<code>dotnet test</code>");
    }

    [Fact]
    public void UnclosedCodeFenceFromAStreamingAnswer_RendersStably()
    {
        var first = Render("Intro\n\n```csharp\nvar x = 1;");
        var second = Render("Intro\n\n```csharp\nvar x = 1;");

        first.Should().Be(second, "a partially streamed fence must not flicker between renders");
        first.Should().Contain("<pre tabindex=\"0\">");
        first.Should().Contain("<code class=\"language-csharp\">");
    }

    [Fact]
    public void EmptyMarkdown_RendersNothing()
    {
        Render(null).Should().BeEmpty();
        Render("   ").Should().BeEmpty();
    }

    // ── Raw HTML is never markup ────────────────────────────────────────────────────────────

    [Fact]
    public void RawHtml_IsEscapedInsteadOfRendered()
    {
        var html = Render("<script>alert(1)</script>\n\n<b>bold</b>\n\n<img src=x onerror=alert(1)>");

        html.Should().NotContain("<script");
        html.Should().NotContain("<b>");
        html.Should().NotContain("<img");
        html.Should().Contain("&lt;script&gt;");
        html.Should().Contain("&lt;b&gt;bold&lt;/b&gt;");
    }

    [Fact]
    public void GenericAttributeSyntax_DoesNotInjectAttributes()
    {
        // UseGenericAttributes is deliberately NOT part of the pipeline: it writes arbitrary
        // attribute names, so "# H {onclick=alert(1)}" would otherwise emit an event handler.
        var html = Render("# Heading {onclick=alert(1)}");

        html.Should().Contain("{onclick=alert(1)}", "the brace syntax stays literal text");
        html.Should().NotContain("<h1 onclick");
        html.Should().NotContain(" onclick=");
    }

    // ── Link policy ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("//evil.example/x")]
    public void UnsafeLinkSchemes_CollapseToAHashHref(string url)
    {
        var html = Render($"[click]({url.Replace(" ", "%20", StringComparison.Ordinal)})");

        html.Should().Contain("href=\"#\"");
        html.Should().NotContainEquivalentOf("javascript:");
        html.Should().NotContainEquivalentOf("vbscript:");
        html.Should().NotContainEquivalentOf("file://");
        html.Should().NotContain("//evil.example");
    }

    [Theory]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("java\nscript:alert(1)")]
    [InlineData("\u0001javascript:alert(1)")]
    public void WhitespaceAndControlCharacterSmuggling_NeverYieldsAnExecutableHref(string url)
    {
        var html = Render($"[click](<{url}>)");

        html.Should().NotContainEquivalentOf("javascript:");
        html.Should().NotContainEquivalentOf("href=\"java");
    }

    [Theory]
    [InlineData("https://example.test/a")]
    [InlineData("http://example.test/a")]
    [InlineData("mailto:someone@example.test")]
    [InlineData("/docs/a")]
    [InlineData("#section")]
    [InlineData("?q=1")]
    public void AllowedLinkTargets_KeepTheirHref(string url)
    {
        Render($"[click]({url})").Should().Contain($"href=\"{url}\"");
    }

    [Fact]
    public void ExternalLinks_CarryBlankTargetAndHardenedRel()
    {
        var html = Render("[docs](https://example.test/a)");

        html.Should().Contain("target=\"_blank\"");
        html.Should().Contain("rel=\"noopener noreferrer nofollow\"");
    }

    [Fact]
    public void Autolinks_GetTheSameHardening()
    {
        var html = Render("<https://example.test/a>");

        html.Should().Contain("href=\"https://example.test/a\"");
        html.Should().Contain("rel=\"noopener noreferrer nofollow\"");
        html.Should().Contain("target=\"_blank\"");
    }

    // ── Images ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HttpImages_Render_AndNonHttpImagesDegradeToTheirAltText()
    {
        Render("![logo](https://example.test/logo.png)")
            .Should().Contain("<img src=\"https://example.test/logo.png\"");

        var unsafeImage = Render("![tracker](data:image/gif;base64,R0lGOD)");
        unsafeImage.Should().NotContain("<img");
        unsafeImage.Should().NotContain("data:image");
        unsafeImage.Should().Contain("tracker");
    }

    // ── Citations ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CiteLinks_BecomeCitationChipsWithASourceId()
    {
        var html = Render("Answer [1](cite:src-1) and [2](cite:src-2).");

        html.Should().Contain("class=\"tm-markdown-view__citation\"");
        html.Should().Contain("data-source-id=\"src-1\"");
        html.Should().Contain("data-source-id=\"src-2\"");
        html.Should().Contain("aria-label=\"Source 1\"");
        html.Should().NotContain("cite:src-1");
        html.Should().NotContain("href=\"cite:");
    }

    [Fact]
    public void ACiteLinkWithoutAnId_IsNotAChip()
    {
        var html = Render("Answer [1](cite:).");

        html.Should().NotContain("tm-markdown-view__citation");
        html.Should().Contain("1");
    }

    [Fact]
    public void ACitationLabelWithoutAFormat_OmitsTheAriaLabel()
    {
        var html = Render("Answer [1](cite:src-1).", new AiMarkdownRenderOptions());

        html.Should().Contain("data-source-id=\"src-1\"");
        html.Should().NotContain("Source 1");
    }

    // ── The shared XSS vector set ───────────────────────────────────────────────────────────

    [Fact]
    public void TheSharedXssVectorSet_ProducesNoActiveContent()
    {
        var renderer = new AiMarkdownRenderer();

        foreach (var payload in XssPayloads.Payloads)
        {
            var html = renderer.Render(payload, Options);

            XssPayloads.AssertNoExecutableMarkup(
                html, $"markdown payload '{payload}' must not produce executable markup");
        }
    }

    [Fact]
    public void TheSharedXssVectorSet_ProducesNoActiveContentAsLinkOrImageTargets()
    {
        var renderer = new AiMarkdownRenderer();

        foreach (var payload in XssPayloads.Payloads)
        {
            // The same payloads forced through the LINK and IMAGE parser, not just through the
            // literal-HTML path.
            var asLink = renderer.Render($"[x]({Uri.EscapeDataString(payload)})", Options);
            var asRawLink = renderer.Render($"[x](<{payload}>)", Options);
            var asImage = renderer.Render($"![x]({Uri.EscapeDataString(payload)})", Options);

            foreach (var html in new[] { asLink, asRawLink, asImage })
            {
                XssPayloads.AssertNoExecutableMarkup(
                    html, $"payload '{payload}' used as a link/image target must not produce executable markup");
            }
        }
    }

    [Fact]
    public void TheFuzzSet_ProducesNoActiveContent()
    {
        var renderer = new AiMarkdownRenderer();

        foreach (var fuzz in XssPayloads.Fuzz(200, seed: 20261031))
        {
            XssPayloads.AssertNoExecutableMarkup(
                renderer.Render(fuzz, Options), $"fuzz '{fuzz}' must not produce executable markup");
        }
    }
}
