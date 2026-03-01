using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Gallery;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Gallery;

public class TmImageGalleryLightboxTests : LocalizationTestBase
{
    private static TestImage Img(string id, string url, string? title = null) =>
        new(id, url, title);

    private record TestImage(
        string Id,
        string Url,
        string? Title = null) : IGalleryImage
    {
        public string? ThumbnailUrl  => null;
        public string? Description   => null;
        public IEnumerable<string> Tags => [];
        public DateTime? UploadedAt  => null;
        public string? UploadedBy   => null;
        public long?   FileSizeBytes => null;
    }

    [Fact]
    public void Gallery_ClickThumbnail_OpensLightbox()
    {
        var images = new[] { Img("1", "http://img/1.jpg", "First") };
        var cut = RenderComponent<TmImageGallery>(p => p.Add(c => c.Images, images));

        cut.Find(".tm-gallery-item").Click();

        cut.FindAll(".tm-lightbox").Should().HaveCount(1);
    }

    [Fact]
    public void Gallery_Lightbox_ShowsFullImage()
    {
        var images = new[] { Img("1", "http://img/1.jpg", "Hero") };
        var cut = RenderComponent<TmImageGallery>(p => p.Add(c => c.Images, images));

        cut.Find(".tm-gallery-item").Click();

        cut.Find(".tm-lightbox img").GetAttribute("src").Should().Contain("1.jpg");
    }

    [Fact]
    public void Gallery_Lightbox_CloseButton_Closes()
    {
        var images = new[] { Img("1", "http://img/1.jpg") };
        var cut = RenderComponent<TmImageGallery>(p => p.Add(c => c.Images, images));

        cut.Find(".tm-gallery-item").Click();
        cut.Find(".tm-lightbox-close").Click();

        cut.FindAll(".tm-lightbox").Should().BeEmpty();
    }

    [Fact]
    public void Gallery_Lightbox_ArrowRight_NavigatesToNext()
    {
        var images = new[]
        {
            Img("1", "http://img/1.jpg", "First"),
            Img("2", "http://img/2.jpg", "Second"),
        };
        var cut = RenderComponent<TmImageGallery>(p => p.Add(c => c.Images, images));

        cut.FindAll(".tm-gallery-item")[0].Click();
        cut.Find(".tm-lightbox-next").Click();

        cut.Find(".tm-lightbox img").GetAttribute("src").Should().Contain("2.jpg");
    }

    [Fact]
    public void Gallery_Lightbox_ArrowLeft_NavigatesToPrevious()
    {
        var images = new[]
        {
            Img("1", "http://img/1.jpg", "First"),
            Img("2", "http://img/2.jpg", "Second"),
        };
        var cut = RenderComponent<TmImageGallery>(p => p.Add(c => c.Images, images));

        cut.FindAll(".tm-gallery-item")[1].Click();
        cut.Find(".tm-lightbox-prev").Click();

        cut.Find(".tm-lightbox img").GetAttribute("src").Should().Contain("1.jpg");
    }

    [Fact]
    public void Gallery_Lightbox_Escape_Closes()
    {
        var images = new[] { Img("1", "http://img/1.jpg") };
        var cut = RenderComponent<TmImageGallery>(p => p.Add(c => c.Images, images));

        cut.Find(".tm-gallery-item").Click();
        cut.Find(".tm-lightbox").KeyDown(Key.Escape);

        cut.FindAll(".tm-lightbox").Should().BeEmpty();
    }

    [Fact]
    public void Gallery_Lightbox_ShowsImageCount()
    {
        var images = new[]
        {
            Img("1", "http://img/1.jpg"),
            Img("2", "http://img/2.jpg"),
            Img("3", "http://img/3.jpg"),
        };
        var cut = RenderComponent<TmImageGallery>(p => p.Add(c => c.Images, images));

        cut.FindAll(".tm-gallery-item")[0].Click();

        cut.Find(".tm-lightbox-counter").TextContent.Should().Contain("1");
        cut.Find(".tm-lightbox-counter").TextContent.Should().Contain("3");
    }

    [Fact]
    public async Task Gallery_WithProvider_FetchesTicketUrl()
    {
        var provider = new FakeProvider("http://ticket/1.jpg");
        Services.AddSingleton<IImageGalleryDataProvider>(provider);

        var images = new[] { Img("img-1", "http://img/1.jpg", "Via ticket") };
        var cut = RenderComponent<TmImageGallery>(p => p
            .Add(c => c.Images,        images)
            .Add(c => c.DataProvider,  provider));

        cut.Find(".tm-gallery-item").Click();
        await cut.InvokeAsync(() => { }); // allow async ticket fetch

        cut.Find(".tm-lightbox img").GetAttribute("src").Should().Be("http://ticket/1.jpg");
    }

    [Fact]
    public void Gallery_DeleteButton_ShowsWhenCanDelete()
    {
        var images = new[] { Img("1", "http://img/1.jpg") };
        var provider = new FakeProvider("http://ticket/1.jpg");
        var cut = RenderComponent<TmImageGallery>(p => p
            .Add(c => c.Images,       images)
            .Add(c => c.DataProvider, provider)
            .Add(c => c.CanDelete,    true));

        cut.Find(".tm-gallery-item").Click();

        cut.FindAll(".tm-lightbox-delete").Should().NotBeEmpty();
    }

    private sealed class FakeProvider(string ticketUrl) : IImageGalleryDataProvider
    {
        public Task<IReadOnlyList<IGalleryImage>> GetImagesAsync(string? entityId = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IGalleryImage>>([]);

        public Task<string> GetImageTicketUrlAsync(string imageId, CancellationToken ct = default)
            => Task.FromResult(ticketUrl);

        public Task DeleteImageAsync(string imageId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
