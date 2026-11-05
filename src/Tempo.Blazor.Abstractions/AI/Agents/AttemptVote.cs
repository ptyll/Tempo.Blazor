namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One voter's verdict on one parallel attempt: who voted, what they decided, the score they gave it and why.
/// <para>
/// The rationale is the reason a reader opens the votes at all, so it is kept as free text and printed verbatim — the
/// surface never parses it and never invents one. <see cref="Outcome"/> is a claim of its own: a score without a
/// verdict (the common "grader gave 0.82") is legal and lands on <see cref="AttemptVoteOutcome.Abstain"/>.
/// </para>
/// <para>
/// <see cref="By"/> is the optional shared identity (<see cref="AgentRef"/>): when it is set the surface paints the
/// voter's palette slot and initials, the same identity it paints in the lanes and in the graph.
/// </para>
/// </summary>
public sealed record AttemptVote
{
    /// <summary>Creates a vote.</summary>
    /// <param name="voter">
    /// The voter's name, in the host's own words ("Reviewer", "Grader #2", a person's name). Empty renders the
    /// surface's own fallback; the vote itself still counts.
    /// </param>
    public AttemptVote(string voter)
    {
        Voter = voter ?? string.Empty;
    }

    /// <summary>The voter's name as the host wants it read. Empty renders the surface's own fallback.</summary>
    public string Voter { get; init; }

    /// <summary>
    /// The voter's identity when the host has one, or <c>null</c>. Setting it paints the agent's palette slot,
    /// initials and (when <see cref="Voter"/> is empty) its name.
    /// </summary>
    public AgentRef? By { get; init; }

    /// <summary>
    /// What the voter decided. Default <see cref="AttemptVoteOutcome.Abstain"/>, so a score-only vote is never read
    /// as an approval.
    /// </summary>
    public AttemptVoteOutcome Outcome { get; init; }

    /// <summary>
    /// The score the voter gave the attempt, on the host's own scale (0–1, 0–10, a percentage). <c>null</c> means "no
    /// score", not zero; the surface prints the number with the host's culture and never rescales it.
    /// </summary>
    public double? Score { get; init; }

    /// <summary>
    /// Why the voter decided so, in the voter's own words. Empty renders no rationale, and the surface never invents
    /// one.
    /// </summary>
    public string? Rationale { get; init; }

    /// <summary>Whether the vote carries a rationale worth opening.</summary>
    public bool HasRationale => !string.IsNullOrWhiteSpace(Rationale);

    /// <summary>Whether the vote carries a score.</summary>
    public bool HasScore => Score is { } score && double.IsFinite(score);

    /// <summary>The name a surface prints: <see cref="Voter"/>, or the identity's name when the voter is unnamed.</summary>
    public string DisplayName => !string.IsNullOrWhiteSpace(Voter)
        ? Voter
        : By is { HasName: true } by
            ? by.Name
            : string.Empty;

    /// <summary>The initials an avatar paints: the identity's own initials, else the initials of the vote's name.</summary>
    public string Initials => By is { } by ? by.Initials : AgentRef.InitialsOf(DisplayName);

    /// <summary>The palette token of the voter's identity, or an empty string when no identity is set.</summary>
    public string ColorToken => By is { } by ? by.ColorToken : string.Empty;
}
