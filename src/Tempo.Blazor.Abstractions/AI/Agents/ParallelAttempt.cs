namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One of several attempts at the SAME piece of work, shown beside its rivals so a reader can compare them and pick a
/// winner: what it is called, the state it ended in, the score it earned, the votes recorded on it and its own summary.
/// <para>
/// The record is a VALUE and carries no state of its own: the winner is either named by the host
/// (<c>BestAttemptId</c> on the surface) or derived from the highest <see cref="Score"/> by
/// <see cref="ParallelAttemptRanking"/>. A component never mutates an attempt and never invents a score.
/// </para>
/// <para>
/// The attempt's own body is deliberately NOT part of the model: a surface renders it through a template, so a diff, a
/// document, a code block or plain prose all fit without the library knowing what an attempt contains.
/// </para>
/// </summary>
public sealed record ParallelAttempt
{
    /// <summary>Creates an attempt.</summary>
    /// <param name="id">
    /// Stable identity of the attempt — what a winner selection reports. Null or whitespace generates one, so a list
    /// built in a loop still renders; a host that has to match the callback to its own record supplies its own id.
    /// </param>
    /// <param name="label">
    /// What distinguishes this attempt from its rivals ("Draft A", "gpt-4o", "Retry with the new prompt"). Empty renders
    /// the surface's own numbered fallback.
    /// </param>
    public ParallelAttempt(string id, string label)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id!;
        Label = label ?? string.Empty;
    }

    /// <summary>Stable identity of the attempt. Never empty: a missing id is generated at construction.</summary>
    public string Id { get; init; }

    /// <summary>What distinguishes this attempt. Empty renders the surface's own numbered fallback.</summary>
    public string Label { get; init; }

    /// <summary>
    /// The state the attempt ended in. Default <see cref="AgentActivityStatus.Idle"/>; painted as an icon, a localized
    /// word and a tone, and an attempt that is still running or failed can never win a comparison by accident.
    /// </summary>
    public AgentActivityStatus Status { get; init; }

    /// <summary>
    /// The score the host (or a grader) gave the attempt, on the host's own scale. <c>null</c> means "not scored", not
    /// zero; the ranking ignores an unscored attempt unless the host names it explicitly.
    /// </summary>
    public double? Score { get; init; }

    /// <summary>The votes recorded on the attempt, in any order. Empty is legal (no votes yet).</summary>
    public IReadOnlyList<AttemptVote> Votes
    {
        get => _votes;
        init => _votes = value ?? [];
    }

    /// <summary>
    /// The attempt's own short summary — what it would be chosen FOR. Empty renders none; the full body comes from the
    /// surface's content template.
    /// </summary>
    public string? Summary { get; init; }

    private readonly IReadOnlyList<AttemptVote> _votes = [];

    /// <summary>Whether the host supplied a label.</summary>
    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    /// <summary>Whether the host supplied a summary worth printing.</summary>
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    /// <summary>Whether a usable score was supplied (a non-finite value counts as none).</summary>
    public bool HasScore => Score is { } score && double.IsFinite(score);

    /// <summary>How many votes the attempt carries.</summary>
    public int VoteCount => _votes.Count;

    /// <summary>Whether at least one vote was recorded.</summary>
    public bool HasVotes => _votes.Count > 0;

    /// <summary>How many voters approved the attempt.</summary>
    public int Approvals => Count(AttemptVoteOutcome.Approve);

    /// <summary>How many voters rejected the attempt.</summary>
    public int Rejections => Count(AttemptVoteOutcome.Reject);

    /// <summary>How many voters abstained (including a score-only vote).</summary>
    public int Abstentions => Count(AttemptVoteOutcome.Abstain);

    /// <summary>Whether at least one voter explained their verdict.</summary>
    public bool HasRationales => _votes.Any(vote => vote is not null && vote.HasRationale);

    private int Count(AttemptVoteOutcome outcome)
    {
        var count = 0;
        foreach (var vote in _votes)
        {
            if (vote is not null && vote.Outcome == outcome)
            {
                count++;
            }
        }

        return count;
    }
}
