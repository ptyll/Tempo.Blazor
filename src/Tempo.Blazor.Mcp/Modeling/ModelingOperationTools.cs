using System.ComponentModel;
using ModelContextProtocol.Server;
using Tempo.Blazor.DocumentLibrary;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Mcp.Modeling;

/// <summary>MCP write tools for architecture/modeling models.</summary>
[McpServerToolType]
public static class ModelingOperationTools
{
    [McpServerTool(Name = "modeling_apply_operations")]
    [Description("Apply an ordered batch of edit operations to a modeling model and save it. operationsJson is a JSON array; each item has an 'op' field: add_element (name, semanticType, optional id/notation/description/parentId), update_element (id + fields to change; parentId=\"\" clears the parent), delete_element (id — referencing relationships AND child elements must be deleted or reparented in the same batch), add_relationship (relationshipType, sourceElementId, targetElementId, optional id/name), update_relationship (id + fields), delete_relationship (id). parentId must resolve to an existing element and must not close a cycle (validated per operation). Relationships are validated against the model's notation rules; an invalid operation fails the WHOLE batch (nothing is saved) with a per-operation explanation. Pass expectedModifiedAt from modeling_get_model_tree for optimistic concurrency.")]
    public static async Task<string> ApplyOperations(
        ITempoDocumentLibraryProvider library,
        IModelingModelDocumentProvider models,
        [Description("Modeling model id (GUID).")] Guid modelId,
        [Description("JSON array of operations.")] string operationsJson,
        IModelingRelationshipRulesProvider? relationshipRules = null,
        [Description("Optional optimistic-concurrency token from modeling_get_model_tree.")] DateTime? expectedModifiedAt = null)
    {
        var entry = await library.GetEntryAsync(TempoDocumentKind.Modeling, modelId);
        if (entry is null)
        {
            return McpToolResults.Failure(McpToolResults.NotFound, $"Model {modelId} not found.");
        }
        if (McpConcurrency.DateTimeConflict(expectedModifiedAt, entry.ModifiedAt, "modeling_get_model_tree") is { } conflict)
        {
            return McpToolResults.Failure(McpToolResults.Conflict, conflict);
        }

        var current = await models.GetModelingModelDocumentAsync(modelId);
        if (current is null)
        {
            return McpToolResults.Failure(McpToolResults.NotFound, $"Model {modelId} not found.");
        }

        var working = McpJsonHelpers.Clone(current, McpJson.Options);
        var result = ModelingOperationEngine.Apply(working, operationsJson, relationshipRules);
        if (!result.Success)
        {
            return McpToolResults.Failure(McpToolResults.ValidationFailed, "One or more operations are invalid; nothing was saved.", result.Errors);
        }

        await models.SaveModelingModelDocumentAsync(modelId, working);
        var saved = await library.GetEntryAsync(TempoDocumentKind.Modeling, modelId);

        return McpToolResults.Success(new
        {
            id = modelId,
            applied = result.Applied,
            createdIds = result.CreatedIds,
            elementCount = working.Elements.Count,
            relationshipCount = working.Relationships.Count,
            modifiedAt = saved?.ModifiedAt
        });
    }
}
