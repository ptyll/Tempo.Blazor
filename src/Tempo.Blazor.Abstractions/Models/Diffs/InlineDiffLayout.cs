namespace Tempo.Blazor.Abstractions.Models.Diffs;

/// <summary>
/// How <c>TmInlineDiff</c> places the changes inside the text: flowing inline with their context, or
/// stacked one side per row.
/// <para>
/// The layout is about the user interface only — the comparison always comes from
/// <see cref="Tempo.Blazor.Diff.TextDiff"/>, so both layouts show exactly the same hunks and the same
/// <see cref="Tempo.Blazor.Diff.TextDiffResult.Apply"/> output, and every decision surface
/// (<see cref="InlineDiffMode.Review"/>) keeps working.
/// </para>
/// </summary>
public enum InlineDiffLayout
{
    /// <summary>
    /// The changes flow inside the surrounding text — the deletion and the insertion of a change sit
    /// one after another in the line. The default and the presentation the component always had.
    /// </summary>
    Inline = 0,

    /// <summary>
    /// The changes stack: each hunk becomes its own row, so the deletion and the insertion of a change
    /// read one below the other. Meant for a one-hunk proposal of a whole value (an assistant suggesting
    /// the complete replacement text of a field), where the inline interleave of two full-length sides
    /// is hard to read.
    /// </summary>
    Stacked = 1,
}
