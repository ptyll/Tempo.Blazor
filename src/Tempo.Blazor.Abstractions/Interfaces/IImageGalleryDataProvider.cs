namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Server-side data provider for TmImageGallery.
/// Implement this interface to load, delete, and stream images from any source.
/// </summary>
public interface IImageGalleryDataProvider
{
    /// <summary>Retrieves all images to display in the gallery, optionally scoped to an entity.</summary>
    Task<IReadOnlyList<IGalleryImage>> GetImagesAsync(string? entityId = null, CancellationToken ct = default);

    /// <summary>
    /// Returns a one-time ticket URL for secure, authenticated image streaming.
    /// Called by TmLightbox when opening a full-size image.
    /// </summary>
    Task<string> GetImageTicketUrlAsync(string imageId, CancellationToken ct = default);

    /// <summary>Deletes an image by its identifier.</summary>
    Task DeleteImageAsync(string imageId, CancellationToken ct = default);
}
