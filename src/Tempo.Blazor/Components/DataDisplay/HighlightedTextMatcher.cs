using System.Text;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>One slice of highlighted text: a run of characters and whether it matched the query.</summary>
/// <param name="Text">The characters of the slice, taken verbatim from the source text.</param>
/// <param name="IsMatch">Whether the slice matched the query and must be wrapped in <c>&lt;mark&gt;</c>.</param>
internal readonly record struct HighlightedTextSegment(string Text, bool IsMatch);

/// <summary>
/// Splitter behind <see cref="TmHighlightedText"/>: finds every non-overlapping occurrence of a search
/// term in a text and cuts the text into plain and matched runs.
/// <para>
/// The implementation is deliberately NOT a regular expression. A search term is user data — passing it to
/// <see cref="System.Text.RegularExpressions.Regex"/> would make <c>(</c> throw, <c>.*</c> match everything
/// and <c>$1</c> mean a backreference, which is a correctness bug and a denial-of-service surface in one.
/// <see cref="string.IndexOf(string, StringComparison)"/> is literal, linear and cannot be surprised by a
/// metacharacter.
/// </para>
/// <para>
/// Accent-insensitive matching normalizes a character at a time and keeps an index map back into the
/// original text, so the marked run is the REAL text ("Ústí"), never the folded query ("usti") — a reader
/// must see the data they were given.
/// </para>
/// </summary>
internal static class HighlightedTextMatcher
{
    /// <summary>
    /// Splits <paramref name="text"/> into plain and matched runs for <paramref name="query"/>. An empty or
    /// whitespace-only query produces a single plain run, and so does a query that does not occur at all.
    /// </summary>
    /// <param name="text">The text to split.</param>
    /// <param name="query">The term to look for, literally.</param>
    /// <param name="caseSensitive">When <c>true</c> the comparison honours casing.</param>
    /// <param name="accentInsensitive">When <c>true</c> diacritics are ignored on both sides.</param>
    public static IReadOnlyList<HighlightedTextSegment> Split(
        string? text, string? query, bool caseSensitive, bool accentInsensitive)
    {
        if (string.IsNullOrEmpty(text)) return [];

        var plain = new[] { new HighlightedTextSegment(text, false) };
        if (string.IsNullOrWhiteSpace(query)) return plain;

        var term = query.Trim();
        IReadOnlyList<int>? indexMap = null;
        var haystack = text;
        if (accentInsensitive)
        {
            haystack = BuildFolded(text, out indexMap);
        }

        var needle = accentInsensitive ? AccentInsensitiveText.RemoveDiacritics(term) : term;
        if (needle.Length == 0 || needle.Length > haystack.Length) return plain;

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        var segments = new List<HighlightedTextSegment>();
        var cursor = 0;
        var searchFrom = 0;
        while (searchFrom <= haystack.Length - needle.Length)
        {
            var hit = haystack.IndexOf(needle, searchFrom, comparison);
            if (hit < 0) break;

            var start = MapIndex(indexMap, hit);
            var end = MapIndex(indexMap, hit + needle.Length - 1) + 1;
            if (start < cursor)
            {
                // Overlaps the previous match (possible only through normalization): keep the first run.
                searchFrom = hit + needle.Length;
                continue;
            }

            if (start > cursor)
            {
                segments.Add(new HighlightedTextSegment(text[cursor..start], false));
            }

            segments.Add(new HighlightedTextSegment(text[start..end], true));
            cursor = end;
            searchFrom = hit + needle.Length;
        }

        if (segments.Count == 0) return plain;
        if (cursor < text.Length) segments.Add(new HighlightedTextSegment(text[cursor..], false));

        return segments;
    }

    /// <summary>
    /// Folds <paramref name="text"/> character by character and returns the folded string together with
    /// the index of the ORIGINAL character behind every folded character. Folding per character (instead of
    /// normalizing the whole string at once) is what keeps the two lengths in step: a combining mark folds
    /// to nothing and simply contributes no entry.
    /// </summary>
    private static string BuildFolded(string text, out IReadOnlyList<int> indexMap)
    {
        var map = new List<int>(text.Length);
        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var folded = AccentInsensitiveText.RemoveDiacritics(text[index].ToString());
            if (folded.Length == 0) continue;

            builder.Append(folded);
            for (var offset = 0; offset < folded.Length; offset++) map.Add(index);
        }

        indexMap = map;
        return builder.ToString();
    }

    private static int MapIndex(IReadOnlyList<int>? map, int normalizedIndex)
        => map is null ? normalizedIndex : map[normalizedIndex];
}
