using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>
/// Part of <see cref="BpmnModelBuilder"/> split out to stay under the 1200-line source ceiling
/// (N270/F13). BPMNDI import: shapes/edges become view nodes/connections, DI flags and label
/// bounds are preserved as <c>bpmndi:*</c> properties for the export round-trip, and models
/// without (or with partial) diagram data are laid out by the headless <see cref="BpmnAutoLayout"/>.
/// </summary>
internal sealed partial class BpmnModelBuilder
{
    /// <summary>Element/relationship property key under which a BPMNLabel bounds is preserved.</summary>
    private const string LabelBoundsProperty = "bpmndi:labelBounds";

    /// <summary>Vertical gap between a view's DI bounding box and the auto-layout fill block below it.</summary>
    private const double FillBlockGapY = 40;

    /// <summary>
    /// Builds the views for every imported model: one view per BPMNPlane (per model the plane
    /// covers) when diagram data is present and honored, otherwise a full deterministic
    /// auto-layout per model.
    /// </summary>
    private void ImportViewsAndAutoLayout(XElement root)
    {
        var diagrams = root.Elements()
            .Where(element => BpmnImportInternals.IsLocal(element, "BPMNDiagram"))
            .ToList();

        if (!_options.ImportBpmndi || diagrams.Count == 0)
        {
            var reason = !_options.ImportBpmndi
                ? "BPMNDI import is disabled (ImportBpmndi=false)"
                : "The BPMN document contains no BPMNDI diagram data";
            foreach (var model in _models)
            {
                model.Views.Add(BpmnAutoLayout.Layout(model));
                _warnings.Add(new BpmnImportWarning
                {
                    Message = reason + "; a deterministic headless auto-layout generated the view positions.",
                    XmlPath = "/definitions",
                });
            }

            return;
        }

        foreach (var diagram in diagrams)
        {
            ImportDiagramViews(diagram);
        }

        FillMissingViewContent();
    }

    private void ImportDiagramViews(XElement diagram)
    {
        var diagramName = diagram.Attribute("name")?.Value ?? string.Empty;
        foreach (var plane in BpmnImportInternals.Children(diagram, "BPMNPlane"))
        {
            ImportPlaneView(plane, diagramName);
        }

        // The font definitions are not carried into the modeling model; the labelStyle REFERENCE
        // is preserved as a bpmndi:labelStyle property, the definition itself is reported.
        foreach (var labelStyle in BpmnImportInternals.Children(diagram, "BPMNLabelStyle"))
        {
            AddLoss(labelStyle, "BPMNLabelStyle",
                "BPMNLabelStyle font definitions are not preserved; the labelStyle reference is kept as a bpmndi:labelStyle property only.");
        }
    }

    private void ImportPlaneView(XElement plane, string diagramName)
    {
        var planeId = plane.Attribute("id")?.Value;
        if (string.IsNullOrWhiteSpace(planeId))
        {
            planeId = BpmnImportInternals.StableId("BPMNPlane", BpmnImportInternals.BuildPath(plane));
        }

        // Resolve every shape/edge against the imported content, remembering the owning model in
        // first-appearance order — a collaboration plane spans several models under PerProcess.
        var shapes = new List<(ModelingModelDto Model, XElement Shape, ModelingElementDto Element)>();
        var edges = new List<(ModelingModelDto Model, XElement Edge, ModelingRelationshipDto Relationship)>();
        var modelsInPlane = new List<ModelingModelDto>();
        foreach (var shape in BpmnImportInternals.Children(plane, "BPMNShape"))
        {
            var reference = StripRef(shape.Attribute("bpmnElement")?.Value);
            if (reference.Length == 0 || !_elementIndex.TryGetValue(reference, out var entry))
            {
                ReportUnresolvedDi(shape, "BPMNShape", reference, "element");
                continue;
            }

            ReportUnsupportedDiPayloads(shape);
            shapes.Add((entry.Model, shape, entry.Element));
            if (!modelsInPlane.Contains(entry.Model))
            {
                modelsInPlane.Add(entry.Model);
            }
        }

        foreach (var edge in BpmnImportInternals.Children(plane, "BPMNEdge"))
        {
            var reference = StripRef(edge.Attribute("bpmnElement")?.Value);
            if (reference.Length == 0 || !_relationshipIndex.TryGetValue(reference, out var edgeEntry))
            {
                ReportUnresolvedDi(edge, "BPMNEdge", reference, "relationship");
                continue;
            }

            ReportUnsupportedDiPayloads(edge);
            edges.Add((edgeEntry.Model, edge, edgeEntry.Relationship));
            if (!modelsInPlane.Contains(edgeEntry.Model))
            {
                modelsInPlane.Add(edgeEntry.Model);
            }
        }

        if (modelsInPlane.Count == 0)
        {
            // Nothing on the plane resolves to imported content: anchor on the plane's own element
            // (a collapsed subprocess exposes its internals this way) before giving up on the plane.
            var planeReference = StripRef(plane.Attribute("bpmnElement")?.Value);
            if (planeReference.Length > 0 && _elementIndex.TryGetValue(planeReference, out var planeElement))
            {
                modelsInPlane.Add(planeElement.Model);
            }
            else
            {
                AddLoss(plane, "BPMNPlane",
                    $"Plane references '{planeReference}' which is not an imported model element and carries no resolvable shapes; the plane is not imported.");
                _warnings.Add(new BpmnImportWarning
                {
                    Message = $"BPMNPlane '{planeId}' references '{planeReference}', which is not an imported element; the plane is skipped.",
                    XmlPath = BpmnImportInternals.BuildPath(plane),
                });
                return;
            }
        }

        foreach (var model in modelsInPlane)
        {
            BuildPlaneView(model, planeId, diagramName, shapes, edges);
        }
    }

    /// <summary><c>di:extension</c> payloads carry tool-specific DI markup the modeling view cannot represent.</summary>
    private void ReportUnsupportedDiPayloads(XElement diElement)
    {
        foreach (var extension in BpmnImportInternals.Children(diElement, "extension"))
        {
            AddLoss(extension, "di:extension",
                "The DI extension payload is not preserved on the imported view.",
                BpmnImportLossSeverity.Loss);
        }
    }

    private void BuildPlaneView(
        ModelingModelDto model,
        string planeId,
        string diagramName,
        List<(ModelingModelDto Model, XElement Shape, ModelingElementDto Element)> shapes,
        List<(ModelingModelDto Model, XElement Edge, ModelingRelationshipDto Relationship)> edges)
    {
        var view = new ModelingViewDto
        {
            Id = planeId,
            Name = diagramName.Length > 0 ? diagramName : planeId,
            Notation = BpmnConstants.Bpmn2Notation,
        };

        foreach (var (owner, shape, element) in shapes)
        {
            if (!ReferenceEquals(owner, model))
            {
                continue;
            }

            var bounds = ReadBounds(shape);
            if (bounds is null)
            {
                AddLoss(shape, "dc:Bounds",
                    $"Shape for '{element.Id}' has no usable dc:Bounds; the position is generated by the auto-layout.");
                _warnings.Add(new BpmnImportWarning
                {
                    Message = $"BPMNShape '{shape.Attribute("id")?.Value}' for '{element.Id}' has no usable dc:Bounds; the position is generated.",
                    XmlPath = BpmnImportInternals.BuildPath(shape),
                });
                continue;
            }

            view.Nodes.Add(new ModelingViewNodeDto
            {
                ElementId = element.Id,
                X = bounds.Value.X,
                Y = bounds.Value.Y,
                Width = bounds.Value.Width,
                Height = bounds.Value.Height,
            });

            ApplyDiFlags(shape, element);
            ApplyLabelBounds(shape, element.Properties);
        }

        ResolveParentNodes(view);

        var nodeByElement = new HashSet<string>(view.Nodes.Select(node => node.ElementId), StringComparer.Ordinal);
        foreach (var (owner, edge, relationship) in edges)
        {
            if (!ReferenceEquals(owner, model))
            {
                continue;
            }

            var waypoints = ReadWaypoints(edge);

            // Preserve the full absolute DI polyline (endpoints included) and the owning plane on
            // the relationship — the phase 4 export writes them back verbatim.
            relationship.Properties["bpmndi:waypoints"] = BpmnImportInternals.JsonElementFrom(
                waypoints.Select<ModelingViewWaypointDto, JsonNode?>(waypoint => new JsonObject
                {
                    ["x"] = waypoint.X,
                    ["y"] = waypoint.Y,
                }).ToArray());
            relationship.Properties["bpmndi:planeId"] = BpmnImportInternals.StringElement(planeId);

            ApplyDiFlags(edge, relationship);
            ApplyLabelBounds(edge, relationship.Properties);

            // A connection renders only when BOTH ends are nodes of THIS view — a cross-model
            // message flow has its ends in other models' views; its geometry survives on the
            // relationship properties above (docs/modeling-bpmn-xml.md §5.4).
            if (!nodeByElement.Contains(relationship.SourceElementId)
                || !nodeByElement.Contains(relationship.TargetElementId))
            {
                continue;
            }

            view.Connections.Add(new ModelingViewConnectionDto
            {
                RelationshipId = relationship.Id,
                SourceNodeId = relationship.SourceElementId,
                TargetNodeId = relationship.TargetElementId,
                Waypoints = waypoints,
            });
        }

        model.Views.Add(view);
    }

    /// <summary>
    /// A view node nests under the node of its model element's semantic parent — a lane, an
    /// expanded subprocess or a pool — but only when that parent is part of the same plane
    /// (per DI <c>isExpanded</c>, a collapsed subprocess's internals live on their own plane).
    /// </summary>
    private void ResolveParentNodes(ModelingViewDto view)
    {
        var nodeByElement = new HashSet<string>(view.Nodes.Select(node => node.ElementId), StringComparer.Ordinal);
        foreach (var node in view.Nodes)
        {
            if (!_elementIndex.TryGetValue(node.ElementId, out var entry)
                || string.IsNullOrWhiteSpace(entry.Element.ParentId)
                || string.Equals(entry.Element.ParentId, node.ElementId, StringComparison.Ordinal)
                || !nodeByElement.Contains(entry.Element.ParentId))
            {
                node.ParentNodeId = null;
                continue;
            }

            node.ParentNodeId = entry.Element.ParentId;
        }
    }

    /// <summary>
    /// Partial BPMNDI: elements that no plane of the model gives a shape (and connections no
    /// plane gives an edge) are filled from a full auto-layout pass, so the stored views are
    /// complete — every element positioned, every resolvable relationship connected. Each missing
    /// element joins ONLY the view that holds its nearest semantic ancestor (fallback: the first
    /// view), offset below that view's DI bounding box, so no ElementId is ever duplicated across
    /// views and the generated block never overlaps stored DI geometry. An element positioned on
    /// ANY plane counts (a collapsed subprocess's internals live exclusively on their own second
    /// plane and must not be duplicated into the overview).
    /// </summary>
    private void FillMissingViewContent()
    {
        foreach (var model in _models)
        {
            if (model.Views.Count == 0)
            {
                // No plane of any diagram covers this model: give it a complete auto-layout view.
                model.Views.Add(BpmnAutoLayout.Layout(model));
                _warnings.Add(new BpmnImportWarning
                {
                    Message = $"No BPMNPlane covers model '{model.Id}'; a deterministic headless auto-layout generated the view positions.",
                    XmlPath = "/definitions",
                });
                continue;
            }

            var placedElements = new HashSet<string>(
                model.Views.SelectMany(view => view.Nodes.Select(node => node.ElementId)), StringComparer.Ordinal);
            var connectedRelationships = new HashSet<string>(
                model.Views.SelectMany(view => view.Connections.Select(connection => connection.RelationshipId)),
                StringComparer.Ordinal);
            var missingElements = model.Elements
                .Where(element => !placedElements.Contains(element.Id))
                .ToList();
            var missingConnections = model.Relationships
                .Where(relationship => !connectedRelationships.Contains(relationship.Id))
                .ToList();

            if (missingElements.Count == 0 && missingConnections.Count == 0)
            {
                continue;
            }

            var generated = BpmnAutoLayout.Layout(
                model, new BpmnAutoLayoutOptions { ViewId = "auto-fill", ViewName = "Auto layout fill" });
            var generatedByElement = generated.Nodes.ToDictionary(node => node.ElementId);

            // Element id → the view holding it, for the nearest-ancestor resolution below.
            var viewByElement = new Dictionary<string, ModelingViewDto>(StringComparer.Ordinal);
            foreach (var view in model.Views)
            {
                foreach (var node in view.Nodes)
                {
                    viewByElement.TryAdd(node.ElementId, view);
                }
            }

            // One target view per missing element — never more (no duplicate ElementId across views).
            var fillByView = new Dictionary<ModelingViewDto, List<ModelingElementDto>>();
            foreach (var element in missingElements)
            {
                var target = FindFillTargetView(element, viewByElement, model.Views[0]);
                if (!fillByView.TryGetValue(target, out var bucket))
                {
                    bucket = [];
                    fillByView[target] = bucket;
                }

                bucket.Add(element);
            }

            foreach (var (view, elements) in fillByView)
            {
                // Offset the generated block below the view's DI bounding box (maxY + gap).
                var diBottom = 0.0;
                foreach (var node in view.Nodes)
                {
                    if (node.Y + node.Height > diBottom)
                    {
                        diBottom = node.Y + node.Height;
                    }
                }

                var generatedTop = double.MaxValue;
                foreach (var element in elements)
                {
                    if (generatedByElement.TryGetValue(element.Id, out var probe) && probe.Y < generatedTop)
                    {
                        generatedTop = probe.Y;
                    }
                }

                var offsetY = view.Nodes.Count > 0 && generatedTop < double.MaxValue
                    ? diBottom + FillBlockGapY - generatedTop
                    : 0;
                foreach (var element in elements)
                {
                    if (!generatedByElement.TryGetValue(element.Id, out var node))
                    {
                        continue;
                    }

                    view.Nodes.Add(new ModelingViewNodeDto
                    {
                        ElementId = node.ElementId,
                        X = node.X,
                        Y = node.Y + offsetY,
                        Width = node.Width,
                        Height = node.Height,
                        ParentNodeId = node.ParentNodeId,
                    });
                }
            }

            foreach (var view in model.Views)
            {
                var nodeByElement = new HashSet<string>(view.Nodes.Select(node => node.ElementId), StringComparer.Ordinal);
                var connectionByRelationship = new HashSet<string>(
                    view.Connections.Select(connection => connection.RelationshipId), StringComparer.Ordinal);
                var viewMissingConnections = missingConnections
                    .Where(relationship => !connectionByRelationship.Contains(relationship.Id)
                        && nodeByElement.Contains(relationship.SourceElementId)
                        && nodeByElement.Contains(relationship.TargetElementId))
                    .ToList();

                var positions = view.Nodes.ToDictionary(
                    node => node.ElementId,
                    node => new BpmnAutoLayout.Placement(node.X, node.Y, node.Width, node.Height));
                foreach (var relationship in viewMissingConnections)
                {
                    view.Connections.Add(new ModelingViewConnectionDto
                    {
                        RelationshipId = relationship.Id,
                        SourceNodeId = relationship.SourceElementId,
                        TargetNodeId = relationship.TargetElementId,
                        Waypoints = BpmnAutoLayout.Route(positions, relationship.SourceElementId, relationship.TargetElementId),
                    });
                }

                if (viewMissingConnections.Count > 0)
                {
                    _warnings.Add(new BpmnImportWarning
                    {
                        Message = $"{viewMissingConnections.Count} connection(s) have no BPMNDI edge in plane '{view.Id}'; their waypoints were generated by the auto-layout: "
                            + string.Join(", ", viewMissingConnections.Select(relationship => relationship.Id)),
                        XmlPath = "/definitions",
                    });
                }

                ResolveParentNodes(view);
            }

            if (missingElements.Count > 0)
            {
                _warnings.Add(new BpmnImportWarning
                {
                    Message = $"{missingElements.Count} element(s) have no BPMNPlane shape in any plane of model '{model.Id}'; their positions were generated by the auto-layout below the plane's diagram bounds: "
                        + string.Join(", ", missingElements.Select(element => element.Id)),
                    XmlPath = "/definitions",
                });
            }
        }
    }

    /// <summary>
    /// The view a missing element joins: the one holding the NEAREST placed ancestor (walking the
    /// <see cref="ModelingElementDto.ParentId"/> chain), falling back to the model's first view
    /// when no ancestor is placed anywhere.
    /// </summary>
    private ModelingViewDto FindFillTargetView(
        ModelingElementDto element,
        IReadOnlyDictionary<string, ModelingViewDto> viewByElement,
        ModelingViewDto fallback)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = element.ParentId;
        while (!string.IsNullOrWhiteSpace(current) && seen.Add(current!))
        {
            if (viewByElement.TryGetValue(current!, out var view))
            {
                return view;
            }

            current = _elementIndex.TryGetValue(current!, out var entry) ? entry.Element.ParentId : null;
        }

        return fallback;
    }

    private void ReportUnresolvedDi(XElement diElement, string construct, string reference, string targetKind)
    {
        AddLoss(diElement, construct,
            $"{construct} references '{reference}' which is not an imported model {targetKind}; the diagram geometry is not preserved.");
        _warnings.Add(new BpmnImportWarning
        {
            Message = $"{construct} '{diElement.Attribute("id")?.Value}' references '{reference}', which is not an imported {targetKind}; the diagram geometry is not preserved.",
            XmlPath = BpmnImportInternals.BuildPath(diElement),
        });
    }

    /// <summary>DI flags (<c>isExpanded</c>, <c>isHorizontal</c>, <c>isMarkerVisible</c>, …) are
    /// preserved on the element as <c>bpmndi:*</c> properties so the export phase can write them
    /// back without loss (design §4 catch-all rule).</summary>
    private static void ApplyDiFlags(XElement diElement, ModelingElementDto element) =>
        ApplyDiFlags(diElement, element.Properties);

    /// <summary>Relationship variant: edge attributes (<c>messageVisibleKind</c>,
    /// <c>sourceElement</c>/<c>targetElement</c>, …) become <c>bpmndi:*</c> relationship
    /// properties for the export round-trip.</summary>
    private static void ApplyDiFlags(XElement diElement, ModelingRelationshipDto relationship) =>
        ApplyDiFlags(diElement, relationship.Properties);

    private static void ApplyDiFlags(XElement diElement, Dictionary<string, JsonElement> properties)
    {
        foreach (var attribute in diElement.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name.LocalName is "id" or "bpmnElement")
            {
                continue;
            }

            properties["bpmndi:" + attribute.Name.LocalName] = BpmnImportInternals.StringElement(attribute.Value);
        }
    }

    private static void ApplyLabelBounds(XElement diElement, Dictionary<string, JsonElement> properties)
    {
        var label = BpmnImportInternals.Child(diElement, "BPMNLabel");
        if (label is null)
        {
            return;
        }

        if (label.Attribute("labelStyle") is { } labelStyle)
        {
            properties["bpmndi:labelStyle"] = BpmnImportInternals.StringElement(labelStyle.Value);
        }

        var bounds = ReadBounds(label);
        if (bounds is null)
        {
            return;
        }

        properties[LabelBoundsProperty] = BpmnImportInternals.JsonElementFrom(new JsonObject
        {
            ["x"] = bounds.Value.X,
            ["y"] = bounds.Value.Y,
            ["width"] = bounds.Value.Width,
            ["height"] = bounds.Value.Height,
        });
    }

    private List<ModelingViewWaypointDto> ReadWaypoints(XElement edge)
    {
        var waypoints = new List<ModelingViewWaypointDto>();
        var reported = false;
        foreach (var waypoint in BpmnImportInternals.Children(edge, "waypoint"))
        {
            if (TryParseDouble(waypoint.Attribute("x")?.Value, out var x)
                && TryParseDouble(waypoint.Attribute("y")?.Value, out var y))
            {
                waypoints.Add(new ModelingViewWaypointDto { X = x, Y = y });
                continue;
            }

            if (!reported)
            {
                reported = true;
                AddLoss(edge, "waypoint",
                    $"Edge '{edge.Attribute("id")?.Value}' carries a waypoint without numeric x/y; that waypoint is skipped.");
            }
        }

        return waypoints;
    }

    private static DiBounds? ReadBounds(XElement container)
    {
        var bounds = BpmnImportInternals.Child(container, "Bounds");
        if (bounds is null
            || !TryParseDouble(bounds.Attribute("x")?.Value, out var x)
            || !TryParseDouble(bounds.Attribute("y")?.Value, out var y)
            || !TryParseDouble(bounds.Attribute("width")?.Value, out var width)
            || !TryParseDouble(bounds.Attribute("height")?.Value, out var height))
        {
            return null;
        }

        return new DiBounds(x, y, width, height);
    }

    private static bool TryParseDouble(string? value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private readonly record struct DiBounds(double X, double Y, double Width, double Height);
}
