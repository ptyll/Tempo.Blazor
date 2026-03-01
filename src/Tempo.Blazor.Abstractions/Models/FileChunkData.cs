namespace Tempo.Blazor.Models;

/// <summary>
/// Represents a single 256 KB chunk in a chunked file upload.
/// Used with IFileAttachmentProvider.UploadChunkAsync().
/// </summary>
public record FileChunkData(
    string FileName,
    string ContentType,
    long TotalSize,
    int ChunkIndex,
    int TotalChunks,
    byte[] Data,
    string? EntityId = null)
{
    /// <summary>True when this is the final chunk of the file.</summary>
    public bool IsLast => ChunkIndex == TotalChunks - 1;
}
