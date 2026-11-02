using System.Globalization;

namespace Tempo.Blazor.Diff;

/// <summary>
/// The shared diff engine of the library: turns two texts into an ordered list of
/// <see cref="TextDiffHunk"/> values that tile both inputs, so a caller can present the changes,
/// accept or reject each of them and produce the resulting text through
/// <see cref="TextDiffResult.Apply"/>.
/// <para>
/// It is a pure, allocation-conscious and UI-free function of its arguments, safe to call from
/// WebAssembly, Server and InteractiveAuto render modes as well as from a backend that never
/// references Blazor. Both historical helpers — the word-level
/// <c>DocumentTextDiffHelper</c> and the line-level <c>TmTextLineDiff</c> — delegate to it, so
/// there is exactly one comparison algorithm in the library.
/// </para>
/// <para>
/// A pathological input never throws and never blocks: above <see cref="TextDiffOptions.MaxTokens"/>
/// or <see cref="TextDiffOptions.MaxCells"/> the comparison degrades to a coarser granularity and
/// says so through <see cref="TextDiffResult.Fallback"/>.
/// </para>
/// </summary>
public static class TextDiff
{
    /// <summary>
    /// Compares two texts at word granularity. A token is a word (including its combining marks and
    /// digits), a run of digits, or a single punctuation character; whitespace is not a token, so a
    /// re-flowed paragraph is not a change, and every hunk keeps the exact characters (including the
    /// whitespace between tokens) so that the hunks still tile the inputs.
    /// </summary>
    /// <param name="oldText">The original text; <c>null</c> is read as empty.</param>
    /// <param name="newText">The new text; <c>null</c> is read as empty.</param>
    /// <param name="options">Limits to apply; <c>null</c> uses <see cref="TextDiffOptions.Default"/>.</param>
    /// <returns>The comparison, degraded to line level when the limits require it.</returns>
    public static TextDiffResult Words(string? oldText, string? newText, TextDiffOptions? options = null)
    {
        var settings = options ?? TextDiffOptions.Default;
        var oldValue = oldText ?? string.Empty;
        var newValue = newText ?? string.Empty;
        var oldUnits = DiffAlgorithm.TokenizeWords(oldValue);
        var newUnits = DiffAlgorithm.TokenizeWords(newValue);

        if (ExceedsLimits(oldUnits.Count, newUnits.Count, oldUnits, newUnits, settings))
        {
            return BuildLines(oldValue, newValue, settings, TextDiffFallbackKind.LineLevel);
        }

        return Build(
            oldValue,
            newValue,
            oldUnits,
            newUnits,
            TextDiffGranularity.Words,
            TextDiffFallbackKind.None);
    }

    /// <summary>
    /// Compares two texts at line granularity. Lines are split on <c>\n</c> and every character
    /// (including a trailing <c>\r</c> and the line breaks) stays inside the hunks, so the hunks
    /// tile both inputs; a mixed CRLF/LF pair therefore reports the affected lines as changed
    /// instead of silently normalizing them.
    /// </summary>
    /// <param name="oldText">The original text; <c>null</c> is read as empty.</param>
    /// <param name="newText">The new text; <c>null</c> is read as empty.</param>
    /// <param name="options">Limits to apply; <c>null</c> uses <see cref="TextDiffOptions.Default"/>.</param>
    /// <returns>The comparison, collapsed to a single change when the limits require it.</returns>
    public static TextDiffResult Lines(string? oldText, string? newText, TextDiffOptions? options = null)
    {
        var settings = options ?? TextDiffOptions.Default;
        return BuildLines(oldText ?? string.Empty, newText ?? string.Empty, settings, TextDiffFallbackKind.None);
    }

    private static TextDiffResult BuildLines(
        string oldValue,
        string newValue,
        TextDiffOptions settings,
        TextDiffFallbackKind fallback)
    {
        var oldUnits = DiffAlgorithm.SplitLines(oldValue);
        var newUnits = DiffAlgorithm.SplitLines(newValue);

        if (ExceedsLimits(oldUnits.Count, newUnits.Count, oldUnits, newUnits, settings))
        {
            return SingleChange(oldValue, newValue, oldUnits.Count, newUnits.Count);
        }

        return Build(oldValue, newValue, oldUnits, newUnits, TextDiffGranularity.Lines, fallback);
    }

    private static bool ExceedsLimits(
        int oldCount,
        int newCount,
        IReadOnlyList<DiffUnit> oldUnits,
        IReadOnlyList<DiffUnit> newUnits,
        TextDiffOptions settings)
    {
        var maxTokens = Math.Max(1, settings.MaxTokens);
        if (Math.Max(oldCount, newCount) > maxTokens)
        {
            return true;
        }

        var maxCells = Math.Max(1, settings.MaxCells);
        var bounds = DiffAlgorithm.FindCommonBounds(oldUnits, newUnits);
        return DiffAlgorithm.CountCells(oldCount, newCount, bounds) > maxCells;
    }

    private static TextDiffResult Build(
        string oldValue,
        string newValue,
        IReadOnlyList<DiffUnit> oldUnits,
        IReadOnlyList<DiffUnit> newUnits,
        TextDiffGranularity granularity,
        TextDiffFallbackKind fallback)
    {
        if (oldUnits.Count == 0 || newUnits.Count == 0)
        {
            // Nothing to align at this granularity — a whitespace-only text has no tokens and a
            // blank text has no lines. Keep the characters in one hunk instead of dropping them.
            return SingleHunk(oldValue, newValue, oldUnits.Count, newUnits.Count, granularity, fallback);
        }

        var bounds = DiffAlgorithm.FindCommonBounds(oldUnits, newUnits);
        var operations = DiffAlgorithm.ComputeOperations(oldUnits, newUnits, bounds);
        var groups = DiffAlgorithm.GroupOperations(operations);
        var hunks = new List<TextDiffHunk>(groups.Count);
        var oldCursor = 0;
        var newCursor = 0;
        foreach (var group in groups)
        {
            var oldStart = oldCursor;
            var newStart = newCursor;
            oldCursor = GroupEnd(oldStart, group.OldStart, group.OldCount, oldUnits, oldValue.Length);
            newCursor = GroupEnd(newStart, group.NewStart, group.NewCount, newUnits, newValue.Length);

            var kind = group.Kind switch
            {
                DiffGroupKind.Insert => TextDiffHunkKind.Insert,
                DiffGroupKind.Delete => TextDiffHunkKind.Delete,
                DiffGroupKind.Replace => TextDiffHunkKind.Replace,
                _ => TextDiffHunkKind.Equal
            };

            hunks.Add(new TextDiffHunk
            {
                Id = HunkId(kind, oldStart, newStart),
                Kind = kind,
                OldText = oldValue.Substring(oldStart, oldCursor - oldStart),
                NewText = newValue.Substring(newStart, newCursor - newStart),
                OldRange = new TextRange(oldStart, oldCursor - oldStart),
                NewRange = new TextRange(newStart, newCursor - newStart),
                OldUnitCount = group.OldCount,
                NewUnitCount = group.NewCount
            });
        }

        return new TextDiffResult(oldValue, newValue, hunks, granularity, fallback);
    }

    /// <summary>
    /// End of a group on one side. A group owns the separator that follows its last unit (up to the
    /// next group's first unit), which is what makes consecutive hunks contiguous; the last group
    /// runs to the end of the text so trailing whitespace is never dropped.
    /// </summary>
    private static int GroupEnd(
        int currentEnd,
        int groupStart,
        int groupCount,
        IReadOnlyList<DiffUnit> units,
        int textLength)
    {
        if (groupCount == 0)
        {
            // A group with nothing on this side is an insertion point; the whole text is one such
            // side only when the input has no units at all (handled by the caller).
            return currentEnd;
        }

        var next = groupStart + groupCount;
        return next < units.Count ? units[next].Start : textLength;
    }

    private static TextDiffResult SingleChange(
        string oldValue,
        string newValue,
        int oldUnitCount,
        int newUnitCount)
        => SingleHunk(
            oldValue,
            newValue,
            oldUnitCount,
            newUnitCount,
            TextDiffGranularity.Lines,
            TextDiffFallbackKind.SingleChange);

    private static TextDiffResult SingleHunk(
        string oldValue,
        string newValue,
        int oldUnitCount,
        int newUnitCount,
        TextDiffGranularity granularity,
        TextDiffFallbackKind fallback)
    {
        var kind = string.Equals(oldValue, newValue, StringComparison.Ordinal)
            ? TextDiffHunkKind.Equal
            : oldValue.Length == 0
                ? TextDiffHunkKind.Insert
                : newValue.Length == 0
                    ? TextDiffHunkKind.Delete
                    : TextDiffHunkKind.Replace;

        var hunk = new TextDiffHunk
        {
            Id = HunkId(kind, 0, 0),
            Kind = kind,
            OldText = oldValue,
            NewText = newValue,
            OldRange = new TextRange(0, oldValue.Length),
            NewRange = new TextRange(0, newValue.Length),
            OldUnitCount = oldUnitCount,
            NewUnitCount = newUnitCount
        };

        return new TextDiffResult(oldValue, newValue, [hunk], granularity, fallback);
    }

    private static string HunkId(TextDiffHunkKind kind, int oldStart, int newStart)
    {
        var prefix = kind switch
        {
            TextDiffHunkKind.Insert => "insert",
            TextDiffHunkKind.Delete => "delete",
            TextDiffHunkKind.Replace => "replace",
            _ => "equal"
        };

        return string.Concat(
            prefix,
            "-",
            oldStart.ToString(CultureInfo.InvariantCulture),
            "-",
            newStart.ToString(CultureInfo.InvariantCulture));
    }
}
