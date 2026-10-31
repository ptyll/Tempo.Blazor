namespace Tempo.Blazor.AI.Models;

/// <summary>Execution status of one <see cref="AiToolCall"/>.</summary>
public enum AiToolCallStatus
{
    /// <summary>Queued but not started.</summary>
    Queued = 0,

    /// <summary>Executing.</summary>
    Running = 1,

    /// <summary>Finished successfully.</summary>
    Succeeded = 2,

    /// <summary>Finished with an error.</summary>
    Failed = 3,

    /// <summary>Cancelled by the host or the user.</summary>
    Cancelled = 4,
}
