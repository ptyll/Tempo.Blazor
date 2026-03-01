using Tempo.Blazor.Models;

namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Server-side provider for TmActivityAttachments.
/// Handles loading, downloading, deleting, and 256 KB-chunked uploading of file attachments.
/// </summary>
public interface IFileAttachmentProvider
{
    /// <summary>Retrieves all attachments belonging to the given entity.</summary>
    Task<IReadOnlyList<IFileAttachment>> GetAttachmentsAsync(string entityId, CancellationToken ct = default);

    /// <summary>Returns a short-lived download URL for the attachment.</summary>
    Task<string> GetDownloadUrlAsync(string attachmentId, CancellationToken ct = default);

    /// <summary>Deletes an attachment by its identifier.</summary>
    Task DeleteAttachmentAsync(string attachmentId, CancellationToken ct = default);

    /// <summary>
    /// Uploads a single 256 KB chunk. Returns an upload session ID (null on first chunk
    /// response if the provider uses an implicit session). <see cref="FileChunkData.IsLast"/>
    /// signals the final chunk.
    /// </summary>
    Task<string?> UploadChunkAsync(FileChunkData chunk, CancellationToken ct = default);
}
