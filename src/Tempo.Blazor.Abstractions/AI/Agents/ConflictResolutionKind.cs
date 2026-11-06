namespace Tempo.Blazor.AI.Models;

/// <summary>
/// How a reader settled a conflicting file: they picked one agent's version to keep, or they decided to merge the file
/// by hand.
/// <para>
/// The enum is the CHOICE, not the outcome. <see cref="Agent"/> says "take this agent's side" and
/// <see cref="ConflictResolution.AgentId"/> names which agent that is; <see cref="Manual"/> says "I will resolve this
/// one myself", which is a real decision and therefore unblocks the merge instead of counting as unresolved.
/// </para>
/// </summary>
public enum ConflictResolutionKind
{
    /// <summary>One agent's version wins; <see cref="ConflictResolution.AgentId"/> names the agent.</summary>
    Agent,

    /// <summary>The reader resolves the file themselves — a decision, so the file is no longer unresolved.</summary>
    Manual,
}
