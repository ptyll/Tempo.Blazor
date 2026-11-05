namespace Tempo.Blazor.AI.Models;

/// <summary>
/// What one voter decided about a parallel attempt.
/// <para>
/// <see cref="Abstain"/> is the default (0) on purpose: a vote whose outcome the host did not set must not silently
/// count as an approval, because an attempt's ranking is computed from these values.
/// </para>
/// </summary>
public enum AttemptVoteOutcome
{
    /// <summary>The voter read the attempt and made no claim either way. The default.</summary>
    Abstain = 0,

    /// <summary>The voter accepts this attempt as the result.</summary>
    Approve = 1,

    /// <summary>The voter rejects this attempt.</summary>
    Reject = 2,
}
