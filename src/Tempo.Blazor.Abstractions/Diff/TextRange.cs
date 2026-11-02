namespace Tempo.Blazor.Diff;

/// <summary>
/// A half-open span of one input text used by a <see cref="TextDiffHunk"/>.
/// <para>
/// Offsets are CHARACTER offsets into the corresponding input (<see cref="TextDiffResult.OldText"/>
/// for the old side, <see cref="TextDiffResult.NewText"/> for the new side), independent of the
/// <see cref="TextDiffGranularity"/>: a word diff and a line diff both report character offsets, so
/// a consumer can slice the original text — or scroll a viewer — without re-running the engine.
/// </para>
/// </summary>
/// <param name="Start">Zero-based offset of the first character covered by the span.</param>
/// <param name="Length">Number of characters covered; <c>0</c> for the empty side of an insertion or deletion.</param>
public readonly record struct TextRange(int Start, int Length)
{
    /// <summary>Offset one past the last covered character.</summary>
    public int End => Start + Length;

    /// <summary>Whether the span covers no character at all.</summary>
    public bool IsEmpty => Length == 0;
}
