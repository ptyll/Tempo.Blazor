namespace Tempo.Blazor.Diff;

/// <summary>
/// Limits that keep a <see cref="TextDiff"/> comparison bounded. The defaults suit interactive
/// review of prose and terminal-sized source files; a host that diffs very large documents on
/// purpose can raise them, and a host that wants an early degradation can lower them.
/// </summary>
public sealed class TextDiffOptions
{
    /// <summary>Default value of <see cref="MaxTokens"/>.</summary>
    public const int DefaultMaxTokens = 20_000;

    /// <summary>Default value of <see cref="MaxCells"/> (4 000 000 cells ≈ 16 MB of table).</summary>
    public const int DefaultMaxCells = 4_000_000;

    /// <summary>Shared immutable instance used when a caller passes no options.</summary>
    public static TextDiffOptions Default { get; } = new();

    /// <summary>
    /// Maximum number of tokens (words or lines) per input before the comparison degrades:
    /// a word diff falls back to line level, a line diff to a single whole-text change. Values
    /// below <c>1</c> are treated as <c>1</c>. Defaults to <see cref="DefaultMaxTokens"/>.
    /// </summary>
    public int MaxTokens { get; init; } = DefaultMaxTokens;

    /// <summary>
    /// Maximum number of cells in the longest-common-subsequence table built after stripping the
    /// common prefix and suffix — the real memory guard, because two very different inputs inside
    /// <see cref="MaxTokens"/> can still require a quadratic table. Exceeding it degrades exactly
    /// like <see cref="MaxTokens"/> does. Values below <c>1</c> are treated as <c>1</c>. Defaults
    /// to <see cref="DefaultMaxCells"/>.
    /// </summary>
    public int MaxCells { get; init; } = DefaultMaxCells;
}
