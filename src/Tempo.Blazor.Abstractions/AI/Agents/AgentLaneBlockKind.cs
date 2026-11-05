namespace Tempo.Blazor.AI.Models;

/// <summary>
/// What one block on an agent lane represents, in the one vocabulary every lane surface shares.
/// <para>
/// The kind is always painted twice — a decorative glyph and a localized WORD — because an icon alone is not
/// information a screen-reader reader can use. The set is deliberately small and transport-neutral: a host maps
/// its own vocabulary (a plan step, a tool call, a room message, a review gate, a delegation) onto these five
/// values and <c>TmAgentLanes</c> stays backend-free.
/// </para>
/// </summary>
public enum AgentLaneBlockKind
{
    /// <summary>A piece of work assigned to the agent — a plan step, a turn, a task. The default.</summary>
    Task = 0,

    /// <summary>A tool call the agent made (a lookup, a write, a command).</summary>
    Tool = 1,

    /// <summary>A message the agent produced or received.</summary>
    Message = 2,

    /// <summary>A review or approval gate — the block a reader usually has to act on.</summary>
    Review = 3,

    /// <summary>A transfer of the work to another agent; the lane surface draws it with a handoff arrow.</summary>
    Handoff = 4,
}
