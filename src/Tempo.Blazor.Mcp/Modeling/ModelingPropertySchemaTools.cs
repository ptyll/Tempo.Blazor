using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Server;
using Tempo.Blazor.DocumentLibrary;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Mcp.Modeling;

/// <summary>
/// Read tool for the host's property schema (phase 7): the sections and typed fields the modeling
/// inspector renders for a notation + semantic type, resolved through the SAME
/// <see cref="IModelingPropertySchemaProvider"/> and request shape
/// <c>modeling_apply_operations</c> validates with, so an agent can discover the required fields
/// (and the lookup descriptors) BEFORE it writes a batch.
/// </summary>
[McpServerToolType]
public static class ModelingPropertySchemaTools
{
    [McpServerTool(Name = "modeling_get_property_schema")]
    [Description("Read the host's property schema for one modeling notation + semantic type (or relationship type): ordered sections with typed fields (key, label, type, required, options, constraints, lookup descriptor). Resolve the target from a stored model with modelId + elementId (or relationshipId), or pass notation + semanticType directly. Returns 'unsupported' when the host registered no IModelingPropertySchemaProvider; hasSchema=false when the host has no schema for that type (the 2.10.0 scalar-only inspector). The SAME provider and request shape judge modeling_apply_operations, so the fields reported here are the fields a batch is validated against.")]
    public static async Task<string> GetPropertySchema(
        ITempoDocumentLibraryProvider library,
        IModelingModelDocumentProvider models,
        IEnumerable<IModelingPropertySchemaProvider>? propertySchemaProviders = null,
        [Description("Optional provider key of the property-schema provider to use when several are registered.")] string? propertySchemaProviderKey = null,
        [Description("Optional modeling model id (GUID); required to resolve elementId/relationshipId and to report the model's own viewpoint.")] Guid? modelId = null,
        [Description("Optional element id (requires modelId): its notation and semantic type are used unless overridden.")] string? elementId = null,
        [Description("Optional relationship id (requires modelId): its relationship type is the semantic type.")] string? relationshipId = null,
        [Description("Notation key (e.g. 'bpmn2'); defaults to the model's or the element's notation.")] string? notation = null,
        [Description("Semantic element type, or the relationship type for a relationship.")] string? semanticType = null,
        [Description("Optional viewpoint key; defaults to the model's first view viewpoint.")] string? viewpointKey = null,
        [Description("Optional UI culture name the labels are requested in; defaults to the server's current UI culture.")] string? culture = null,
        CancellationToken cancellationToken = default)
    {
        var provider = ModelingSchemaProviders.Resolve(propertySchemaProviders, propertySchemaProviderKey);
        if (provider is null)
        {
            return McpToolResults.Failure(
                McpToolResults.Unsupported,
                "No modeling property schema provider is registered on the host.");
        }

        ModelingModelDto? model = null;
        if (modelId is { } id)
        {
            var entry = await library.GetEntryAsync(TempoDocumentKind.Modeling, id, cancellationToken);
            if (entry is null)
            {
                return McpToolResults.Failure(McpToolResults.NotFound, $"Model {id} not found.");
            }

            model = await models.GetModelingModelDocumentAsync(id);
            if (model is null)
            {
                return McpToolResults.Failure(McpToolResults.NotFound, $"Model {id} not found.");
            }
        }

        var effectiveNotation = notation;
        var effectiveType = semanticType;

        if (!string.IsNullOrWhiteSpace(elementId))
        {
            if (model is null)
            {
                return McpToolResults.Failure(
                    McpToolResults.ValidationFailed,
                    "elementId requires modelId so the element can be resolved.");
            }

            var element = model.Elements.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
            if (element is null)
            {
                return McpToolResults.Failure(McpToolResults.NotFound, $"Element '{elementId}' not found.");
            }

            effectiveNotation ??= string.IsNullOrWhiteSpace(element.Notation) ? model.Notation : element.Notation;
            effectiveType ??= element.SemanticType;
        }
        else if (!string.IsNullOrWhiteSpace(relationshipId))
        {
            if (model is null)
            {
                return McpToolResults.Failure(
                    McpToolResults.ValidationFailed,
                    "relationshipId requires modelId so the relationship can be resolved.");
            }

            var relationship = model.Relationships.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, relationshipId, StringComparison.Ordinal));
            if (relationship is null)
            {
                return McpToolResults.Failure(
                    McpToolResults.NotFound,
                    $"Relationship '{relationshipId}' not found.");
            }

            effectiveNotation ??= model.Notation;
            effectiveType ??= relationship.RelationshipType;
        }

        if (string.IsNullOrWhiteSpace(effectiveType))
        {
            return McpToolResults.Failure(
                McpToolResults.ValidationFailed,
                "semanticType is required (pass semanticType directly, or modelId with elementId/relationshipId).");
        }

        var effectiveViewpoint = viewpointKey
            ?? model?.Views.FirstOrDefault()?.ViewpointKey
            ?? string.Empty;
        var effectiveCulture = string.IsNullOrWhiteSpace(culture)
            ? CultureInfo.CurrentUICulture.Name
            : culture!;
        var request = new ModelingPropertySchemaRequest
        {
            ModelId = modelId?.ToString() ?? string.Empty,
            Notation = effectiveNotation ?? model?.Notation ?? string.Empty,
            SemanticType = effectiveType,
            ViewpointKey = effectiveViewpoint,
            Culture = effectiveCulture
        };

        ModelingPropertySchemaDto? schema;
        try
        {
            schema = await provider.GetSchemaAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return McpToolResults.Failure(
                McpToolResults.Error,
                $"The property schema provider '{provider.ProviderKey}' failed: {ex.Message}");
        }

        return McpToolResults.Success(new
        {
            providerKey = provider.ProviderKey,
            modelId,
            notation = request.Notation,
            semanticType = request.SemanticType,
            viewpointKey = request.ViewpointKey,
            culture = request.Culture,
            hasSchema = schema is not null,
            schema = schema is null
                ? null
                : JsonSerializer.SerializeToNode(schema, McpJson.Options)
        });
    }
}
