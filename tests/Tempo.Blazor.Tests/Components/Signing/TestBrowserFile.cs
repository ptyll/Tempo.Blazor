using Microsoft.AspNetCore.Components.Forms;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// A minimal <see cref="IBrowserFile"/> double whose <see cref="RequestImageFileAsync"/> actually
/// returns re-readable image data. bUnit's own <c>InputFileContent</c> fakes have no usable image
/// path, so the attachment step's local preview (phase 6) is exercised through this double — the
/// same path a real browser file takes.
/// </summary>
internal sealed class TestBrowserFile(string name, string contentType, byte[] content) : IBrowserFile
{
    /// <summary>A valid 1×1 PNG — the smallest image a preview can be computed from.</summary>
    public static byte[] PngBytes { get; } =
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public string Name { get; } = name;

    public DateTimeOffset LastModified { get; } = DateTimeOffset.Now;

    public long Size => content.Length;

    public string ContentType { get; } = contentType;

    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        => new MemoryStream(content);

    public Task<IBrowserFile> RequestImageFileAsync(string requestContentType, int maxAllowedWidth, int maxAllowedHeight)
        => Task.FromResult<IBrowserFile>(this);
}
