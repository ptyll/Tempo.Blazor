namespace Tempo.Blazor.Diff;

/// <summary>
/// How far a <see cref="TextDiffResult"/> had to degrade to stay within the configured limits.
/// The engine never throws and never blocks its caller on a pathological input — it coarsens the
/// comparison instead, and reports that it did so, so a consumer can tell the user the diff is
/// approximate rather than silently presenting a line-level result as a word-level one.
/// </summary>
public enum TextDiffFallbackKind
{
    /// <summary>The comparison ran at the requested granularity and within the limits.</summary>
    None = 0,

    /// <summary>
    /// Word-level comparison exceeded <see cref="TextDiffOptions.MaxTokens"/> or
    /// <see cref="TextDiffOptions.MaxCells"/>; the result describes whole lines instead.
    /// </summary>
    LineLevel = 1,

    /// <summary>
    /// Even the line-level comparison exceeded the limits; the result is a single change covering
    /// the whole input (or a single <see cref="TextDiffHunkKind.Equal"/> hunk when the inputs match).
    /// </summary>
    SingleChange = 2
}
