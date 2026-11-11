using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>
/// Global-definition rebuild and BPMNDI emission half of <see cref="BpmnExportBuilder"/>: the
/// importer keeps used <c>message</c>/<c>signal</c>/<c>error</c>/<c>escalation</c> definitions in
/// <c>Properties["bpmnDefinitions"]</c> so the export can restore the <c>definitions</c> root, and
/// view geometry becomes one <c>BPMNPlane</c> per distinct view id (a collaboration plane merges
/// the per-model views that share it).
/// </summary>
internal sealed partial class BpmnExportBuilder
{
    private sealed record GlobalDefinition(string Kind, string Id, string Name, string Code);

    /// <summary>Rebuilds the global definitions (message/signal/error/escalation/dataStore/category)
    /// referenced from the models, in deterministic first-use order (docs §4).</summary>
    private void WriteGlobalDefinitions()
    {
        var definitions = new List<GlobalDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string kind, string id, string name, string code)
        {
            if (id.Length > 0 && seen.Add(id))
            {
                definitions.Add(new GlobalDefinition(kind, id, name, code));
            }
        }

        // 1. Definitions preserved by the importer (declaration order of the imported document).
        foreach (var model in _models)
        {
            if (!model.Properties.TryGetValue("bpmnDefinitions", out var preserved)
                || preserved.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in preserved.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                Add(GetProperty(entry, "kind") ?? string.Empty,
                    GetProperty(entry, "id") ?? string.Empty,
                    GetProperty(entry, "name") ?? string.Empty,
                    GetProperty(entry, "code") ?? string.Empty);
            }
        }

        // 2. Definitions synthesized from event definition references, data stores and groups, so a
        //    clean import → export cycle cannot dangle a reference.
        foreach (var element in _elements.Values)
        {
            if (element.Properties.TryGetValue("eventDefinitions", out var eventDefinitions)
                && eventDefinitions.ValueKind == JsonValueKind.Array)
            {
                foreach (var definition in eventDefinitions.EnumerateArray())
                {
                    var type = GetProperty(definition, "type");
                    if (type is not ("message" or "signal" or "error" or "escalation"))
                    {
                        continue;
                    }

                    Add(type,
                        GetProperty(definition, "ref") ?? string.Empty,
                        GetProperty(definition, "name") ?? string.Empty,
                        GetProperty(definition, "code") ?? string.Empty);
                }
            }

            if (string.Equals(element.SemanticType, BpmnSemanticTypes.DataStore, StringComparison.Ordinal)
                && TryGetString(element.Properties, "dataStoreRef", out var dataStoreRef))
            {
                Add("dataStore", dataStoreRef,
                    TryGetString(element.Properties, "dataStoreName", out var dataStoreName) ? dataStoreName : string.Empty,
                    string.Empty);
            }

            if (string.Equals(element.SemanticType, BpmnSemanticTypes.Group, StringComparison.Ordinal)
                && TryGetString(element.Properties, "categoryValueRef", out var categoryValueRef))
            {
                Add("categoryValue", categoryValueRef,
                    TryGetString(element.Properties, "categoryValue", out var categoryValue) ? categoryValue : string.Empty,
                    string.Empty);
            }
        }

        var categoryIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            switch (definition.Kind)
            {
                case "message":
                    AddGlobal(new XElement(Ns + "message",
                        new XAttribute("id", definition.Id),
                        Attr("name", definition.Name)));
                    break;
                case "signal":
                    AddGlobal(new XElement(Ns + "signal",
                        new XAttribute("id", definition.Id),
                        Attr("name", definition.Name)));
                    break;
                case "error":
                    AddGlobal(new XElement(Ns + "error",
                        new XAttribute("id", definition.Id),
                        Attr("errorCode", definition.Code),
                        Attr("name", definition.Name)));
                    break;
                case "escalation":
                    AddGlobal(new XElement(Ns + "escalation",
                        new XAttribute("id", definition.Id),
                        Attr("escalationCode", definition.Code),
                        Attr("name", definition.Name)));
                    break;
                case "dataStore":
                    AddGlobal(new XElement(Ns + "dataStore",
                        new XAttribute("id", definition.Id),
                        Attr("name", definition.Name)));
                    break;
                case "categoryValue":
                {
                    // Group category values rebuild under a deterministic category element; the
                    // category id itself is not preserved by the import, so it is a stable hash.
                    if (!categoryIds.TryGetValue(definition.Id, out var categoryId))
                    {
                        var stable = BpmnImportInternals.StableId("category", definition.Id);
                        categoryId = "Category_" + stable["category_".Length..];
                        categoryIds[definition.Id] = categoryId;
                    }

                    var category = _root.Elements(Ns + "category")
                        .FirstOrDefault(candidate => candidate.Attribute("id")?.Value == categoryId);
                    if (category is null)
                    {
                        category = new XElement(Ns + "category", new XAttribute("id", categoryId));
                        _root.Add(category);
                    }

                    category.Add(new XElement(Ns + "categoryValue",
                        new XAttribute("id", definition.Id),
                        new XAttribute("value", definition.Name)));
                    break;
                }
            }
        }

        return;

        void AddGlobal(XElement element) => _root.Add(element);
    }

    private static XAttribute? Attr(string name, string value) =>
        value.Length > 0 ? new XAttribute(name, value) : null;

    // ------------------------------------------------------------------------------ BPMNDI

    private sealed class PlaneGroup
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public List<(ModelingModelDto Model, ModelingViewDto View)> Views { get; } = [];
        public XElement Plane { get; set; } = null!;
        public Dictionary<string, BpmnAutoLayout.Placement> Positions { get; } = new(StringComparer.Ordinal);
    }

    private void WriteDiagrams(string? collaborationId, string fallbackProcessId)
    {
        var groups = new List<PlaneGroup>();
        var groupById = new Dictionary<string, PlaneGroup>(StringComparer.Ordinal);
        foreach (var model in _models)
        {
            foreach (var view in model.Views)
            {
                if (!groupById.TryGetValue(view.Id, out var group))
                {
                    group = new PlaneGroup { Id = view.Id, Name = view.Name };
                    groupById[view.Id] = group;
                    groups.Add(group);
                }

                group.Views.Add((model, view));
            }
        }

        if (groups.Count == 0)
        {
            return;
        }

        var coveredRelationships = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            WritePlaneShapes(group, collaborationId, fallbackProcessId);
            WritePlaneConnectionEdges(group, coveredRelationships);
        }

        WriteRelationshipGeometryEdges(groups, coveredRelationships);
        FillMissingDiagramContent(groups, coveredRelationships);

        foreach (var group in groups)
        {
            var diagram = new XElement(DiNs + "BPMNDiagram",
                new XAttribute("id", "BPMNDiagram_" + group.Id),
                new XAttribute("name", group.Name));
            diagram.Add(group.Plane);
            _root.Add(diagram);
        }
    }

    private void WritePlaneShapes(PlaneGroup group, string? collaborationId, string fallbackProcessId)
    {
        var plane = new XElement(DiNs + "BPMNPlane",
            new XAttribute("id", group.Id),
            new XAttribute("bpmnElement", ResolvePlaneElement(group, collaborationId, fallbackProcessId)));
        group.Plane = plane;

        foreach (var (_, view) in group.Views)
        {
            foreach (var node in view.Nodes)
            {
                if (!_elements.TryGetValue(node.ElementId, out var element))
                {
                    AddLoss(node.ElementId, "BPMNShape",
                        "The view node references an element that is not part of the exported models; the shape is skipped.");
                    continue;
                }

                var shape = new XElement(DiNs + "BPMNShape",
                    new XAttribute("id", DiId(node.ElementId + "_di")),
                    new XAttribute("bpmnElement", node.ElementId));
                WriteDiFlags(shape, element.Properties);
                shape.Add(new XElement(DcNs + "Bounds",
                    new XAttribute("x", Format(node.X)),
                    new XAttribute("y", Format(node.Y)),
                    new XAttribute("width", Format(node.Width)),
                    new XAttribute("height", Format(node.Height))));
                WriteLabel(shape, element.Properties);
                plane.Add(shape);
                group.Positions[node.ElementId] =
                    new BpmnAutoLayout.Placement(node.X, node.Y, node.Width, node.Height);
            }
        }
    }

    /// <summary>
    /// The plane's <c>bpmnElement</c>: a plane that holds participant (pool) shapes or
    /// message-flow edges — or that spans several models — references the collaboration
    /// (finding 3); a plane whose nodes all live inside one collapsed subprocess references
    /// that subprocess; every other plane references its process (docs §5.4 — the import does
    /// not preserve the plane's original bpmnElement, so the exporter derives it
    /// deterministically).
    /// </summary>
    private string ResolvePlaneElement(PlaneGroup group, string? collaborationId, string fallbackProcessId)
    {
        var models = group.Views
            .SelectMany(entry => entry.View.Nodes.Select(node => _elementOwners.GetValueOrDefault(node.ElementId)))
            .Where(model => model is not null)
            .Distinct()
            .ToList();

        if (collaborationId is not null
            && (models.Count != 1
                || group.Views.SelectMany(entry => entry.View.Nodes).Any(node =>
                    _elements.TryGetValue(node.ElementId, out var element) && IsPool(element))
                || group.Views.SelectMany(entry => entry.View.Connections).Any(connection =>
                    _relationships.TryGetValue(connection.RelationshipId, out var relationship)
                    && string.Equals(relationship.RelationshipType, BpmnRelationshipTypes.MessageFlow, StringComparison.Ordinal))))
        {
            return collaborationId;
        }

        if (models.Count != 1)
        {
            return fallbackProcessId;
        }

        var nodeIds = new HashSet<string>(group.Views.SelectMany(entry => entry.View.Nodes.Select(node => node.ElementId)), StringComparer.Ordinal);
        string? best = null;
        var bestDepth = int.MaxValue;
        foreach (var element in models[0].Elements.Where(IsSubProcessLike))
        {
            if (nodeIds.Contains(element.Id) || !nodeIds.All(id => IsDescendantOf(id, element.Id)))
            {
                continue;
            }

            var depth = AncestorDepth(element);
            if (depth < bestDepth)
            {
                best = element.Id;
                bestDepth = depth;
            }
        }

        return best ?? models[0].Id;
    }

    private bool IsDescendantOf(string elementId, string ancestorId)
    {
        var guard = 0;
        var current = _elements.GetValueOrDefault(elementId);
        while (current is not null && guard++ < 10_000)
        {
            if (string.Equals(current.Id, ancestorId, StringComparison.Ordinal))
            {
                return true;
            }

            current = current.ParentId.Length > 0 ? _elements.GetValueOrDefault(current.ParentId) : null;
        }

        return false;
    }

    private int AncestorDepth(ModelingElementDto element)
    {
        var depth = 0;
        var guard = 0;
        var current = element;
        while (current.ParentId.Length > 0 && _elements.TryGetValue(current.ParentId, out var parent) && guard++ < 10_000)
        {
            depth++;
            current = parent;
        }

        return depth;
    }

    private void WritePlaneConnectionEdges(PlaneGroup group, HashSet<string> coveredRelationships)
    {
        foreach (var (_, view) in group.Views)
        {
            foreach (var connection in view.Connections)
            {
                if (!_relationships.TryGetValue(connection.RelationshipId, out var relationship))
                {
                    AddLoss(connection.RelationshipId, "BPMNEdge",
                        "The view connection references a relationship that is not part of the exported models; the edge is skipped.");
                    continue;
                }

                var edge = new XElement(DiNs + "BPMNEdge",
                    new XAttribute("id", DiId(relationship.Id + "_di")),
                    new XAttribute("bpmnElement", relationship.Id));
                WriteDiFlags(edge, relationship.Properties);

                var waypoints = connection.Waypoints.Count > 0
                    ? connection.Waypoints
                    : SynthesizeWaypoints(group, relationship);
                foreach (var waypoint in waypoints)
                {
                    edge.Add(new XElement(DdNs + "waypoint",
                        new XAttribute("x", Format(waypoint.X)),
                        new XAttribute("y", Format(waypoint.Y))));
                }

                WriteLabel(edge, relationship.Properties);
                group.Plane.Add(edge);
                coveredRelationships.Add(relationship.Id);
            }
        }
    }

    /// <summary>§5.4: an auto-layouted connection carries no waypoints — synthesize at least the
    /// endpoints from the DI positions of the nodes.</summary>
    private List<ModelingViewWaypointDto> SynthesizeWaypoints(PlaneGroup group, ModelingRelationshipDto relationship)
    {
        if (group.Positions.ContainsKey(relationship.SourceElementId)
            && group.Positions.ContainsKey(relationship.TargetElementId))
        {
            return BpmnAutoLayout.Route(group.Positions, relationship.SourceElementId, relationship.TargetElementId);
        }

        AddLoss(relationship.Id, "BPMNEdge",
            "The connection has no stored waypoints and one endpoint has no shape on the plane; a two-point fallback geometry is emitted.",
            BpmnExportLossSeverity.Info);
        var origin = group.Positions.Values.FirstOrDefault();
        return
        [
            new ModelingViewWaypointDto { X = origin.X, Y = origin.Y },
            new ModelingViewWaypointDto { X = origin.X + 1, Y = origin.Y },
        ];
    }

    /// <summary>
    /// Relationships whose geometry survives only on the relationship itself (cross-model message
    /// flows keep <c>bpmndi:waypoints</c>/<c>bpmndi:planeId</c>, see docs §5.4) are written back
    /// into the plane that holds them.
    /// </summary>
    private void WriteRelationshipGeometryEdges(List<PlaneGroup> groups, HashSet<string> coveredRelationships)
    {
        foreach (var relationship in _models.SelectMany(model => model.Relationships))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!coveredRelationships.Add(relationship.Id))
            {
                continue;
            }

            if (!relationship.Properties.TryGetValue("bpmndi:waypoints", out var waypoints)
                || waypoints.ValueKind != JsonValueKind.Array)
            {
                coveredRelationships.Remove(relationship.Id);
                continue;
            }

            PlaneGroup? target = null;
            if (TryGetString(relationship.Properties, "bpmndi:planeId", out var planeId))
            {
                target = groups.FirstOrDefault(group => string.Equals(group.Id, planeId, StringComparison.Ordinal));
            }

            target ??= groups.FirstOrDefault(group =>
                group.Positions.ContainsKey(relationship.SourceElementId)
                && group.Positions.ContainsKey(relationship.TargetElementId));
            if (target is null)
            {
                AddLoss(relationship.Id, "BPMNEdge",
                    "The relationship carries DI geometry but no plane holds both endpoints; the geometry is not written back.",
                    BpmnExportLossSeverity.Approximated);
                continue;
            }

            var edge = new XElement(DiNs + "BPMNEdge",
                new XAttribute("id", DiId(relationship.Id + "_di")),
                new XAttribute("bpmnElement", relationship.Id));
            WriteDiFlags(edge, relationship.Properties);
            foreach (var waypoint in ReadWaypoints(waypoints))
            {
                edge.Add(new XElement(DdNs + "waypoint",
                    new XAttribute("x", Format(waypoint.X)),
                    new XAttribute("y", Format(waypoint.Y))));
            }

            WriteLabel(edge, relationship.Properties);
            target.Plane.Add(edge);
        }
    }

    /// <summary>
    /// Elements without a shape in any plane and relationships without any geometry are filled from
    /// a deterministic auto-layout pass, mirroring the import's partial-DI fill, so the exported
    /// document is always renderable.
    /// </summary>
    private void FillMissingDiagramContent(List<PlaneGroup> groups, HashSet<string> coveredRelationships)
    {
        foreach (var model in _models)
        {
            var placed = new HashSet<string>(groups.SelectMany(group => group.Positions.Keys), StringComparer.Ordinal);
            var missing = model.Elements.Where(element => !placed.Contains(element.Id)).ToList();
            Dictionary<string, BpmnAutoLayout.Placement>? generated = null;
            foreach (var element in missing)
            {
                generated ??= BpmnAutoLayout.Layout(model).Nodes.ToDictionary(
                    node => node.ElementId,
                    node => new BpmnAutoLayout.Placement(node.X, node.Y, node.Width, node.Height));
                if (!generated.TryGetValue(element.Id, out var placement))
                {
                    continue;
                }

                var target = FindAncestorPlane(groups, element) ?? groups[0];
                var shape = new XElement(DiNs + "BPMNShape",
                    new XAttribute("id", DiId(element.Id + "_di")),
                    new XAttribute("bpmnElement", element.Id),
                    new XElement(DcNs + "Bounds",
                        new XAttribute("x", Format(placement.X)),
                        new XAttribute("y", Format(placement.Y)),
                        new XAttribute("width", Format(placement.Width)),
                        new XAttribute("height", Format(placement.Height))));
                WriteLabel(shape, element.Properties);
                target.Plane.Add(shape);
                target.Positions[element.Id] = placement;
                AddLoss(element.Id, "BPMNShape",
                    "The element has no view node in any view; its position was generated by the deterministic auto-layout.",
                    BpmnExportLossSeverity.Info);
            }

            foreach (var relationship in model.Relationships)
            {
                if (!coveredRelationships.Add(relationship.Id))
                {
                    continue;
                }

                var target = groups.FirstOrDefault(group =>
                    group.Positions.ContainsKey(relationship.SourceElementId)
                    && group.Positions.ContainsKey(relationship.TargetElementId));
                if (target is null)
                {
                    AddLoss(relationship.Id, "BPMNEdge",
                        "The relationship has no geometry in any view; the edge is not written.",
                        BpmnExportLossSeverity.Approximated);
                    continue;
                }

                var waypoints = target.Positions.ContainsKey(relationship.SourceElementId)
                    && target.Positions.ContainsKey(relationship.TargetElementId)
                        ? BpmnAutoLayout.Route(target.Positions, relationship.SourceElementId, relationship.TargetElementId)
                        : [];
                var edge = new XElement(DiNs + "BPMNEdge",
                    new XAttribute("id", DiId(relationship.Id + "_di")),
                    new XAttribute("bpmnElement", relationship.Id));
                foreach (var waypoint in waypoints)
                {
                    edge.Add(new XElement(DdNs + "waypoint",
                        new XAttribute("x", Format(waypoint.X)),
                        new XAttribute("y", Format(waypoint.Y))));
                }

                target.Plane.Add(edge);
            }
        }
    }

    private PlaneGroup? FindAncestorPlane(List<PlaneGroup> groups, ModelingElementDto element)
    {
        var guard = 0;
        var current = element;
        while (guard++ < 10_000)
        {
            if (current.ParentId.Length == 0 || !_elements.TryGetValue(current.ParentId, out var parent))
            {
                return null;
            }

            var plane = groups.FirstOrDefault(group => group.Positions.ContainsKey(parent.Id));
            if (plane is not null)
            {
                return plane;
            }

            current = parent;
        }

        return null;
    }

    private static List<ModelingViewWaypointDto> ReadWaypoints(JsonElement waypoints)
    {
        var result = new List<ModelingViewWaypointDto>();
        foreach (var waypoint in waypoints.EnumerateArray())
        {
            if (waypoint.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (waypoint.TryGetProperty("x", out var x) && waypoint.TryGetProperty("y", out var y)
                && x.ValueKind == JsonValueKind.Number && y.ValueKind == JsonValueKind.Number)
            {
                result.Add(new ModelingViewWaypointDto { X = x.GetDouble(), Y = y.GetDouble() });
            }
        }

        return result;
    }

    /// <summary>DI flags (<c>isExpanded</c>, <c>isHorizontal</c>, <c>isMarkerVisible</c>, …) are
    /// restored from the <c>bpmndi:*</c> properties the import preserved (docs §4 catch-all).</summary>
    private static void WriteDiFlags(XElement diElement, Dictionary<string, JsonElement> properties)
    {
        foreach (var (key, value) in properties)
        {
            if (!key.StartsWith("bpmndi:", StringComparison.Ordinal)
                || key is "bpmndi:labelBounds" or "bpmndi:labelStyle"
                    or "bpmndi:waypoints" or "bpmndi:planeId")
            {
                continue;
            }

            diElement.Add(new XAttribute(key["bpmndi:".Length..], PropertyText(value)));
        }
    }

    private static void WriteLabel(XElement diElement, Dictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("bpmndi:labelBounds", out var bounds)
            || bounds.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var label = new XElement(DiNs + "BPMNLabel");
        if (TryGetString(properties, "bpmndi:labelStyle", out var labelStyle))
        {
            label.Add(new XAttribute("labelStyle", labelStyle));
        }

        if (bounds.TryGetProperty("x", out var x) && bounds.TryGetProperty("y", out var y)
            && bounds.TryGetProperty("width", out var width) && bounds.TryGetProperty("height", out var height))
        {
            label.Add(new XElement(DcNs + "Bounds",
                new XAttribute("x", Format(x.GetDouble())),
                new XAttribute("y", Format(y.GetDouble())),
                new XAttribute("width", Format(width.GetDouble())),
                new XAttribute("height", Format(height.GetDouble()))));
        }

        diElement.Add(label);
    }

    private static string Format(double value) => XmlConvert.ToString(value);
}
