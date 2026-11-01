namespace Tempo.Blazor.DocumentEditor.Models;

/// <summary>Kind of a text diff segment.</summary>
public enum DocumentTextDiffSegmentKind
{
    /// <summary>Text is present in both inputs.</summary>
    Unchanged,

    /// <summary>Text was added in the new input.</summary>
    Added,

    /// <summary>Text was removed from the old input.</summary>
    Removed
}

/// <summary>Single word-level diff segment.</summary>
public sealed class DocumentTextDiffSegment
{
    /// <summary>Segment kind.</summary>
    public DocumentTextDiffSegmentKind Kind { get; set; }

    /// <summary>Segment text.</summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>Result of a word-level text diff.</summary>
public sealed class DocumentTextDiffResult
{
    /// <summary>Ordered diff segments.</summary>
    public List<DocumentTextDiffSegment> Segments { get; set; } = [];

    /// <summary>
    /// Whether the comparison had to degrade to stay within the engine's limits
    /// (<c>TextDiffOptions.DefaultMaxTokens</c> / <c>DefaultMaxCells</c>). A degraded diff is
    /// approximate: the segments describe whole lines instead of words, and for an input that
    /// exceeds even the line limits the whole old text is one <see cref="DocumentTextDiffSegmentKind.Removed"/>
    /// segment followed by the whole new text as one <see cref="DocumentTextDiffSegmentKind.Added"/>
    /// segment. A consumer that surfaces a review decision should say the comparison is coarse
    /// instead of presenting it as a word-level result.
    /// </summary>
    public bool IsDegraded { get; init; }

    /// <summary>Whether the compared text is identical.</summary>
    public bool HasChanges => Segments.Any(segment => segment.Kind != DocumentTextDiffSegmentKind.Unchanged);
}
