namespace Tempo.Blazor.AI.Models;

/// <summary>
/// What one node of an agent run represents. The kind drives the icon of the row (and of a
/// <see cref="AgentMonitorStep"/>), never its colour alone: the state is always carried by an icon plus a
/// word.
/// </summary>
public enum AgentRunNodeKind
{
    /// <summary>The agent that owns the run (usually the root node).</summary>
    Agent = 0,

    /// <summary>A delegated sub-agent the parent started.</summary>
    SubAgent = 1,

    /// <summary>A tool call the agent made.</summary>
    Tool = 2,

    /// <summary>A human approval the run is waiting for.</summary>
    Approval = 3,

    /// <summary>A plain step of the run that is neither an agent nor a tool call.</summary>
    Step = 4,
}

/// <summary>
/// What one step of an ongoing run is: the kind decides the glyph the monitor's timeline draws for it.
/// </summary>
public enum AgentStepKind
{
    /// <summary>The agent is reasoning; nothing is executed yet.</summary>
    Thinking = 0,

    /// <summary>The agent called a tool.</summary>
    ToolCall = 1,

    /// <summary>The agent produced a message for the reader.</summary>
    Message = 2,

    /// <summary>The step is waiting for a human decision.</summary>
    Approval = 3,

    /// <summary>The run handed over to another agent.</summary>
    Handoff = 4,

    /// <summary>The step produced the run's result.</summary>
    Result = 5,
}
