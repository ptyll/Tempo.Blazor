using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Mcp.Notion;

/// <summary>MCP block tools for NotionEditor.</summary>
[McpServerToolType]
public static class NotionBlockTools
{
    [McpServerTool(Name = "notion_get_block_tree")]
    [Description("Read one canonical Notion page as a recursively ordered block tree. Returns page metadata, opaque concurrencyToken, digest and logical rich table rows/cells without merge markers.")]
    public static async Task<string> GetBlockTree(
        INotionAggregateProvider? provider,
        [Description("Non-empty page GUID string.")] string pageId,
        CancellationToken cancellationToken = default)
    {
        if (provider is null)
        {
            return McpToolResults.Failure(
                McpToolResults.Unsupported,
                "The host has not registered INotionAggregateProvider.");
        }
        if (!Guid.TryParse(pageId, out var parsedPageId) || parsedPageId == Guid.Empty)
        {
            return McpToolResults.Failure(
                McpToolResults.ValidationFailed,
                "pageId must be a non-empty GUID string.");
        }

        var load = await provider.LoadPageAsync(parsedPageId, cancellationToken);
        if (!load.Found || load.Snapshot is null)
        {
            return McpToolResults.Failure(
                McpToolResults.NotFound,
                $"Notion page '{pageId}' not found.");
        }

        var snapshot = load.Snapshot;
        var validationIssues = NotionAggregateValidator.Validate([snapshot]);
        var allIssues = load.Issues.Concat(validationIssues).ToList();
        if (allIssues.Any(issue => issue.Severity == NotionIssueSeverity.Error))
        {
            return McpToolResults.Failure(
                McpToolResults.ValidationFailed,
                "The loaded Notion aggregate is not canonical.",
                allIssues.Select(FormatIssue));
        }

        var childrenByParent = snapshot.Blocks
            .Where(block => block.ParentBlockId is not null)
            .GroupBy(block => block.ParentBlockId!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<NotionBlockSnapshot>)group
                    .OrderBy(block => block.Order)
                    .ThenBy(block => block.Id)
                    .ToList());
        var roots = snapshot.Blocks
            .Where(block => block.ParentBlockId is null)
            .OrderBy(block => block.Order)
            .ThenBy(block => block.Id)
            .ToList();
        var visiting = new HashSet<Guid>();
        var tree = roots.Select(block =>
            BuildReadNode(block, childrenByParent, visiting)).ToList();
        return McpToolResults.Success(new
        {
            schemaVersion = snapshot.SchemaVersion,
            page = snapshot.Page,
            concurrencyToken = snapshot.ConcurrencyToken,
            digest = snapshot.Digest,
            totalCount = snapshot.Blocks.Count,
            blocks = tree,
            issues = allIssues
        });
    }

    [McpServerTool(Name = "notion_apply_block_operations")]
    [Description("Atomically apply a strict JSON operation array. Supported op values: createBlock, createBlocks, createTable, patchBlockContent, moveBlock, reorderBlocks, convertBlockType, deleteBlock, replaceBlocks. Every request requires a stable idempotencyKey. Legacy aliases and unknown fields are rejected.")]
    public static async Task<string> ApplyBlockOperations(
        IServiceProvider services,
        INotionAggregateProvider? provider,
        [Description("Stable idempotency key for this logical request. Reusing it with the same canonical request replays the original result; a different request is rejected.")] string idempotencyKey,
        [Description("Strict JSON array of operations. Each item uses the 'op' discriminator and may include clientRef for created/updated/deleted ID mapping.")] string operationsJson,
        [Description("Optional JSON array of {pageId, concurrencyToken} values from the latest aggregate read. Stale tokens reject the whole request before application.")] string expectedPageVersionsJson = "[]",
        CancellationToken cancellationToken = default)
    {
        if (provider is null)
        {
            return McpToolResults.Failure(
                McpToolResults.Unsupported,
                "The host has not registered INotionAggregateProvider.");
        }
        var receipts = services.GetService<InMemoryNotionIdempotencyReceiptStore>();
        if (receipts is null && provider is not INotionIdempotentAggregateProvider)
        {
            return McpToolResults.Failure(
                McpToolResults.Unsupported,
                "The host must call AddTempoNotionMcpTools to register the fallback authoring " +
                "runtime or implement INotionIdempotentAggregateProvider.");
        }

        if (!TryParseExpectedVersions(
                expectedPageVersionsJson,
                out var expectedVersions,
                out var parseIssue))
        {
            return Serialize(new NotionAtomicAuthoringResult
            {
                Errors = [parseIssue!]
            });
        }

        var engine = new NotionAtomicAuthoringEngine(
            provider,
            new NotionStrictOperationCompiler(),
            receipts);
        var result = await engine.ExecuteAsync(
            new NotionAtomicAuthoringRequest
            {
                IdempotencyKey = idempotencyKey,
                OperationsJson = operationsJson,
                ExpectedPageVersions = expectedVersions
            },
            cancellationToken);
        return Serialize(result);
    }

    private static bool TryParseExpectedVersions(
        string json,
        out IReadOnlyList<NotionExpectedPageVersion> versions,
        out NotionAggregateIssue? issue)
    {
        versions = [];
        issue = null;
        JsonArray? array;
        try
        {
            array = JsonNode.Parse(json) as JsonArray;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            array = null;
        }
        if (array is null)
        {
            issue = Error(
                "expected_versions_invalid",
                "expectedPageVersionsJson must contain a JSON array.",
                "$.expectedPageVersions");
            return false;
        }

        var parsed = new List<NotionExpectedPageVersion>();
        for (var index = 0; index < array.Count; index++)
        {
            var path = $"$.expectedPageVersions[{index}]";
            if (array[index] is not JsonObject item)
            {
                issue = Error(
                    "expected_version_must_be_object",
                    "Each expected page version must be an object.",
                    path);
                return false;
            }
            var unknown = item.Select(property => property.Key)
                .FirstOrDefault(name => name is not ("pageId" or "concurrencyToken"));
            if (unknown is not null)
            {
                issue = Error(
                    "unknown_field",
                    $"Unknown field '{unknown}'.",
                    $"{path}.{unknown}");
                return false;
            }
            if (item["pageId"] is not JsonValue pageValue ||
                !pageValue.TryGetValue<string>(out var pageText) ||
                !Guid.TryParse(pageText, out var pageId) ||
                pageId == Guid.Empty)
            {
                issue = Error(
                    "guid_required",
                    "pageId must be a non-empty GUID string.",
                    $"{path}.pageId");
                return false;
            }
            if (item["concurrencyToken"] is not JsonValue tokenValue ||
                !tokenValue.TryGetValue<string>(out var token) ||
                string.IsNullOrWhiteSpace(token))
            {
                issue = Error(
                    "concurrency_token_required",
                    "concurrencyToken must be a non-empty string.",
                    $"{path}.concurrencyToken");
                return false;
            }

            parsed.Add(new NotionExpectedPageVersion(pageId, token));
        }

        versions = parsed;
        return true;
    }

    private static string Serialize(NotionAtomicAuthoringResult result)
        => JsonSerializer.Serialize(result, McpJson.Options);

    private static NotionAggregateIssue Error(string code, string message, string path)
        => new()
        {
            Code = code,
            Severity = NotionIssueSeverity.Error,
            Message = message,
            Path = path
        };

    private static JsonObject BuildReadNode(
        NotionBlockSnapshot block,
        IReadOnlyDictionary<Guid, IReadOnlyList<NotionBlockSnapshot>> childrenByParent,
        HashSet<Guid> visiting)
    {
        if (!visiting.Add(block.Id))
        {
            throw new InvalidDataException(
                $"Canonical Notion aggregate contains a cycle at block '{block.Id}'.");
        }

        try
        {
            var children = childrenByParent.GetValueOrDefault(block.Id, []);
            var content = JsonNode.Parse(block.Content.GetRawText()) as JsonObject
                ?? throw new InvalidDataException(
                    $"Canonical content for block '{block.Id}' is not a JSON object.");
            var node = new JsonObject
            {
                ["id"] = block.Id,
                ["pageId"] = block.PageId,
                ["parentBlockId"] = block.ParentBlockId,
                ["type"] = JsonNamingPolicy.CamelCase.ConvertName(block.Type.ToString()),
                ["order"] = block.Order,
                ["content"] = content,
                ["createdAt"] = block.CreatedAt,
                ["lastEditedAt"] = block.LastEditedAt
            };

            if (block.Type == BlockType.Table)
            {
                var rows = new JsonArray();
                foreach (var rowBlock in children.Where(
                             child => child.Type == BlockType.TableRow))
                {
                    var row = rowBlock.Content.Deserialize<NotionAuthoringTableRow>(
                        NotionAggregateJson.Options) ?? new NotionAuthoringTableRow();
                    rows.Add(new JsonObject
                    {
                        ["id"] = rowBlock.Id,
                        ["order"] = rowBlock.Order,
                        ["cells"] = JsonSerializer.SerializeToNode(
                            row.Cells,
                            NotionAggregateJson.Options)
                    });
                }

                content["rows"] = rows;
                children = children.Where(
                        child => child.Type != BlockType.TableRow)
                    .ToList();
            }

            node["children"] = new JsonArray(
                children.Select(child =>
                        (JsonNode)BuildReadNode(child, childrenByParent, visiting))
                    .ToArray());
            return node;
        }
        finally
        {
            visiting.Remove(block.Id);
        }
    }

    private static string FormatIssue(NotionAggregateIssue issue)
    {
        var suggestedFix = string.IsNullOrWhiteSpace(issue.SuggestedFix)
            ? string.Empty
            : $" Suggested fix: {issue.SuggestedFix}";
        return $"{issue.Code} at {issue.Path}: {issue.Message}{suggestedFix}";
    }
}
