using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Shared plumbing of the editor's structural commands (decision
/// <c>MODELING-AUTHORING-CHANGESET</c> §6.4): every FORWARD structural change is expressed as one
/// ordered <see cref="ModelingOperationBatch"/> applied by the ONE
/// <see cref="ModelingOperationEngine"/> (so the editor and <c>modeling_apply_operations</c> share
/// one semantics and the same error list), while UNDO is a lossless rollback of exactly what the
/// command added or removed — a rollback is not a new semantic operation, and going through the
/// operation vocabulary would drop fields the vocabulary does not carry (an element's source
/// triple, tags or governance).
/// </summary>
internal static class ModelingStructureCommandSupport
{
    /// <summary>The engine's typed operation for a NEW element (the <c>add_element</c> shape).</summary>
    internal static ModelingOperation AddElementOperation(ModelingElementAdd add) => new()
    {
        Op = "add_element",
        Id = add.Id,
        Name = add.Name,
        SemanticType = add.SemanticType,
        Notation = string.IsNullOrEmpty(add.Notation) ? null : add.Notation,
        Description = string.IsNullOrEmpty(add.Description) ? null : add.Description,
        ParentId = add.ParentId,
        Properties = add.Properties.Count > 0 ? add.Properties : null
    };

    /// <summary>The engine's typed operation for a NEW relationship (the <c>add_relationship</c> shape).</summary>
    internal static ModelingOperation AddRelationshipOperation(ModelingRelationshipAdd add) => new()
    {
        Op = "add_relationship",
        Id = add.Id,
        RelationshipType = add.RelationshipType,
        SourceElementId = add.SourceElementId,
        TargetElementId = add.TargetElementId,
        Name = string.IsNullOrEmpty(add.Name) ? null : add.Name,
        Properties = add.Properties.Count > 0 ? add.Properties : null
    };

    /// <summary>The <c>set_view_node</c> op of a NEW view node (X/Y are mandatory for a new node).</summary>
    internal static ModelingOperation SetViewNodeOperation(ModelingViewNodeChange node, string? viewId) => new()
    {
        Op = "set_view_node",
        ViewId = string.IsNullOrEmpty(viewId) ? null : viewId,
        ElementId = node.ElementId,
        X = node.X,
        Y = node.Y,
        Width = node.Width,
        Height = node.Height,
        ParentNodeId = node.ParentNodeId
    };

    /// <summary>The <c>set_view_connection</c> op of a NEW view connection (an empty waypoint list
    /// is a real value — "no manual routing").</summary>
    internal static ModelingOperation SetViewConnectionOperation(string relationshipId, string? viewId) => new()
    {
        Op = "set_view_connection",
        ViewId = string.IsNullOrEmpty(viewId) ? null : viewId,
        RelationshipId = relationshipId,
        Waypoints = []
    };

    /// <summary>The view a layout change targets: the explicit id, else the model's default view.</summary>
    internal static ModelingViewDto? FindView(ModelingModelDto model, string? viewId)
        => string.IsNullOrEmpty(viewId)
            ? model.Views.FirstOrDefault()
            : model.Views.FirstOrDefault(view => string.Equals(view.Id, viewId, StringComparison.Ordinal));

    /// <summary>Rollback of a NEW element: drops its view node and the element itself.</summary>
    internal static void RemoveAddedElement(ModelingModelDto model, string elementId, string? viewId)
    {
        var view = FindView(model, viewId);
        var node = view?.Nodes.FirstOrDefault(item => string.Equals(item.ElementId, elementId, StringComparison.Ordinal));
        if (node is not null)
        {
            view!.Nodes.Remove(node);
        }

        var element = model.Elements.FirstOrDefault(item => string.Equals(item.Id, elementId, StringComparison.Ordinal));
        if (element is not null)
        {
            model.Elements.Remove(element);
        }
    }

    /// <summary>Rollback of a NEW relationship: drops its view connection and the relationship itself.</summary>
    internal static void RemoveAddedRelationship(ModelingModelDto model, string relationshipId, string? viewId)
    {
        var view = FindView(model, viewId);
        var connection = view?.Connections.FirstOrDefault(item => string.Equals(item.RelationshipId, relationshipId, StringComparison.Ordinal));
        if (connection is not null)
        {
            view!.Connections.Remove(connection);
        }

        var relationship = model.Relationships.FirstOrDefault(item => string.Equals(item.Id, relationshipId, StringComparison.Ordinal));
        if (relationship is not null)
        {
            model.Relationships.Remove(relationship);
        }
    }

    /// <summary>
    /// Deep copies of the removed DTOs, so undo restores exactly the state the user deleted (an
    /// element keeps its source triple, tags, governance and property kinds).
    /// </summary>
    internal static (List<ModelingElementDto> Elements, List<ModelingRelationshipDto> Relationships) CaptureRemoved(
        ModelingModelDto model,
        IReadOnlyCollection<string> elementIds,
        IReadOnlyCollection<string> relationshipIds)
    {
        var snapshot = ModelingOperationEngine.Clone(model);
        var elementSet = new HashSet<string>(elementIds, StringComparer.Ordinal);
        var relationshipSet = new HashSet<string>(relationshipIds, StringComparer.Ordinal);

        return (
            [.. snapshot.Elements.Where(element => elementSet.Contains(element.Id))],
            [.. snapshot.Relationships.Where(relationship => relationshipSet.Contains(relationship.Id))]);
    }

    /// <summary>Deep copies of the removed view nodes / connections of the target view.</summary>
    internal static (List<ModelingViewNodeDto> Nodes, List<ModelingViewConnectionDto> Connections) CaptureRemovedView(
        ModelingModelDto model,
        string? viewId,
        IReadOnlyCollection<string> elementIds,
        IReadOnlyCollection<string> relationshipIds)
    {
        var view = FindView(model, viewId);
        if (view is null)
        {
            return ([], []);
        }

        var elementSet = new HashSet<string>(elementIds, StringComparer.Ordinal);
        var relationshipSet = new HashSet<string>(relationshipIds, StringComparer.Ordinal);
        return (
            [.. view.Nodes
                .Where(node => elementSet.Contains(node.ElementId))
                .Select(node => new ModelingViewNodeDto
                {
                    ElementId = node.ElementId,
                    X = node.X,
                    Y = node.Y,
                    Width = node.Width,
                    Height = node.Height,
                    ParentNodeId = node.ParentNodeId
                })],
            [.. view.Connections
                .Where(connection => relationshipSet.Contains(connection.RelationshipId))
                .Select(connection => new ModelingViewConnectionDto
                {
                    RelationshipId = connection.RelationshipId,
                    SourceNodeId = connection.SourceNodeId,
                    TargetNodeId = connection.TargetNodeId,
                    Waypoints = [.. connection.Waypoints.Select(waypoint => new ModelingViewWaypointDto { X = waypoint.X, Y = waypoint.Y })]
                })]);
    }

    /// <summary>Element ids ordered parents-first (a container is re-added before its children).</summary>
    internal static List<string> OrderParentsFirst(IReadOnlyCollection<ModelingElementDto> elements)
    {
        var parentById = elements.ToDictionary(
            element => element.Id,
            element => element.ParentId ?? string.Empty,
            StringComparer.Ordinal);
        return [.. elements.Select(element => element.Id).OrderBy(id => Depth(parentById, id))];
    }

    /// <summary>Element ids ordered DESCENDANTS-first — the engine's <c>still_has_children</c>
    /// check passes without a silent cascade (decision Q3).</summary>
    internal static List<string> OrderDescendantsFirst(IReadOnlyCollection<ModelingElementDto> elements)
    {
        var parentById = elements.ToDictionary(
            element => element.Id,
            element => element.ParentId ?? string.Empty,
            StringComparer.Ordinal);
        return [.. elements.Select(element => element.Id).OrderByDescending(id => Depth(parentById, id))];
    }

    private static int Depth(IReadOnlyDictionary<string, string> parentById, string elementId)
    {
        var depth = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = elementId;
        while (seen.Add(current)
            && parentById.TryGetValue(current, out var parent)
            && !string.IsNullOrEmpty(parent))
        {
            depth++;
            current = parent;
        }

        return depth;
    }
}
