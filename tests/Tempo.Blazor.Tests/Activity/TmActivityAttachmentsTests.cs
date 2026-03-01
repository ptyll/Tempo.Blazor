using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using NSubstitute;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

file record Attachment(
    string Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    DateTimeOffset UploadedAt,
    string? UploadedByName,
    bool CanDelete,
    bool IsImage) : IFileAttachment;

public class TmActivityAttachmentsTests : LocalizationTestBase
{
    private static List<IFileAttachment> SampleAttachments() =>
    [
        new Attachment("a1", "photo.png",  "image/png",        1_024,        DateTimeOffset.Now.AddDays(-1), "Alice", true,  true),
        new Attachment("a2", "report.pdf", "application/pdf",  512 * 1_024,  DateTimeOffset.Now.AddDays(-2), "Bob",   false, false),
    ];

    private static IFileAttachmentProvider BuildProvider(
        IReadOnlyList<IFileAttachment>? list = null)
    {
        var p = Substitute.For<IFileAttachmentProvider>();
        p.GetAttachmentsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
         .Returns(Task.FromResult(list ?? (IReadOnlyList<IFileAttachment>)SampleAttachments()));
        p.UploadChunkAsync(Arg.Any<FileChunkData>(), Arg.Any<CancellationToken>())
         .Returns(Task.FromResult<string?>(null));
        p.DeleteAttachmentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
         .Returns(Task.CompletedTask);
        p.GetDownloadUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
         .Returns(Task.FromResult("https://cdn.example.com/file"));
        return p;
    }

    [Fact]
    public void Attachments_RendersFileList()
    {
        var cut = RenderComponent<TmActivityAttachments>(p => p
            .Add(c => c.Attachments, SampleAttachments()));

        cut.FindAll(".tm-attach-item").Count.Should().Be(2);
    }

    [Fact]
    public void Attachments_Empty_RendersEmptyState()
    {
        var cut = RenderComponent<TmActivityAttachments>(p => p
            .Add(c => c.Attachments, Array.Empty<IFileAttachment>()));

        cut.FindAll(".tm-attach-item").Should().BeEmpty();
        cut.FindAll(".tm-empty-state, .tm-attach-empty").Should().NotBeEmpty();
    }

    [Fact]
    public void Attachments_FileIcon_ByMimeType()
    {
        var cut = RenderComponent<TmActivityAttachments>(p => p
            .Add(c => c.Attachments, SampleAttachments()));

        var items = cut.FindAll(".tm-attach-item");
        // photo.png → image icon
        items[0].QuerySelectorAll(".tm-attach-icon-image").Length.Should().Be(1);
        // report.pdf → pdf icon
        items[1].QuerySelectorAll(".tm-attach-icon-pdf").Length.Should().Be(1);
    }

    [Fact]
    public void Attachments_FormatSize_BytesKBMB()
    {
        var list = new[]
        {
            new Attachment("a1", "tiny.txt",  "text/plain",       512,            DateTimeOffset.Now, null, false, false),
            new Attachment("a2", "mid.zip",   "application/zip",  2 * 1024,       DateTimeOffset.Now, null, false, false),
            new Attachment("a3", "big.mp4",   "video/mp4",        3 * 1024 * 1024, DateTimeOffset.Now, null, false, false),
        };
        var cut = RenderComponent<TmActivityAttachments>(p => p.Add(c => c.Attachments, list));

        var sizes = cut.FindAll(".tm-attach-size");
        sizes[0].TextContent.Should().Contain("B");
        sizes[1].TextContent.Should().Contain("KB");
        sizes[2].TextContent.Should().Contain("MB");
    }

    [Fact]
    public void Attachments_DeleteButton_ShowsWhenCanDelete()
    {
        var cut = RenderComponent<TmActivityAttachments>(p => p
            .Add(c => c.Attachments, SampleAttachments()));

        var items = cut.FindAll(".tm-attach-item");
        items[0].QuerySelectorAll(".tm-attach-delete-btn").Length.Should().Be(1);  // CanDelete=true
        items[1].QuerySelectorAll(".tm-attach-delete-btn").Length.Should().Be(0);  // CanDelete=false
    }

    [Fact]
    public async Task Attachments_Delete_CallsProvider()
    {
        var provider = BuildProvider();
        var cut = RenderComponent<TmActivityAttachments>(p => p
            .Add(c => c.Attachments, SampleAttachments())
            .Add(c => c.Provider, provider)
            .Add(c => c.EntityId, "entity-1"));

        cut.Find(".tm-attach-delete-btn").Click();
        await cut.InvokeAsync(() => { });

        await provider.Received(1).DeleteAttachmentAsync("a1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Attachments_Upload_ChunksFile_256KB()
    {
        const int ChunkSize = 256 * 1024;
        var provider = BuildProvider(new List<IFileAttachment>());
        var cut = RenderComponent<TmActivityAttachments>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.EntityId, "entity-1")
            .Add(c => c.AllowUpload, true));

        // 512KB file → exactly 2 chunks of 256KB
        var content = new byte[2 * ChunkSize];
        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(content, "large.bin", contentType: "application/octet-stream"));

        await cut.InvokeAsync(() => { });

        await provider.Received(2).UploadChunkAsync(
            Arg.Any<FileChunkData>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Attachments_UploadProgress_Displayed()
    {
        var blocker = new TaskCompletionSource<string?>();
        var provider = BuildProvider(new List<IFileAttachment>());
        provider.UploadChunkAsync(Arg.Any<FileChunkData>(), Arg.Any<CancellationToken>())
                .Returns(blocker.Task);

        var cut = RenderComponent<TmActivityAttachments>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.EntityId, "entity-1")
            .Add(c => c.AllowUpload, true));

        // Kick off upload in background
        _ = Task.Run(() =>
            cut.FindComponent<InputFile>().UploadFiles(
                InputFileContent.CreateFromBinary(new byte[100], "f.bin", contentType: "application/octet-stream")));

        // Brief yield to let the component reach its first await
        await Task.Delay(80);
        cut.Render();

        cut.FindAll(".tm-attach-progress").Should().NotBeEmpty();

        blocker.SetResult(null);
    }

    [Fact]
    public async Task Attachments_Upload_Complete_RefreshesListAsync()
    {
        var provider = BuildProvider(new List<IFileAttachment>());
        var cut = RenderComponent<TmActivityAttachments>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.EntityId, "entity-1")
            .Add(c => c.AllowUpload, true));

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(new byte[100], "f.txt", contentType: "text/plain"));
        await cut.InvokeAsync(() => { });

        await provider.Received(1).GetAttachmentsAsync("entity-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Attachments_DuplicateFile_Rejected()
    {
        var cut = RenderComponent<TmActivityAttachments>(p => p
            .Add(c => c.Attachments, SampleAttachments())
            .Add(c => c.AllowUpload, true));

        // photo.png already in the list
        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("ignored", "photo.png", contentType: "image/png"));

        cut.FindAll(".tm-attach-error").Should().NotBeEmpty();
    }
}
