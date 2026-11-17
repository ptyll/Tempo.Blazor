using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Files;

public class TmAttachmentManagerTests : LocalizationTestBase
{
    [Fact]
    public void AttachmentManager_WithoutProvider_ShowsDropZoneOnly()
    {
        var cut = Render<TmAttachmentManager>();

        cut.FindAll(".tm-file-drop-zone").Should().NotBeEmpty();
        cut.FindAll(".tm-attachment-list").Should().BeEmpty();
    }

    [Fact]
    public void AttachmentManager_WithProvider_LoadsExistingFiles()
    {
        var provider = new FakeAttachmentProvider(new[]
        {
            Attachment("1", "report.pdf"),
        });
        var cut = Render<TmAttachmentManager>(p => p
            .Add(c => c.AttachmentProvider, provider)
            .Add(c => c.FileProvider, provider)
            .Add(c => c.EntityId, "entity-123"));

        cut.FindAll(".tm-attachment-item").Should().HaveCount(1);
    }

    [Fact]
    public void AttachmentManager_FileList_RendersCorrectly()
    {
        var provider = new FakeAttachmentProvider(new[]
        {
            Attachment("1", "document.pdf"),
            Attachment("2", "photo.jpg", "image/jpeg"),
        });
        var cut = Render<TmAttachmentManager>(p => p
            .Add(c => c.AttachmentProvider, provider)
            .Add(c => c.FileProvider, provider)
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
        var cut = Render<TmAttachmentManager>(p => p
            .Add(c => c.AttachmentProvider, provider)
            .Add(c => c.FileProvider, provider)
            .Add(c => c.EntityId, "entity-1"));

        await cut.InvokeAsync(() => { });  // ensure async init completes

        // Drop zone should always be present for uploading new files
        cut.FindAll(".tm-file-drop-zone").Should().NotBeEmpty();
    }

    [Fact]
    public async Task AttachmentManager_ReadOnly_HidesDelete_DropZone_AndMarksRoot()
    {
        var provider = new FakeAttachmentProvider(new[]
        {
            Attachment("1", "report.pdf"),
        });
        var cut = Render<TmAttachmentManager>(p => p
            .Add(c => c.AttachmentProvider, provider)
            .Add(c => c.FileProvider, provider)
            .Add(c => c.EntityId, "entity-123")
            .Add(c => c.ReadOnly, true));
        await cut.InvokeAsync(() => { });

        cut.FindAll(".tm-attachment-delete").Should().BeEmpty("ReadOnly hides the delete action");
        cut.FindAll(".tm-file-drop-zone").Should().BeEmpty("ReadOnly hides the upload drop zone");
        cut.Find(".tm-attachment-manager").ClassList
            .Should().Contain("tm-attachment-manager--readonly");
        cut.Markup.Should().NotContain("aria-readonly",
            "the manager is a plain div — the state is carried by the class, not an ARIA attribute");
    }

    [Fact]
    public async Task AttachmentManager_ReadOnly_KeepsListAndDownload()
    {
        var provider = new FakeAttachmentProvider(new[]
        {
            Attachment("1", "report.pdf"),
        });
        var cut = Render<TmAttachmentManager>(p => p
            .Add(c => c.AttachmentProvider, provider)
            .Add(c => c.FileProvider, provider)
            .Add(c => c.EntityId, "entity-123")
            .Add(c => c.ReadOnly, true));
        await cut.InvokeAsync(() => { });

        cut.FindAll(".tm-attachment-item").Should().HaveCount(1);
        cut.FindAll(".tm-attachment-download").Should().HaveCount(1, "ReadOnly hides delete/upload only");
    }

    [Fact]
    public async Task AttachmentManager_NotReadOnly_KeepsDeleteAndDropZone()
    {
        var provider = new FakeAttachmentProvider(new[]
        {
            Attachment("1", "report.pdf"),
        });
        var cut = Render<TmAttachmentManager>(p => p
            .Add(c => c.AttachmentProvider, provider)
            .Add(c => c.FileProvider, provider)
            .Add(c => c.EntityId, "entity-123"));
        await cut.InvokeAsync(() => { });

        cut.FindAll(".tm-attachment-delete").Should().NotBeEmpty();
        cut.FindAll(".tm-file-drop-zone").Should().NotBeEmpty();
        cut.Find(".tm-attachment-manager").ClassList
            .Should().NotContain("tm-attachment-manager--readonly");
    }

    [Fact]
    public async Task AttachmentManager_ReadOnly_Empty_ShowsEmptyState()
    {
        var provider = new FakeAttachmentProvider([]);
        var cut = Render<TmAttachmentManager>(p => p
            .Add(c => c.AttachmentProvider, provider)
            .Add(c => c.FileProvider, provider)
            .Add(c => c.EntityId, "entity-123")
            .Add(c => c.ReadOnly, true));
        await cut.InvokeAsync(() => { });

        cut.Find(".tm-attachment-manager__empty").TextContent.Should().NotBeEmpty(
            "a read-only manager with no attachments renders a visible empty state instead of nothing");
        cut.FindAll(".tm-file-drop-zone").Should().BeEmpty();
        cut.FindAll(".tm-attachment-delete").Should().BeEmpty();
    }

    private static TmAttachment Attachment(
        string id,
        string fileName,
        string contentType = "application/pdf")
        => new()
        {
            Id = id,
            AssetId = id,
            EntityRef = TmEntityRef.Create("attachment-manager-entity", "entity-123"),
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = 1024,
            UploadedAt = DateTimeOffset.Now,
            UploadedBy = new TmUserRef { Id = "test-user", DisplayName = "Test User" },
            CanDelete = true,
            CanDownload = true
        };

    private sealed class FakeAttachmentProvider(IReadOnlyList<TmAttachment> attachments) : ITmAttachmentProvider, ITmFileProvider
    {
        TmAttachmentProviderCapabilities ITmAttachmentProvider.Capabilities
            => TmAttachmentProviderCapabilities.Read | TmAttachmentProviderCapabilities.Add | TmAttachmentProviderCapabilities.Remove;

        TmAttachmentProviderCapabilities ITmCapabilityProvider<TmAttachmentProviderCapabilities>.Capabilities
            => TmAttachmentProviderCapabilities.Read | TmAttachmentProviderCapabilities.Add | TmAttachmentProviderCapabilities.Remove;

        TmFileProviderCapabilities ITmFileProvider.Capabilities
            => TmFileProviderCapabilities.Upload | TmFileProviderCapabilities.Resolve | TmFileProviderCapabilities.Delete;

        TmFileProviderCapabilities ITmCapabilityProvider<TmFileProviderCapabilities>.Capabilities
            => TmFileProviderCapabilities.Upload | TmFileProviderCapabilities.Resolve | TmFileProviderCapabilities.Delete;

        public Task<IReadOnlyList<TmAttachment>> GetForEntityAsync(TmEntityRef entityRef, CancellationToken cancellationToken = default)
            => Task.FromResult(attachments);

        public Task<TmAttachment> AddAsync(TmAttachment attachment, CancellationToken cancellationToken = default)
            => Task.FromResult(attachment);

        public Task RemoveAsync(TmEntityRef entityRef, string attachmentId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<TmFileUploadResult> UploadAsync(
            TmFileUploadRequest request,
            Stream content,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new TmFileUploadResult
            {
                Success = true,
                IsComplete = true,
                AssetId = Guid.NewGuid().ToString("N"),
                FileName = request.FileName,
                ContentType = request.ContentType,
                SizeBytes = request.SizeBytes
            });

        public Task<TmFileResolveResult> ResolveAsync(
            TmFileResolveRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new TmFileResolveResult
            {
                Success = true,
                AssetId = request.AssetId,
                Url = $"https://files.example.com/{request.AssetId}"
            });

        public Task DeleteAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
