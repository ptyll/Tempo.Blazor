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
    [Description("Apply an ordered batch of edit operations to a modeling model and save it (atomic: one invalid operation saves nothing). operationsJson is a JSON array; each item has an 'op' field: add_element (name, semanticType, optional id/notation/description/parentId/properties), update_element (id + fields to change; parentId=\"\" clears the parent; optional properties/removedProperties), delete_element (id — referencing relationships AND child elements must be deleted or reparented in the same batch), add_relationship (relationshipType, sourceElementId, targetElementId, optional id/name/properties), update_relationship (id + fields; optional properties/removedProperties), delete_relationship (id), set_properties (id of an element OR relationship + a properties object, optional removedProperties), set_view_node (elementId + x/y for a new node, optional viewId/x/y/width/height/parentNodeId where parentNodeId=\"\" clears it), remove_view_node (elementId, optional viewId), set_view_connection (relationshipId + waypoints [{x,y}]; [] clears, optional viewId), remove_view_connection (relationshipId, optional viewId). viewId omitted/empty = the target view (Views[0], created on demand). View operations are layout changes and run under the SAME policy as the editor's save (view operations allowed; removals are still never cascaded). 'properties' is a JSON object of values and 'removedProperties' a JSON array of keys; both are optional on the four add/update operations AND together form set_properties. parentId must resolve to an existing element and must not close a cycle (validated per operation). Relationships are validated against the model's notation rules; an invalid operation fails the WHOLE batch (nothing is saved) with a per-operation explanation. After a parent change the batch re-validates every incident relationship too (the editor's save does the same), so a lane move that invalidates a flow fails the batch. Pass expectedModifiedAt from modeling_get_model_tree for optimistic concurrency. When the host registers an IModelingPropertySchemaProvider the batch also validates every property payload against the SAME schema and validator the editor uses: a new element/relationship is judged in full (a missing required field fails with property_required at its own operation index), an update only for the keys it touches. Without such a provider nothing about property values is validated. The success payload carries the applied diff (added/removed/changed/layout-only element and relationship ids).")]
    public static async Task<string> ApplyOperations(
        ITempoDocumentLibraryProvider library,
        IModelingModelDocumentProvider models,
        [Description("Modeling model id (GUID).")] Guid modelId,
        [Description("JSON array of operations.")] string operationsJson,
        IModelingRelationshipRulesProvider? relationshipRules = null,
        [Description("Optional optimistic-concurrency token from modeling_get_model_tree.")] DateTime? expectedModifiedAt = null,
        IEnumerable<IModelingPropertySchemaProvider>? propertySchemaProviders = null,
        [Description("Optional provider key of the property-schema provider to use when several are registered.")] string? propertySchemaProviderKey = null,
        IModelingViewpointRulesProvider? viewpointRules = null)
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

        // ONE engine, shared with the editor (decision MODELING-UNIFIED-OPERATION-ENGINE): the MCP
        // tool is a caller that loads, applies atomically to a clone and persists.
        var working = ModelingOperationEngine.Clone(current);

        // Phase 5 review (A3): when the host registered a property-schema provider, preload the
        // schemas the batch can need and validate every property payload with the SAME shared
        // validator the editor uses ("one semantics, same errors"). Without a provider the resolver
        // stays null and the batch behaves EXACTLY as it did in 2.10.0.
        var schemaProvider = ModelingSchemaProviders.Resolve(propertySchemaProviders, propertySchemaProviderKey);
        var resolver = await ModelingPropertySchemaResolver.PreloadAsync(
            schemaProvider,
            working,
            ModelingPropertySchemaResolver.PropertySchemaPairsFromJson(working, operationsJson),
            working.Views.FirstOrDefault()?.ViewpointKey,
            System.Globalization.CultureInfo.CurrentUICulture.Name);

        var result = ModelingOperationEngine.ApplyJson(
            working,
            operationsJson,
            new ModelingOperationContext
            {
                RelationshipRules = relationshipRules,
                ViewpointRules = viewpointRules,
                ResolvePropertySchema = resolver,
                // Phase 7 (docs/modeling-authoring.md §6.2/§6.4): the MCP batch now runs the SAME
                // policy the editor's save runs — view (layout) operations are allowed and a parent
                // change re-validates the relationships incident to it — so the two surfaces share
                // ONE semantics and the SAME error list ("one semantics, same errors").
                Policy = ModelingOperationPolicy.Authoring
            });
        if (!result.Success)
        {
            return McpToolResults.Failure(
                McpToolResults.ValidationFailed,
                "One or more operations are invalid; nothing was saved.",
                result.Errors.Select(error => error.Message).ToList());
        }

        await models.SaveModelingModelDocumentAsync(modelId, working);
        var saved = await library.GetEntryAsync(TempoDocumentKind.Modeling, modelId);

        // Phase 7: the headless diff of what this batch actually changed. Computed in Abstractions
        // (ModelingModelDiff), so a host renders the same overlay without the Blazor package; the
        // baseline model itself is not echoed (the ids are enough for an agent's next read).
        var diff = ModelingModelDiff.Compute(current, working);

        return McpToolResults.Success(new
        {
            id = modelId,
            applied = result.Applied,
            createdIds = result.CreatedIds,
            elementCount = working.Elements.Count,
            relationshipCount = working.Relationships.Count,
            modifiedAt = saved?.ModifiedAt,
            diff = new
            {
                addedElementIds = diff.AddedElementIds,
                removedElementIds = diff.RemovedElementIds,
                changedElementIds = diff.ChangedElementIds,
                layoutOnlyElementIds = diff.LayoutOnlyElementIds,
                addedRelationshipIds = diff.AddedRelationshipIds,
                removedRelationshipIds = diff.RemovedRelationshipIds,
                changedRelationshipIds = diff.ChangedRelationshipIds,
                counts = new
                {
                    addedElements = diff.AddedElementIds.Count,
                    removedElements = diff.RemovedElementIds.Count,
                    changedElements = diff.ChangedElementIds.Count,
                    layoutOnlyElements = diff.LayoutOnlyElementIds.Count,
                    addedRelationships = diff.AddedRelationshipIds.Count,
                    removedRelationships = diff.RemovedRelationshipIds.Count,
                    changedRelationships = diff.ChangedRelationshipIds.Count
                }
            }
        });
    }
}
