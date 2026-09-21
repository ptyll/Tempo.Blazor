using FluentAssertions;
using Tempo.Blazor.Components.NotionEditor.Services;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Block content is written straight into the DOM with innerHTML, so it must be sanitized —
/// but the editor's own inline chrome (status chips, mentions, inline math, comment highlights)
/// has to survive, otherwise sanitizing would destroy the user's content.
/// </summary>
public sealed class NotionBlockContentSanitizerTests
{
    // ── Attacks must not survive ────────────────────────────────────────────

    [Theory]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<iframe src=\"https://evil.test\"></iframe>")]
    [InlineData("<object data=\"x\"></object>")]
    [InlineData("<svg onload=alert(1)></svg>")]
    public void SanitizeBlockContent_RemovesUnsafeElements(string html)
    {
        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().NotContain("<img");
        sanitized.Should().NotContain("<script");
        sanitized.Should().NotContain("<iframe");
        sanitized.Should().NotContain("<object");
        sanitized.Should().NotContain("<svg");
        sanitized.Should().NotContainEquivalentOf("onerror");
        sanitized.Should().NotContainEquivalentOf("onload");
    }

    [Theory]
    [InlineData("<span onclick=\"steal()\">x</span>")]
    [InlineData("<span class=\"tm-notion-status\" onmouseover=\"steal()\">x</span>")]
    [InlineData("<mark class=\"tm-notion-comment-highlight\" onfocus=\"steal()\">x</mark>")]
    public void SanitizeBlockContent_StripsEventHandlers(string html)
    {
        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().NotContainEquivalentOf("onclick");
        sanitized.Should().NotContainEquivalentOf("onmouseover");
        sanitized.Should().NotContainEquivalentOf("onfocus");
        sanitized.Should().Contain("x");
    }

    [Fact]
    public void SanitizeBlockContent_StripsStyleAttribute()
    {
        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(
            "<span class=\"tm-notion-status\" style=\"position:fixed;top:0\">x</span>");

        sanitized.Should().NotContainEquivalentOf("style=");
    }

    [Fact]
    public void SanitizeBlockContent_DropsUnknownSpanClasses()
    {
        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(
            "<span class=\"attacker-class\">x</span>");

        sanitized.Should().NotContain("attacker-class");
        sanitized.Should().Contain("x");
    }

    [Theory]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"vbscript:x\">x</a>")]
    public void SanitizeBlockContent_DropsUnsafeHref(string html)
    {
        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().NotContainEquivalentOf("javascript:");
        sanitized.Should().NotContainEquivalentOf("vbscript:");
        sanitized.Should().Contain("<a>");
    }

    // ── The editor's own inline chrome must survive ─────────────────────────

    [Fact]
    public void SanitizeBlockContent_KeepsBasicFormatting()
    {
        const string html = "<strong>b</strong><em>i</em><u>u</u><s>s</s><code>c</code><br>";

        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().Contain("<strong>b</strong>");
        sanitized.Should().Contain("<em>i</em>");
        sanitized.Should().Contain("<code>c</code>");
        sanitized.Should().Contain("<br>");
    }

    [Fact]
    public void SanitizeBlockContent_KeepsSafeLink()
    {
        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(
            "<a href=\"https://example.test\">x</a>");

        sanitized.Should().Contain("href=\"https://example.test\"");
        sanitized.Should().Contain("rel=\"noopener noreferrer\"");
    }

    [Fact]
    public void SanitizeBlockContent_KeepsInlineMath()
    {
        const string html = """<span class="tm-notion-inline-math" data-expr="x^2">x^2</span>""";

        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().Contain("tm-notion-inline-math");
        sanitized.Should().Contain("data-expr=\"x^2\"");
    }

    [Fact]
    public void SanitizeBlockContent_KeepsCommentHighlight()
    {
        const string html = """<mark class="tm-notion-comment-highlight" data-comment-id="c1" data-block-id="b1">x</mark>""";

        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().Contain("<mark");
        sanitized.Should().Contain("tm-notion-comment-highlight");
        sanitized.Should().Contain("data-comment-id=\"c1\"");
        sanitized.Should().Contain("data-block-id=\"b1\"");
    }

    [Fact]
    public void SanitizeBlockContent_KeepsStatusChip()
    {
        const string html = """<span contenteditable="false" class="tm-notion-status tm-notion-status--green" data-status-label="Done" data-status-color="green"><span class="tm-notion-status__label">Done</span></span>""";

        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().Contain("tm-notion-status--green");
        sanitized.Should().Contain("contenteditable=\"false\"");
        sanitized.Should().Contain("data-status-label=\"Done\"");
        sanitized.Should().Contain("tm-notion-status__label");
    }

    [Fact]
    public void SanitizeBlockContent_KeepsMentionChip()
    {
        const string html = """<span contenteditable="false" class="tm-notion-mention tm-notion-mention--user" data-type="user" data-id="u1" title="Alice" aria-label="Alice">@Alice</span>""";

        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().Contain("tm-notion-mention--user");
        sanitized.Should().Contain("data-id=\"u1\"");
        sanitized.Should().Contain("aria-label=\"Alice\"");
    }

    // ── Edge cases ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeBlockContent_EmptyInputYieldsEmptyOutput(string? html)
    {
        NotionInlineHtmlSanitizer.SanitizeBlockContent(html).Should().BeEmpty();
    }

    [Fact]
    public void SanitizeBlockContent_PlainTextIsUntouched()
    {
        NotionInlineHtmlSanitizer.SanitizeBlockContent("just text").Should().Be("just text");
    }

    [Fact]
    public void SanitizeBlockContent_IsIdempotent()
    {
        const string html = """Hi <strong>you</strong> <span class="tm-notion-inline-math" data-expr="x">x</span>""";

        var once = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);
        var twice = NotionInlineHtmlSanitizer.SanitizeBlockContent(once);

        twice.Should().Be(once, "sanitizing already-sanitized content must not degrade it");
    }

    [Fact]
    public void SanitizeHtmlFragment_KeepsItsNarrowWhitelist()
    {
        // The pre-existing fragment profile must not change: it still drops span and mark.
        var sanitized = NotionInlineHtmlSanitizer.SanitizeHtmlFragment(
            "<span class=\"tm-notion-status\">x</span>");

        sanitized.Should().NotContain("<span");
        sanitized.Should().Contain("x");
    }

    // ── Inline colours ─────────────────────────────────────────────────────
    //
    // The colour picker writes span[style]. Dropping the style attribute would silently discard
    // every colour the user picked the moment the block is saved.

    [Theory]
    [InlineData("""<span style="color: rgb(220, 38, 38)">red</span>""", "color: rgb(220, 38, 38)")]
    [InlineData("""<span style="background-color: #fee">hl</span>""", "background-color: #fee")]
    [InlineData("""<span style="color:red;background-color:yellow">both</span>""", "color:red")]
    public void SanitizeBlockContent_KeepsColourStyles(string html, string expectedFragment)
    {
        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().Contain(expectedFragment);
    }

    [Theory]
    [InlineData("""<span style="position: fixed; top: 0">x</span>""")]
    [InlineData("""<span style="background-image: url(javascript:alert(1))">x</span>""")]
    [InlineData("""<span style="color: expression(alert(1))">x</span>""")]
    [InlineData("""<span style="background-color: url('x')">x</span>""")]
    public void SanitizeBlockContent_DropsEverythingThatIsNotAPlainColour(string html)
    {
        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().NotContain("style=");
        sanitized.Should().Contain("x");
    }

    [Fact]
    public void SanitizeBlockContent_DoesNotKeepStyleOnTagsThatCannotCarryIt()
    {
        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(
            """<strong style="color:red">bold</strong>""");

        sanitized.Should().Be("<strong>bold</strong>");
    }

    [Theory]
    [InlineData("""<span style="color: red&quot; onmouseover=&quot;alert(1)">x</span>""")]
    [InlineData("""<span style="color: <script>">x</span>""")]
    public void SanitizeBlockContent_ColourStyleCannotEscapeItsAttribute(string html)
    {
        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().NotContain("onmouseover");
        sanitized.Should().NotContain("<script");
    }

    [Fact]
    public void SanitizeBlockContent_ColourStyleIsIdempotent()
    {
        const string html = """<span style="color: rgb(1, 2, 3)">x</span>""";

        var once = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);
        var twice = NotionInlineHtmlSanitizer.SanitizeBlockContent(once);

        twice.Should().Be(once);
        once.Should().Contain("color: rgb(1, 2, 3)");
    }

    // ── First-party inline chips (must survive round-trip) ─────────────────
    //
    // The editor writes tokens, comment-text mentions and smart-link chips into block content.
    // If the sanitizer drops their classes/data-* the chips collapse into bare text on the next
    // render — which is exactly what the seeded E2E page surfaced.

    [Fact]
    public void SanitizeBlockContent_KeepsTokenChip()
    {
        // Exact markup produced by SeedE2EMentionTokenPage.
        const string html = """Invoice deadline <span contenteditable="false" class="tm-notion-token tm-notion-token--unknown" data-key="unknown.invoice_deadline">{{unknown.invoice_deadline}}</span>""";

        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().Contain("tm-notion-token");
        sanitized.Should().Contain("tm-notion-token--unknown");
        sanitized.Should().Contain("data-key=\"unknown.invoice_deadline\"");
        sanitized.Should().Contain("contenteditable=\"false\"");
        sanitized.Should().Contain("{{unknown.invoice_deadline}}");
    }

    [Fact]
    public void SanitizeBlockContent_KeepsJsTokenChipChildren()
    {
        // Exact structure _createTokenChip builds in notion-editor.js.
        const string html = """<span contenteditable="false" class="tm-notion-token tm-notion-token--unknown" data-key="unknown.invoice_deadline"><span class="tm-notion-token__text">{{ unknown.invoice_deadline }}</span><span class="tm-notion-token__delete" aria-label="Remove token" role="button">×</span></span>""";

        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().Contain("tm-notion-token__text");
        sanitized.Should().Contain("tm-notion-token__delete");
        sanitized.Should().Contain("role=\"button\"");
        sanitized.Should().Contain("aria-label=\"Remove token\"");
        sanitized.Should().Contain("data-key=\"unknown.invoice_deadline\"");
    }

    [Fact]
    public void SanitizeBlockContent_KeepsCommentTextMention()
    {
        // Exact markup produced by MentionParser.ReplaceMentions for comment text.
        const string html = """cc <span class="tm-mention" data-user-id="u42">@bob</span>""";

        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().Contain("tm-mention");
        sanitized.Should().Contain("data-user-id=\"u42\"");
        sanitized.Should().Contain("@bob");
    }

    [Fact]
    public void SanitizeBlockContent_KeepsSmartLinkChip()
    {
        // Exact markup notion-editor.js writes for a pasted link (span-favicon variant).
        const string html = """<a class="tm-notion-smart-link" href="https://example.test/spec" target="_blank" rel="noopener noreferrer" contenteditable="false"><span class="tm-notion-smart-link__favicon" aria-hidden="true"></span><span class="tm-notion-smart-link__title">Spec</span><span class="tm-notion-smart-link__domain">example.test</span></a>""";

        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().Contain("tm-notion-smart-link\"");
        sanitized.Should().Contain("tm-notion-smart-link__favicon");
        sanitized.Should().Contain("tm-notion-smart-link__title");
        sanitized.Should().Contain("tm-notion-smart-link__domain");
        sanitized.Should().Contain("href=\"https://example.test/spec\"");
        sanitized.Should().Contain("target=\"_blank\"");
        sanitized.Should().Contain("rel=\"noopener noreferrer\"");
        sanitized.Should().Contain("contenteditable=\"false\"");
    }

    [Fact]
    public void SanitizeBlockContent_KeepsCommentHighlightStateModifier()
    {
        const string html = """<mark class="tm-notion-comment-highlight tm-notion-comment-highlight--active" data-comment-id="c1">x</mark>""";

        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().Contain("tm-notion-comment-highlight");
        sanitized.Should().Contain("tm-notion-comment-highlight--active");
    }

    [Fact]
    public void SanitizeBlockContent_KeepsSmartLinkFaviconImage()
    {
        // Exact markup notion-editor.js writes when the resolver returns a favicon.
        const string html = """<a class="tm-notion-smart-link" href="https://example.test" target="_blank" contenteditable="false"><img class="tm-notion-smart-link__favicon" src="https://example.test/favicon.ico" alt="" loading="lazy"><span class="tm-notion-smart-link__title">Spec</span></a>""";

        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().Contain("tm-notion-smart-link__favicon");
        sanitized.Should().Contain("src=\"https://example.test/favicon.ico\"");
        sanitized.Should().Contain("loading=\"lazy\"");
    }

    [Theory]
    [InlineData("""<img src="javascript:alert(1)" class="tm-notion-smart-link__favicon">""")] // scriptable src
    [InlineData("""<img src="https://example.test/x.png" class="attacker">""")]              // foreign class
    [InlineData("""<img src="https://example.test/x.png">""")]                                // no class at all
    public void SanitizeBlockContent_DropsImageThatIsNotFaviconChrome(string html)
    {
        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

        sanitized.Should().NotContain("<img");
    }

    // ── The chrome extensions must not open a new hole ──────────────────────

    [Fact]
    public void SanitizeBlockContent_DropsUnsafeAnchorTarget()
    {
        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(
            """<a class="tm-notion-smart-link" href="https://example.test" target="evil">x</a>""");

        sanitized.Should().NotContain("target=");
        sanitized.Should().Contain("tm-notion-smart-link");
    }

    [Fact]
    public void SanitizeBlockContent_StillStripsHandlersOnChips()
    {
        var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(
            """<span class="tm-notion-token" data-key="k" onclick="steal()">{{k}}</span>""");

        sanitized.Should().NotContainEquivalentOf("onclick");
        sanitized.Should().Contain("tm-notion-token");
        sanitized.Should().Contain("data-key=\"k\"");
    }
}
