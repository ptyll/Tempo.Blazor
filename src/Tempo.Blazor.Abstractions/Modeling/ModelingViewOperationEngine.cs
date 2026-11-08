using System.Text.Json.Nodes;
using static Tempo.Blazor.Modeling.ModelingOperationEngine;

namespace Tempo.Blazor.Modeling;

/// <summary>
/// View (layout) operation handlers of <see cref="ModelingOperationEngine"/> (decision
/// <c>MODELING-UNIFIED-OPERATION-ENGINE</c> — the <c>Authoring</c> half of the operation catalog):
/// view node and view connection upserts/removals plus the viewpoint gate that keeps an element out
/// of a view whose viewpoint does not allow it. Internal — the public surface is the engine itself —
/// and split out of the engine file to stay under the 1200-line source ceiling.
/// </summary>
internal static class ModelingViewOperationEngine
{
    // ── Views ──────────────────────────────────────────────────────────────────

    internal static void ApplySetViewNode(
        ModelingModelDto model,
        JsonObject operation,
        int index,
        ModelingOperationResult result,
        ModelingOperationContext context)
    {
        const string op = "set_view_node";
        var elementId = GetString(operation, "elementId");
        if (string.IsNullOrWhiteSpace(elementId))
        {
            AddError(result, index, op, "missing_field", $"operations[{index}] {op}: 'elementId' is required.");
            return;
        }

        if (!model.Elements.Any(element => string.Equals(element.Id, elementId, StringComparison.Ordinal)))
        {
            AddError(result, index, op, "not_found", $"operations[{index}] {op}: element '{elementId}' not found.", elementId: elementId);
            return;
        }

        var viewId = GetString(operation, "viewId") ?? string.Empty;
        if (!TryResolveTargetView(model, viewId, create: true, index, op, result, out var view))
        {
            return;
        }

        if (!ValidateViewpoint(model, elementId, view!, context, index, op, result))
        {
            return;
        }

        // A non-empty parentNodeId must resolve to an ELEMENT (view nodes carry the parent's
        // element id — they have no id of their own) and must never close a cycle in the view's
        // parent chain. Validated BEFORE any geometry is written, so a rejected operation leaves
        // the view untouched.
        if (operation.ContainsKey("parentNodeId")
            && GetString(operation, "parentNodeId") is { Length: > 0 } requestedParentNodeId
            && !ValidateViewNodeParent(model, view!, elementId, requestedParentNodeId, index, op, result))
        {
            return;
        }

        var node = view!.Nodes.FirstOrDefault(item => string.Equals(item.ElementId, elementId, StringComparison.Ordinal));
        var x = GetDouble(operation, "x");
        var y = GetDouble(operation, "y");
        var width = GetDouble(operation, "width");
        var height = GetDouble(operation, "height");

        if (node is null)
        {
            if (x is null)
            {
                AddError(result, index, op, "missing_field", $"operations[{index}] {op}: 'x' is required for a new view node.", elementId: elementId);
                return;
            }

            if (y is null)
            {
                AddError(result, index, op, "missing_field", $"operations[{index}] {op}: 'y' is required for a new view node.", elementId: elementId);
                return;
            }

            node = new ModelingViewNodeDto
            {
                ElementId = elementId,
                X = x.Value,
                Y = y.Value,
                Width = width ?? 0,
                Height = height ?? 0
            };
            view.Nodes.Add(node);
        }
        else
        {
            if (x is not null)
            {
                node.X = x.Value;
            }

            if (y is not null)
            {
                node.Y = y.Value;
            }

            if (width is not null)
            {
                node.Width = width.Value;
            }

            if (height is not null)
            {
                node.Height = height.Value;
            }
        }

        // null = unchanged, "" = clear (the node becomes a root). A cleared parent is stored as
        // null so the DTO keeps ONE representation of "no parent" (the generator treats both the
        // same, but a diff/marshalling host should not see two spellings of one state).
        if (operation.ContainsKey("parentNodeId") && GetString(operation, "parentNodeId") is { } parentNodeId)
        {
            node.ParentNodeId = string.IsNullOrEmpty(parentNodeId) ? null : parentNodeId;
        }
    }

    internal static void ApplyRemoveViewNode(
        ModelingModelDto model,
        JsonObject operation,
        int index,
        ModelingOperationResult result)
    {
        const string op = "remove_view_node";
        var elementId = GetString(operation, "elementId");
        if (string.IsNullOrWhiteSpace(elementId))
        {
            AddError(result, index, op, "missing_field", $"operations[{index}] {op}: 'elementId' is required.");
            return;
        }

        var viewId = GetString(operation, "viewId") ?? string.Empty;
        if (!TryResolveTargetView(model, viewId, create: false, index, op, result, out var view))
        {
            return;
        }

        var node = view!.Nodes.FirstOrDefault(item => string.Equals(item.ElementId, elementId, StringComparison.Ordinal));
        if (node is null)
        {
            AddError(
                result,
                index,
                op,
                "not_found",
                $"operations[{index}] {op}: view node for element '{elementId}' not found.",
                elementId: elementId);
            return;
        }

        view.Nodes.Remove(node);
    }

    internal static void ApplySetViewConnection(
        ModelingModelDto model,
        JsonObject operation,
        int index,
        ModelingOperationResult result)
    {
        const string op = "set_view_connection";
        var relationshipId = GetString(operation, "relationshipId");
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            AddError(result, index, op, "missing_field", $"operations[{index}] {op}: 'relationshipId' is required.");
            return;
        }

        var relationship = model.Relationships.FirstOrDefault(item => string.Equals(item.Id, relationshipId, StringComparison.Ordinal));
        if (relationship is null)
        {
            AddError(
                result,
                index,
                op,
                "not_found",
                $"operations[{index}] {op}: relationship '{relationshipId}' not found.",
                relationshipId: relationshipId);
            return;
        }

        var viewId = GetString(operation, "viewId") ?? string.Empty;
        if (!TryResolveTargetView(model, viewId, create: true, index, op, result, out var view))
        {
            return;
        }

        var connection = view!.Connections.FirstOrDefault(item => string.Equals(item.RelationshipId, relationshipId, StringComparison.Ordinal));
        if (connection is null)
        {
            connection = new ModelingViewConnectionDto
            {
                RelationshipId = relationshipId,
                SourceNodeId = relationship.SourceElementId,
                TargetNodeId = relationship.TargetElementId
            };
            view.Connections.Add(connection);
        }

        // null = unchanged, an EMPTY array clears the manual routing.
        if (operation.TryGetPropertyValue("waypoints", out var waypointsNode) && waypointsNode is not null)
        {
            if (waypointsNode is not JsonArray waypoints)
            {
                AddError(result, index, op, "property_type", $"operations[{index}] {op}: 'waypoints' must be a JSON array of points.");
                return;
            }

            var parsed = new List<ModelingViewWaypointDto>(waypoints.Count);
            var position = 0;
            foreach (var waypoint in waypoints)
            {
                if (waypoint is not JsonObject point
                    || GetDouble(point, "x") is not { } pointX
                    || GetDouble(point, "y") is not { } pointY)
                {
                    AddError(
                        result,
                        index,
                        op,
                        "property_type",
                        $"operations[{index}] {op}: waypoints[{position}] must be an object with numeric 'x' and 'y'.");
                    return;
                }

                parsed.Add(new ModelingViewWaypointDto { X = pointX, Y = pointY });
                position++;
            }

            connection.Waypoints = parsed;
        }
    }

    internal static void ApplyRemoveViewConnection(
        ModelingModelDto model,
        JsonObject operation,
        int index,
        ModelingOperationResult result)
    {
        const string op = "remove_view_connection";
        var relationshipId = GetString(operation, "relationshipId");
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            AddError(result, index, op, "missing_field", $"operations[{index}] {op}: 'relationshipId' is required.");
            return;
        }

        var viewId = GetString(operation, "viewId") ?? string.Empty;
        if (!TryResolveTargetView(model, viewId, create: false, index, op, result, out var view))
        {
            return;
        }

        var connection = view!.Connections.FirstOrDefault(item => string.Equals(item.RelationshipId, relationshipId, StringComparison.Ordinal));
        if (connection is null)
        {
            AddError(
                result,
                index,
                op,
                "not_found",
                $"operations[{index}] {op}: view connection for relationship '{relationshipId}' not found.",
                relationshipId: relationshipId);
            return;
        }

        view.Connections.Remove(connection);
    }

    /// <summary>
    /// Validates the optional <c>parentNodeId</c> of a <c>set_view_node</c> operation: a view node is
    /// parented by the ELEMENT it is nested under, so the id must resolve to an existing element and
    /// must not close a CYCLE in the view's parent chain (code <c>parent_cycle</c>). The chain walk
    /// matters: rejecting only a self-parent let <c>A parent B</c> + <c>B parent A</c> through in one
    /// batch, which the generator cannot nest. Without this check an unknown or cyclic parent would
    /// be stored and the generator would build a broken tree.
    /// </summary>
    private static bool ValidateViewNodeParent(
        ModelingModelDto model,
        ModelingViewDto view,
        string elementId,
        string parentNodeId,
        int index,
        string opName,
        ModelingOperationResult result)
    {
        if (string.Equals(parentNodeId, elementId, StringComparison.Ordinal))
        {
            AddError(
                result,
                index,
                opName,
                "parent_cycle",
                $"operations[{index}] {opName}: element '{elementId}' cannot be its own parent (cycle).",
                elementId: elementId);
            return false;
        }

        if (!model.Elements.Any(element => string.Equals(element.Id, parentNodeId, StringComparison.Ordinal)))
        {
            AddError(
                result,
                index,
                opName,
                "parent_not_found",
                $"operations[{index}] {opName}: parent element '{parentNodeId}' not found.",
                elementId: parentNodeId);
            return false;
        }

        // Walk the EXISTING view parent chain: parent -> its parent -> ... . Reaching the node being
        // placed means the new parent would close a cycle. The visited set also makes a chain that
        // was already cyclic terminate instead of hanging.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = parentNodeId;
        while (!string.IsNullOrEmpty(current) && visited.Add(current))
        {
            if (string.Equals(current, elementId, StringComparison.Ordinal))
            {
                AddError(
                    result,
                    index,
                    opName,
                    "parent_cycle",
                    $"operations[{index}] {opName}: parenting element '{elementId}' under '{parentNodeId}' would close a cycle.",
                    elementId: elementId);
                return false;
            }

            current = view.Nodes.FirstOrDefault(item =>
                string.Equals(item.ElementId, current, StringComparison.Ordinal))?.ParentNodeId;
        }

        return true;
    }

    /// <summary>
    /// Resolves the operation's target view: a non-empty <paramref name="viewId"/> addresses that
    /// view; an empty id addresses the model's DEFAULT view (<c>Views[0]</c> — the authoring
    /// contract, NOT the generator's <c>SelectView</c> meaning) and, for a model without a view,
    /// creates ONE view with <c>Id = "default"</c> and the model's notation.
    /// </summary>
    private static bool TryResolveTargetView(
        ModelingModelDto model,
        string viewId,
        bool create,
        int index,
        string opName,
        ModelingOperationResult result,
        out ModelingViewDto? view)
    {
        if (!string.IsNullOrEmpty(viewId))
        {
            view = model.Views.FirstOrDefault(item => string.Equals(item.Id, viewId, StringComparison.Ordinal));
            if (view is null)
            {
                AddError(result, index, opName, "not_found", $"operations[{index}] {opName}: view '{viewId}' not found.");
            }

            return view is not null;
        }

        view = model.Views.FirstOrDefault();
        if (view is null && create)
        {
            view = new ModelingViewDto
            {
                Id = DefaultViewId,
                Name = string.Empty,
                Notation = model.Notation
            };
            model.Views.Add(view);
        }

        if (view is null)
        {
            AddError(result, index, opName, "not_found", $"operations[{index}] {opName}: the model has no views.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// When the host supplied <see cref="IModelingViewpointRulesProvider"/> and the target view
    /// declares a viewpoint, a node may only enter that view when the viewpoint rules ALLOW the
    /// element's semantic type (code <c>viewpoint_disallowed</c>). Without a rules provider (and
    /// for a view without a viewpoint key) nothing is checked — 2.10.0 MCP behavior kept.
    /// </summary>
    private static bool ValidateViewpoint(
        ModelingModelDto model,
        string elementId,
        ModelingViewDto view,
        ModelingOperationContext context,
        int index,
        string opName,
        ModelingOperationResult result)
    {
        if (context.ViewpointRules is null || string.IsNullOrEmpty(view.ViewpointKey))
        {
            return true;
        }

        var element = model.Elements.FirstOrDefault(item => string.Equals(item.Id, elementId, StringComparison.Ordinal));
        if (element is null)
        {
            return true;
        }

        var notationKey = string.IsNullOrWhiteSpace(element.Notation) ? model.Notation : element.Notation;
        var outcome = context.ViewpointRules.ValidateElementViewpoint(new ModelingViewpointRuleContext
        {
            NotationKey = notationKey,
            ViewpointKey = view.ViewpointKey,
            Element = element
        });

        if (outcome.IsAllowed)
        {
            return true;
        }

        var message = $"operations[{index}] {opName}: element '{elementId}' of type '{element.SemanticType}' is not allowed in viewpoint '{view.ViewpointKey}'.";
        if (!string.IsNullOrWhiteSpace(outcome.Message))
        {
            message = $"{message} {outcome.Message}";
        }

        AddError(
            result,
            index,
            opName,
            "viewpoint_disallowed",
            message,
            suggestedFix: string.IsNullOrWhiteSpace(outcome.SuggestedFix) ? null : outcome.SuggestedFix,
            elementId: elementId);
        return false;
    }
}
