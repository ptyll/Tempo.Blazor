using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Components;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 9A security — the WYSIWYG canvas paints UNTRUSTED model content, so it must never render anything
/// executable or anything that fetches a remote resource the pipeline would refuse:
/// <list type="bullet">
///   <item>block text goes through <see cref="IHtmlContentSanitizer"/> (fail-closed without one);</item>
///   <item><c>Src</c>/<c>Href</c>/<c>BackgroundUrl</c> pass the ONE URL allow-list of the pipeline
///   (<see cref="EmailUrlPolicy"/>) — <c>javascript:</c> and <c>data:</c> never reach the canvas;</item>
///   <item>an <c>mj-raw</c> block is NEVER rendered as HTML (escaped placeholder only);</item>
///   <item>no link on the canvas is navigable — a click selects the block, nothing else;</item>
///   <item><c>{{ … }}</c> tokens are chips, never substituted values.</item>
/// </list>
/// Every case here is an XSS or tracking payload an MJML import or a JSON payload could carry.
/// </summary>
public class TmEmailCanvasSecurityTests : EmailCanvasTestBase
{
    // ── text content: the sanitizer is the only way in ─────────────────────────────────────

    [Fact]
    public void Text_with_a_script_tag_never_renders_it()
    {
        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "Hi<script>window.alert('xss')</script>there",
        }));

        cut.FindAll("script").Should().BeEmpty();
        cut.Markup.Should().NotContain("<script");
        Body(cut).TextContent.Should().Contain("Hi").And.Contain("there");
    }

    [Fact]
    public void Text_with_an_img_onerror_payload_never_renders_an_image()
    {
        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "Hi<img src=x onerror=\"window.alert('xss')\">",
        }));

        cut.FindAll("img").Should().BeEmpty("an img inside inline HTML is not on the allow-list");
        cut.Markup.Should().NotContain("onerror=", "the payload degrades to escaped text");
    }

    [Fact]
    public void Text_with_a_javascript_href_renders_no_anchor_at_all()
    {
        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "Hi <a href=\"javascript:alert(1)\">click</a>",
        }));

        cut.FindAll("a").Should().BeEmpty("the canvas never renders a navigable link");
        cut.Markup.Should().NotContain("javascript:");
        cut.Markup.Should().NotContain("href=");
    }

    [Fact]
    public void Text_with_an_allowed_href_still_renders_no_anchor()
    {
        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "Read <a href=\"https://example.com/docs\">the docs</a>",
        }));

        cut.FindAll("a").Should().BeEmpty("a click on the canvas selects the block, it never navigates");
        cut.Markup.Should().NotContain("href=");
        Body(cut).TextContent.Should().Contain("the docs", "the LINK TEXT survives, only the navigation dies");
    }

    [Fact]
    public void Text_with_an_svg_onload_payload_never_renders_svg()
    {
        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "<svg onload=\"window.alert('xss')\"><circle /></svg>after",
        }));

        Body(cut).QuerySelectorAll("svg").Should().BeEmpty("the canvas chrome's own icons do not count");
        cut.Markup.Should().NotContain("onload=");
        Body(cut).TextContent.Should().Contain("after");
    }

    [Fact]
    public void Text_style_with_url_is_dropped_with_the_whole_style_attribute()
    {
        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "<span style=\"background-image: url(https://tracker.example/p.gif)\">t</span>",
        }));

        var body = Body(cut);
        body.QuerySelector("span").Should().NotBeNull("the allowed span survives");
        body.QuerySelector("span")!.GetAttribute("style").Should().BeNull(
            "no style attribute from untrusted content reaches the canvas (it could load a url() or escape the block)");
        cut.Markup.Should().NotContain("tracker.example");
    }

    [Fact]
    public void Text_style_with_position_fixed_is_dropped_so_content_cannot_paint_over_the_editor()
    {
        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "<span style=\"position: fixed; inset: 0; z-index: 9999\">overlay</span>",
        }));

        Body(cut).QuerySelector("span")!.GetAttribute("style").Should().BeNull();
    }

    [Fact]
    public void Text_without_a_registered_sanitizer_renders_escaped_text_only()
    {
        // Fail-closed: the canvas must not fall back to raw markup when the host registered no sanitizer.
        Services.RemoveAll<IHtmlContentSanitizer>();

        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "Hi <b>bold</b><img src=x onerror=alert(1)>",
        }));

        cut.FindAll("b").Should().BeEmpty("without a sanitizer the markup is not interpreted");
        cut.FindAll("img").Should().BeEmpty();
        Body(cut).InnerHtml.Should().Contain("&lt;b&gt;bold&lt;/b&gt;",
            "the author still sees the text, but as escaped text");
        Body(cut).QuerySelectorAll("*").Should().BeEmpty("nothing from the content became an element");
    }

    [Fact]
    public void Raw_block_never_renders_its_content_as_markup()
    {
        var cut = RenderCanvas(DocWith(new EmailRawBlock
        {
            Content = "<script>alert(1)</script><img src=x onerror=alert(1)>",
        }));

        cut.FindAll("script").Should().BeEmpty();
        cut.FindAll("img").Should().BeEmpty();
        cut.Find(".tm-email-block__raw-text").QuerySelectorAll("*").Should().BeEmpty(
            "the preview is text: not one element of the payload survives");
        cut.Find("[data-tm-raw-preview]").TextContent.Should().Contain("<script>",
            "the placeholder shows the first characters as TEXT so the author can recognise the block");
    }

    // ── URLs from the model: the pipeline's one allow-list ──────────────────────────────────

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/svg+xml;base64,PHN2Zy8+")]
    [InlineData("//evil.example/x.png")]
    [InlineData("vbscript:msgbox(1)")]
    public void Image_with_a_blocked_src_renders_no_image(string src)
    {
        var cut = RenderCanvas(DocWith(new EmailImageBlock { Src = src, Alt = "Logo" }));

        cut.FindAll("img").Should().BeEmpty("a refused URL never becomes a request from the editor");
        cut.Markup.Should().NotContain("javascript:").And.NotContain("vbscript:");
        var placeholder = cut.Find("[data-tm-image-placeholder]");
        placeholder.HasAttribute("data-tm-image-blocked").Should().BeTrue(
            "a refused URL is marked as blocked, not as a privacy placeholder");
    }

    [Fact]
    public void Image_with_a_blocked_src_keeps_the_alt_text_visible()
    {
        var cut = RenderCanvas(DocWith(new EmailImageBlock { Src = "javascript:alert(1)", Alt = "Company logo" }));

        cut.Markup.Should().Contain("Company logo");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    public void Hero_background_url_outside_the_allow_list_renders_no_image(string url)
    {
        var cut = RenderCanvas(DocWith(new EmailHeroBlock { BackgroundUrl = url }));

        cut.FindAll("img").Should().BeEmpty();
        cut.Markup.Should().NotContain("javascript:");
    }

    [Fact]
    public void Wrapper_background_url_outside_the_allow_list_renders_no_image()
    {
        var cut = RenderCanvas(DocWith(new EmailWrapperBlock { BackgroundUrl = "javascript:alert(1)" }));

        cut.FindAll("img").Should().BeEmpty();
    }

    [Fact]
    public void Section_background_url_outside_the_allow_list_renders_no_image()
    {
        var document = DocWith(new EmailTextBlock { Content = "Hi" });
        document.Sections[0].BackgroundUrl = "javascript:alert(1)";

        var cut = RenderCanvas(document);

        cut.FindAll("img").Should().BeEmpty();
    }

    [Fact]
    public void Section_background_url_inside_the_allow_list_is_painted_as_an_image()
    {
        var document = DocWith(new EmailTextBlock { Content = "Hi" });
        document.Sections[0].BackgroundUrl = "https://example.com/bg.png";

        var cut = RenderCanvas(document);

        var background = cut.Find("img.tm-email-canvas-section__bg");
        background.GetAttribute("src").Should().Be("https://example.com/bg.png");
        background.GetAttribute("alt").Should().BeEmpty();
        background.GetAttribute("aria-hidden").Should().Be("true", "a decorative background is not content");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("java\tscript:alert(1)")]
    public void Table_cell_with_a_blocked_href_is_marked_and_not_a_link(string href)
    {
        var table = new EmailTableBlock();
        var row = new EmailTableRow();
        row.Cells.Add(new EmailTableCell { Text = "Widget", Href = href });
        table.Rows.Add(row);

        var cut = RenderCanvas(DocWith(table));

        cut.FindAll("a").Should().BeEmpty();
        cut.Markup.Should().NotContain("javascript:");
        cut.Find("[data-tm-cell-link-blocked]").Should().NotBeNull(
            "a refused cell link is visible as refused, not silently dropped");
    }

    [Fact]
    public void Button_and_navbar_and_social_links_are_never_anchors()
    {
        var button = new EmailButtonBlock { Text = "Go", Href = "https://example.com" };
        var navbar = new EmailNavbarBlock();
        navbar.Links.Add(new EmailNavbarLink { Text = "Docs", Href = "https://example.com/docs" });
        var social = new EmailSocialBlock();
        social.Elements.Add(new EmailSocialElement { Name = "facebook", Href = "https://facebook.com/x" });

        var cut = RenderCanvas(DocWith(button, navbar, social));

        cut.FindAll("a").Should().BeEmpty("nothing on the canvas is navigable");
        cut.Markup.Should().NotContain("href=");
    }

    [Fact]
    public void A_document_full_of_links_renders_no_anchor_and_no_href_anywhere()
    {
        var table = new EmailTableBlock();
        var row = new EmailTableRow();
        row.Cells.Add(new EmailTableCell { Text = "Row", Href = "https://example.com" });
        table.Rows.Add(row);
        var navbar = new EmailNavbarBlock();
        navbar.Links.Add(new EmailNavbarLink { Text = "A", Href = "https://example.com/a" });
        var button = new EmailButtonBlock { Text = "B", Href = "https://example.com/b" };
        var image = new EmailImageBlock { Src = "https://example.com/i.png", Href = "https://example.com/i", Alt = "i" };

        var cut = RenderCanvas(DocWith(button, navbar, table, image,
            new EmailTextBlock { Content = "<a href=\"https://example.com/t\">t</a>" }));

        cut.FindAll("a").Should().BeEmpty();
        cut.Markup.Should().NotContain("href=");
        cut.FindAll("[data-tm-block-id]").Should().HaveCount(5);
    }

    // ── template tokens: chips, never substitution ─────────────────────────────────────────

    [Fact]
    public void Template_tokens_are_rendered_as_chips_not_substituted()
    {
        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "Welcome, {{ user.first_name }}!",
        }));

        var chips = cut.FindAll(".tm-email-block__token");
        chips.Should().ContainSingle("the token stays visible as a chip");
        chips[0].TextContent.Should().Contain("{{ user.first_name }}");
        chips[0].GetAttribute("data-tm-token").Should().Be("user.first_name");
        cut.Markup.Should().Contain("Welcome, ");
    }

    [Fact]
    public void Block_tags_are_chips_too()
    {
        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "{{ for item in items }}<b>{{ item.name }}</b>{{ end }}",
        }));

        cut.FindAll(".tm-email-block__token").Should().HaveCount(3);
        Body(cut).QuerySelector("b").Should().NotBeNull("the markup BETWEEN tokens is still rendered");
    }

    [Fact]
    public void A_token_carrying_markup_is_escaped_and_never_executes()
    {
        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "x {{ \"<img src=x onerror=alert(1)>\" }} y",
        }));

        cut.FindAll("img").Should().BeEmpty("a token is text on the canvas — never restored verbatim");
        cut.Find(".tm-email-block__token")!.TextContent.Should().Contain("onerror");
        cut.Find(".tm-email-block__token")!.QuerySelectorAll("*").Should().BeEmpty();
    }

    [Fact]
    public void A_token_cannot_smuggle_an_anchor()
    {
        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "{{ \"<a href=javascript:alert(1)>x</a>\" }}",
        }));

        cut.FindAll("a").Should().BeEmpty();
        cut.Find(".tm-email-block__token").QuerySelectorAll("*").Should().BeEmpty();
        Body(cut).QuerySelectorAll("span:not(.tm-email-block__token)").Should().BeEmpty();
    }

    // ── the editor surface stays inert ─────────────────────────────────────────────────────

    [Fact]
    public void A_block_with_a_hostile_document_renders_no_active_content_on_the_editor()
    {
        var document = DocWith(
            new EmailTextBlock { Content = "<script>alert(1)</script>" },
            new EmailImageBlock { Src = "javascript:alert(1)", Alt = "x" },
            new EmailRawBlock { Content = "<iframe src=javascript:alert(1)></iframe>" });

        var cut = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, document));

        cut.FindAll("script").Should().BeEmpty();
        cut.FindAll("iframe").Should().BeEmpty();
        cut.FindAll("img").Should().BeEmpty(
            "the only image in the document has a refused URL, so it is a placeholder");
        cut.Find("[data-tm-raw-preview]").QuerySelectorAll("iframe, script, img").Should().BeEmpty(
            "the raw block's payload stays text inside its placeholder");
    }
}
