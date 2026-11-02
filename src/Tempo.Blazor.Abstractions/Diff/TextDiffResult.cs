using System.Text;

namespace Tempo.Blazor.Diff;

/// <summary>
/// Result of a <see cref="TextDiff"/> comparison: an ordered list of
/// <see cref="TextDiffHunk"/> values that tiles both inputs, plus the operation that turns any
/// subset of the changes into a text.
/// </summary>
public sealed class TextDiffResult
{
    private readonly TextDiffHunk[] _hunks;
    private readonly TextDiffHunk[] _changes;
    private readonly string[] _hunkIds;

    internal TextDiffResult(
        string oldText,
        string newText,
        IEnumerable<TextDiffHunk> hunks,
        TextDiffGranularity granularity,
        TextDiffFallbackKind fallback)
    {
        OldText = oldText;
        NewText = newText;
        _hunks = hunks.ToArray();
        _changes = _hunks.Where(hunk => hunk.IsChange).ToArray();
        _hunkIds = _hunks.Select(hunk => hunk.Id).ToArray();
        Granularity = granularity;
        Fallback = fallback;
    }

    /// <summary>The old input the result describes (never <c>null</c>; a <c>null</c> argument is read as empty).</summary>
    public string OldText { get; }

    /// <summary>The new input the result describes (never <c>null</c>; a <c>null</c> argument is read as empty).</summary>
    public string NewText { get; }

    /// <summary>Every hunk in document order — equal and changed pieces interleaved.</summary>
    public IReadOnlyList<TextDiffHunk> Hunks => _hunks;

    /// <summary>Only the hunks a consumer can accept or reject, in document order.</summary>
    public IReadOnlyList<TextDiffHunk> Changes => _changes;

    /// <summary>Ids of every hunk, in document order. Passing this collection to <see cref="Apply"/> accepts everything.</summary>
    public IReadOnlyList<string> HunkIds => _hunkIds;

    /// <summary>Granularity the result was actually produced at (may be coarser than requested, see <see cref="Fallback"/>).</summary>
    public TextDiffGranularity Granularity { get; }

    /// <summary>Whether and how far the comparison had to degrade to stay within the configured limits.</summary>
    public TextDiffFallbackKind Fallback { get; }

    /// <summary>Whether the comparison degraded at all (<see cref="Fallback"/> is not <see cref="TextDiffFallbackKind.None"/>).</summary>
    public bool IsDegraded => Fallback != TextDiffFallbackKind.None;

    /// <summary>Whether the two inputs differ at all.</summary>
    public bool HasChanges => _changes.Length > 0;

    /// <summary>
    /// Builds the resulting text for a set of accepted hunk ids: every accepted hunk contributes
    /// <see cref="TextDiffHunk.NewText"/>, every other hunk contributes
    /// <see cref="TextDiffHunk.OldText"/>.
    /// <para>
    /// Because the hunks tile both inputs, passing no id (or <c>null</c>) returns
    /// <see cref="OldText"/> character for character and passing every id
    /// (<see cref="HunkIds"/>) returns <see cref="NewText"/> character for character. Any mix in
    /// between returns a text where the rejected changes keep their old wording. Ids that do not
    /// belong to this result are ignored, so a stale accepted-change set cannot corrupt the output.
    /// </para>
    /// </summary>
    /// <param name="acceptedHunkIds">Ids of the hunks to take from the new text. <c>null</c> or an empty collection rejects every change.</param>
    /// <returns>The resulting text.</returns>
    public string Apply(IEnumerable<string>? acceptedHunkIds)
    {
        if (acceptedHunkIds is null)
        {
            return OldText;
        }

        HashSet<string>? accepted = null;
        foreach (var id in acceptedHunkIds)
        {
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            (accepted ??= new HashSet<string>(StringComparer.Ordinal)).Add(id);
        }

        if (accepted is null || accepted.Count == 0)
        {
            return OldText;
        }

        var builder = new StringBuilder(NewText.Length + 16);
        foreach (var hunk in _hunks)
        {
            builder.Append(accepted.Contains(hunk.Id) ? hunk.NewText : hunk.OldText);
        }

        return builder.ToString();
    }
}
