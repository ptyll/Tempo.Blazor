using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.EmailTemplates.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 9A review fixes — the code findings of the code+arch and UX review, one test class per review
/// round so the phase's original specs stay readable.
/// <para>
/// The two findings that matter most are <b>token masking</b> (a <c>{{ … }}</c> span inside an attribute
/// used to destroy the tag stream around it, so a link rendered as literal <c>&lt;a</c> text) and
/// <b>image URLs</b> (the canvas accepted any allow-listed URL as an image source, including a
/// <c>mailto:</c>/<c>tel:</c> link or a relative API route, which the browser then requested).
/// Everything else here is the same kind of "the canvas must not lie about the e-mail" assertion.
/// </para>
/// </summary>
public class TmEmailCanvasReviewFixTests : EmailCanvasTestBase
{
    /// <summary>
    /// The canvas resolves its texts through the package's own <c>ITmEmailLocalizer</c> (never the host's
    /// <c>IStringLocalizerFactory</c>), so an echoing stub keeps every "which reason/message" assertion
    /// readable and culture-independent.
    /// </summary>
    public TmEmailCanvasReviewFixTests()
    {
        Services.RemoveAll<ITmEmailLocalizer>();
        Services.AddSingleton<ITmEmailLocalizer>(new EchoEmailLocalizer());
    }

    private sealed class EchoEmailLocalizer : ITmEmailLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments]
            => arguments.Length == 0 ? key : key + "[" + string.Join(",", arguments) + "]";
    }
    // ── Code 1: a token must not destroy the tag stream around it ──────────────────────────

    /// <summary>
    /// The masker in the sanitizer keeps a <c>{{ … }}</c> span verbatim, so a token inside an ATTRIBUTE
    /// reaches the canvas as part of the tag. Reducing tags BEFORE the tokens were masked meant the tag
    /// regexp swallowed the token and the rest of the tag, and the <c>&lt;a</c> opening leaked into the
    /// text as <c>&amp;lt;a</c>. Both sanitizers must produce the same inert link.
    /// </summary>
    [Theory]
    [InlineData("angle")]
    [InlineData("conservative")]
    public void A_token_inside_an_href_keeps_the_link_and_loses_the_attribute(string sanitizer)
    {
        UseSanitizer(sanitizer);

        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "<p>Hi <a href=\"{{ unsubscribe_url }}\">Unsubscribe</a></p>",
        }));

        cut.Markup.Should().NotContain("&lt;a", "the anchor must never leak into the text as literal markup");
        cut.Markup.Should().NotContain("&lt;/a");
        cut.Markup.Should().NotContain("href=", "the canvas never emits an href");

        var link = Body(cut).QuerySelector("span.tm-email-block__link");
        link.Should().NotBeNull("the link TEXT survives as an inert span");
        link!.TextContent.Should().Be("Unsubscribe");

        CountTags(Body(cut).InnerHtml, "span").Should().Be((1, 1), "one span opened, one closed — a balanced stream");
    }

    /// <summary>
    /// A token inside a <c>style</c> attribute disappears WITH the attribute (which is dropped anyway) and
    /// the element around it survives: the mask has to be positional, not textual.
    /// </summary>
    [Theory]
    [InlineData("angle")]
    [InlineData("conservative")]
    public void A_token_inside_a_dropped_attribute_does_not_eat_the_element(string sanitizer)
    {
        UseSanitizer(sanitizer);

        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "<span style=\"color:{{ c }}\">x</span>",
        }));

        var span = Body(cut).QuerySelector("span");
        span.Should().NotBeNull("the element survives even though its attribute carried a token");
        span!.GetAttribute("style").Should().BeNull();
        span.TextContent.Should().Be("x");
        cut.Markup.Should().NotContain("&lt;");
        CountTags(Body(cut).InnerHtml, "span").Should().Be((1, 1));
    }

    /// <summary>
    /// A token whose body is entity text is encoded ONCE: <c>HtmlEncode(HtmlDecode(token))</c>. Encoding
    /// the raw token turned <c>&amp;gt;</c> into <c>&amp;amp;gt;</c>, which the author read as
    /// <c>&amp;gt;</c> instead of <c>&gt;</c>.
    /// </summary>
    [Theory]
    [InlineData("angle")]
    [InlineData("conservative")]
    public void A_token_is_encoded_exactly_once(string sanitizer)
    {
        UseSanitizer(sanitizer);

        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "{{ a &gt; b }}",
        }));

        var chip = cut.Find(".tm-email-block__token");
        chip.TextContent.Should().Be("{{ a > b }}", "one decode + one encode never double-encodes");
        chip.GetAttribute("data-tm-token").Should().Be("a > b");
        cut.Markup.Should().NotContain("&amp;gt;");
    }

    /// <summary>A token that could make its own markup round-trip is still inert text (regression guard).</summary>
    [Theory]
    [InlineData("angle")]
    [InlineData("conservative")]
    public void A_hostile_token_body_stays_inert_after_the_mask_fix(string sanitizer)
    {
        UseSanitizer(sanitizer);

        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "<b>{{ \"<img src=x onerror=alert(1)>\" }}</b>",
        }));

        cut.FindAll("img").Should().BeEmpty();
        cut.FindAll("script").Should().BeEmpty();
        Body(cut).QuerySelector("b").Should().NotBeNull(Body(cut).InnerHtml);
        Body(cut).QuerySelector(".tm-email-block__token")!.QuerySelectorAll("*").Should().BeEmpty();
    }

    // ── Code 2: only a real absolute http(s) image may become a request ─────────────────────

    /// <summary>
    /// The canvas used to accept every URL the pipeline's allow-list accepts — including <c>mailto:</c>,
    /// <c>tel:</c> and a RELATIVE route — and turned it into <c>&lt;img src&gt;</c>. A relative value such
    /// as <c>/api/logout</c> (an import or a JSON payload can carry anything) then became a GET against the
    /// editor's own origin. Only an absolute <c>http</c>/<c>https</c> URL without a template token is an
    /// image source on the canvas; everything else is a placeholder that says so.
    /// </summary>
    [Theory]
    [InlineData("/api/logout")]
    [InlineData("x.png")]
    [InlineData("../a.png")]
    [InlineData("mailto:a@b.c")]
    [InlineData("tel:1")]
    [InlineData("{{ logo_url }}")]
    public void An_image_source_that_is_not_an_absolute_http_url_is_a_placeholder(string src)
    {
        var cut = RenderCanvas(DocWith(new EmailImageBlock { Src = src, Alt = "Logo" }));

        cut.FindAll("img").Should().BeEmpty("'{0}' must never become an image request", src);
        var placeholder = cut.Find("[data-tm-image-placeholder]");
        placeholder.Should().NotBeNull("the author must see WHY the image is not painted");
        placeholder.HasAttribute("data-tm-image-blocked").Should().BeFalse(
            "'{0}' is not loadable, not dangerous: the reason text must say so", src);
        cut.Find(".tm-email-block__image-placeholder-reason").TextContent.Should()
            .Be("EmailEditor_Canvas_ImageNotLoadable",
                "a non-http value gets its own reason, not the unsafe-address one");
    }

    /// <summary>A refused scheme keeps the stronger "unsafe address" reason (this is what the review fixed).</summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    public void An_image_source_with_a_refused_scheme_keeps_the_blocked_reason(string src)
    {
        var cut = RenderCanvas(DocWith(new EmailImageBlock { Src = src, Alt = "Logo" }));

        cut.Find("[data-tm-image-placeholder]").HasAttribute("data-tm-image-blocked").Should().BeTrue();
        cut.Markup.Should().Contain("EmailEditor_Canvas_ImageBlocked");
        cut.Markup.Should().NotContain("EmailEditor_Canvas_ImageNotLoadable");
    }

    /// <summary>An absolute https image is still painted, and it is a lazy, no-referrer request.</summary>
    [Fact]
    public void An_absolute_https_image_is_painted_as_a_lazy_no_referrer_image()
    {
        var cut = RenderCanvas(DocWith(new EmailImageBlock
        {
            Src = " https://example.com/logo.png ",
            Alt = "Company logo",
            Width = "120px",
        }));

        var image = cut.Find("img.tm-email-block__image");
        image.GetAttribute("src").Should().Be("https://example.com/logo.png", "the value is trimmed, not passed raw");
        image.GetAttribute("referrerpolicy").Should().Be("no-referrer",
            "the author's browser must not leak the editor URL to the image host");
        image.GetAttribute("loading").Should().Be("lazy");
        image.GetAttribute("width").Should().Be("120");
    }

    /// <summary>Hero / section / carousel use the same gate, so no other path can smuggle a relative URL.</summary>
    [Theory]
    [InlineData("angle")]
    [InlineData("conservative")]
    public void Hero_section_and_carousel_backgrounds_use_the_same_image_gate(string sanitizer)
    {
        UseSanitizer(sanitizer);

        var hero = new EmailHeroBlock { BackgroundUrl = "/api/logout" };
        hero.Blocks.Add(new EmailTextBlock { Content = "Hi" });
        var carousel = new EmailCarouselBlock();
        carousel.Images.Add(new EmailCarouselImage { Src = "/api/logout", Alt = "slide" });
        var document = DocWith(hero, carousel);
        document.Sections[0].BackgroundUrl = "/api/logout";

        var cut = RenderCanvas(document);

        cut.FindAll("img").Should().BeEmpty("no canvas path may turn a relative URL into a request");
    }

    // ── Code 6 + 7: signature and font-stack hardening ────────────────────────────────────

    /// <summary>
    /// The signature joins the painted values with U+001F. Without a length prefix, a value that CONTAINS
    /// the separator (a hostile import can carry U+001F) could shift the boundary and make two different
    /// blocks compare equal — the canvas would then show stale content.
    /// </summary>
    [Fact]
    public void The_block_signature_is_unambiguous_when_a_value_contains_the_separator()
    {
        // Both blocks are the SAME block (same id) with the same painted values in a different ORDER of
        // ownership: "a<US>b" as the content + "c" as the colour vs "a" as the content + "b<US>c" as the
        // colour. Joined naively both signatures are "…<US>a<US>b<US>c".
        var id = Guid.NewGuid();
        var sneaky = new EmailTextBlock { Id = id, Content = "a\u001fb", Color = "c" };
        var shifted = new EmailTextBlock { Id = id, Content = "a", Color = "b\u001fc" };

        EmailBlockSignature.Of(sneaky).Should().NotBe(EmailBlockSignature.Of(shifted),
            "a length prefix keeps the value boundaries unambiguous");
    }

    /// <summary>
    /// A font stack either has balanced quotes or is dropped: the old pattern accepted an unbalanced
    /// <c>"</c>, which then escaped the declaration it was interpolated into.
    /// </summary>
    [Theory]
    [InlineData("a\"b")]
    [InlineData("a'b")]
    [InlineData("'Open Sans")]
    [InlineData("\"Open Sans")]
    [InlineData("Arial; background:url(x)")]
    [InlineData("Arial(1)")]
    public void An_unbalanced_or_dangerous_font_stack_is_refused(string value)
        => EmailCanvasValue.FontFamily(value).Should().BeNull();

    [Theory]
    [InlineData("Arial", "Arial")]
    [InlineData("Arial, sans-serif", "Arial, sans-serif")]
    [InlineData("'Open Sans', Arial, sans-serif", "'Open Sans', Arial, sans-serif")]
    [InlineData("\"Open Sans\", Arial", "\"Open Sans\", Arial")]
    [InlineData("  Arial  ", "Arial")]
    public void A_well_formed_font_stack_survives(string value, string expected)
        => EmailCanvasValue.FontFamily(value).Should().Be(expected);

    // ── UX 9 (blocker): typing into the rich text keeps the typed characters ───────────────

    /// <summary>
    /// The rich editor in the inspector used to be fed
    /// <c>SanitizeTemplateContent(textBlock.Content)</c> on EVERY render. The host echo of a keystroke is
    /// re-rendered immediately, so the sanitized value (which differs from what the editor holds — the
    /// sanitizer drops attributes) was written back with <c>setHtml</c>, the caret jumped to the start and the
    /// letters came out in the wrong order (" EDIT" became "TIDE"). The sanitizer still runs when the editor
    /// OPENS on a block and when the value changes externally (undo), which is the stored-XSS guard from F9.
    /// </summary>
    [Fact]
    public void The_rich_text_editor_is_not_re_sanitized_on_the_echo_of_a_keystroke()
    {
        var block = new EmailTextBlock { Content = "<div>hi</div>" };
        var document = DocWith(block);

        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.SelectedId, block.Id));

        var editor = cut.FindComponent<TmRichEditorSimple>();
        editor.Instance.Value.Should().Be("hi",
            "opening the editor on an imported block still sanitizes on the way in (the F9 stored-XSS guard)");

        // The author types: the editor emits its RAW HTML, so the model now holds markup the sanitizer would
        // rewrite. The host then re-renders with that value.
        cut.InvokeAsync(() => editor.Instance.ValueChanged.InvokeAsync("<div>hi there</div>"));
        cut.WaitForAssertion(() => block.Content.Should().Be("<div>hi there</div>"));

        cut.FindComponent<TmRichEditorSimple>().Instance.Value.Should().Be("<div>hi there</div>",
            "the value must not be re-sanitized on the echo: a different string makes the editor re-set its "
            + "innerHTML and the caret jumps to the start");
    }

    /// <summary>An EXTERNAL change (undo, a host write) is still sanitized into the editor.</summary>
    [Fact]
    public void An_external_change_to_the_text_block_is_still_sanitized_for_the_editor()
    {
        var block = new EmailTextBlock { Content = "<div>hi</div>" };
        var document = DocWith(block);

        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.SelectedId, block.Id));

        // Undo / a host write lands in the model while the panel shows the block.
        block.Content = "<div onclick=\"alert(1)\">restored</div>";
        cut.Render(p => p.Add(c => c.Document, document).Add(c => c.SelectedId, block.Id));

        cut.FindComponent<TmRichEditorSimple>().Instance.Value.Should().Be("restored",
            "an external value is sanitized before it reaches innerHTML (fail-closed)");
    }

    // ── UX 10 + 13: an empty block is visible and every block has a real accessible name ───

    [Theory]
    [InlineData("text")]
    [InlineData("button")]
    [InlineData("navbar")]
    [InlineData("social")]
    [InlineData("table")]
    [InlineData("carousel")]
    [InlineData("accordion")]
    public void An_empty_block_says_so_instead_of_rendering_nothing(string kind)
    {
        EmailBlockBase block = kind switch
        {
            "text" => new EmailTextBlock { Content = "  " },
            "button" => new EmailButtonBlock { Text = " " },
            "navbar" => new EmailNavbarBlock(),
            "social" => new EmailSocialBlock(),
            "table" => new EmailTableBlock(),
            "carousel" => new EmailCarouselBlock(),
            _ => new EmailAccordionBlock(),
        };

        var cut = RenderCanvas(DocWith(block));

        var marker = cut.Find("[data-tm-block-empty]");
        marker.TextContent.Should().Be("EmailEditor_Canvas_EmptyBlock");
        marker.ClassList.Should().Contain("tm-email-block__empty");
    }

    [Fact]
    public void A_block_that_has_content_renders_no_empty_marker()
        => RenderCanvas(DocWith(new EmailTextBlock { Content = "<p>Hello</p>" }))
            .FindAll("[data-tm-block-empty]").Should().BeEmpty();

    [Fact]
    public void A_block_accessible_name_carries_its_type_and_its_brief_content()
    {
        var cut = RenderCanvas(DocWith(
            new EmailTextBlock { Content = "<p>Welcome, <b>{{ first_name }}</b>!</p>" },
            new EmailButtonBlock { Text = "Get started" },
            new EmailImageBlock { Src = "https://example.com/a.png", Alt = "Company logo" }));

        var labels = cut.FindAll("[data-tm-block-id]")
            .Select(el => el.GetAttribute("aria-label"))
            .ToList();

        labels.Should().HaveCount(3);
        labels[0].Should().Be("EmailEditor_Canvas_BlockLabel[block.text.name,Welcome, {{ first_name }}!]",
            "tags are stripped, the token stays as the author wrote it");
        labels[1].Should().Contain("Get started");
        labels[2].Should().Contain("Company logo");
    }

    [Fact]
    public void An_empty_block_accessible_name_says_it_is_empty()
    {
        var cut = RenderCanvas(DocWith(new EmailTextBlock { Content = string.Empty }));

        cut.Find("[data-tm-block-id]").GetAttribute("aria-label")
            .Should().Be("EmailEditor_Canvas_BlockLabelEmpty[block.text.name]");
    }

    [Fact]
    public void A_long_content_is_capped_in_the_accessible_name()
    {
        var longText = new string('a', 200);
        var cut = RenderCanvas(DocWith(new EmailTextBlock { Content = longText }));

        var label = cut.Find("[data-tm-block-id]").GetAttribute("aria-label")!;
        label.Should().Contain(new string('a', TmEmailTemplateCanvas.BlockLabelContentLength) + "…");
        label.Should().NotContain(new string('a', TmEmailTemplateCanvas.BlockLabelContentLength + 1));
    }

    // ── UX 4: a reordered block keeps its own renderer (the @key contract) ─────────────────

    [Fact]
    public void Reordering_blocks_keeps_each_renderer_attached_to_its_own_block()
    {
        var first = new EmailTextBlock { Content = "First" };
        var second = new EmailTextBlock { Content = "Second" };
        var document = DocWith(first, second);

        var cut = RenderCanvas(document);
        var instances = cut.FindComponents<TmEmailCanvasBlock>()
            .ToDictionary(component => component.Instance.Block.Id, component => component.Instance);

        // Move the FIRST block behind the second. Without @key the diff reuses component instances by
        // POSITION, so the instance that painted "First" would be handed the data of "Second" — and the render
        // gate would then compare a signature that belongs to a different block (stale canvas).
        var column = document.Sections[0].Columns[0];
        (column.Blocks[0], column.Blocks[1]) = (column.Blocks[1], column.Blocks[0]);
        cut.Render();

        foreach (var component in cut.FindComponents<TmEmailCanvasBlock>())
        {
            instances[component.Instance.Block.Id].Should().BeSameAs(component.Instance,
                "the @key keeps the component instance together with its block");
        }
    }

    // ── UX 19: the document's own paper colour and font are painted ────────────────────────

    [Fact]
    public void The_paper_paints_the_documents_background_and_font()
    {
        var document = DocWith(new EmailTextBlock { Content = "Hi" });
        document.Styles.BackgroundColor = "#fdfaf3";
        document.Styles.FontFamily = "'Open Sans', Arial";

        var cut = RenderCanvas(document);

        var style = Style(cut.Find("[data-tm-canvas-paper]"));
        style.Should().Contain("background-color:#fdfaf3");
        style.Should().Contain("font-family:'OpenSans',Arial");
    }

    [Theory]
    [InlineData("red;background:url(//evil)", "Arial")]
    [InlineData("#fdfaf3", "a\"b")]
    public void An_invalid_paper_colour_or_font_falls_back(string color, string font)
    {
        var document = DocWith(new EmailTextBlock { Content = "Hi" });
        document.Styles.BackgroundColor = color;
        document.Styles.FontFamily = font;

        var cut = RenderCanvas(document);

        var style = Style(cut.Find("[data-tm-canvas-paper]"));
        style.Should().NotContain("url(");
        style.Should().NotContain("a\"b");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────

    /// <summary>Replaces the registered sanitizer so both implementations are held to the same contract.</summary>
    private void UseSanitizer(string name)
    {
        Services.RemoveAll<IHtmlContentSanitizer>();
        Services.AddSingleton<IHtmlContentSanitizer>(name == "angle"
            ? AngleSharpHtmlContentSanitizer.Instance
            : ConservativeHtmlContentSanitizer.Instance);
    }

    /// <summary>Counts the opening and the closing occurrences of <paramref name="tag"/> in a fragment.</summary>
    private static (int Open, int Close) CountTags(string html, string tag)
        => (System.Text.RegularExpressions.Regex.Matches(html, "<" + tag + "[\\s>]").Count,
            System.Text.RegularExpressions.Regex.Matches(html, "</" + tag + ">").Count);
}
