using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Tempo.Blazor.Components.Diagram.Serialization;
using Tempo.Blazor.DocumentLibrary;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Mcp.Modeling;

/// <summary>MCP tools for listing and reading stored architecture/modeling models.</summary>
[McpServerToolType]
public static class ModelingModelTools
{
    [McpServerTool(Name = "modeling_list_models")]
    [Description("List stored architecture/modeling models (id, name, folder, last-modified). Filter by folderPath or search. Use the id with modeling_get_model_tree.")]
    public static async Task<string> ListModels(
        ITempoDocumentLibraryProvider library,
        [Description("Optional folder to list (e.g. '/Architecture'). Omit for the root.")] string? folderPath = null,
        [Description("Optional free-text search across model names.")] string? search = null,
        [Description("Pagination offset.")] int skip = 0,
        [Description("Maximum number of models to return (default 50).")] int take = 50,
        [Description("Optional app id (GUID) scoping the listing; required when the API key grants access to more than one app.")] string? scopeAppId = null)
    {
        var page = await library.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Modeling,
            FolderPath = folderPath,
            Search = search,
            Skip = Math.Max(0, skip),
            Take = Math.Clamp(take, 1, 500),
            ScopeAppId = scopeAppId
        });

        return McpToolResults.Success(new
        {
            totalCount = page.TotalCount,
            items = page.Items.Select(i => new
            {
                id = i.Id,
                name = i.Name,
                folderPath = i.FolderPath,
                modifiedAt = i.ModifiedAt
            }).ToList()
        });
    }

    [McpServerTool(Name = "modeling_get_model_tree")]
    [Description("Get an architecture/modeling model by id as a structured tree: notation, elements (id, notation, semanticType, name, description, parentId — the model tree hierarchy, empty at the roots), relationships (id, type, source→target), views, counts, existing issues and concurrencyToken (pass as expectedModifiedAt to modeling_apply_operations). Returns not_found if missing.")]
    public static async Task<string> GetModelTree(
        ITempoDocumentLibraryProvider library,
        IModelingModelDocumentProvider models,
        [Description("Modeling model id (GUID).")] Guid modelId)
    {
        var entry = await library.GetEntryAsync(TempoDocumentKind.Modeling, modelId);
        if (entry is null)
        {
            return McpToolResults.Failure(McpToolResults.NotFound, $"Model {modelId} not found.");
        }

        var model = await models.GetModelingModelDocumentAsync(modelId);
        if (model is null)
        {
            return McpToolResults.Failure(McpToolResults.NotFound, $"Model {modelId} not found.");
        }

        return McpToolResults.Success(new
        {
            id = modelId,
            title = model.Title,
            notation = model.Notation,
            supportedNotations = model.SupportedNotations,
            concurrencyToken = entry.ModifiedAt,
            counts = new
            {
                elements = model.Elements.Count,
                relationships = model.Relationships.Count,
                views = model.Views.Count
            },
            elements = model.Elements.Select(e => new
            {
                id = e.Id,
                notation = e.Notation,
                semanticType = e.SemanticType,
                name = e.Name,
                description = e.Description,
                parentId = e.ParentId,
                tags = e.Tags
            }).ToList(),
            relationships = model.Relationships.Select(r => new
            {
                id = r.Id,
                relationshipType = r.RelationshipType,
                sourceElementId = r.SourceElementId,
                targetElementId = r.TargetElementId,
                name = r.Name
            }).ToList(),
            views = model.Views.Select(v => new
            {
                id = v.Id,
                name = v.Name,
                viewpointKey = v.ViewpointKey
            }).ToList(),
            issues = model.Issues
        });
    }

    [McpServerTool(Name = "modeling_get_view")]
    [Description("Project a model view/viewpoint into a diagram document (DiagramDocument JSON) usable with diagram_render_svg. Optionally target a specific viewId and/or viewpointKey. Requires the host to register the modeling diagram projector; returns 'unsupported' when it is not available.")]
    public static async Task<string> GetView(
        ITempoDocumentLibraryProvider library,
        IModelingModelDocumentProvider models,
        [Description("Modeling model id (GUID).")] Guid modelId,
        IModelingDiagramProjector? projector = null,
        [Description("Optional view id to render. Omit for the default/first view.")] string? viewId = null,
        [Description("Optional viewpoint key used to filter and arrange the view.")] string? viewpointKey = null)
    {
        var entry = await library.GetEntryAsync(TempoDocumentKind.Modeling, modelId);
        if (entry is null)
        {
            return McpToolResults.Failure(McpToolResults.NotFound, $"Model {modelId} not found.");
        }
        if (projector is null)
        {
            return McpToolResults.Failure(McpToolResults.Unsupported, "No modeling diagram projector is registered on the host.");
        }

        var model = await models.GetModelingModelDocumentAsync(modelId);
        if (model is null)
        {
            return McpToolResults.Failure(McpToolResults.NotFound, $"Model {modelId} not found.");
        }

        var generation = projector.Generate(model, new ModelingDiagramGenerationOptionsDto
        {
            ViewId = viewId ?? string.Empty,
            ViewpointKey = viewpointKey ?? string.Empty,
            IncludeIssues = true
        });

        return McpToolResults.Success(new
        {
            id = modelId,
            concurrencyToken = entry.ModifiedAt,
            generatedAt = generation.GeneratedAt,
            issues = generation.Issues,
            document = generation.Document is null
                ? null
                : JsonSerializer.SerializeToNode(generation.Document, DiagramJsonOptions.Default)
        });
    }
}
