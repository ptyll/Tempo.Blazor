namespace Tempo.Blazor.Abstractions.Models.Diffs;

/// <summary>
/// One human decision on a single change of a <c>TmInlineDiff</c>: which hunk was decided, whether it
/// was accepted and the text the decision produces.
/// <para>
/// <c>TmInlineDiff</c> hands this to its <c>OnDecision</c> callback <em>before</em> it applies the
/// decision and only applies it when the callback completes without throwing, so a failed save never
/// looks like an accepted change. <see cref="Result"/> is the exact
/// <see cref="Tempo.Blazor.Diff.TextDiffResult.Apply"/> output the change would produce, which lets a
/// host persist the resulting wording in the same round trip as the decision itself.
/// </para>
/// </summary>
/// <param name="HunkId">Identifier of the decided change (the hunk's stable <c>Id</c>).</param>
/// <param name="Accepted">Whether the change was accepted (<c>true</c>) or rejected (<c>false</c>).</param>
/// <param name="Result">The whole resulting text after the decision.</param>
public sealed record InlineDiffDecision(string HunkId, bool Accepted, string Result);
