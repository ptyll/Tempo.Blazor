using System.Collections.Concurrent;
using Tempo.Blazor.Demo.Shared;

namespace Tempo.Blazor.Demo.Api.Data;

public class MockImageStore
{
    public List<GalleryImageDto> Images { get; } = Enumerable.Range(1, 24)
        .Select(i => new GalleryImageDto(
            Id: i.ToString(),
            Title: $"Photo {i}",
            ThumbnailUrl: $"https://picsum.photos/seed/{i}/200/150",
            Url: $"https://picsum.photos/seed/{i}/1200/900",
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
