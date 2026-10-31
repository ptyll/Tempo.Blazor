namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One tool/function invocation the model asked for, together with its live status, arguments and
/// result. Streamed through <see cref="AiToolCallStartedDelta"/>, <see cref="AiToolCallUpdatedDelta"/>
/// and <see cref="AiToolCallCompletedDelta"/>.
/// </summary>
public sealed class AiToolCall
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string? _displayName;
    private string? _argumentsJson;
    private string? _resultJson;
    private string? _error;

    /// <summary>Model-supplied call identifier; updates and the completion target it.</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>Technical tool name.</summary>
    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    /// <summary>Optional human readable tool name rendered instead of <see cref="Name"/>.</summary>
    public string? DisplayName
    {
        get => _displayName;
        set => _displayName = value;
    }

    /// <summary>Current status.</summary>
    public AiToolCallStatus Status { get; set; } = AiToolCallStatus.Queued;

    /// <summary>Raw JSON arguments, pretty-printed by the card.</summary>
    public string? ArgumentsJson
    {
        get => _argumentsJson;
        set => _argumentsJson = value;
    }

    /// <summary>Raw JSON result, pretty-printed by the card.</summary>
    public string? ResultJson
    {
        get => _resultJson;
        set => _resultJson = value;
    }

    /// <summary>Failure reason when <see cref="Status"/> is <see cref="AiToolCallStatus.Failed"/>.</summary>
    public string? Error
    {
        get => _error;
        set => _error = value;
    }

    /// <summary>UTC start of the execution, set by the start delta.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>UTC end of the execution, set by the completion delta.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>When true the arguments and result are hidden and the card renders a localized notice instead.</summary>
    public bool IsRedacted { get; set; }

    /// <summary>Elapsed execution time, or <see langword="null"/> while the call is still running.</summary>
    public TimeSpan? Duration => StartedAt is { } started && CompletedAt is { } completed
        ? completed - started
        : null;
}
