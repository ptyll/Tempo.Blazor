using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 9A — every block type has a VISUAL renderer on the canvas: the canvas approximates the MJML output
/// (colours, alignment, images, columns) instead of printing "type + 80 characters of text". The
/// authoritative output stays the server preview (<c>IEmailPreviewRenderer</c>); this is a faithful
/// approximation of the same model values, so an author sees the email they are writing.
/// </summary>
public class TmEmailCanvasBlockVisualTests : EmailCanvasTestBase
{
    [Fact]
    public void Text_block_renders_its_sanitized_html_content()
    {
        var doc = DocWith(new EmailTextBlock { Content = "Hello <b>world</b>" });

        var body = Body(RenderCanvas(doc));

        body.InnerHtml.Should().Contain("<b>world</b>",
            "a text block is inline HTML — the WYSIWYG canvas shows the bold word, not the source");
    }

    [Fact]
    public void Text_block_applies_typography_alignment_and_padding()
    {
        var doc = DocWith(new EmailTextBlock
        {
            Content = "Hi",
            Color = "#112233",
            FontSize = "16px",
            FontWeight = "600",
            Align = "right",
            LineHeight = "1.5",
            LetterSpacing = "1px",
            TextTransform = "uppercase",
            Padding = "4px 8px",
        });

        var body = Body(RenderCanvas(doc));

        ShouldDeclare(body, "color:#112233");
        ShouldDeclare(body, "font-size:16px");
        ShouldDeclare(body, "font-weight:600");
        ShouldDeclare(body, "text-align:right");
        ShouldDeclare(body, "line-height:1.5");
        ShouldDeclare(body, "letter-spacing:1px");
        ShouldDeclare(body, "text-transform:uppercase");
        ShouldDeclare(body, "padding:4px 8px");
    }

    [Fact]
    public void Button_block_renders_a_pill_with_background_colour_radius_and_alignment()
    {
        var doc = DocWith(new EmailButtonBlock
        {
            Text = "Get started",
            Href = "https://example.com/start",
            BackgroundColor = "#414141",
            Color = "#ffffff",
            BorderRadius = "8px",
            InnerPadding = "12px 24px",
            Align = "left",
        });

        var body = Body(RenderCanvas(doc));

        ShouldDeclare(body, "text-align:left");
        var pill = body.QuerySelector(".tm-email-block__button");
        pill.Should().NotBeNull("the button is drawn as a pill, not a label");
        pill!.TextContent.Should().Contain("Get started");
        ShouldDeclare(pill, "background-color:#414141");
        ShouldDeclare(pill, "color:#ffffff");
        ShouldDeclare(pill, "border-radius:8px");
        ShouldDeclare(pill, "padding:12px 24px");
    }

    [Fact]
    public void Image_block_renders_an_img_with_src_alt_and_width()
    {
        var doc = DocWith(new EmailImageBlock
        {
            Src = "https://example.com/logo.png",
            Alt = "Logo",
            Width = "300px",
            BorderRadius = "6px",
            Align = "left",
        });

        var body = Body(RenderCanvas(doc));

        var image = body.QuerySelector("img");
        image.Should().NotBeNull();
        image!.GetAttribute("src").Should().Be("https://example.com/logo.png");
        image.GetAttribute("alt").Should().Be("Logo");
        image.GetAttribute("width").Should().Be("300", "a px width becomes the img width attribute (MJML parity)");
        ShouldDeclare(image, "border-radius:6px");
        ShouldDeclare(body, "text-align:left");
    }

    [Fact]
    public void Divider_block_renders_a_rule_with_its_border_and_width()
    {
        var doc = DocWith(new EmailDividerBlock
        {
            BorderColor = "#334455",
            BorderWidth = "2px",
            BorderStyle = "dashed",
            Width = "50%",
        });

        var body = Body(RenderCanvas(doc));

        var rule = body.QuerySelector("hr");
        rule.Should().NotBeNull();
        ShouldDeclare(rule!, "border-top:2pxdashed#334455");
        ShouldDeclare(rule!, "inline-size:50%");
    }

    [Fact]
    public void Spacer_block_renders_its_height()
    {
        var doc = DocWith(new EmailSpacerBlock { Height = "40px" });

        var body = Body(RenderCanvas(doc));

        var spacer = body.QuerySelector(".tm-email-block__spacer");
        spacer.Should().NotBeNull();
        ShouldDeclare(spacer!, "block-size:40px");
        spacer!.GetAttribute("aria-hidden").Should().Be("true", "a spacer carries no information");
    }

    [Fact]
    public void Raw_block_renders_a_placeholder_never_the_markup()
    {
        var doc = DocWith(new EmailRawBlock { Content = "<p>Custom <em>HTML</em></p>" });

        var body = Body(RenderCanvas(doc));

        body.QuerySelector("p").Should().BeNull("mj-raw never reaches the canvas as markup");
        body.QuerySelector("[data-tm-raw-preview]").Should().NotBeNull("the author still sees WHICH block it is");
        body.TextContent.Should().Contain("<p>Custom", "the first characters are shown as TEXT");
    }

    [Fact]
    public void Table_block_renders_a_header_row_and_linked_cells()
    {
        var table = new EmailTableBlock();
        var header = new EmailTableRow { IsHeader = true };
        header.Cells.Add(new EmailTableCell { Text = "Item" });
        header.Cells.Add(new EmailTableCell { Text = "Price" });
        var row = new EmailTableRow();
        row.Cells.Add(new EmailTableCell { Text = "Widget", Href = "https://example.com/widget", BackgroundColor = "#f5f5f5" });
        row.Cells.Add(new EmailTableCell { Text = "$9.99" });
        table.Rows.Add(header);
        table.Rows.Add(row);

        var body = Body(RenderCanvas(DocWith(table)));

        var head = body.QuerySelector("thead");
        head.Should().NotBeNull("the header row is a real thead row");
        head!.QuerySelectorAll("th").Should().HaveCount(2);
        var cells = body.QuerySelectorAll("tbody td");
        cells.Should().HaveCount(2);
        ShouldDeclare(cells[0], "background-color:#f5f5f5");
        var link = cells[0].QuerySelector(".tm-email-block__cell-link");
        link.Should().NotBeNull("the cell link is marked as a link");
        link!.TextContent.Should().Be("Widget");
        body.QuerySelectorAll("a").Should().BeEmpty("no cell link is navigable from the editor");
    }

    [Fact]
    public void Social_block_renders_its_elements_with_labels()
    {
        var social = new EmailSocialBlock();
        social.Elements.Add(new EmailSocialElement { Name = "facebook", Label = "Facebook" });
        social.Elements.Add(new EmailSocialElement { Name = "instagram", Label = "Instagram" });
        social.Align = "left";

        var body = Body(RenderCanvas(DocWith(social)));

        body.QuerySelectorAll(".tm-email-block__social-item").Should().HaveCount(2);
        body.TextContent.Should().Contain("Facebook").And.Contain("Instagram");
        ShouldDeclare(body, "text-align:left");
    }

    [Fact]
    public void Navbar_block_renders_its_links()
    {
        var navbar = new EmailNavbarBlock { Align = "right" };
        navbar.Links.Add(new EmailNavbarLink { Text = "Products", Href = "https://example.com/products" });
        navbar.Links.Add(new EmailNavbarLink { Text = "Support" });

        var body = Body(RenderCanvas(DocWith(navbar)));

        body.QuerySelectorAll(".tm-email-block__nav-link").Should().HaveCount(2);
        body.TextContent.Should().Contain("Products").And.Contain("Support");
        ShouldDeclare(body, "text-align:right");
    }

    [Fact]
    public void Hero_block_renders_its_background_and_nested_content()
    {
        var hero = new EmailHeroBlock
        {
            BackgroundColor = "#eef2ff",
            BackgroundUrl = "https://example.com/hero.jpg",
            VerticalAlign = "middle",
        };
        hero.Blocks.Add(new EmailTextBlock { Content = "<strong>Welcome</strong>" });

        var body = Body(RenderCanvas(DocWith(hero)));

        ShouldDeclare(body, "background-color:#eef2ff");
        var background = body.QuerySelector("img.tm-email-block__hero-bg");
        background.Should().NotBeNull("a hero background image is painted as an image, never as a CSS url()");
        background!.GetAttribute("src").Should().Be("https://example.com/hero.jpg");
        body.InnerHtml.Should().Contain("<strong>Welcome</strong>", "the hero holds real content blocks");
    }

    [Fact]
    public void Carousel_block_renders_the_first_image_and_the_count()
    {
        var carousel = new EmailCarouselBlock();
        carousel.Images.Add(new EmailCarouselImage { Src = "https://example.com/1.png", Alt = "One" });
        carousel.Images.Add(new EmailCarouselImage { Src = "https://example.com/2.png", Alt = "Two" });
        carousel.Images.Add(new EmailCarouselImage { Src = "https://example.com/3.png", Alt = "Three" });

        var body = Body(RenderCanvas(DocWith(carousel)));

        var image = body.QuerySelector("img.tm-email-block__carousel-image");
        image.Should().NotBeNull();
        image!.GetAttribute("src").Should().Be("https://example.com/1.png");
        image.GetAttribute("alt").Should().Be("One");
        body.QuerySelector(".tm-email-block__carousel-count")!.TextContent.Should().Contain("1 / 3");
    }

    [Fact]
    public void Accordion_block_renders_its_titles()
    {
        var accordion = new EmailAccordionBlock();
        accordion.Items.Add(new EmailAccordionItem { Title = "Shipping", Content = "<p>Two days</p>" });
        accordion.Items.Add(new EmailAccordionItem { Title = "Returns", Content = "<p>Thirty days</p>" });

        var body = Body(RenderCanvas(DocWith(accordion)));

        body.QuerySelectorAll(".tm-email-block__accordion-title").Should().HaveCount(2);
        body.TextContent.Should().Contain("Shipping").And.Contain("Returns");
        body.QuerySelector("p").Should().BeNull("the collapsed item content is not expanded on the canvas");
    }

    [Fact]
    public void Wrapper_block_renders_a_frame_with_its_nested_sections()
    {
        var wrapper = new EmailWrapperBlock { BackgroundColor = "#f8fafc" };
        var nested = new EmailSection { BackgroundColor = "#ffffff", Padding = "8px 0" };
        var column = new EmailColumn();
        column.Blocks.Add(new EmailTextBlock { Content = "Inside the wrapper" });
        nested.Columns.Add(column);
        wrapper.Sections.Add(nested);

        var body = Body(RenderCanvas(DocWith(wrapper)));

        ShouldDeclare(body, "background-color:#f8fafc");
        body.QuerySelector(".tm-email-block__frame").Should().NotBeNull("a wrapper draws the frame it represents");
        body.QuerySelector("[data-tm-nested-section]").Should().NotBeNull();
        body.TextContent.Should().Contain("Inside the wrapper");
    }

    [Fact]
    public void Group_block_renders_a_frame_with_its_nested_columns()
    {
        var group = new EmailGroupBlock { BackgroundColor = "#f1f5f9" };
        var first = new EmailColumn { Width = "60%" };
        first.Blocks.Add(new EmailTextBlock { Content = "Left" });
        var second = new EmailColumn { Width = "40%" };
        second.Blocks.Add(new EmailTextBlock { Content = "Right" });
        group.Columns.Add(first);
        group.Columns.Add(second);

        var body = Body(RenderCanvas(DocWith(group)));

        var frame = body.QuerySelector(".tm-email-block__frame");
        frame.Should().NotBeNull();
        var columns = body.QuerySelectorAll("[data-tm-nested-column]");
        columns.Should().HaveCount(2);
        ShouldDeclare(columns[0], "flex:601 0");
        ShouldDeclare(columns[1], "flex:401 0");
        body.TextContent.Should().Contain("Left").And.Contain("Right");
    }

    [Fact]
    public void Section_respects_its_background_padding_and_alignment()
    {
        var document = DocWith(new EmailTextBlock { Content = "Hi" });
        document.Sections[0].BackgroundColor = "#101828";
        document.Sections[0].Padding = "24px 0";
        document.Sections[0].TextAlign = "left";
        document.Sections[0].BorderRadius = "12px";

        var section = RenderCanvas(document).Find("[data-tm-section]");

        ShouldDeclare(section, "background-color:#101828");
        ShouldDeclare(section, "padding:24px 0");
        ShouldDeclare(section, "text-align:left");
        ShouldDeclare(section, "border-radius:12px");
    }

    [Fact]
    public void Column_width_drives_the_flex_ratio_and_its_own_background()
    {
        var document = new EmailTemplateDocument();
        var section = new EmailSection();
        var left = new EmailColumn { Width = "33%", BackgroundColor = "#e0e7ff" };
        left.Blocks.Add(new EmailTextBlock { Content = "Left" });
        var right = new EmailColumn { Width = "66%" };
        right.Blocks.Add(new EmailTextBlock { Content = "Right" });
        section.Columns.Add(left);
        section.Columns.Add(right);
        document.Sections.Add(section);

        var columns = RenderCanvas(document).FindAll("[data-tm-column]");

        ShouldDeclare(columns[0], "flex:331 0");
        ShouldDeclare(columns[0], "background-color:#e0e7ff");
        ShouldDeclare(columns[1], "flex:661 0");
    }

    [Fact]
    public void Paper_uses_the_document_content_width()
    {
        var document = DocWith(new EmailTextBlock { Content = "Hi" });
        document.Styles.ContentWidth = "700px";

        var paper = RenderCanvas(document).Find("[data-tm-canvas-paper]");

        ShouldDeclare(paper, "inline-size:700px");
        ShouldDeclare(paper, "max-inline-size:100%");
    }

    [Fact]
    public void Paper_defaults_to_the_600px_content_width()
    {
        var paper = RenderCanvas(DocWith(new EmailTextBlock { Content = "Hi" })).Find("[data-tm-canvas-paper]");

        ShouldDeclare(paper, "inline-size:600px");
    }

    [Fact]
    public void Paper_falls_back_to_600px_for_an_invalid_content_width()
    {
        var document = DocWith(new EmailTextBlock { Content = "Hi" });
        // A crafted document must not be able to steer the paper through the style attribute.
        document.Styles.ContentWidth = "600px; background-image: url(https://tracker.example/p.gif)";

        var paper = RenderCanvas(document).Find("[data-tm-canvas-paper]");

        ShouldDeclare(paper, "inline-size:600px");
        Style(paper).Should().NotContain("url(").And.NotContain("tracker");
        Style(paper).Should().NotContain("background-image");
    }

    [Fact]
    public void Block_honours_container_background_and_padding_overrides()
    {
        var doc = DocWith(new EmailTextBlock
        {
            Content = "Hi",
            ContainerBackgroundColor = "#fef3c7",
            Padding = null,
            PaddingTop = "2px",
            PaddingBottom = "6px",
        });

        var body = Body(RenderCanvas(doc));

        ShouldDeclare(body, "background-color:#fef3c7");
        ShouldDeclare(body, "padding-top:2px");
        ShouldDeclare(body, "padding-bottom:6px");
    }

    [Fact]
    public void A_token_inside_an_element_keeps_the_markup_balanced()
    {
        // Found in the phase's screenshot review: sanitizing the fragments AROUND a token handed the
        // sanitizer an orphan closing tag, so "Welcome, {{ first_name }}!" painted a literal "</strong>".
        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "<strong>Welcome, {{ first_name }}!</strong>",
            FontSize = "24px",
        }));

        var body = Body(cut);
        body.QuerySelector("strong").Should().NotBeNull("the heading keeps its bold element");
        body.QuerySelector(".tm-email-block__token").Should().NotBeNull();
        body.TextContent.Should().NotContain("</strong>", "the closing tag must never become visible text");
        body.TextContent.Should().NotContain("<strong>");
    }

    [Fact]
    public void The_same_markup_renders_identically_with_the_conservative_sanitizer()
    {
        // The WASM editor has no engine, so ConservativeHtmlContentSanitizer is what paints the canvas there.
        // It is stricter (an unknown tag becomes TEXT), and this pins that the token chips do not depend on
        // which sanitizer is registered.
        Services.Replace(ServiceDescriptor.Singleton<IHtmlContentSanitizer>(ConservativeHtmlContentSanitizer.Instance));

        var cut = RenderCanvas(DocWith(new EmailTextBlock
        {
            Content = "<strong>Welcome, {{ first_name }}!</strong>",
        }));

        var body = Body(cut);
        body.QuerySelector("strong").Should().NotBeNull();
        body.QuerySelector(".tm-email-block__token").Should().NotBeNull();
        body.TextContent.Should().NotContain("</strong>");
    }

    [Fact]
    public void Every_block_kind_renders_a_body_element()
    {
        var document = DocWith(
            new EmailTextBlock(),
            new EmailButtonBlock(),
            new EmailImageBlock(),
            new EmailDividerBlock(),
            new EmailSpacerBlock(),
            new EmailRawBlock(),
            new EmailTableBlock(),
            new EmailSocialBlock(),
            new EmailHeroBlock(),
            new EmailNavbarBlock(),
            new EmailCarouselBlock(),
            new EmailAccordionBlock(),
            new EmailWrapperBlock(),
            new EmailGroupBlock());

        var cut = RenderCanvas(document);

        var bodies = cut.FindAll("[data-tm-block-body]");
        bodies.Should().HaveCount(14, "each of the 14 block types has its own visual renderer");
        bodies.Select(b => b.GetAttribute("data-tm-block-kind"))
            .Should().OnlyHaveUniqueItems("the renderer declares which block it drew");
    }
}
