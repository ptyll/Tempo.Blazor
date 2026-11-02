using Tempo.Blazor.Diff;
using Tempo.Blazor.DocumentEditor.Models;

namespace Tempo.Blazor.DocumentEditor.Services;

/// <summary>
/// Creates deterministic word-level diffs for document text.
/// <para>
/// The comparison itself lives in the shared UI-free engine <see cref="TextDiff"/>; this helper is
/// the document-editor façade over it. It keeps the historical segment shape (one segment per run
/// of equal, removed or added tokens, tokens joined the way document text reads) so the rendered
/// comparison does not change, and it adds the document-specific plumbing: extracting readable plain
/// text from a snapshot and from a single block.
/// </para>
/// <para>
/// Above <see cref="TextDiffOptions.DefaultMaxTokens"/> tokens per text (or
/// <see cref="TextDiffOptions.DefaultMaxCells"/> table cells) the comparison degrades to line level
/// instead of building an unbounded table, and <see cref="DocumentTextDiffResult.IsDegraded"/> says
/// so, so a caller can present the comparison as coarse rather than as word-level.
/// </para>
/// </summary>
public static class DocumentTextDiffHelper
{
    /// <summary>Diffs old and new text at word/punctuation level.</summary>
    /// <param name="oldText">The original text; <c>null</c> is read as empty.</param>
    /// <param name="newText">The new text; <c>null</c> is read as empty.</param>
    /// <returns>
    /// The ordered segments of the comparison; <see cref="DocumentTextDiffResult.IsDegraded"/> says
    /// whether the engine had to coarsen it to stay within its limits.
    /// </returns>
    public static DocumentTextDiffResult Diff(string? oldText, string? newText)
    {
        var diff = TextDiff.Words(oldText, newText);
        var segments = new List<DocumentTextDiffSegment>(diff.Hunks.Count);

        foreach (var hunk in diff.Hunks)
        {
            switch (hunk.Kind)
            {
                case TextDiffHunkKind.Equal:
                    Append(segments, DocumentTextDiffSegmentKind.Unchanged, JoinTokens(hunk.OldText));
                    break;

                case TextDiffHunkKind.Delete:
                    Append(segments, DocumentTextDiffSegmentKind.Removed, JoinTokens(hunk.OldText));
                    break;

                case TextDiffHunkKind.Insert:
                    Append(segments, DocumentTextDiffSegmentKind.Added, JoinTokens(hunk.NewText));
                    break;

                default:
                    // A replacement is one decision in the shared engine but two segments here, in
                    // the same order the historical implementation emitted them.
                    Append(segments, DocumentTextDiffSegmentKind.Removed, JoinTokens(hunk.OldText));
                    Append(segments, DocumentTextDiffSegmentKind.Added, JoinTokens(hunk.NewText));
                    break;
            }
        }

        return new DocumentTextDiffResult
        {
            Segments = segments,
            IsDegraded = diff.IsDegraded
        };
    }

    /// <summary>Extracts readable plain text from a document snapshot.</summary>
    /// <param name="document">The document snapshot; <c>null</c> yields an empty string.</param>
    /// <returns>The block texts joined by the platform line separator.</returns>
    public static string ExtractPlainText(DocumentEditorDocument? document)
    {
        if (document is null)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, document.Blocks
            .OrderBy(block => block.Order)
            .Select(GetBlockText)
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    /// <summary>Extracts readable plain text from a single document block.</summary>
    /// <param name="block">The block to read.</param>
    /// <returns>The block text.</returns>
    public static string ExtractBlockPlainText(DocumentBlock block)
        => GetBlockText(block);

    private static void Append(ICollection<DocumentTextDiffSegment> segments, DocumentTextDiffSegmentKind kind, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        segments.Add(new DocumentTextDiffSegment
        {
            Kind = kind,
            Text = text
        });
    }

    /// <summary>
    /// Re-joins the tokens of a hunk the way document text reads: tokens sit next to each other
    /// unless a space is needed between a word and a following word or opening bracket. The hunk
    /// carries the exact characters of the input, so the join has to run over the tokens rather
    /// than over the raw text — and it runs over the engine's own tokenizer
    /// (<see cref="DiffAlgorithm.TokenizeWords"/>) so this join can never drift from the tokens the
    /// hunk boundaries were computed on.
    /// </summary>
    private static string JoinTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var joined = string.Empty;
        foreach (var unit in DiffAlgorithm.TokenizeWords(text))
        {
            joined = joined.Length == 0
                ? unit.Text
                : NeedsSpace(joined, unit.Text)
                    ? joined + " " + unit.Text
                    : joined + unit.Text;
        }

        return joined;
    }

    private static bool NeedsSpace(string current, string next)
    {
        if (current.Length == 0 || next.Length == 0)
        {
            return false;
        }

        var previous = current[^1];
        var first = next[0];
        return (char.IsLetterOrDigit(previous) || previous is ')' or ']' or '}' or '.')
            && (char.IsLetterOrDigit(first) || first is '(' or '[' or '{');
    }

    private static string GetBlockText(DocumentBlock block)
    {
        return block.Content switch
        {
            ParagraphBlockContent paragraph => GetInlineText(paragraph.Inlines),
            HeadingBlockContent heading => GetInlineText(heading.Inlines),
            ListBlockContent list => GetInlineText(list.Inlines),
            QuoteBlockContent quote => GetInlineText(quote.Inlines),
            ImageBlockContent image => image.Caption ?? image.AltText ?? image.Url ?? image.AssetId ?? string.Empty,
            PageBreakBlockContent => string.Empty,
            TableBlockContent table => string.Join(" ", table.Rows
                .SelectMany(row => row.Cells)
                .Where(cell => cell.Merge.IsOrigin)
                .SelectMany(cell => cell.Blocks)
                .Select(GetBlockText)
                .Where(text => !string.IsNullOrWhiteSpace(text))),
            _ => string.Empty
        };
    }

    private static string GetInlineText(IEnumerable<InlineContent> inlines)
    {
        return string.Concat(inlines.Select(inline => inline switch
        {
            TextRun run => run.Text,
            TokenRun token => string.IsNullOrWhiteSpace(token.DisplayName) ? token.Key : token.DisplayName,
            DocumentNoteReferenceRun note => note.DisplayMarker ?? note.NoteId,
            _ => string.Empty
        }));
    }
}
