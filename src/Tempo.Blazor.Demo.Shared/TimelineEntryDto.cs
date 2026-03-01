using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Demo.Shared;

public record TimelineEntryDto(
    string Id,
    string EntryType,
    string AuthorName,
    string? AuthorAvatarUrl,
    DateTimeOffset CreatedAt,
    string? HtmlContent,
    string? PlainContent,
    bool IsInternal = false,
    IReadOnlyDictionary<string, string>? Metadata = null) : ITimelineEntry;
