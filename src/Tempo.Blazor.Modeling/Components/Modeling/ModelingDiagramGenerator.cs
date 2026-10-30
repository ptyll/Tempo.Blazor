using System.Text.Json;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>Generates diagram documents from source-backed modeling DTOs.</summary>
public sealed class ModelingDiagramGenerator : IModelingDiagramProjector
{
    private const string DefaultNodeStencilId = "general.rectangle";
    private const string DefaultEdgeStencilId = "relationships.association";
    private const string DefaultLayerId = "modeling-default";
    private const int GridColumns = 5;
    private const double GridOriginX = 80;
    private const double GridOriginY = 80;
    private const double GridStepX = 220;
    private const double GridStepY = 150;

    private readonly IModelingStencilMapper _stencilMapper;
    private readonly DiagramStencilRegistry _stencilRegistry;
    private readonly IModelingNotationProfileProvider? _notationProfiles;
    private readonly IModelingRelationshipRulesProvider? _relationshipRules;
    private readonly IModelingViewpointRulesProvider? _viewpointRules;

    /// <summary>Creates a modeling diagram generator.</summary>
    /// <param name="stencilMapper">Maps semantic modeling types to diagram stencils.</param>
    /// <param name="stencilRegistry">Diagram stencil registry used to validate and apply mapped stencils.</param>
    /// <param name="notationProfiles">Optional notation profile lookup used for notation-level generation policy.</param>
    /// <param name="relationshipRules">Optional notation-specific relationship rules.</param>
    /// <param name="viewpointRules">Optional notation-specific viewpoint rules.</param>
    public ModelingDiagramGenerator(
        IModelingStencilMapper stencilMapper,
        DiagramStencilRegistry stencilRegistry,
        IModelingNotationProfileProvider? notationProfiles = null,
        IModelingRelationshipRulesProvider? relationshipRules = null,
        IModelingViewpointRulesProvider? viewpointRules = null)
    {
        _stencilMapper = stencilMapper ?? throw new ArgumentNullException(nameof(stencilMapper));
        _stencilRegistry = stencilRegistry ?? throw new ArgumentNullException(nameof(stencilRegistry));
        _notationProfiles = notationProfiles;
        _relationshipRules = relationshipRules;
        _viewpointRules = viewpointRules;
    }

    /// <summary>Generates a diagram document from the supplied modeling model.</summary>
    /// <param name="model">Modeling model to project into a diagram.</param>
    /// <param name="options">Optional generation options.</param>
    /// <returns>Generation result containing the document and any non-blocking issues.</returns>
    public ModelingDiagramGenerationResultDto Generate(
        ModelingModelDto model,
        ModelingDiagramGenerationOptionsDto? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        var issues = new List<ModelingIssueDto>();
        var selectedView = SelectView(model, options, issues);
        var viewpointKey = selectedView?.ViewpointKey ?? options?.ViewpointKey ?? string.Empty;
        var viewNodes = CreateViewNodeMap(selectedView, issues);
        var page = CreatePage(selectedView);
        var generationTimestamp = GetGenerationTimestamp(model);
        var document = CreateDocument(model, page, generationTimestamp);
        var elementToNodeId = new Dictionary<string, string>(StringComparer.Ordinal);
        var elementById = new Dictionary<string, ModelingElementDto>(StringComparer.Ordinal);
        var usedNodeIds = new HashSet<string>(StringComparer.Ordinal);
        var elementOrder = BuildElementOrder(model);

        for (var index = 0; index < model.Elements.Count; index++)
        {
            var element = model.Elements[index];
            if (selectedView is not null && !viewNodes.ContainsKey(element.Id))
                continue;

            if (string.IsNullOrWhiteSpace(element.Id))
            {
                issues.Add(CreateElementIssue(
                    $"modeling-generator-element-{index}-empty-id",
                    element,
                    "validation",
                    "Modeling element has an empty Id and was skipped.",
                    "Provide a stable model element Id."));
                continue;
            }

            if (elementById.ContainsKey(element.Id))
            {
                issues.Add(CreateElementIssue(
                    $"modeling-generator-element-{index}-duplicate-id",
                    element,
                    "validation",
                    $"Duplicate modeling element Id '{element.Id}' was skipped.",
                    "Ensure each modeling element Id is unique within the model."));
                continue;
            }

            elementById[element.Id] = element;
            if (!ValidateElementViewpointRule(model, element, index, viewpointKey, issues))
                continue;

            var nodeId = GetStableNodeId(element);
            if (!usedNodeIds.Add(nodeId))
            {
                issues.Add(CreateElementIssue(
                    $"modeling-generator-element-{index}-duplicate-node-id",
                    element,
                    "validation",
                    $"Duplicate generated diagram node Id '{nodeId}' was skipped.",
                    "Use unique SourceId values or leave SourceId empty to fall back to the element Id."));
                continue;
            }

            var stencil = ResolveNodeStencil(model, element, index, issues);
            if (stencil is null)
                continue;

            viewNodes.TryGetValue(element.Id, out var viewNode);
            var position = viewNode is null ? GetGridPosition(page.Nodes.Count) : (viewNode.X, viewNode.Y);
            var node = new DiagramNode
            {
                Id = nodeId,
                StencilId = stencil.Id,
                X = position.X,
                Y = position.Y,
                W = viewNode?.Width > 0 ? viewNode.Width : stencil.DefaultWidth,
                H = viewNode?.Height > 0 ? viewNode.Height : stencil.DefaultHeight,
                ZIndex = elementOrder.GetValueOrDefault(element.Id, page.Nodes.Count),
                ParentNodeId = string.IsNullOrWhiteSpace(viewNode?.ParentNodeId) ? null : viewNode.ParentNodeId,
                LayerId = DefaultLayerId,
                IsCollapsible = stencil.IsCollapsible,
                Data = CreateNodeData(stencil, element)
            };

            page.Nodes.Add(node);
            elementToNodeId[element.Id] = node.Id;
        }

        var usedEdgeIds = new HashSet<string>(StringComparer.Ordinal);
        if (selectedView is null)
        {
            for (var index = 0; index < model.Relationships.Count; index++)
            {
                AddRelationshipEdge(
                    page,
                    model,
                    model.Relationships[index],
                    index,
                    null,
                    elementById,
                    elementToNodeId,
                    usedEdgeIds,
                    issues,
                    viewpointKey);
            }
        }
        else
        {
            var relationshipsById = model.Relationships
                .Select((relationship, index) => (relationship, index))
                .Where(entry => !string.IsNullOrWhiteSpace(entry.relationship.Id))
                .GroupBy(entry => entry.relationship.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            for (var index = 0; index < selectedView.Connections.Count; index++)
            {
                var connection = selectedView.Connections[index];
                if (!relationshipsById.TryGetValue(connection.RelationshipId, out var entry))
                {
                    issues.Add(CreateViewIssue(
                        $"modeling-generator-view-connection-{index}-missing-relationship",
                        ModelingIssueSeverity.Warning,
                        $"View connection references missing relationship '{connection.RelationshipId}' and was skipped.",
                        "Ensure each view connection references a relationship included in the model."));
                    continue;
                }

                AddRelationshipEdge(
                    page,
                    model,
                    entry.relationship,
                    entry.index,
                    connection,
                    elementById,
                    elementToNodeId,
                    usedEdgeIds,
                    issues,
                    viewpointKey);
            }
        }

        return new ModelingDiagramGenerationResultDto
        {
            Document = document,
            Issues = options?.IncludeIssues == false ? [] : issues,
            GeneratedAt = generationTimestamp
        };
    }

    /// <summary>Generates a diagram document asynchronously from the supplied modeling model.</summary>
    /// <param name="model">Modeling model to project into a diagram.</param>
    /// <param name="options">Optional generation options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generation result containing the document and any non-blocking issues.</returns>
    public Task<ModelingDiagramGenerationResultDto> GenerateAsync(
        ModelingModelDto model,
        ModelingDiagramGenerationOptionsDto? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Generate(model, options));
    }

    private static DiagramDocument CreateDocument(ModelingModelDto model, DiagramPage page, DateTimeOffset generationTimestamp)
        => new()
        {
            Id = string.IsNullOrWhiteSpace(model.Id) ? "modeling-document" : model.Id,
            Title = string.IsNullOrWhiteSpace(model.Title) ? "Modeling diagram" : model.Title,
            CreatedAt = generationTimestamp.UtcDateTime,
            ModifiedAt = generationTimestamp.UtcDateTime,
            Pages = [page],
            ActivePageIndex = 0
        };

    private static DiagramPage CreatePage(ModelingViewDto? view)
        => new()
        {
            Id = view is not null && !string.IsNullOrWhiteSpace(view.Id) ? view.Id : "modeling-page-default",
            Name = view is not null && !string.IsNullOrWhiteSpace(view.Name) ? view.Name : "Model",
            Layers =
            [
                new DiagramLayer
                {
                    Id = DefaultLayerId,
                    Name = "Model",
                    Order = 0
                }
            ]
        };

    private static DateTimeOffset GetGenerationTimestamp(ModelingModelDto model)
        => model.Metadata.LoadedAt == default ? DateTimeOffset.UnixEpoch : model.Metadata.LoadedAt.ToUniversalTime();

    private static ModelingViewDto? SelectView(
        ModelingModelDto model,
        ModelingDiagramGenerationOptionsDto? options,
        List<ModelingIssueDto> issues)
    {
        if (string.IsNullOrWhiteSpace(options?.ViewId))
            return null;

        var requestedViewId = options.ViewId.Trim();
        var view = model.Views.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, requestedViewId, StringComparison.Ordinal));

        if (view is not null)
            return view;

        issues.Add(CreateViewIssue(
            "modeling-generator-view-not-found",
            ModelingIssueSeverity.Info,
            $"Requested modeling view '{requestedViewId}' was not found. A default view was generated.",
            "Pass an existing ViewId or leave ViewId empty to use the default generated view."));
        return null;
    }

    private static Dictionary<string, ModelingViewNodeDto> CreateViewNodeMap(
        ModelingViewDto? view,
        List<ModelingIssueDto> issues)
    {
        var viewNodes = new Dictionary<string, ModelingViewNodeDto>(StringComparer.Ordinal);
        if (view is null)
            return viewNodes;

        for (var index = 0; index < view.Nodes.Count; index++)
        {
            var viewNode = view.Nodes[index];
            if (string.IsNullOrWhiteSpace(viewNode.ElementId))
            {
                issues.Add(CreateViewIssue(
                    $"modeling-generator-view-node-{index}-empty-element-id",
                    ModelingIssueSeverity.Warning,
                    "View node has an empty ElementId and was skipped.",
                    "Ensure each view node references a model element."));
                continue;
            }

            if (viewNodes.ContainsKey(viewNode.ElementId))
            {
                issues.Add(CreateViewIssue(
                    $"modeling-generator-view-node-{index}-duplicate-element-id",
                    ModelingIssueSeverity.Warning,
                    $"View contains duplicate node for element '{viewNode.ElementId}'. The first node was used.",
                    "Keep one view node per element in the selected view."));
                continue;
            }

            viewNodes[viewNode.ElementId] = viewNode;
        }

        return viewNodes;
    }

    private void AddRelationshipEdge(
        DiagramPage page,
        ModelingModelDto model,
        ModelingRelationshipDto relationship,
        int relationshipIndex,
        ModelingViewConnectionDto? viewConnection,
        IReadOnlyDictionary<string, ModelingElementDto> elementById,
        IReadOnlyDictionary<string, string> elementToNodeId,
        HashSet<string> usedEdgeIds,
        List<ModelingIssueDto> issues,
        string viewpointKey)
    {
        var edge = CreateEdge(model, relationship, relationshipIndex, elementById, elementToNodeId, issues, viewpointKey);
        if (edge is null)
            return;

        edge.Id = GetUniqueEdgeId(relationship, relationshipIndex, usedEdgeIds, issues);
        edge.Label = string.IsNullOrWhiteSpace(relationship.Name) ? null : relationship.Name;
        edge.ZIndex = model.Elements.Count + relationshipIndex;
        edge.LayerId = DefaultLayerId;

        if (viewConnection is not null && viewConnection.Waypoints.Count > 0)
        {
            edge.Waypoints = viewConnection.Waypoints
                .Select(waypoint => new DiagramPoint(waypoint.X, waypoint.Y))
                .ToList();
            edge.IsManuallyRouted = true;
        }

        page.Edges.Add(edge);
    }

    private DiagramStencil? ResolveNodeStencil(
        ModelingModelDto model,
        ModelingElementDto element,
        int elementIndex,
        List<ModelingIssueDto> issues)
    {
        var notationKey = GetNotationKey(model, element);
        var mappedStencilId = _stencilMapper.GetStencilId(notationKey, element.SemanticType);
        var mappedStencil = GetNodeStencil(mappedStencilId);
        if (mappedStencil is not null)
            return mappedStencil;

        if (UsesStrictNodeStencilMapping(notationKey))
        {
            issues.Add(CreateElementIssue(
                $"modeling-generator-element-{elementIndex}-unsupported-semantic-type",
                element,
                "mapping",
                $"No node stencil mapping was found for semantic type '{element.SemanticType}'. The element was skipped.",
                GetStrictNodeStencilMappingSuggestedFix(notationKey)));
            return null;
        }

        issues.Add(CreateElementIssue(
            $"modeling-generator-element-{elementIndex}-unsupported-semantic-type",
            element,
            "mapping",
            $"No node stencil mapping was found for semantic type '{element.SemanticType}'. A fallback stencil was used.",
            "Register an IModelingStencilMapper mapping for this semantic type."));

        return GetNodeStencil(DefaultNodeStencilId)
            ?? _stencilRegistry.GetAll().FirstOrDefault(stencil => stencil.Kind == DiagramStencilKind.Node)
            ?? new DiagramStencil { Id = DefaultNodeStencilId, Name = "Rectangle", Kind = DiagramStencilKind.Node };
    }

    private bool UsesStrictNodeStencilMapping(string notationKey)
        => _notationProfiles?.GetProfile(notationKey)?.EnforcesStrictStencilMapping == true;

    private static string GetStrictNodeStencilMappingSuggestedFix(string notationKey)
        => string.Equals(notationKey?.Trim(), ErdNotationProfile.Key, StringComparison.OrdinalIgnoreCase)
            ? "Register ERD diagram stencils and an IModelingStencilMapper mapping for this semantic type, or remove the element from the view."
            : "Add an ArchiMate 3.2 stencil mapping for this semantic type or remove the element from the view.";

    private DiagramEdge? CreateEdge(
        ModelingModelDto model,
        ModelingRelationshipDto relationship,
        int relationshipIndex,
        IReadOnlyDictionary<string, ModelingElementDto> elementById,
        IReadOnlyDictionary<string, string> elementToNodeId,
        List<ModelingIssueDto> issues,
        string viewpointKey)
    {
        if (!elementToNodeId.TryGetValue(relationship.SourceElementId, out var sourceNodeId)
            || !elementToNodeId.TryGetValue(relationship.TargetElementId, out var targetNodeId))
        {
            issues.Add(CreateRelationshipIssue(
                $"modeling-generator-relationship-{relationshipIndex}-missing-end",
                relationship,
                "validation",
                $"Relationship '{GetRelationshipDisplayId(relationship, relationshipIndex)}' references a missing source or target element and was skipped.",
                "Ensure SourceElementId and TargetElementId refer to elements included in the model."));
            return null;
        }

        if (string.Equals(sourceNodeId, targetNodeId, StringComparison.Ordinal))
        {
            issues.Add(CreateRelationshipIssue(
                $"modeling-generator-relationship-{relationshipIndex}-self-reference",
                relationship,
                "validation",
                $"Relationship '{GetRelationshipDisplayId(relationship, relationshipIndex)}' is self-referential and was skipped.",
                "Model self-references explicitly only when the editor supports them for this notation."));
            return null;
        }

        var notationKey = GetRelationshipNotationKey(model, relationship, elementById);
        var relationshipRuleResult = ValidateRelationshipRule(notationKey, relationship, elementById, viewpointKey);
        if (!relationshipRuleResult.IsValid)
        {
            issues.Add(CreateRelationshipIssue(
                $"modeling-generator-relationship-{relationshipIndex}-invalid-relationship-rule",
                relationship,
                "validation",
                string.IsNullOrWhiteSpace(relationshipRuleResult.Message)
                    ? $"Relationship '{GetRelationshipDisplayId(relationship, relationshipIndex)}' is not valid for notation '{notationKey}' and was skipped."
                    : relationshipRuleResult.Message,
                string.IsNullOrWhiteSpace(relationshipRuleResult.SuggestedFix)
                    ? "Use a relationship allowed by the selected notation profile."
                    : relationshipRuleResult.SuggestedFix));
            return null;
        }

        var mappedStencilId = _stencilMapper.GetEdgeStencilId(notationKey, relationship.RelationshipType);
        var mappedStencil = GetEdgeStencil(mappedStencilId);
        if (mappedStencil is not null)
            return DiagramEdgeStencilFactory.CreateEdge(mappedStencil, sourceNodeId, targetNodeId: targetNodeId);

        issues.Add(CreateRelationshipIssue(
            $"modeling-generator-relationship-{relationshipIndex}-unsupported-relationship-type",
            relationship,
            "mapping",
            $"No edge stencil mapping was found for relationship type '{relationship.RelationshipType}'. Association was used.",
            "Register an IModelingStencilMapper mapping for this relationship type."));

        var fallbackStencil = GetEdgeStencil(DefaultEdgeStencilId);
        return fallbackStencil is null
            ? new DiagramEdge
            {
                SourceNodeId = sourceNodeId,
                TargetNodeId = targetNodeId,
                ConnectorType = "association",
                Routing = "straight",
                EndArrow = "none"
            }
            : DiagramEdgeStencilFactory.CreateEdge(fallbackStencil, sourceNodeId, targetNodeId: targetNodeId);
    }

    private DiagramStencil? GetNodeStencil(string? stencilId)
    {
        if (string.IsNullOrWhiteSpace(stencilId))
            return null;

        var stencil = _stencilRegistry.GetStencil(stencilId);
        return stencil?.Kind == DiagramStencilKind.Node ? stencil : null;
    }

    private DiagramStencil? GetEdgeStencil(string? stencilId)
    {
        if (string.IsNullOrWhiteSpace(stencilId))
            return null;

        var stencil = _stencilRegistry.GetStencil(stencilId);
        return stencil?.Kind == DiagramStencilKind.Edge ? stencil : null;
    }

    private ModelingRelationshipRuleResult ValidateRelationshipRule(
        string notationKey,
        ModelingRelationshipDto relationship,
        IReadOnlyDictionary<string, ModelingElementDto> elementById,
        string viewpointKey)
    {
        if (_relationshipRules is null)
            return ModelingRelationshipRuleResult.Valid;

        if (!elementById.TryGetValue(relationship.SourceElementId, out var source)
            || !elementById.TryGetValue(relationship.TargetElementId, out var target))
        {
            return ModelingRelationshipRuleResult.Valid;
        }

        if (!string.IsNullOrWhiteSpace(source.Notation)
            && !string.IsNullOrWhiteSpace(target.Notation)
            && !string.Equals(source.Notation, target.Notation, StringComparison.OrdinalIgnoreCase))
        {
            return ModelingRelationshipRuleResult.Valid;
        }

        return _relationshipRules.ValidateRelationship(new ModelingRelationshipRuleContext
        {
            NotationKey = notationKey,
            ViewpointKey = viewpointKey,
            Relationship = relationship,
            SourceElement = source,
            TargetElement = target,
            ElementsById = elementById
        });
    }

    private static Dictionary<string, object> CreateNodeData(DiagramStencil stencil, ModelingElementDto element)
    {
        var data = new Dictionary<string, object>(stencil.DefaultData, StringComparer.Ordinal)
        {
            ["name"] = element.Name,
            ["label"] = element.Name,
            ["modelElementId"] = element.Id,
            ["sourceId"] = element.SourceId,
            ["sourceType"] = element.SourceType,
            ["sourcePath"] = element.SourcePath,
            ["notation"] = element.Notation,
            ["semanticType"] = element.SemanticType
        };

        if (!string.IsNullOrWhiteSpace(element.Description))
            data["description"] = element.Description;

        foreach (var property in element.Properties)
        {
            if (string.IsNullOrWhiteSpace(property.Key))
                continue;

            data[property.Key] = ConvertJsonElement(property.Value);
        }

        return data;
    }

    private static object ConvertJsonElement(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Select(ConvertJsonElement).ToArray(),
            JsonValueKind.Object => value.Clone(),
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when value.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            _ => value.ToString()
        };

    private bool ValidateElementViewpointRule(
        ModelingModelDto model,
        ModelingElementDto element,
        int elementIndex,
        string viewpointKey,
        List<ModelingIssueDto> issues)
    {
        var notationKey = GetNotationKey(model, element);
        if (_viewpointRules is null || string.IsNullOrWhiteSpace(viewpointKey))
            return true;

        var ruleResult = _viewpointRules.ValidateElementViewpoint(new ModelingViewpointRuleContext
        {
            NotationKey = notationKey,
            ViewpointKey = viewpointKey,
            Element = element
        });

        if (ruleResult.HasIssue)
        {
            issues.Add(CreateElementIssue(
                $"modeling-generator-element-{elementIndex}-viewpoint-scope",
                element,
                "viewpoint",
                ruleResult.Message,
                ruleResult.SuggestedFix,
                ruleResult.Severity));
        }

        return ruleResult.IsAllowed;
    }

    private static string GetStableNodeId(ModelingElementDto element)
        => string.IsNullOrWhiteSpace(element.SourceId) ? element.Id : element.SourceId;

    /// <summary>
    /// Orders element ids parents-first (stable by declaration order otherwise) so a container
    /// element — an id referenced by some other element's <see cref="ModelingElementDto.ParentId"/>
    /// — always receives a LOWER diagram <c>ZIndex</c> than its descendants and can never paint
    /// over them. Hierarchy-free models keep the plain declaration order.
    /// </summary>
    private static Dictionary<string, int> BuildElementOrder(ModelingModelDto model)
    {
        var elementsById = new Dictionary<string, ModelingElementDto>(StringComparer.Ordinal);
        foreach (var element in model.Elements)
        {
            if (!string.IsNullOrWhiteSpace(element.Id) && !elementsById.ContainsKey(element.Id))
                elementsById.Add(element.Id, element);
        }

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>(elementsById.Count);
        foreach (var element in model.Elements)
        {
            if (string.IsNullOrWhiteSpace(element.Id))
                continue;

            EmitWithAncestors(element.Id, elementsById, emitted, ordered);
        }

        return ordered
            .Select((id, index) => (id, index))
            .GroupBy(entry => entry.id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);
    }

    private static void EmitWithAncestors(
        string id,
        Dictionary<string, ModelingElementDto> elementsById,
        HashSet<string> emitted,
        List<string> ordered)
    {
        if (!emitted.Add(id))
            return;

        if (elementsById.TryGetValue(id, out var element)
            && !string.IsNullOrWhiteSpace(element.ParentId)
            && !string.Equals(element.ParentId.Trim(), id, StringComparison.Ordinal))
        {
            EmitWithAncestors(element.ParentId.Trim(), elementsById, emitted, ordered);
        }

        ordered.Add(id);
    }

    private static (double X, double Y) GetGridPosition(int index)
        => (GridOriginX + (index % GridColumns) * GridStepX, GridOriginY + (index / GridColumns) * GridStepY);

    private static string GetNotationKey(ModelingModelDto model, ModelingElementDto element)
    {
        if (!string.IsNullOrWhiteSpace(element.Notation))
            return element.Notation;

        if (!string.IsNullOrWhiteSpace(model.Notation))
            return model.Notation;

        return model.SupportedNotations.FirstOrDefault() ?? string.Empty;
    }

    private static string GetRelationshipNotationKey(
        ModelingModelDto model,
        ModelingRelationshipDto relationship,
        IReadOnlyDictionary<string, ModelingElementDto> elementById)
    {
        if (elementById.TryGetValue(relationship.SourceElementId, out var source)
            && !string.IsNullOrWhiteSpace(source.Notation))
        {
            return source.Notation;
        }

        if (elementById.TryGetValue(relationship.TargetElementId, out var target)
            && !string.IsNullOrWhiteSpace(target.Notation))
        {
            return target.Notation;
        }

        return !string.IsNullOrWhiteSpace(model.Notation)
            ? model.Notation
            : model.SupportedNotations.FirstOrDefault() ?? string.Empty;
    }

    private static string GetUniqueEdgeId(
        ModelingRelationshipDto relationship,
        int relationshipIndex,
        HashSet<string> usedEdgeIds,
        List<ModelingIssueDto> issues)
    {
        var requestedId = !string.IsNullOrWhiteSpace(relationship.Id)
            ? relationship.Id
            : !string.IsNullOrWhiteSpace(relationship.SourceId)
                ? relationship.SourceId
                : $"relationship-{relationshipIndex}";

        if (usedEdgeIds.Add(requestedId))
            return requestedId;

        var fallbackId = $"{requestedId}-{relationshipIndex}";
        usedEdgeIds.Add(fallbackId);
        issues.Add(CreateRelationshipIssue(
            $"modeling-generator-relationship-{relationshipIndex}-duplicate-id",
            relationship,
            "validation",
            $"Duplicate relationship Id '{requestedId}' was renamed to '{fallbackId}'.",
            "Ensure each modeling relationship Id is unique within the model."));
        return fallbackId;
    }

    private static string GetRelationshipDisplayId(ModelingRelationshipDto relationship, int index)
        => !string.IsNullOrWhiteSpace(relationship.Id) ? relationship.Id : $"#{index}";

    private static ModelingIssueDto CreateElementIssue(
        string id,
        ModelingElementDto element,
        string category,
        string message,
        string suggestedFix,
        ModelingIssueSeverity severity = ModelingIssueSeverity.Warning)
        => new()
        {
            Id = id,
            Severity = severity,
            Category = category,
            SourceElementId = element.Id,
            Message = message,
            SuggestedFix = suggestedFix
        };

    private static ModelingIssueDto CreateRelationshipIssue(
        string id,
        ModelingRelationshipDto relationship,
        string category,
        string message,
        string suggestedFix)
        => new()
        {
            Id = id,
            Severity = ModelingIssueSeverity.Warning,
            Category = category,
            SourceRelationshipId = relationship.Id,
            Message = message,
            SuggestedFix = suggestedFix
        };

    private static ModelingIssueDto CreateViewIssue(
        string id,
        ModelingIssueSeverity severity,
        string message,
        string suggestedFix)
        => new()
        {
            Id = id,
            Severity = severity,
            Category = "view",
            Message = message,
            SuggestedFix = suggestedFix
        };
}
