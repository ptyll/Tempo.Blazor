namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents a single entry in TmTimeline / TmActivityTimeline (activity log, audit trail, history).
/// </summary>
public interface ITimelineEntry
{
    /// <summary>Unique identifier for this entry.</summary>
    string Id { get; }

    /// <summary>
    /// Entry type identifier for icon/style selection
    /// (e.g. "comment", "status_change", "system", "assignment", "attachment").
    /// </summary>
    string EntryType { get; }

    /// <summary>Display name of the user who triggered this entry.</summary>
    string AuthorName { get; }

    /// <summary>Optional avatar URL for the author.</summary>
    string? AuthorAvatarUrl { get; }

    /// <summary>When this entry occurred (DateTimeOffset for timezone awareness).</summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>Raw HTML content of the entry – render with (MarkupString).</summary>
    string? HtmlContent { get; }

    /// <summary>Plain-text fallback when HtmlContent is null.</summary>
    string? PlainContent { get; }

    /// <summary>Whether this entry is internal (board-only visibility indicator).</summary>
    bool IsInternal { get; }

    /// <summary>Optional key/value metadata for specialised entry types.</summary>
    IReadOnlyDictionary<string, string>? Metadata { get; }

    // ── Backward-compatibility shims for TmTimeline (old field names) ──────

    /// <inheritdoc cref="AuthorAvatarUrl"/>
    string? AuthorAvatarSrc => AuthorAvatarUrl;

    /// <inheritdoc cref="CreatedAt"/>
    DateTime Timestamp => CreatedAt.LocalDateTime;

    /// <summary>Content shorthand: PlainContent ?? HtmlContent.</summary>
    string? Content => PlainContent ?? HtmlContent;
}
