using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tempo.Blazor.Mcp;

/// <summary>
/// Builds the JSON envelope every wireframe MCP tool returns, so the LLM can reliably tell
/// success from failure: <c>{ "success": true, ... }</c> or
/// <c>{ "success": false, "error": "...", "message": "...", "validationErrors": [...] }</c>.
/// </summary>
public static class McpToolResults
{
    /// <summary>Error code for a missing resource.</summary>
    public const string NotFound = "not_found";

    /// <summary>Error code for a payload that failed validation.</summary>
    public const string ValidationFailed = "validation_failed";

    /// <summary>Error code for an optimistic-concurrency conflict.</summary>
    public const string Conflict = "conflict";

    /// <summary>Error code for a syntactically valid but unsupported operation.</summary>
    public const string InvalidOperation = "invalid_operation";

    /// <summary>Error code for an optional host capability that is not registered.</summary>
    public const string Unsupported = "unsupported";

    /// <summary>Generic error code.</summary>
    public const string Error = "error";

    /// <summary>
    /// A successful result. The properties of <paramref name="data"/> are merged at the top level
    /// next to <c>success: true</c>.
    /// </summary>
    public static string Success(object? data = null)
    {
        var result = new JsonObject { ["success"] = true };
        if (data is not null)
        {
            var node = JsonSerializer.SerializeToNode(data, McpJson.Options);
            if (node is JsonObject obj)
            {
                foreach (var kvp in obj)
                {
                    result[kvp.Key] = kvp.Value?.DeepClone();
                }
            }
            else
            {
                result["value"] = node?.DeepClone();
            }
        }

        return result.ToJsonString(McpJson.Options);
    }

    /// <summary>
    /// A failure result with an error code, message and optional validation details. The
    /// properties of <paramref name="data"/> (e.g. a partial-progress report) are merged at the
    /// top level next to <c>success: false</c>.
    /// </summary>
    public static string Failure(
        string error, string message, IEnumerable<string>? validationErrors = null, object? data = null)
    {
        var result = new JsonObject
        {
            ["success"] = false,
            ["error"] = error,
            ["message"] = message
        };

        if (validationErrors is not null)
        {
            var array = new JsonArray();
            foreach (var e in validationErrors)
            {
                array.Add(e);
            }
            result["validationErrors"] = array;
        }

        if (data is not null)
        {
            var node = JsonSerializer.SerializeToNode(data, McpJson.Options);
            if (node is JsonObject obj)
            {
                foreach (var kvp in obj)
                {
                    result[kvp.Key] = kvp.Value?.DeepClone();
                }
            }
        }

        return result.ToJsonString(McpJson.Options);
    }
}
