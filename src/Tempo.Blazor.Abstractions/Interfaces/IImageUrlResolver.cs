namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Strategy for resolving the display URL of a full-size image on demand.
/// Called FRESH for every image when the lightbox navigates to it.
///
/// IMPORTANT: Do NOT cache results — tickets are single-use and will fail on reuse.
///
/// Built-in implementations (Tempo.Blazor.Models):
/// - DirectImageUrlResolver  : imageId IS the URL (default when no resolver provided)
/// - FuncImageUrlResolver    : wraps a Func&lt;string, Task&lt;string&gt;&gt; lambda
///
/// Usage factory: ImageUrlResolvers.Direct() / ImageUrlResolvers.FromFunc(...)
/// </summary>
public interface IImageUrlResolver
{
    /// <summary>
    /// Resolves the display URL for the given image ID.
    /// For ticket-based access: calls the API to obtain a fresh single-use ticket URL.
    /// For direct access: returns the URL directly (or the imageId itself).
    /// </summary>
    /// <param name="imageId">The image identifier (matches IGalleryImage.Id).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> ResolveAsync(string imageId, CancellationToken ct = default);
}
