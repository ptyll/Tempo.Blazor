using Tempo.Blazor.DocumentEditor.Models;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.DocumentEditor.Services;

/// <summary>Searches a <see cref="DocumentEditorDocument"/> for text matches.</summary>
public sealed class DocumentSearchService
{
    /// <summary>Characters of context kept on each side of a match (<see cref="DocumentSearchResult.ContextBefore"/> / <see cref="DocumentSearchResult.ContextAfter"/>).</summary>
    private const int ContextLength = 30;

    /// <summary>Finds all occurrences of <paramref name="query"/> in <paramref name="document"/>.</summary>
    public IReadOnlyList<DocumentSearchResult> Search(DocumentEditorDocument document, DocumentSearchQuery query)
    {
        if (string.IsNullOrEmpty(query.Text))
            return [];

        var results = new List<DocumentSearchResult>();

        if (query.Scope is DocumentSearchScope.Body or DocumentSearchScope.All)
            CollectFromBlocks(document.Blocks, query, DocumentSearchScope.Body, results);

        if (query.Scope is DocumentSearchScope.HeadersFooters or DocumentSearchScope.All)
        {
            foreach (var hf in document.HeadersFooters)
                CollectFromBlocks(hf.Blocks, query, DocumentSearchScope.HeadersFooters, results);
        }

        if (query.Scope is DocumentSearchScope.Comments or DocumentSearchScope.All)
            CollectFromComments(document.Comments, query, results);

        // Re-index globally
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            results[i] = new DocumentSearchResult
            {
                Index = i,
                BlockId = r.BlockId,
                BlockTextOffset = r.BlockTextOffset,
                Length = r.Length,
                Scope = r.Scope,
                MarkerId = BuildMarkerId(i, r),
                Preview = r.Preview,
                ContextBefore = r.ContextBefore,
                ContextAfter = r.ContextAfter
            };
        }

        return results;
    }

    private static void CollectFromBlocks(
        IEnumerable<DocumentBlock> blocks,
        DocumentSearchQuery query,
        DocumentSearchScope resultScope,
        List<DocumentSearchResult> results)
    {
        foreach (var block in blocks)
            CollectFromBlock(block, query, resultScope, results);
    }

    private static void CollectFromBlock(
        DocumentBlock block,
        DocumentSearchQuery query,
        DocumentSearchScope resultScope,
        List<DocumentSearchResult> results)
    {
        switch (block.Content)
        {
            case ParagraphBlockContent p:
                CollectFromInlines(block.Id, p.Inlines, query, resultScope, results);
                break;

            case HeadingBlockContent h:
                CollectFromInlines(block.Id, h.Inlines, query, resultScope, results);
                break;

            case ListBlockContent l:
                CollectFromInlines(block.Id, l.Inlines, query, resultScope, results);
                break;

            case QuoteBlockContent q:
                CollectFromInlines(block.Id, q.Inlines, query, resultScope, results);
                break;

            case TableBlockContent t:
                foreach (var row in t.Rows)
                    foreach (var cell in row.Cells)
                        CollectFromBlocks(cell.Blocks, query, resultScope, results);
                break;

            case ContentControlBlockContent cc:
                CollectFromBlocks(cc.Blocks, query, resultScope, results);
                break;
        }
    }

    private static void CollectFromInlines(
        string blockId,
        IEnumerable<InlineContent> inlines,
        DocumentSearchQuery query,
        DocumentSearchScope resultScope,
        List<DocumentSearchResult> results)
    {
        // Flatten all text runs into a single string, keeping offset map
        var flatText = string.Concat(
            inlines.OfType<TextRun>().Select(r => r.Text));

        if (string.IsNullOrEmpty(flatText))
            return;

        CollectMatches(blockId, flatText, query, resultScope, results);
    }

    private static bool IsWholeWordMatch(string text, int start, int length)
    {
        var before = start > 0 ? text[start - 1] : ' ';
        var after = start + length < text.Length ? text[start + length] : ' ';
        return !char.IsLetterOrDigit(before) && !char.IsLetterOrDigit(after);
    }

    private static void CollectFromComments(
        IEnumerable<DocumentComment> comments,
        DocumentSearchQuery query,
        List<DocumentSearchResult> results)
    {
        foreach (var comment in comments)
        {
            foreach (var entry in comment.Entries)
            {
                CollectFromPlainText(
                    $"comment:{comment.Id}:{entry.Id}",
                    entry.Text,
                    query,
                    DocumentSearchScope.Comments,
                    results);
            }
        }
    }

    private static void CollectFromPlainText(
        string blockId,
        string text,
        DocumentSearchQuery query,
        DocumentSearchScope resultScope,
        List<DocumentSearchResult> results)
    {
        if (string.IsNullOrEmpty(text))
            return;

        CollectMatches(blockId, text, query, resultScope, results);
    }

    private static void CollectMatches(
        string blockId,
        string text,
        DocumentSearchQuery query,
        DocumentSearchScope resultScope,
        List<DocumentSearchResult> results)
    {
        if (query.UseRegex)
        {
            CollectRegexMatches(blockId, text, query, resultScope, results);
            return;
        }

        var comparison = query.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var pos = 0;
        while (pos <= text.Length - query.Text.Length)
        {
            var idx = text.IndexOf(query.Text, pos, comparison);
            if (idx < 0) break;

            if (!query.WholeWord || IsWholeWordMatch(text, idx, query.Text.Length))
            {
                AddResult(blockId, text, idx, query.Text.Length, resultScope, results);
            }

            // Advance by the matched query length so find/replace uses non-overlapping matches.
            pos = idx + Math.Max(1, query.Text.Length);
        }
    }

    private static void CollectRegexMatches(
        string blockId,
        string text,
        DocumentSearchQuery query,
        DocumentSearchScope resultScope,
        List<DocumentSearchResult> results)
    {
        Regex regex;
        try
        {
            regex = new Regex(
                query.Text,
                RegexOptions.CultureInvariant | (query.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase),
                TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException)
        {
            return;
        }

        foreach (Match match in regex.Matches(text))
        {
            if (!match.Success || match.Length == 0)
            {
                continue;
            }

            if (!query.WholeWord || IsWholeWordMatch(text, match.Index, match.Length))
            {
                AddResult(blockId, text, match.Index, match.Length, resultScope, results);
            }
        }
    }

    private static void AddResult(
        string blockId,
        string text,
        int start,
        int length,
        DocumentSearchScope resultScope,
        List<DocumentSearchResult> results)
    {
        var matchText = text.Substring(start, length);
        var beforeStart = Math.Max(0, start - ContextLength);
        var afterEnd = Math.Min(text.Length, start + length + ContextLength);
        results.Add(new DocumentSearchResult
        {
            Index = results.Count,
            BlockId = blockId,
            BlockTextOffset = start,
            Length = length,
            Scope = resultScope,
            Preview = matchText.Length <= 80 ? matchText : matchText[..80],
            ContextBefore = text[beforeStart..start],
            ContextAfter = text[(start + length)..afterEnd]
        });
    }

    private static string BuildMarkerId(int index, DocumentSearchResult result) =>
        $"search-{index}-{result.Scope}-{result.BlockId}-{result.BlockTextOffset}-{result.Length}";
}
