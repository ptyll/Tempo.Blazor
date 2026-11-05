namespace Tempo.Blazor.AI.Models;

/// <summary>
/// What kind of thing one agent handed to the next — the coarse label a handoff surface paints beside an item so a
/// reader can tell a shared instruction from a dropped tool.
/// <para>
/// The vocabulary is deliberately small and transport-neutral: a host maps its own payload (a system prompt, a tool
/// catalogue, a file, a scratchpad) onto one of these five kinds, and the surface renders the kind as a localized word
/// plus an icon. <see cref="Context"/> is the default because a piece of conversation is the most common thing a
/// handoff carries.
/// </para>
/// </summary>
public enum HandoffItemKind
{
    /// <summary>Conversation context: the messages, the task statement, the reason the work moved on.</summary>
    Context = 0,

    /// <summary>An instruction the receiving agent has to follow (a system prompt, a constraint, a goal).</summary>
    Instruction = 1,

    /// <summary>A tool or capability the receiving agent is allowed to use.</summary>
    Tool = 2,

    /// <summary>An artifact: a document, a file, a generated output.</summary>
    Artifact = 3,

    /// <summary>Remembered state: a scratchpad, a plan, facts the previous agent looked up.</summary>
    Memory = 4,
}
