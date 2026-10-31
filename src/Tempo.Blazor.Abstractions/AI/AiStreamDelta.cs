namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One incremental step of an answer stream. The host supplies the model call as an
/// <c>IAsyncEnumerable&lt;AiStreamDelta&gt;</c>; <c>AiMessageStreamApplier</c> folds each delta into the
/// <see cref="AiMessage"/> it belongs to, so no component has to know how a message is assembled.
/// </summary>
public abstract class AiStreamDelta
{
    /// <summary>Creates the delta; used by the concrete kinds only.</summary>
    protected AiStreamDelta()
    {
    }
}

/// <summary>A chunk of answer text, appended to the trailing text part.</summary>
public sealed class AiTextDelta : AiStreamDelta
{
    /// <summary>Text chunk. An empty chunk is a no-op for the applier.</summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>A chunk of reasoning text.</summary>
public sealed class AiReasoningDelta : AiStreamDelta
{
    /// <summary>Reasoning chunk appended to the trailing reasoning step.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>When true the chunk starts a new reasoning step instead of extending the last one.</summary>
    public bool NewStep { get; set; }
}

/// <summary>The model started a tool call.</summary>
public sealed class AiToolCallStartedDelta : AiStreamDelta
{
    /// <summary>The started call; <see cref="AiToolCall.Id"/> targets later updates and the completion.</summary>
    public AiToolCall ToolCall { get; set; } = new();
}

/// <summary>Arguments, result or status of a running tool call were updated.</summary>
public sealed class AiToolCallUpdatedDelta : AiStreamDelta
{
    /// <summary>Identifier of the call being updated; a call that was never started is created.</summary>
    public string ToolCallId { get; set; } = string.Empty;

    /// <summary>New technical name, when it changed.</summary>
    public string? Name { get; set; }

    /// <summary>New display name, when it changed.</summary>
    public string? DisplayName { get; set; }

    /// <summary>New raw arguments JSON, when it changed.</summary>
    public string? ArgumentsJson { get; set; }

    /// <summary>New raw result JSON, when it changed.</summary>
    public string? ResultJson { get; set; }

    /// <summary>New status, when it changed.</summary>
    public AiToolCallStatus? Status { get; set; }
}

/// <summary>A tool call finished (successfully, with a failure or cancelled).</summary>
public sealed class AiToolCallCompletedDelta : AiStreamDelta
{
    /// <summary>Identifier of the finished call; a call that was never started is created as finished.</summary>
    public string ToolCallId { get; set; } = string.Empty;

    /// <summary>Final status; defaults to <see cref="AiToolCallStatus.Succeeded"/>.</summary>
    public AiToolCallStatus Status { get; set; } = AiToolCallStatus.Succeeded;

    /// <summary>Final raw result JSON.</summary>
    public string? ResultJson { get; set; }

    /// <summary>Failure reason when <see cref="Status"/> is <see cref="AiToolCallStatus.Failed"/>.</summary>
    public string? Error { get; set; }

    /// <summary>When true the arguments and result stay hidden.</summary>
    public bool IsRedacted { get; set; }
}

/// <summary>The answer cited a source; the chip in the text points at its <see cref="AiSource.Id"/>.</summary>
public sealed class AiCitationAddedDelta : AiStreamDelta
{
    /// <summary>The cited source. The id must be non-empty, and a re-delivered id is ignored.</summary>
    public AiSource Source { get; set; } = new();
}

/// <summary>The model finished the answer.</summary>
public sealed class AiCompletedDelta : AiStreamDelta
{
}

/// <summary>The stream failed.</summary>
public sealed class AiErrorDelta : AiStreamDelta
{
    /// <summary>Human readable failure reason, surfaced by the message component.</summary>
    public string Message { get; set; } = string.Empty;
}
