using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Components;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 9A — <see cref="CanvasImageLoading"/>: the editor canvas is the one place where a template's
/// remote images become requests from the AUTHOR's browser (a 1×1 tracker in an imported template phones
/// home on every keystroke otherwise). The default stays <see cref="CanvasImageLoading.Load"/> — the
/// author is writing the email and wants to see it — and <see cref="CanvasImageLoading.Placeholder"/>
/// renders a framed alt-text placeholder with no request at all.
/// </summary>
public class TmEmailCanvasImageLoadingTests : EmailCanvasTestBase
{
    private static EmailTemplateDocument DocWithImage()
        => DocWith(new EmailImageBlock { Src = "https://tracker.example/pixel.gif", Alt = "Company logo" });

    [Fact]
    public void Load_is_the_default_and_renders_the_external_source()
    {
        var cut = RenderCanvas(DocWithImage());

        var image = cut.Find("img");
        image.GetAttribute("src").Should().Be("https://tracker.example/pixel.gif");
    }

    [Fact]
    public void Placeholder_emits_no_src_at_all_so_the_browser_makes_no_request()
    {
        var cut = RenderCanvas(DocWithImage(), imageLoading: CanvasImageLoading.Placeholder);

        cut.FindAll("img").Should().BeEmpty("placeholder mode must not create an img with a remote src");
        cut.Markup.Should().NotContain("tracker.example");
    }

    [Fact]
    public void Placeholder_shows_a_frame_with_the_alt_text()
    {
        var cut = RenderCanvas(DocWithImage(), imageLoading: CanvasImageLoading.Placeholder);

        var placeholder = cut.Find("[data-tm-image-placeholder]");
        placeholder.TextContent.Should().Contain("Company logo");
        placeholder.GetAttribute("role").Should().Be("img", "the placeholder replaces the image for assistive tech");
        placeholder.GetAttribute("aria-label").Should().Be("Company logo");
        placeholder.TextContent.Should().Contain("privacy mode",
            "privacy mode is not a refused URL — the reason must say so (a screenshot found it said 'unsafe')");
        placeholder.HasAttribute("data-tm-image-blocked").Should().BeFalse();
    }

    [Fact]
    public void Placeholder_also_covers_the_hero_background()
    {
        var cut = RenderCanvas(
            DocWith(new EmailHeroBlock { BackgroundUrl = "https://tracker.example/hero.jpg" }),
            imageLoading: CanvasImageLoading.Placeholder);

        cut.FindAll("img").Should().BeEmpty();
        cut.Markup.Should().NotContain("tracker.example");
    }

    [Fact]
    public void Placeholder_also_covers_the_carousel()
    {
        var carousel = new EmailCarouselBlock();
        carousel.Images.Add(new EmailCarouselImage { Src = "https://tracker.example/1.png", Alt = "One" });
        carousel.Images.Add(new EmailCarouselImage { Src = "https://tracker.example/2.png", Alt = "Two" });

        var cut = RenderCanvas(DocWith(carousel), imageLoading: CanvasImageLoading.Placeholder);

        cut.FindAll("img").Should().BeEmpty();
        cut.Find("[data-tm-image-placeholder]").Should().NotBeNull();
        cut.Find(".tm-email-block__carousel-count").TextContent.Should().Contain("1 / 2",
            "the count stays: it is model information, not a request");
    }

    [Fact]
    public void Placeholder_also_covers_the_section_background()
    {
        var document = DocWith(new EmailTextBlock { Content = "Hi" });
        document.Sections[0].BackgroundUrl = "https://tracker.example/bg.png";

        var cut = RenderCanvas(document, imageLoading: CanvasImageLoading.Placeholder);

        cut.FindAll("img").Should().BeEmpty();
        cut.Markup.Should().NotContain("tracker.example");
    }

    [Fact]
    public void Placeholder_also_covers_the_wrapper_background()
    {
        var cut = RenderCanvas(
            DocWith(new EmailWrapperBlock { BackgroundUrl = "https://tracker.example/w.png" }),
            imageLoading: CanvasImageLoading.Placeholder);

        cut.FindAll("img").Should().BeEmpty();
    }

    [Fact]
    public void The_editor_forwards_the_canvas_image_loading_mode()
    {
        var document = DocWithImage();

        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.CanvasImageLoading, CanvasImageLoading.Placeholder));

        cut.FindAll("img").Should().BeEmpty("the editor parameter reaches every block renderer");
        cut.Find("[data-tm-image-placeholder]").Should().NotBeNull();
    }
}
