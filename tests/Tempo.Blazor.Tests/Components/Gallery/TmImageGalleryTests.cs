using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Gallery;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Gallery;

file record TestGalleryImage(
    string Id,
    string? Url,
    string? ThumbnailUrl,
    string? Title) : IGalleryImage
{
    string? IGalleryImage.Description => null;
    IEnumerable<string> IGalleryImage.Tags => [];
    DateTime? IGalleryImage.UploadedAt => null;
    string? IGalleryImage.UploadedBy => null;
    long? IGalleryImage.FileSizeBytes => null;
}

/// <summary>TDD tests for TmImageGallery.</summary>
public class TmImageGalleryTests : LocalizationTestBase
{
    private static List<IGalleryImage> MakeImages(int count) =>
        Enumerable.Range(0, count)
            .Select(i => (IGalleryImage)new TestGalleryImage(
                $"img{i}", $"http://example.com/{i}.jpg",
                $"http://example.com/thumb{i}.jpg", $"Image {i}"))
            .ToList();

    [Fact]
    public void TmImageGallery_Renders_Gallery()
    {
        var cut = Render<TmImageGallery>(p => p
            .Add(c => c.Images, MakeImages(3)));

        cut.Find(".tm-image-gallery").Should().NotBeNull();
    }

    [Fact]
    public void TmImageGallery_Renders_Item_Per_Image()
    {
        var cut = Render<TmImageGallery>(p => p
            .Add(c => c.Images, MakeImages(3)));

        cut.FindAll(".tm-gallery-item").Count.Should().Be(3);
    }

    [Fact]
    public void TmImageGallery_Loading_Shows_Spinner()
    {
        var cut = Render<TmImageGallery>(p => p
            .Add(c => c.Images, MakeImages(0))
            .Add(c => c.IsLoading, true));

        cut.FindAll(".tm-spinner").Should().NotBeEmpty();
    }

    [Fact]
    public void TmImageGallery_Empty_Shows_EmptyState()
    {
        var cut = Render<TmImageGallery>(p => p
            .Add(c => c.Images, MakeImages(0))
            .Add(c => c.EmptyTitle, "No images found"));

        cut.FindAll(".tm-empty-state").Should().NotBeEmpty();
    }

    [Fact]
    public void TmImageGallery_Item_Shows_ThumbnailUrl()
    {
        var img = new TestGalleryImage("img1",
            "http://example.com/full.jpg",
            "http://example.com/thumb.jpg",
            "Test");
        var cut = Render<TmImageGallery>(p => p
            .Add(c => c.Images, new List<IGalleryImage> { img }));

        cut.Find(".tm-gallery-item img").GetAttribute("src")
            .Should().Be("http://example.com/thumb.jpg");
    }

    [Fact]
    public void TmImageGallery_Item_Falls_Back_To_Url_When_No_Thumbnail()
    {
        var img = new TestGalleryImage("img1",
            "http://example.com/full.jpg",
            null,
            "Test");
        var cut = Render<TmImageGallery>(p => p
            .Add(c => c.Images, new List<IGalleryImage> { img }));

        cut.Find(".tm-gallery-item img").GetAttribute("src")
            .Should().Be("http://example.com/full.jpg");
    }

    [Fact]
    public void TmImageGallery_Item_Shows_Title()
    {
        var img = new TestGalleryImage("img1", null, null, "My Photo");
        var cut = Render<TmImageGallery>(p => p
            .Add(c => c.Images, new List<IGalleryImage> { img }));

        cut.Find(".tm-gallery-item-title").TextContent.Should().Contain("My Photo");
    }

    [Fact]
    public void TmImageGallery_Click_Fires_OnImageClick()
    {
        IGalleryImage? clicked = null;
        var img = new TestGalleryImage("img1", "http://example.com/img.jpg", null, "Test");
        var cut = Render<TmImageGallery>(p => p
            .Add(c => c.Images, new List<IGalleryImage> { img })
            .Add(c => c.OnImageClick, EventCallback.Factory.Create<IGalleryImage>(
                this, i => clicked = i)));

        cut.Find(".tm-gallery-item").Click();

        clicked.Should().NotBeNull();
        clicked!.Id.Should().Be("img1");
    }

    // ── Native <button> items (keyboard-activation-convention.md) ──────────────

    [Fact]
    public void TmImageGallery_Item_IsANativeButton()
    {
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, MakeImages(2)));

        var item = cut.Find(".tm-gallery-item");
        item.TagName.Should().Be("BUTTON",
            "a native button gives keyboard users activation for free and gives the lightbox's focus" +
            " restore something focusable to return to");
        item.GetAttribute("type").Should().Be("button",
            "a gallery item must never submit an enclosing form");
    }

    [Fact]
    public void TmImageGallery_Item_CarriesNoEmulationResidue()
    {
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, MakeImages(1)));

        var item = cut.Find(".tm-gallery-item");
        item.GetAttribute("role").Should().BeNull("role=button on a <button> is emulation residue");
        item.GetAttribute("tabindex").Should().BeNull("a native button is already in the tab order");
    }

    [Fact]
    public void TmImageGallery_Item_HasNoEmulatedKeyHandler()
    {
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, MakeImages(1)));

        var item = cut.Find(".tm-gallery-item");
        // Enter AND the full native Space split (keydown, then the keyup the click rides on):
        // a native button activates itself; an @onkeydown/@onkeyup handler on any of them would
        // double-fire the activation (docs/keyboard-activation-convention.md).
        var enterDown = () => item.KeyDown("Enter");
        enterDown.Should().Throw<MissingEventHandlerException>();
        var spaceDown = () => item.KeyDown(" ");
        spaceDown.Should().Throw<MissingEventHandlerException>();
        var spaceUp = () => item.KeyUp(" ");
        spaceUp.Should().Throw<MissingEventHandlerException>();
    }

    [Fact]
    public void TmImageGallery_Items_CarryPerInstanceIds()
    {
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, MakeImages(3)));

        var ids = cut.FindAll(".tm-gallery-item")
            .Select(item => item.GetAttribute("id"))
            .ToList();
        ids.Should().OnlyHaveUniqueItems(
            "the lightbox restore target points at one specific thumbnail");
        ids[0].Should().StartWith("tm-gallery-").And.EndWith("-item-0");
        ids[1].Should().EndWith("-item-1");
        ids[2].Should().EndWith("-item-2");
    }

    [Fact]
    public void TmImageGallery_TwoGalleries_HaveDistinctItemIds()
    {
        var first = Render<TmImageGallery>(p => p.Add(c => c.Images, MakeImages(1)));
        var second = Render<TmImageGallery>(p => p.Add(c => c.Images, MakeImages(1)));

        var firstId = first.Find(".tm-gallery-item").GetAttribute("id");
        var secondId = second.Find(".tm-gallery-item").GetAttribute("id");

        firstId.Should().NotBeNullOrWhiteSpace();
        firstId.Should().NotBe(secondId,
            "focus restore must land in the gallery that opened the lightbox");
    }

    [Fact]
    public void TmImageGallery_Item_Title_IsASpanInsideTheButton()
    {
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, MakeImages(1)));

        cut.Find(".tm-gallery-item .tm-gallery-item-title").TagName.Should().Be("SPAN");
    }

    [Fact]
    public void TmImageGallery_Item_WithoutTitle_IsNamedByPosition()
    {
        var img = new TestGalleryImage("img1", "http://example.com/img.jpg", null, null);
        var cut = Render<TmImageGallery>(p => p
            .Add(c => c.Images, new List<IGalleryImage> { img, img }));

        var item = cut.Find(".tm-gallery-item");
        item.GetAttribute("aria-label").Should().Be("Open image 1 of 2",
            "a thumbnail without a Title still needs an accessible name (WCAG 4.1.2)");
        cut.Find(".tm-gallery-item img").GetAttribute("alt").Should().BeEmpty(
            "the button carries the name; the image would only double it");
    }
}
