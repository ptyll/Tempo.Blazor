namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents a single comment entry in TmActivityComments.
/// </summary>
public interface ICommentEntry
{
    /// <summary>Unique identifier for this comment.</summary>
    string Id { get; }

    /// <summary>Display name of the comment author.</summary>
    string AuthorName { get; }

    /// <summary>Optional avatar URL for the author.</summary>
    string? AuthorAvatarUrl { get; }

    /// <summary>When the comment was originally posted.</summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>When the comment was last edited, or null if never edited.</summary>
    DateTimeOffset? UpdatedAt { get; }

    /// <summary>Raw HTML content of the comment.</summary>
    string HtmlContent { get; }

    /// <summary>Whether the current user may edit this comment.</summary>
    bool CanEdit { get; }

    /// <summary>Whether the current user may delete this comment.</summary>
    bool CanDelete { get; }
}
