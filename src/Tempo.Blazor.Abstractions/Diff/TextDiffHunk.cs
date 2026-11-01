namespace Tempo.Blazor.Diff;

/// <summary>
/// One contiguous piece of a <see cref="TextDiffResult"/>. The hunks of a result are ordered and
/// tile both inputs: concatenating <see cref="OldText"/> over all hunks reproduces the old input
/// exactly and concatenating <see cref="NewText"/> reproduces the new input exactly. That invariant
/// is what lets a consumer accept an arbitrary subset of the changes and still get a valid text out
/// of <see cref="TextDiffResult.Apply"/>.
/// </summary>
public sealed class TextDiffHunk
{
    /// <summary>
    /// Deterministic identifier built from the kind and both offsets, for example
    /// <c>replace-15-15</c>. It stays the same across repeated runs over the same inputs, so an
    /// accepted-change set can be kept in a component's state across re-renders, and it is unique
    /// within a result.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>What kind of piece this hunk is.</summary>
    public required TextDiffHunkKind Kind { get; init; }

    /// <summary>
    /// Exact text of the old input covered by this hunk (empty for
    /// <see cref="TextDiffHunkKind.Insert"/>). It may include the whitespace or line break that
    /// separates it from the next hunk, which is what keeps the hunks an exact tiling.
    /// </summary>
    public required string OldText { get; init; }

    /// <summary>
    /// Exact text of the new input covered by this hunk (empty for
    /// <see cref="TextDiffHunkKind.Delete"/>).
    /// </summary>
    public required string NewText { get; init; }

    /// <summary>Span of the old input this hunk covers.</summary>
    public required TextRange OldRange { get; init; }

    /// <summary>Span of the new input this hunk covers.</summary>
    public required TextRange NewRange { get; init; }

    /// <summary>
    /// How many comparison units of the old input the hunk covers — word tokens for a word diff,
    /// lines for a line diff. A line-level renderer needs the count rather than the character span:
    /// a trailing empty line occupies no character at all, so the span cannot express it.
    /// </summary>
    public required int OldUnitCount { get; init; }

    /// <summary>How many comparison units of the new input the hunk covers.</summary>
    public required int NewUnitCount { get; init; }

    /// <summary>Whether the hunk represents an actual change (anything but <see cref="TextDiffHunkKind.Equal"/>).</summary>
    public bool IsChange => Kind != TextDiffHunkKind.Equal;
}
