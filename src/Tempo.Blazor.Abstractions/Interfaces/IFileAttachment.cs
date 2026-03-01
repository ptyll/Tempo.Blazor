namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents a file attachment in TmActivityAttachments and TmFileDropZone.
/// </summary>
public interface IFileAttachment
{
    /// <summary>Unique identifier for the attachment.</summary>
    string Id { get; }

    /// <summary>Original file name (e.g. "document.pdf").</summary>
    string FileName { get; }

    /// <summary>MIME type (e.g. "image/jpeg", "application/pdf").</summary>
    string ContentType { get; }

    /// <summary>File size in bytes.</summary>
    long FileSizeBytes { get; }

    /// <summary>When this file was uploaded.</summary>
    DateTimeOffset UploadedAt { get; }

    /// <summary>Display name of the uploader (null if unknown).</summary>
    string? UploadedByName { get; }

    /// <summary>Whether the current user may delete this attachment.</summary>
    bool CanDelete { get; }

    /// <summary>True if this attachment is an image (can show preview).</summary>
    bool IsImage { get; }
}
