using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Diff;

namespace Tempo.Blazor.Components.Files;

/// <summary>
/// Computes a line-by-line diff between two text versions.
/// Useful for <see cref="Abstractions.Interfaces.IFileVersioningHook"/> implementations that
/// version text content.
/// <para>
/// The comparison itself lives in the shared UI-free engine <see cref="TextDiff"/>; this helper is
/// the file-versioning façade over it. It keeps the historical behaviour — CRLF is normalized to LF
/// before the comparison, lines carry 1-based numbers in both versions and an insert/remove pair is
/// emitted as removed-then-added — so version history output does not change.
/// </para>
/// <para>
/// The one visible difference is for very large inputs: above
/// <see cref="TextDiffOptions.DefaultMaxTokens"/> lines per text (or
/// <see cref="TextDiffOptions.DefaultMaxCells"/> table cells) the comparison degrades instead of
/// allocating an unbounded table, and the result becomes one block of removed lines followed by one
/// block of added lines covering the whole text.
/// </para>
/// </summary>
public static class TmTextLineDiff
{
    /// <summary>Produces added/removed/unchanged lines describing the change from old to new text.</summary>
    /// <param name="oldText">The original text; <c>null</c> is read as empty.</param>
    /// <param name="newText">The new text; <c>null</c> is read as empty.</param>
    /// <returns>The ordered lines of the comparison.</returns>
    public static IReadOnlyList<TmFileVersionDiffLine> Compute(string? oldText, string? newText)
    {
        var diff = TextDiff.Lines(Normalize(oldText), Normalize(newText));
        var result = new List<TmFileVersionDiffLine>(diff.Hunks.Count);
        var oldLine = 1;
        var newLine = 1;

        foreach (var hunk in diff.Hunks)
        {
            switch (hunk.Kind)
            {
                case TextDiffHunkKind.Equal:
                    foreach (var line in SplitLines(hunk.OldText, hunk.OldUnitCount))
                    {
                        result.Add(new TmFileVersionDiffLine
                        {
                            Kind = TmFileVersionDiffKind.Unchanged,
                            Text = line,
                            OldLineNumber = oldLine++,
                            NewLineNumber = newLine++
                        });
                    }

                    break;

                case TextDiffHunkKind.Delete:
                    foreach (var line in SplitLines(hunk.OldText, hunk.OldUnitCount))
                    {
                        result.Add(new TmFileVersionDiffLine
                        {
                            Kind = TmFileVersionDiffKind.Removed,
                            Text = line,
                            OldLineNumber = oldLine++
                        });
                    }

                    break;

                case TextDiffHunkKind.Insert:
                    foreach (var line in SplitLines(hunk.NewText, hunk.NewUnitCount))
                    {
                        result.Add(new TmFileVersionDiffLine
                        {
                            Kind = TmFileVersionDiffKind.Added,
                            Text = line,
                            NewLineNumber = newLine++
                        });
                    }

                    break;

                default:
                    foreach (var line in SplitLines(hunk.OldText, hunk.OldUnitCount))
                    {
                        result.Add(new TmFileVersionDiffLine
                        {
                            Kind = TmFileVersionDiffKind.Removed,
                            Text = line,
                            OldLineNumber = oldLine++
                        });
                    }

                    foreach (var line in SplitLines(hunk.NewText, hunk.NewUnitCount))
                    {
                        result.Add(new TmFileVersionDiffLine
                        {
                            Kind = TmFileVersionDiffKind.Added,
                            Text = line,
                            NewLineNumber = newLine++
                        });
                    }

                    break;
            }
        }

        return result;
    }

    private static string Normalize(string? text)
        => text is null ? string.Empty : text.Replace("\r\n", "\n");

    /// <summary>
    /// Splits a hunk's text into exactly <paramref name="count"/> lines. The count comes from the
    /// engine because a trailing empty line occupies no character, so splitting on the line break
    /// alone cannot say whether the last empty piece is a line or the terminator of the previous
    /// one.
    /// </summary>
    private static IEnumerable<string> SplitLines(string text, int count)
    {
        if (count <= 0)
        {
            yield break;
        }

        var pieces = text.Split('\n');
        for (var index = 0; index < count; index++)
        {
            yield return index < pieces.Length ? pieces[index] : string.Empty;
        }
    }
}
