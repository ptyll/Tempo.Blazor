namespace Tempo.Blazor.AI.Models;

/// <summary>Lifecycle of an <see cref="AiMessage"/>, driven by the deltas of <see cref="AiStreamDelta"/>.</summary>
public enum AiMessageStatus
{
    /// <summary>Created but not started yet.</summary>
    Pending = 0,

    /// <summary>Deltas are arriving.</summary>
    Streaming = 1,

    /// <summary>The model finished the answer.</summary>
    Completed = 2,

    /// <summary>The user stopped the generation; the content received so far is kept.</summary>
    Stopped = 3,

    /// <summary>The stream failed; <see cref="AiMessage.Error"/> carries the reason.</summary>
    Failed = 4,
}
