using System.Collections.Concurrent;
using Tempo.Blazor.Demo.Shared;

namespace Tempo.Blazor.Demo.Api.Data;

public class MockImageStore
{
    // Local demo images: the SVG files live in Tempo.Blazor.Demo.SharedUI/wwwroot/gallery so every
    // demo host serves them at the page origin (thumbnails), and the API serves the same files as
    // inline content for the lightbox ticket endpoint. The previous picsum.photos URLs needed
    // internet access, so the gallery rendered empty in offline E2E runs and TmLightbox could
    // never be exercised (F3 carry-forward).
    public const int ImageCount = 8;

    public const string GalleryAssetBase = "_content/Tempo.Blazor.Demo.SharedUI/gallery";

    public List<GalleryImageDto> Images { get; } = Enumerable.Range(1, ImageCount)
        .Select(i => new GalleryImageDto(
            Id: i.ToString(),
            Title: $"Photo {i}",
            ThumbnailUrl: $"{GalleryAssetBase}/photo-{i}.svg",
            Url: $"{GalleryAssetBase}/photo-{i}.svg",
            UploadedAt: DateTime.Today.AddDays(-i),
            UploadedBy: i % 3 == 0 ? "Alice" : i % 3 == 1 ? "Bob" : "Carol",
            FileSizeBytes: 100_000 + i * 15_000
        )).ToList();

    private readonly ConcurrentDictionary<string, TicketInfo> _tickets = new();

    public string CreateTicket(string imageId)
    {
        var ticket = Guid.NewGuid().ToString("N");
        _tickets[ticket] = new TicketInfo(imageId, DateTimeOffset.UtcNow.AddMinutes(5));
        return ticket;
    }

    public string? ResolveTicket(string ticket)
    {
        if (!_tickets.TryRemove(ticket, out var info))
            return null;

        if (info.ExpiresAt < DateTimeOffset.UtcNow)
            return null;

        return info.ImageId;
    }

    public bool DeleteImage(string imageId)
    {
        var image = Images.FirstOrDefault(i => i.Id == imageId);
        if (image is null) return false;
        Images.Remove(image);
        return true;
    }

    private sealed record TicketInfo(string ImageId, DateTimeOffset ExpiresAt);
}
