using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Demo.Shared;

public record CommentDto(
    string Id,
    string AuthorName,
    string? AuthorAvatarUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string HtmlContent,
    bool CanEdit,
    bool CanDelete) : ICommentEntry;
