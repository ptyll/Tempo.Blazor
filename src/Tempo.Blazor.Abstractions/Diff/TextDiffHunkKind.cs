namespace Tempo.Blazor.Diff;

/// <summary>
/// Nature of a single <see cref="TextDiffHunk"/>. The value drives the rendering (an insertion is
/// presented as inserted text, a deletion as removed text) and the accept/reject decision the
/// consumer offers — a <see cref="Replace"/> hunk is ONE decision even though it covers both sides.
/// </summary>
public enum TextDiffHunkKind
{
    /// <summary>The text is present in both inputs; there is nothing to decide.</summary>
    Equal = 0,

    /// <summary>Text exists only in the new input (<see cref="TextDiffHunk.OldText"/> is empty).</summary>
    Insert = 1,

    /// <summary>Text exists only in the old input (<see cref="TextDiffHunk.NewText"/> is empty).</summary>
    Delete = 2,

    /// <summary>
    /// Old text was replaced by new text. Both sides are non-empty and are accepted or rejected
    /// together, which is what makes a word replacement one reviewable change.
    /// </summary>
    Replace = 3
}
