using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Demo.Shared;

public record AttachmentDto(
    string Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    DateTimeOffset UploadedAt,
    string? UploadedByName,
    bool CanDelete,
    bool IsImage) : IFileAttachment;
