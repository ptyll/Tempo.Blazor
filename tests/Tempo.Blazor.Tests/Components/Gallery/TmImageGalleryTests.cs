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
        var cut = RenderComponent<TmImageGallery>(p => p
            .Add(c => c.Images, MakeImages(3)));

        cut.Find(".tm-image-gallery").Should().NotBeNull();
    }

    [Fact]
    public void TmImageGallery_Renders_Item_Per_Image()
    {
        var cut = RenderComponent<TmImageGallery>(p => p
            .Add(c => c.Images, MakeImages(3)));

        cut.FindAll(".tm-gallery-item").Count.Should().Be(3);
    }

    [Fact]
    public void TmImageGallery_Loading_Shows_Spinner()
    {
        var cut = RenderComponent<TmImageGallery>(p => p
            .Add(c => c.Images, MakeImages(0))
            .Add(c => c.IsLoading, true));

        cut.FindAll(".tm-spinner").Should().NotBeEmpty();
    }

    [Fact]
    public void TmImageGallery_Empty_Shows_EmptyState()
    {
        var cut = RenderComponent<TmImageGallery>(p => p
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
        var cut = RenderComponent<TmImageGallery>(p => p
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
        var cut = RenderComponent<TmImageGallery>(p => p
            .Add(c => c.Images, new List<IGalleryImage> { img }));

        cut.Find(".tm-gallery-item img").GetAttribute("src")
            .Should().Be("http://example.com/full.jpg");
    }

    [Fact]
    public void TmImageGallery_Item_Shows_Title()
    {
        var img = new TestGalleryImage("img1", null, null, "My Photo");
        var cut = RenderComponent<TmImageGallery>(p => p
            .Add(c => c.Images, new List<IGalleryImage> { img }));

        cut.Find(".tm-gallery-item-title").TextContent.Should().Contain("My Photo");
    }

    [Fact]
    public void TmImageGallery_Click_Fires_OnImageClick()
    {
        IGalleryImage? clicked = null;
        var img = new TestGalleryImage("img1", "http://example.com/img.jpg", null, "Test");
        var cut = RenderComponent<TmImageGallery>(p => p
            .Add(c => c.Images, new List<IGalleryImage> { img })
            .Add(c => c.OnImageClick, EventCallback.Factory.Create<IGalleryImage>(
                this, i => clicked = i)));

        cut.Find(".tm-gallery-item").Click();

        clicked.Should().NotBeNull();
        clicked!.Id.Should().Be("img1");
    }
}
