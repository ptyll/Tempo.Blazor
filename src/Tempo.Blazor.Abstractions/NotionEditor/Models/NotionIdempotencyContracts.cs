using System.Text.Json.Serialization;

namespace Tempo.Blazor.NotionEditor.Models;

/// <summary>Provider-neutral metadata for one idempotent aggregate operation.</summary>
public sealed class NotionIdempotentExecutionRequest
{
    /// <summary>Stable operation namespace, such as an MCP tool name.</summary>
    [JsonPropertyName("operationScope")]
    public string OperationScope { get; init; } = string.Empty;

    /// <summary>Caller-supplied stable key for one logical request.</summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    /// <summary>SHA-256 hash of the complete canonical request.</summary>
    [JsonPropertyName("requestHash")]
    public string RequestHash { get; init; } = string.Empty;

    /// <summary>How long a committed receipt remains eligible for replay.</summary>
    [JsonPropertyName("retention")]
    public TimeSpan Retention { get; init; } = TimeSpan.FromHours(24);
}

/// <summary>Outcome of a provider-level idempotent aggregate execution.</summary>
public enum NotionIdempotentExecutionStatus
{
    /// <summary>The callback and its response receipt were committed atomically.</summary>
    Executed,

    /// <summary>An existing response for the same canonical request was returned.</summary>
    Replayed,

    /// <summary>The scoped key already belongs to a different canonical request.</summary>
    Collision
}

/// <summary>Provider result containing an opaque authoring response when available.</summary>
public sealed class NotionIdempotentExecutionResult
{
    /// <summary>Execution, replay, or hash-collision outcome.</summary>
    [JsonPropertyName("status")]
    public NotionIdempotentExecutionStatus Status { get; init; }

    /// <summary>
    /// Exact response produced by the callback or loaded from the durable receipt.
    /// </summary>
    [JsonPropertyName("responseJson")]
    public string? ResponseJson { get; init; }
}
