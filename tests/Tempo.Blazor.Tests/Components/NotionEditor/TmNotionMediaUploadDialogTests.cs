using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>N11: media dialog keyboard, tabs, and the two-step insert.</summary>
public sealed class TmNotionMediaUploadDialogTests : LocalizationTestBase
{
    public TmNotionMediaUploadDialogTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Close"] = "Close",
            ["Tm_Cancel"] = "Cancel",
            ["TmNotionMediaUploadDialog_TabUpload"] = "Upload",
            ["TmNotionMediaUploadDialog_TabEmbed"] = "Embed",
            ["TmNotionMediaUploadDialog_TabLibrary"] = "Library",
            ["TmNotionMediaUploadDialog_DropZone"] = "Drop a file",
            ["TmNotionMediaUploadDialog_BrowseFiles"] = "or browse",
            ["TmNotionMediaUploadDialog_ChooseFile"] = "Choose file",
            ["TmNotionMediaUploadDialog_Formats"] = "Supported: {0}",
            ["TmNotionMediaUploadDialog_SelectedFile"] = "Selected file",
            ["TmNotionMediaUploadDialog_RemoveFile"] = "Remove selected file",
            ["TmNotionMediaUploadDialog_UrlPlaceholder"] = "https://",
            ["TmNotionMediaUploadDialog_Insert"] = "Insert",
            ["TmNotionMediaUploadDialog_SearchPlaceholder"] = "Search",
            ["TmNotionMediaUploadDialog_LoadMore"] = "Load more",
            ["TmNotionMediaUploadDialog_NoResults"] = "Nothing here",
            ["TmNotionMediaUploadDialog_Uploading"] = "Uploading",
        });
    }

    [Fact]
    public void Tabs_ExposeATablistWithControlsAndPanels()
    {
        var cut = RenderDialog();

        cut.Find("[role='tablist']").Should().NotBeNull();
        cut.Find("#tm-media-tab-upload").GetAttribute("aria-controls").Should().Be("tm-media-panel-upload");
        cut.Find("#tm-media-tab-embed").GetAttribute("aria-controls").Should().Be("tm-media-panel-embed");
        cut.Find("#tm-media-panel-upload").GetAttribute("role").Should().Be("tabpanel");
        cut.Find("#tm-media-panel-embed").GetAttribute("role").Should().Be("tabpanel");
    }

    [Fact]
    public async Task Tabs_ArrowKeys_MoveBetweenTabs()
    {
        var cut = RenderDialog(withLibrary: true);

        cut.Find("#tm-media-tab-upload").GetAttribute("aria-selected").Should().Be("true");
        cut.Find("#tm-media-tab-embed").GetAttribute("tabindex").Should().Be("-1");

        await cut.Find("#tm-media-tab-upload")
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        cut.Find("#tm-media-tab-embed").GetAttribute("aria-selected").Should().Be("true");
        cut.Find("#tm-media-tab-upload").GetAttribute("tabindex").Should().Be("-1");

        await cut.Find("#tm-media-tab-embed")
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        cut.Find("#tm-media-tab-library").GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void Insert_StartsDisabled_UntilSomethingIsChosen()
    {
        var cut = RenderDialog();

        cut.Find("[data-testid='notion-media-insert']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Upload_StatesTheSupportedFormats()
    {
        var cut = RenderDialog();

        cut.Find(".tm-media-dialog__formats").TextContent.Should().Contain("PNG");
        cut.Find(".tm-media-dialog__choose-file").TextContent.Should().Contain("Choose file");
    }

    [Fact]
    public async Task Close_RaisesOnCancelledOnce()
    {
        // Escape is the core TmModal's own handler and takes this same OnClose path.
        var cancels = 0;
        var cut = RenderDialog(onCancelled: () => cancels++);

        await cut.Find(".tm-modal-close").ClickAsync(new MouseEventArgs());

        cancels.Should().Be(1);
    }

    private IRenderedComponent<TmNotionMediaUploadDialog> RenderDialog(
        bool withLibrary = false, Action? onCancelled = null)
    {
        var context = new NotionEditorContext
        {
            DataProvider = Substitute.For<INotionDataProvider>(),
            BlockService = Substitute.For<INotionEditorBlockService>(),
            FileProvider = Substitute.For<ITmFileProvider>(),
            MediaLibraryProvider = withLibrary ? Library() : null,
        };

        return Render<TmNotionMediaUploadDialog>(parameters =>
        {
            parameters.Add(component => component.IsOpen, true);
            parameters.Add(component => component.MediaType, "image");
            parameters.AddCascadingValue(context);
            if (onCancelled is not null)
                parameters.Add(component => component.OnCancelled, EventCallback.Factory.Create(this, onCancelled));
        });
    }

    private static INotionMediaLibraryProvider Library()
    {
        var provider = Substitute.For<INotionMediaLibraryProvider>();
        provider.SearchAsync(
                Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<INotionMediaLibraryItem>>([]));
        return provider;
    }
}
