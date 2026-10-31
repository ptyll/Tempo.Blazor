namespace Tempo.Blazor.AI.Models;

/// <summary>Who produced an <see cref="AiMessage"/>.</summary>
public enum AiMessageRole
{
    /// <summary>The end user's prompt.</summary>
    User = 0,

    /// <summary>The model's answer.</summary>
    Assistant = 1,

    /// <summary>A system/instruction turn supplied by the host.</summary>
    System = 2,

    /// <summary>The result of a tool execution, fed back to the model.</summary>
    Tool = 3,
}
