using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NSubstitute.ReturnsExtensions;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// MLD-01..05: Media Library tab in TmNotionMediaUploadDialog.
/// TDD: tests written before implementation — initially expected to FAIL (RED),
/// then pass after UI is implemented (GREEN).
/// </summary>
public class TmNotionMediaUploadDialogLibraryTests : LocalizationTestBase
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private NotionEditorContext BuildContext(
        INotionMediaLibraryProvider? library  = null,
        ITmFileProvider?             file     = null)
        => new()
        {
            DataProvider         = Substitute.For<INotionDataProvider>(),
            BlockService        = Substitute.For<INotionEditorBlockService>(),
            FileProvider         = file,
            MediaLibraryProvider = library,
        };

    private static INotionMediaLibraryProvider ProviderWith(
        params INotionMediaLibraryItem[] items)
    {
        var p = Substitute.For<INotionMediaLibraryProvider>();
        p.SearchAsync(
                Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
         .Returns(Task.FromResult<IEnumerable<INotionMediaLibraryItem>>(items));
        return p;
    }

    private static NotionMediaLibraryItem MakeItem(string id, string name, string url) =>
        new() { Id = id, Name = name, Url = url, ContentType = "image/jpeg", ThumbnailUrl = url };

    // ── MLD-01: Library tab hidden without provider ───────────────────────────

    [Fact]
    public void LibraryTab_WithoutProvider_IsNotVisible()
    {
        var ctx = BuildContext();
        var cut = Render<TmNotionMediaUploadDialog>(p => p
            .Add(x => x.IsOpen,    true)
            .Add(x => x.MediaType, "image")
            .AddCascadingValue(ctx));

        cut.FindAll("[data-tab='library']").Should().BeEmpty();
    }

    // ── MLD-02: Library tab visible with provider ─────────────────────────────

    [Fact]
    public void LibraryTab_WithProvider_IsVisible()
    {
        var ctx = BuildContext(library: ProviderWith());
        var cut = Render<TmNotionMediaUploadDialog>(p => p
            .Add(x => x.IsOpen,    true)
            .Add(x => x.MediaType, "image")
            .AddCascadingValue(ctx));

        cut.Find("[data-tab='library']").Should().NotBeNull();
    }

    // ── MLD-03: Library tab is initial active tab when no FileProvider ─────────

    [Fact]
    public void LibraryTab_NoFileProvider_IsFirstActiveTab()
    {
        var ctx = BuildContext(library: ProviderWith());
        var cut = Render<TmNotionMediaUploadDialog>(p => p
            .Add(x => x.IsOpen,    true)
            .Add(x => x.MediaType, "image")
            .AddCascadingValue(ctx));

        var libraryTab = cut.Find("[data-tab='library']");
        libraryTab.ClassList.Should().Contain("tm-media-dialog__tab--active");
    }

    // ── MLD-04: Selecting library item fires OnConfirmed with correct URL ─────

    [Fact]
    public async Task LibraryItem_Click_FiresOnConfirmedWithItemUrl()
    {
        var item = MakeItem("img1", "photo.jpg", "https://example.com/photo.jpg");
        var ctx  = BuildContext(library: ProviderWith(item));

        (string? FileId, string? Url) confirmed = default;

        var cut = Render<TmNotionMediaUploadDialog>(p => p
            .Add(x => x.IsOpen,    true)
            .Add(x => x.MediaType, "image")
            .Add(x => x.OnConfirmed,
                 EventCallback.Factory.Create<(string?, string?)>(
                     this, args => confirmed = args))
            .AddCascadingValue(ctx));

        // Switch to library tab (may already be active)
        var libraryTab = cut.Find("[data-tab='library']");
        await cut.InvokeAsync(() => libraryTab.Click());

        // Wait for items to render
        cut.WaitForState(() => cut.FindAll(".tm-media-library__item").Count > 0);

        var btn = cut.Find(".tm-media-library__item");
        await cut.InvokeAsync(() => btn.Click());
        confirmed.Url.Should().BeNull("a click only selects; Insert confirms");
        await cut.InvokeAsync(() => cut.Find("[data-testid=notion-media-insert]").Click());

        confirmed.Url.Should().Be("https://example.com/photo.jpg");
    }

    // ── MLD-05: Selecting library item fires OnConfirmed with item Id as FileId

    [Fact]
    public async Task LibraryItem_Click_FiresOnConfirmedWithItemId()
    {
        var item = MakeItem("img42", "hero.jpg", "https://example.com/hero.jpg");
        var ctx  = BuildContext(library: ProviderWith(item));

        (string? FileId, string? Url) confirmed = default;

        var cut = Render<TmNotionMediaUploadDialog>(p => p
            .Add(x => x.IsOpen,    true)
            .Add(x => x.MediaType, "image")
            .Add(x => x.OnConfirmed,
                 EventCallback.Factory.Create<(string?, string?)>(
                     this, args => confirmed = args))
            .AddCascadingValue(ctx));

        var libraryTab = cut.Find("[data-tab='library']");
        await cut.InvokeAsync(() => libraryTab.Click());

        cut.WaitForState(() => cut.FindAll(".tm-media-library__item").Count > 0);

        await cut.InvokeAsync(() => cut.Find(".tm-media-library__item").Click());
        await cut.InvokeAsync(() => cut.Find("[data-testid=notion-media-insert]").Click());

        confirmed.FileId.Should().Be("img42");
    }

    // ── MLD-06: the library panel renders while upload is also available ──────

    [Fact]
    public async Task LibraryTab_WithFileProvider_ShowsSearchAndItems()
    {
        var item = MakeItem("img7", "cover.png", "https://example.com/cover.png");
        var ctx = BuildContext(library: ProviderWith(item), file: Substitute.For<ITmFileProvider>());

        var cut = Render<TmNotionMediaUploadDialog>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.MediaType, "image")
            .AddCascadingValue(ctx));

        await cut.InvokeAsync(() => cut.Find("[data-tab='library']").Click());

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-media-library__search-input").Should().NotBeNull();
            cut.FindAll(".tm-media-library__item").Should().ContainSingle()
                .Which.TextContent.Should().Contain("cover.png");
        });
    }

    // ── MLD-07: a provider error is one request, not a render loop ────────────

    [Fact]
    public async Task LibraryTab_ProviderError_SearchesOnce()
    {
        var library = Substitute.For<INotionMediaLibraryProvider>();
        library.SearchAsync(
                Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("library down"));
        var ctx = BuildContext(library: library, file: Substitute.For<ITmFileProvider>());

        var cut = Render<TmNotionMediaUploadDialog>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.MediaType, "image")
            .AddCascadingValue(ctx));

        await cut.InvokeAsync(() => cut.Find("[data-tab='library']").Click());

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("TmNotionMediaUploadDialog_LibraryError"));
        await cut.InvokeAsync(() => cut.Find("[data-tab='library']").Click(new MouseEventArgs()));

        await library.Received(1).SearchAsync(
            Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
