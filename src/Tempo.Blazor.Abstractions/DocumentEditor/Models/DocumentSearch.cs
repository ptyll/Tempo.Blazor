namespace Tempo.Blazor.DocumentEditor.Models;

/// <summary>Scope for a search operation within a document.</summary>
public enum DocumentSearchScope
{
    /// <summary>Search only the main body blocks.</summary>
    Body,
    /// <summary>Search only headers and footers.</summary>
    HeadersFooters,
    /// <summary>Search only comment threads.</summary>
    Comments,
    /// <summary>Search body, headers, footers, and comments.</summary>
    All
}

/// <summary>Parameters for a Find (and optionally Replace) operation.</summary>
public sealed class DocumentSearchQuery
{
    /// <summary>Text to find. Empty string produces no results.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Whether the match must be case-sensitive.</summary>
    public bool CaseSensitive { get; init; }

    /// <summary>Whether the match must be a whole word (bounded by non-word characters).</summary>
    public bool WholeWord { get; init; }

    /// <summary>Whether <see cref="Text"/> should be interpreted as a regular expression.</summary>
    public bool UseRegex { get; init; }

    /// <summary>Which parts of the document to search.</summary>
    public DocumentSearchScope Scope { get; init; } = DocumentSearchScope.Body;
}

/// <summary>Runtime replace request raised by the find panel.</summary>
public sealed class DocumentFindReplaceRequest
{
    /// <summary>Current search query.</summary>
    public DocumentSearchQuery Query { get; init; } = new();

    /// <summary>Replacement text.</summary>
    public string Replacement { get; init; } = string.Empty;

    /// <summary>Active search result for single-result replacement.</summary>
    public DocumentSearchResult? ActiveResult { get; init; }
}

/// <summary>A single match returned by <see cref="Tempo.Blazor.DocumentEditor.Services.DocumentSearchService"/>.</summary>
public sealed class DocumentSearchResult
{
    /// <summary>Zero-based index across all results in the document.</summary>
    public int Index { get; init; }

    /// <summary>ID of the block that contains this match.</summary>
    public string BlockId { get; init; } = string.Empty;

    /// <summary>Character offset of the match start within the block's flattened plain text.</summary>
    public int BlockTextOffset { get; init; }

    /// <summary>Length of the matched text in characters.</summary>
    public int Length { get; init; }

    /// <summary>Search scope that produced the result.</summary>
    public DocumentSearchScope Scope { get; init; } = DocumentSearchScope.Body;

    /// <summary>Stable transient marker id used by the runtime marker layer.</summary>
    public string MarkerId { get; init; } = string.Empty;

    /// <summary>Short context snippet (the matched text itself, trimmed to 80 chars).</summary>
    public string Preview { get; init; } = string.Empty;

    /// <summary>
    /// The text just before the match inside the same block (up to 30 characters, empty at the start of the block). Together with
    /// <see cref="ContextAfter"/> it lets a results list show the match in its sentence. Defaults to empty, so a host that builds
    /// results itself is unaffected.
    /// </summary>
    public string ContextBefore { get; init; } = string.Empty;

    /// <summary>The text just after the match inside the same block (up to 30 characters, empty at the end of the block).</summary>
    public string ContextAfter { get; init; } = string.Empty;
}
