namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents an image in TmImageGallery, TmImageLightbox, and TmImagePreview.
/// </summary>
public interface IGalleryImage
{
    /// <summary>Unique identifier used by IImageUrlResolver to fetch the full-size URL on demand.</summary>
    string Id { get; }

    /// <summary>
    /// Direct URL for the full-size image. When set and no IImageUrlResolver is provided,
    /// this URL is used directly. When null, IImageUrlResolver.ResolveAsync(Id) is called.
    /// </summary>
    string? Url { get; }

    /// <summary>
    /// Direct URL for the thumbnail/preview image (always used as-is, no resolver).
    /// If null, falls back to Url.
    /// </summary>
    string? ThumbnailUrl { get; }

    /// <summary>Optional display title.</summary>
    string? Title { get; }

    /// <summary>Optional description shown in the details panel.</summary>
    string? Description { get; }

    /// <summary>Tags associated with this image for filtering.</summary>
    IEnumerable<string> Tags { get; }

    /// <summary>Upload timestamp for display in details panel.</summary>
    DateTime? UploadedAt { get; }

    /// <summary>Name of the user who uploaded this image.</summary>
    string? UploadedBy { get; }

    /// <summary>File size in bytes for display in details panel.</summary>
    long? FileSizeBytes { get; }
}
