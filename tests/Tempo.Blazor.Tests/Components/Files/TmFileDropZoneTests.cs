using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>TDD tests for TmFileDropZone.</summary>
public class TmFileDropZoneTests : LocalizationTestBase
{
    [Fact]
    public void TmFileDropZone_Renders_DropZone()
    {
        var cut = Render<TmFileDropZone>();

        cut.Find(".tm-file-drop-zone").Should().NotBeNull();
    }

    [Fact]
    public void TmFileDropZone_Has_DropArea()
    {
        var cut = Render<TmFileDropZone>();

        cut.Find(".tm-file-drop-zone__area").Should().NotBeNull();
    }

    [Fact]
    public void TmFileDropZone_Has_FileInput()
    {
        var cut = Render<TmFileDropZone>();

        cut.FindAll("input[type=file]").Should().NotBeEmpty();
    }

    [Fact]
    public void TmFileDropZone_Disabled_Has_Disabled_Class()
    {
        var cut = Render<TmFileDropZone>(p => p
            .Add(c => c.Disabled, true));

        cut.Find(".tm-file-drop-zone").ClassList
            .Should().Contain("tm-file-drop-zone--disabled");
    }

    [Fact]
    public void TmFileDropZone_Multiple_Sets_Input_Multiple()
    {
        var cut = Render<TmFileDropZone>(p => p
            .Add(c => c.Multiple, true));

        cut.Find("input[type=file]").HasAttribute("multiple").Should().BeTrue();
    }

    [Fact]
    public void TmFileDropZone_Accept_Sets_Input_Accept()
    {
        var cut = Render<TmFileDropZone>(p => p
            .Add(c => c.Accept, "image/*,.pdf"));

        cut.Find("input[type=file]").GetAttribute("accept")
            .Should().Be("image/*,.pdf");
    }

    [Fact]
    public void TmFileDropZone_Shows_DefaultContent()
    {
        var cut = Render<TmFileDropZone>();

        cut.Find(".tm-file-drop-zone__hint").Should().NotBeNull();
        cut.Find(".tm-file-drop-zone__browse").Should().NotBeNull();
    }

    [Fact]
    public void TmFileDropZone_Shows_CustomContent()
    {
        var cut = Render<TmFileDropZone>(p => p
            .AddChildContent("<span class='custom'>Upload here</span>"));

        cut.Find(".custom").TextContent.Should().Contain("Upload here");
        cut.FindAll(".tm-file-drop-zone__hint").Count.Should().Be(0);
    }

    [Fact]
    public void TmFileDropZone_InputCoversDropArea()
    {
        var cut = Render<TmFileDropZone>();

        // InputFile should have the overlay class inside the area
        cut.Find(".tm-file-drop-zone__area .tm-file-drop-zone__input").Should().NotBeNull();
    }

    [Fact]
    public void TmFileDropZone_HasIcon()
    {
        var cut = Render<TmFileDropZone>();

        cut.Find(".tm-file-drop-zone__icon").Should().NotBeNull();
    }

    [Fact]
    public void TmFileDropZone_NoFilesInitially()
    {
        var cut = Render<TmFileDropZone>();

        cut.FindAll(".tm-file-drop-zone__file-item").Count.Should().Be(0);
    }

    [Fact]
    public void TmFileDropZone_RemoveFile_Raises_OnFileRemoved()
    {
        IBrowserFile? removed = null;
        var cut = Render<TmFileDropZone>(p => p
            .Add(c => c.OnFileRemoved, EventCallback.Factory.Create<IBrowserFile>(this, f => removed = f)));

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("x", "a.txt"));
        cut.FindAll(".tm-file-drop-zone__file-item").Should().HaveCount(1);

        cut.Find(".tm-file-drop-zone__file-remove").Click();

        removed.Should().NotBeNull();
        removed!.Name.Should().Be("a.txt");
        cut.Instance.SelectedFiles.Should().BeEmpty();
        cut.FindAll(".tm-file-drop-zone__file-item").Should().BeEmpty();
    }

    [Fact]
    public void TmFileDropZone_RemoveFile_CallsBackOncePerFile()
    {
        var removedNames = new List<string>();
        var cut = Render<TmFileDropZone>(p => p
            .Add(c => c.Multiple, true)
            .Add(c => c.OnFileRemoved, EventCallback.Factory.Create<IBrowserFile>(this, f => removedNames.Add(f.Name))));

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("x", "a.txt"),
            InputFileContent.CreateFromText("y", "b.txt"));

        cut.FindAll(".tm-file-drop-zone__file-remove")[1].Click();

        removedNames.Should().ContainSingle().Which.Should().Be("b.txt");
        cut.Instance.SelectedFiles.Select(f => f.Name).Should().BeEquivalentTo("a.txt");
    }

    [Fact]
    public async Task TmFileDropZone_RemoveAsync_FromOutside_Rerenders()
    {
        var removedNames = new List<string>();
        var cut = Render<TmFileDropZone>(p => p
            .Add(c => c.OnFileRemoved, EventCallback.Factory.Create<IBrowserFile>(this, f => removedNames.Add(f.Name))));

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("x", "a.txt"));
        cut.FindAll(".tm-file-drop-zone__file-item").Should().HaveCount(1);

        // Host-controlled removal from OUTSIDE an event handler of the zone: the built-in
        // list must re-render so the removed file does not linger on screen.
        await cut.InvokeAsync(() => cut.Instance.RemoveAsync(cut.Instance.SelectedFiles[0]));

        cut.Instance.SelectedFiles.Should().BeEmpty();
        cut.FindAll(".tm-file-drop-zone__file-item").Should().BeEmpty();
        removedNames.Should().ContainSingle().Which.Should().Be("a.txt");
    }

    [Fact]
    public async Task TmFileDropZone_RemoveAsync_UnknownFile_DoesNotRaise_OnFileRemoved()
    {
        var removedNames = new List<string>();
        var cut = Render<TmFileDropZone>(p => p
            .Add(c => c.OnFileRemoved, EventCallback.Factory.Create<IBrowserFile>(this, f => removedNames.Add(f.Name))));

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("x", "a.txt"));

        await cut.InvokeAsync(() => cut.Instance.RemoveAsync(new UnknownBrowserFile("never-selected.txt")));

        removedNames.Should().BeEmpty("OnFileRemoved is not raised for a file that was never in the selection");
        cut.Instance.SelectedFiles.Should().ContainSingle();
    }

    private sealed class UnknownBrowserFile(string name) : IBrowserFile
    {
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.Now;
        public long Size => 1;
        public string ContentType => "text/plain";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
            => new MemoryStream([]);
    }
}
