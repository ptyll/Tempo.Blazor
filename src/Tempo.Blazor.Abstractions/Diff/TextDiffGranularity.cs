namespace Tempo.Blazor.Diff;

/// <summary>
/// Unit a <see cref="TextDiff"/> comparison works on. The granularity decides what a hunk's text
/// contains and how fine-grained an accept/reject decision is: word level splits the input into
/// word, number and punctuation tokens (whitespace is part of the surrounding hunk), line level
/// splits it into lines including their line break.
/// </summary>
public enum TextDiffGranularity
{
    /// <summary>Word, number and punctuation tokens. Whitespace is not a token of its own.</summary>
    Words = 0,

    /// <summary>Whole lines, so every difference is a whole-line insertion, deletion or replacement.</summary>
    Lines = 1
}
