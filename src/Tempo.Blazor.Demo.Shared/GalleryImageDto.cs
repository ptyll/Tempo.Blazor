using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Demo.Shared;

public record GalleryImageDto(
    string Id,
    string? Title,
    string? ThumbnailUrl,
    string? Url,
    string? Description = null,
    DateTime? UploadedAt = null,
    string? UploadedBy = null,
    long? FileSizeBytes = null) : IGalleryImage
{
    public IEnumerable<string> Tags => [];
}
