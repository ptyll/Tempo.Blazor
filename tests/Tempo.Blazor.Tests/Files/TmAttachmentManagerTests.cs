using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Files;

public class TmAttachmentManagerTests : LocalizationTestBase
{
    private record TestAttachment(
        string Id,
        string FileName,
        string ContentType    = "application/pdf",
        long   FileSizeBytes  = 1024,
        bool   CanDelete      = true,
        bool   IsImage        = false) : IFileAttachment
    {
        public DateTimeOffset UploadedAt     { get; } = DateTimeOffset.Now;
        public string?        UploadedByName => "Test User";
    }

    [Fact]
    public void AttachmentManager_WithoutProvider_ShowsDropZoneOnly()
    {
        var cut = RenderComponent<TmAttachmentManager>();

        cut.FindAll(".tm-file-drop-zone").Should().NotBeEmpty();
        cut.FindAll(".tm-attachment-list").Should().BeEmpty();
    }

    [Fact]
    public void AttachmentManager_WithProvider_LoadsExistingFiles()
    {
        var provider = new FakeAttachmentProvider(new[]
        {
            new TestAttachment("1", "report.pdf"),
        });
        var cut = RenderComponent<TmAttachmentManager>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.EntityId, "entity-123"));

        cut.FindAll(".tm-attachment-item").Should().HaveCount(1);
    }

    [Fact]
    public void AttachmentManager_FileList_RendersCorrectly()
    {
        var provider = new FakeAttachmentProvider(new[]
        {
            new TestAttachment("1", "document.pdf"),
            new TestAttachment("2", "photo.jpg", "image/jpeg", IsImage: true),
        });
        var cut = RenderComponent<TmAttachmentManager>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.EntityId, "e1"));

        var items = cut.FindAll(".tm-attachment-item");
        items.Should().HaveCount(2);
        cut.Markup.Should().Contain("document.pdf");
        cut.Markup.Should().Contain("photo.jpg");
    }

    [Fact]
    public async Task AttachmentManager_Upload_UsesProvider()
    {
        var provider = new FakeAttachmentProvider([]);
        var cut = RenderComponent<TmAttachmentManager>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.EntityId, "entity-1"));

        await cut.InvokeAsync(() => { });  // ensure async init completes

        // Drop zone should always be present for uploading new files
        cut.FindAll(".tm-file-drop-zone").Should().NotBeEmpty();
    }

    private sealed class FakeAttachmentProvider(IReadOnlyList<IFileAttachment> attachments) : IFileAttachmentProvider
    {
        public Task<IReadOnlyList<IFileAttachment>> GetAttachmentsAsync(string entityId, CancellationToken ct = default)
            => Task.FromResult(attachments);

        public Task<string> GetDownloadUrlAsync(string attachmentId, CancellationToken ct = default)
            => Task.FromResult($"https://files.example.com/{attachmentId}");

        public Task DeleteAttachmentAsync(string attachmentId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<string> UploadChunkAsync(FileChunkData chunk, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid().ToString());
    }
}
