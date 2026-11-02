using System.Text.RegularExpressions;

namespace Tempo.Blazor.Diff;

/// <summary>A single comparison unit (a word/punctuation token or a line) with its offset in the input.</summary>
internal readonly record struct DiffUnit(string Text, int Start);

/// <summary>One step of the longest-common-subsequence walk.</summary>
internal enum DiffOperationKind
{
    Equal,
    Delete,
    Insert
}

/// <summary>
/// One step of the walk. <see cref="OldIndex"/> and <see cref="NewIndex"/> are the unit indices the
/// step consumes; a delete consumes only the old side, an insert only the new one, and both carry
/// the index of the not-yet-consumed unit on the other side.
/// </summary>
internal readonly record struct DiffOperation(DiffOperationKind Kind, int OldIndex, int NewIndex);

/// <summary>Kind of a grouped hunk before it becomes a <see cref="TextDiffHunk"/>.</summary>
internal enum DiffGroupKind
{
    Equal,
    Insert,
    Delete,
    Replace
}

/// <summary>A contiguous run of operations that share one kind, expressed as unit index ranges.</summary>
internal readonly record struct DiffGroup(DiffGroupKind Kind, int OldStart, int OldCount, int NewStart, int NewCount);

/// <summary>
/// The tokenising, longest-common-subsequence and grouping work behind <see cref="TextDiff"/>.
/// <para>
/// The walk is the tie-breaking the library has always used (<c>removal first</c> when both sides
/// of the table are equally good), because <c>DocumentTextDiffHelper</c> and <c>TmTextLineDiff</c>
/// delegate here and their rendered output must not move. What is new is the performance work: the
/// common prefix and suffix are stripped before the table is built, so editing one word of a long
/// document costs a table over the changed neighbourhood instead of over the whole input, and the
/// table itself is a plain <c>int[,]</c> allocated only for that neighbourhood.
/// </para>
/// </summary>
internal static class DiffAlgorithm
{
    /// <summary>
    /// Word, number and punctuation tokens. Whitespace is deliberately not a token: it makes the
    /// comparison ignore re-flowing (the historical behaviour of the document diff) and the
    /// surrounding hunk keeps the original characters, so the hunks still tile the input.
    /// </summary>
    private static readonly Regex WordTokenRegex = new(
        @"\p{L}[\p{L}\p{M}\p{N}]*|\p{N}+|[^\s]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static List<DiffUnit> TokenizeWords(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var matches = WordTokenRegex.Matches(text);
        var units = new List<DiffUnit>(matches.Count);
        foreach (Match match in matches)
        {
            units.Add(new DiffUnit(match.Value, match.Index));
        }

        return units;
    }

    /// <summary>
    /// Splits on <c>\n</c> and keeps every character (including a trailing <c>\r</c> and the line
    /// breaks themselves) inside the unit offsets, so concatenating a line diff's hunks reproduces
    /// the input byte for byte.
    /// </summary>
    internal static List<DiffUnit> SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var units = new List<DiffUnit>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            units.Add(new DiffUnit(text.Substring(start, index - start), start));
            start = index + 1;
        }

        units.Add(new DiffUnit(text.Substring(start), start));
        return units;
    }

    /// <summary>Lengths of the common prefix and common suffix that can be matched without a table.</summary>
    internal static (int Prefix, int Suffix) FindCommonBounds(
        IReadOnlyList<DiffUnit> oldUnits,
        IReadOnlyList<DiffUnit> newUnits)
    {
        var shortest = Math.Min(oldUnits.Count, newUnits.Count);
        var prefix = 0;
        while (prefix < shortest && Same(oldUnits[prefix], newUnits[prefix]))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < shortest - prefix &&
               Same(oldUnits[oldUnits.Count - 1 - suffix], newUnits[newUnits.Count - 1 - suffix]))
        {
            suffix++;
        }

        return (prefix, suffix);
    }

    /// <summary>Cells the table needs once the common bounds are stripped.</summary>
    internal static long CountCells(int oldCount, int newCount, (int Prefix, int Suffix) bounds)
    {
        var middleOld = oldCount - bounds.Prefix - bounds.Suffix;
        var middleNew = newCount - bounds.Prefix - bounds.Suffix;
        return ((long)middleOld + 1) * ((long)middleNew + 1);
    }

    internal static List<DiffOperation> ComputeOperations(
        IReadOnlyList<DiffUnit> oldUnits,
        IReadOnlyList<DiffUnit> newUnits,
        (int Prefix, int Suffix) bounds)
    {
        var oldCount = oldUnits.Count;
        var newCount = newUnits.Count;
        var prefix = bounds.Prefix;
        var suffix = bounds.Suffix;
        var middleOld = oldCount - prefix - suffix;
        var middleNew = newCount - prefix - suffix;
        var operations = new List<DiffOperation>(oldCount + newCount);

        for (var index = 0; index < prefix; index++)
        {
            operations.Add(new DiffOperation(DiffOperationKind.Equal, index, index));
        }

        // table[i, j] = length of the LCS of oldUnits[prefix + i..] and newUnits[prefix + j..] with
        // the common suffix excluded; comparing two entries that both gain the same suffix length
        // keeps the tie-breaking identical to a table built over the full inputs.
        var table = new int[middleOld + 1, middleNew + 1];
        for (var i = middleOld - 1; i >= 0; i--)
        {
            for (var j = middleNew - 1; j >= 0; j--)
            {
                table[i, j] = Same(oldUnits[prefix + i], newUnits[prefix + j])
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        int oldCursor = 0, newCursor = 0;
        while (oldCursor < middleOld && newCursor < middleNew)
        {
            if (Same(oldUnits[prefix + oldCursor], newUnits[prefix + newCursor]))
            {
                operations.Add(new DiffOperation(
                    DiffOperationKind.Equal, prefix + oldCursor, prefix + newCursor));
                oldCursor++;
                newCursor++;
            }
            else if (table[oldCursor + 1, newCursor] >= table[oldCursor, newCursor + 1])
            {
                operations.Add(new DiffOperation(
                    DiffOperationKind.Delete, prefix + oldCursor, prefix + newCursor));
                oldCursor++;
            }
            else
            {
                operations.Add(new DiffOperation(
                    DiffOperationKind.Insert, prefix + oldCursor, prefix + newCursor));
                newCursor++;
            }
        }

        while (oldCursor < middleOld)
        {
            operations.Add(new DiffOperation(
                DiffOperationKind.Delete, prefix + oldCursor, prefix + newCursor));
            oldCursor++;
        }

        while (newCursor < middleNew)
        {
            operations.Add(new DiffOperation(
                DiffOperationKind.Insert, prefix + oldCursor, prefix + newCursor));
            newCursor++;
        }

        for (var index = 0; index < suffix; index++)
        {
            operations.Add(new DiffOperation(
                DiffOperationKind.Equal, oldCount - suffix + index, newCount - suffix + index));
        }

        return operations;
    }

    /// <summary>
    /// Runs the operations into hunks. Runs of one kind merge, and a run of deletions directly
    /// followed by a run of insertions becomes a single <see cref="DiffGroupKind.Replace"/> — the
    /// unit a reviewer accepts or rejects. An insertion that comes FIRST stays its own group so the
    /// segmented output keeps the walk's order.
    /// </summary>
    internal static List<DiffGroup> GroupOperations(IReadOnlyList<DiffOperation> operations)
    {
        var groups = new List<DiffGroup>();
        var index = 0;
        while (index < operations.Count)
        {
            var operation = operations[index];
            if (operation.Kind == DiffOperationKind.Equal)
            {
                var count = 0;
                while (index < operations.Count && operations[index].Kind == DiffOperationKind.Equal)
                {
                    count++;
                    index++;
                }

                groups.Add(new DiffGroup(DiffGroupKind.Equal, operation.OldIndex, count, operation.NewIndex, count));
                continue;
            }

            if (operation.Kind == DiffOperationKind.Insert)
            {
                var count = 0;
                while (index < operations.Count && operations[index].Kind == DiffOperationKind.Insert)
                {
                    count++;
                    index++;
                }

                groups.Add(new DiffGroup(DiffGroupKind.Insert, operation.OldIndex, 0, operation.NewIndex, count));
                continue;
            }

            var deleteCount = 0;
            while (index < operations.Count && operations[index].Kind == DiffOperationKind.Delete)
            {
                deleteCount++;
                index++;
            }

            if (index < operations.Count && operations[index].Kind == DiffOperationKind.Insert)
            {
                var insertStart = operations[index].NewIndex;
                var insertCount = 0;
                while (index < operations.Count && operations[index].Kind == DiffOperationKind.Insert)
                {
                    insertCount++;
                    index++;
                }

                groups.Add(new DiffGroup(
                    DiffGroupKind.Replace, operation.OldIndex, deleteCount, insertStart, insertCount));
                continue;
            }

            groups.Add(new DiffGroup(
                DiffGroupKind.Delete, operation.OldIndex, deleteCount, operation.NewIndex, 0));
        }

        return groups;
    }

    private static bool Same(DiffUnit left, DiffUnit right)
        => string.Equals(left.Text, right.Text, StringComparison.Ordinal);
}
