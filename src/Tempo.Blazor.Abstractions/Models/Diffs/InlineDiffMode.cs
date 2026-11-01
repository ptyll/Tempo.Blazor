namespace Tempo.Blazor.Abstractions.Models.Diffs;

/// <summary>
/// How <c>TmInlineDiff</c> presents the comparison of two texts: as a read-only rendering of the
/// changes, or as a review where every change is a decision the host can persist.
/// <para>
/// The mode is about the user interface only — the comparison itself always comes from
/// <see cref="Tempo.Blazor.Diff.TextDiff"/>, so both modes show exactly the same hunks and the same
/// <see cref="Tempo.Blazor.Diff.TextDiffResult.Apply"/> output.
/// </para>
/// </summary>
public enum InlineDiffMode
{
    /// <summary>
    /// The changes are rendered as <c>&lt;ins&gt;</c> / <c>&lt;del&gt;</c> with their visually hidden
    /// labels and nothing else: no markers, no popover, no bulk actions. The default — presenting a
    /// diff is the primary job, deciding about it is an opt-in.
    /// </summary>
    Display = 0,

    /// <summary>
    /// Adds one decision per change: a marker button that opens an Accept / Reject popover, the bulk
    /// Accept all / Reject all actions and one level of Undo. A host that sets
    /// <c>TmInlineDiff.ReadOnly</c> gets the <see cref="Display"/> presentation back even in this mode.
    /// </summary>
    Review = 1,
}
