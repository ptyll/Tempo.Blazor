namespace Tempo.Blazor.AI.Models;

/// <summary>
/// How far one handoff in a chain has got — whether the receiving agent has taken the work over.
/// <para>
/// <see cref="Pending"/> is the default (0) on purpose: a step a host has not classified yet must never read as
/// accepted, because "the work is now someone else's" is a claim about a run, not a decoration.
/// </para>
/// </summary>
public enum HandoffStatus
{
    /// <summary>Proposed but not answered yet: the receiving agent has not picked the work up.</summary>
    Pending = 0,

    /// <summary>The receiving agent took the work over.</summary>
    Accepted = 1,

    /// <summary>The receiving agent refused the work; nothing was handed over.</summary>
    Rejected = 2,

    /// <summary>The receiving agent finished what it took over.</summary>
    Completed = 3,
}
