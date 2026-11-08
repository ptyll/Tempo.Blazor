using System.Globalization;
using System.Runtime.CompilerServices;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>One row of the viewer's text alternative: a drawn diagram node/edge or a baseline-only
/// (removed) item. Built in topological order from the model and the generated document.</summary>
internal sealed class ModelingViewerListEntry
{
    public required string Id { get; init; }

    public bool IsRelationship { get; init; }

    public required string SemanticType { get; init; }

    public required string Name { get; init; }

    public bool IsRemoved { get; init; }

    public string? DiffState { get; init; }

    public double? HeatmapValue { get; init; }

    /// <summary>A stable DOM/test key: element and relationship ids may theoretically collide.</summary>
    public string Key => IsRemoved
        ? (IsRelationship ? "removed-r:" : "removed-e:") + Id
        : (IsRelationship ? "r:" : "e:") + Id;
}

/// <summary>The overlay state resolved for one viewer pass: per-id states and the legend counts.</summary>
internal sealed class ModelingViewerOverlaySummary
{
    public static ModelingViewerOverlaySummary Empty { get; } = new();

    public IReadOnlyDictionary<string, string> States { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    public int AddedCount { get; init; }

    public int RemovedCount { get; init; }

    public int ChangedCount { get; init; }

    public int LayoutCount { get; init; }

    public string? StateFor(string id)
        => States.TryGetValue(id, out var state) ? state : null;
}

/// <summary>
/// Phase 6 projection helpers for <see cref="TmModelingViewer"/>: it turns the semantic model and the
/// generated document into the keyboard/text-alternative order and applies the diff / heatmap
/// overlays to the viewer-owned presentation copy through the public diagram model only
/// (<see cref="DiagramStyle"/>, <see cref="DiagramNode.Data"/> and <see cref="DiagramEdge.Title"/>).
/// No DiagramEditor internals are touched.
/// </summary>
internal static class ModelingViewerProjection
{
    internal const string DiffAdded = "diff-added";
    internal const string DiffChanged = "diff-changed";
    internal const string DiffLayout = "diff-layout";
    internal const string DiffRemoved = "diff-removed";
    internal const string HighlightState = "highlight";
    internal const string HeatmapState = "heatmap";

    /// <summary>Node <c>Data</c> key read by the canvas as the <c>data-viewer-state</c> DOM attribute.</summary>
    internal const string ViewerStateKey = "viewerState";

    /// <summary>Node <c>Data</c> key read by the canvas as the short overlay badge ("+", "~", "↔").</summary>
    internal const string ViewerBadgeKey = "viewerBadge";

    /// <summary>Node <c>Data</c> key read by the canvas as the group's <c>&lt;title&gt;</c>.</summary>
    internal const string ViewerTitleKey = "viewerTitle";

    internal const string TokenHeat0 = "--tm-color-heat-0";
    internal const string TokenHeat1 = "--tm-color-heat-1";
    internal const string TokenHeat2 = "--tm-color-heat-2";
    internal const string TokenHeat3 = "--tm-color-heat-3";
    internal const string TokenHeat3Text = "--tm-color-heat-3-text";
    internal const string TokenHeatInk = "--tm-color-heat-ink";
    internal const string TokenPrimary = "--tm-color-primary";
    internal const string TokenSuccess = "--tm-color-success";
    internal const string TokenWarning = "--tm-color-warning";
    internal const string TokenBorderStrong = "--tm-border-color-strong";
    internal const string TokenDiffAddedStroke = "--tm-color-diff-added-stroke";
    internal const string TokenDiffChangedStroke = "--tm-color-diff-changed-stroke";
    internal const string TokenDiffLayoutStroke = "--tm-color-diff-layout-stroke";

    /// <summary>Every token the viewer may write into a presentation style — the export resolves exactly these.</summary>
    internal static readonly string[] ViewerTokens =
    [
        TokenHeat0, TokenHeat1, TokenHeat2, TokenHeat3, TokenHeat3Text, TokenHeatInk,
        TokenPrimary, TokenSuccess, TokenWarning, TokenBorderStrong,
        TokenDiffAddedStroke, TokenDiffChangedStroke, TokenDiffLayoutStroke
    ];

    /// <summary>
    /// The four-step scale of the heat overlay. Steps are quarter bands of the normalized value; the
    /// fills spread their lightness (a pale green through amber to a dark red) so the highest band is
    /// unmistakable without relying on hue alone.
    /// </summary>
    internal static int HeatBand(double value)
    {
        var normalized = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);
        if (normalized < 0.25)
        {
            return 0;
        }

        if (normalized < 0.5)
        {
            return 1;
        }

        return normalized < 0.75 ? 2 : 3;
    }

    /// <summary>Returns the four-step heat token for a normalized value (0..1 clamped).</summary>
    internal static string HeatToken(double value)
        => HeatBand(value) switch
        {
            0 => TokenHeat0,
            1 => TokenHeat1,
            2 => TokenHeat2,
            _ => TokenHeat3
        };

    /// <summary>
    /// The label colour token of a heat-tinted node: the dark bands keep white ink (the third and
    /// fourth band are saturated), the pale bands the paper ink — so the value is legible on both a
    /// white page and a dark shell.
    /// </summary>
    internal static string HeatTextToken(double value)
        => HeatBand(value) == 3 ? TokenHeat3Text : TokenHeatInk;

    /// <summary>The localization key of the band label ("Low", "Moderate", "Elevated", "High").</summary>
    internal static string HeatBandResourceKey(double value)
        => HeatBand(value) switch
        {
            0 => "TmModelingViewer_HeatBandLow",
            1 => "TmModelingViewer_HeatBandModerate",
            2 => "TmModelingViewer_HeatBandElevated",
            _ => "TmModelingViewer_HeatBandHigh"
        };

    /// <summary>
    /// The non-colour cue of a diff state: a short glyph rendered on the canvas node. The legend
    /// swatches mirror it with a matching border style so the state never rides on hue alone.
    /// </summary>
    internal static string? DiffBadge(string? state) => state switch
    {
        DiffAdded => "+",
        DiffChanged => "~",
        DiffLayout => "↔",
        _ => null
    };

    /// <summary>
    /// Builds the text-alternative rows: the drawn elements in BFS (topological) order, each followed
    /// by its outgoing relationships, then any unreached elements / relationships in model order, then
    /// the baseline-only removed rows.
    /// </summary>
    internal static IReadOnlyList<ModelingViewerListEntry> BuildEntries(
        ModelingModelDto model,
        DiagramDocument document,
        ModelingDiffOverlayDto? diff,
        ModelingHeatmapDto? heatmap,
        ModelingViewerOverlaySummary summary)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(document);

        var elementById = new Dictionary<string, ModelingElementDto>(StringComparer.Ordinal);
        foreach (var element in model.Elements)
        {
            if (!string.IsNullOrWhiteSpace(element.Id))
            {
                elementById.TryAdd(element.Id, element);
            }
        }

        var drawnElementIds = new HashSet<string>(StringComparer.Ordinal);
        var nodeSemanticType = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in document.Pages.SelectMany(page => page.Nodes))
        {
            var elementId = ReadNodeString(node, "modelElementId");
            if (string.IsNullOrWhiteSpace(elementId))
            {
                continue;
            }

            drawnElementIds.Add(elementId);
            if (ReadNodeString(node, "semanticType") is { Length: > 0 } semanticType)
            {
                nodeSemanticType[elementId] = semanticType;
            }
        }

        var edgeIds = new HashSet<string>(
            document.Pages.SelectMany(page => page.Edges).Select(edge => edge.Id),
            StringComparer.Ordinal);

        var presentRelationships = model.Relationships
            .Where(relationship => !string.IsNullOrWhiteSpace(relationship.Id)
                && edgeIds.Contains(relationship.Id)
                && drawnElementIds.Contains(relationship.SourceElementId)
                && drawnElementIds.Contains(relationship.TargetElementId))
            .ToList();

        var outgoing = presentRelationships
            .GroupBy(relationship => relationship.SourceElementId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(relationship => IndexOfRelationship(model, relationship)).ToList(),
                StringComparer.Ordinal);
        var incomingCount = drawnElementIds.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var relationship in presentRelationships)
        {
            if (incomingCount.ContainsKey(relationship.TargetElementId))
            {
                incomingCount[relationship.TargetElementId]++;
            }
        }

        var entries = new List<ModelingViewerListEntry>(drawnElementIds.Count + presentRelationships.Count);
        var visitedElements = new HashSet<string>(StringComparer.Ordinal);
        var emittedRelationships = new HashSet<string>(StringComparer.Ordinal);

        void AddElement(string elementId)
        {
            if (!visitedElements.Add(elementId))
            {
                return;
            }

            elementById.TryGetValue(elementId, out var element);
            entries.Add(new ModelingViewerListEntry
            {
                Id = elementId,
                IsRelationship = false,
                SemanticType = element?.SemanticType ?? nodeSemanticType.GetValueOrDefault(elementId, string.Empty),
                Name = DisplayName(element?.Name, elementId),
                DiffState = summary.StateFor(elementId),
                HeatmapValue = GetHeatValue(heatmap, elementId)
            });

            if (!outgoing.TryGetValue(elementId, out var list))
            {
                return;
            }

            foreach (var relationship in list)
            {
                if (!emittedRelationships.Add(relationship.Id))
                {
                    continue;
                }

                entries.Add(new ModelingViewerListEntry
                {
                    Id = relationship.Id,
                    IsRelationship = true,
                    SemanticType = relationship.RelationshipType,
                    Name = DisplayName(relationship.Name, relationship.Id),
                    DiffState = summary.StateFor(relationship.Id),
                    HeatmapValue = GetHeatValue(heatmap, relationship.Id)
                });
            }
        }

        var queue = new Queue<string>();
        foreach (var element in model.Elements)
        {
            if (drawnElementIds.Contains(element.Id)
                && incomingCount.GetValueOrDefault(element.Id) == 0)
            {
                queue.Enqueue(element.Id);
            }
        }

        if (queue.Count == 0)
        {
            foreach (var element in model.Elements)
            {
                if (drawnElementIds.Contains(element.Id))
                {
                    queue.Enqueue(element.Id);
                    break;
                }
            }
        }

        while (queue.Count > 0)
        {
            var elementId = queue.Dequeue();
            if (visitedElements.Contains(elementId))
            {
                continue;
            }

            AddElement(elementId);
            if (!outgoing.TryGetValue(elementId, out var list))
            {
                continue;
            }

            foreach (var relationship in list)
            {
                if (drawnElementIds.Contains(relationship.TargetElementId)
                    && !visitedElements.Contains(relationship.TargetElementId))
                {
                    queue.Enqueue(relationship.TargetElementId);
                }
            }
        }

        // A cycle (no start element) or a disconnected island: append the remainder in model order so
        // the text alternative is always complete and deterministic.
        foreach (var element in model.Elements)
        {
            if (drawnElementIds.Contains(element.Id))
            {
                AddElement(element.Id);
            }
        }

        foreach (var relationship in presentRelationships)
        {
            if (emittedRelationships.Add(relationship.Id))
            {
                entries.Add(new ModelingViewerListEntry
                {
                    Id = relationship.Id,
                    IsRelationship = true,
                    SemanticType = relationship.RelationshipType,
                    Name = DisplayName(relationship.Name, relationship.Id),
                    DiffState = summary.StateFor(relationship.Id),
                    HeatmapValue = GetHeatValue(heatmap, relationship.Id)
                });
            }
        }

        if (diff is not null)
        {
            AddRemovedEntries(entries, diff.RemovedElementIds, isRelationship: false, diff.BaselineModel);
            AddRemovedEntries(entries, diff.RemovedRelationshipIds, isRelationship: true, diff.BaselineModel);
        }

        return entries;
    }

    /// <summary>Applies the diff / heatmap / highlight / selection overlays to the viewer-owned copy.</summary>
    internal static ModelingViewerOverlaySummary ApplyPresentation(
        DiagramDocument document,
        ModelingDiffOverlayDto? diff,
        ModelingHeatmapDto? heatmap,
        IReadOnlyCollection<string>? highlightElementIds,
        string? selectedElementId)
    {
        ArgumentNullException.ThrowIfNull(document);

        var addedElements = ToSet(diff?.AddedElementIds);
        var changedElements = ToSet(diff?.ChangedElementIds);
        var layoutElements = ToSet(diff?.LayoutOnlyElementIds);
        var removedElements = ToSet(diff?.RemovedElementIds);
        var addedRelationships = ToSet(diff?.AddedRelationshipIds);
        var changedRelationships = ToSet(diff?.ChangedRelationshipIds);
        var removedRelationships = ToSet(diff?.RemovedRelationshipIds);
        var highlighted = ToSet(highlightElementIds);
        var states = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var page in document.Pages)
        {
            foreach (var node in page.Nodes)
            {
                ResetOverlayStyle(node.Style);
                node.Data.Remove(ViewerBadgeKey);
                var elementId = ReadNodeString(node, "modelElementId");
                if (string.IsNullOrWhiteSpace(elementId))
                {
                    node.Data.Remove(ViewerStateKey);
                    continue;
                }

                var heatValue = heatmap is not null && heatmap.Values.TryGetValue(elementId, out var value)
                    ? value
                    : (double?)null;
                if (heatValue is { } heat)
                {
                    node.Style.Fill = $"var({HeatToken(heat)})";
                    node.Style.Color = $"var({HeatTextToken(heat)})";
                    node.Style.Opacity = 0.85;
                }

                string? state = null;
                if (addedElements.Contains(elementId))
                {
                    ApplyDiffStroke(node.Style, DiffAdded);
                    state = DiffAdded;
                }
                else if (changedElements.Contains(elementId))
                {
                    ApplyDiffStroke(node.Style, DiffChanged);
                    state = DiffChanged;
                }
                else if (layoutElements.Contains(elementId))
                {
                    ApplyDiffStroke(node.Style, DiffLayout);
                    state = DiffLayout;
                }

                if (highlighted.Contains(elementId))
                {
                    state ??= HighlightState;
                    ApplyStroke(node.Style, TokenPrimary, width: 3);
                }

                if (string.Equals(selectedElementId, elementId, StringComparison.Ordinal))
                {
                    ApplyStroke(node.Style, TokenPrimary, width: 3);
                    node.Style.StrokeDasharray = null;
                }

                if (state is null && heatValue is not null)
                {
                    state = HeatmapState;
                }

                if (state is null)
                {
                    node.Data.Remove(ViewerStateKey);
                }
                else
                {
                    node.Data[ViewerStateKey] = state;
                    states[elementId] = state;
                    if (DiffBadge(state) is { } badge)
                    {
                        node.Data[ViewerBadgeKey] = badge;
                    }
                }
            }

            foreach (var edge in page.Edges)
            {
                ResetOverlayStyle(edge.Style);
                edge.Title = null;
                var relationshipId = edge.Id;
                var heatValue = heatmap is not null && heatmap.Values.TryGetValue(relationshipId, out var value)
                    ? value
                    : (double?)null;
                if (heatValue is { } heat)
                {
                    edge.Style.Stroke = $"var({HeatToken(heat)})";
                    edge.Style.StrokeWidth = 3;
                    edge.Style.Opacity = 0.9;
                }

                string? state = null;
                if (addedRelationships.Contains(relationshipId))
                {
                    ApplyDiffStroke(edge.Style, DiffAdded, isEdge: true);
                    state = DiffAdded;
                }
                else if (changedRelationships.Contains(relationshipId))
                {
                    ApplyDiffStroke(edge.Style, DiffChanged, isEdge: true);
                    state = DiffChanged;
                }

                if (highlighted.Contains(relationshipId))
                {
                    state ??= HighlightState;
                    ApplyStroke(edge.Style, TokenPrimary, width: 3);
                }

                if (string.Equals(selectedElementId, relationshipId, StringComparison.Ordinal))
                {
                    ApplyStroke(edge.Style, TokenPrimary, width: 3);
                    edge.Style.StrokeDasharray = null;
                }

                if (state is null && heatValue is not null)
                {
                    state = HeatmapState;
                }

                if (state is not null)
                {
                    states[relationshipId] = state;
                }
            }
        }

        foreach (var removed in removedElements)
        {
            states.Remove(removed);
        }

        foreach (var removed in removedRelationships)
        {
            states.Remove(removed);
        }

        return new ModelingViewerOverlaySummary
        {
            States = states,
            AddedCount = addedElements.Count + addedRelationships.Count,
            RemovedCount = removedElements.Count + removedRelationships.Count,
            ChangedCount = changedElements.Count + changedRelationships.Count,
            LayoutCount = layoutElements.Count
        };
    }

    /// <summary>Returns true when the document has no drawn node or edge.</summary>
    internal static bool IsEmpty(DiagramDocument? document)
        => document is null || document.Pages.All(page => page.Nodes.Count == 0 && page.Edges.Count == 0);

    /// <summary>
    /// Shrinks the presentation copy's page to the drawn content plus a small margin. The generator
    /// emits its default 3000×2000 page, so a fitted diagram was surrounded by empty paper: at
    /// fit-to-width the content sat in the top-left of a huge white sheet and the page's own outline
    /// showed up as a stray line/gutter (review fix, U10). Only the PAGE shrinks — never a node
    /// position: a stored view's coordinates stay absolute, and the fitted scale is computed from the
    /// content anyway. Both the document proxy and every page are set, so a multi-page copy keeps a
    /// consistent paper size.
    /// </summary>
    internal static void FitPageToContent(DiagramDocument document, double margin = 24)
    {
        ArgumentNullException.ThrowIfNull(document);

        var maxX = 0.0;
        var maxY = 0.0;
        var any = false;
        foreach (var page in document.Pages)
        {
            foreach (var node in page.Nodes)
            {
                maxX = Math.Max(maxX, node.X + node.W);
                maxY = Math.Max(maxY, node.Y + node.H);
                any = true;
            }

            foreach (var edge in page.Edges)
            {
                foreach (var point in edge.Waypoints)
                {
                    maxX = Math.Max(maxX, point.X);
                    maxY = Math.Max(maxY, point.Y);
                }

                if (edge.SourcePoint is { } source)
                {
                    maxX = Math.Max(maxX, source.X);
                    maxY = Math.Max(maxY, source.Y);
                }

                if (edge.TargetPoint is { } target)
                {
                    maxX = Math.Max(maxX, target.X);
                    maxY = Math.Max(maxY, target.Y);
                }
            }
        }

        if (!any)
        {
            return;
        }

        var width = maxX + margin;
        var height = maxY + margin;
        foreach (var page in document.Pages)
        {
            page.Width = width;
            page.Height = height;
        }
    }

    /// <summary>Formats a heatmap value with the overlay format (invariant fallback 0..1 with two decimals).</summary>
    internal static string FormatHeatValue(ModelingHeatmapDto? heatmap, double value)
        => string.IsNullOrWhiteSpace(heatmap?.Format)
            ? value.ToString("0.##", CultureInfo.CurrentCulture)
            : string.Format(CultureInfo.CurrentCulture, heatmap!.Format!, value);

    private static void AddRemovedEntries(
        List<ModelingViewerListEntry> entries,
        IReadOnlyCollection<string>? ids,
        bool isRelationship,
        ModelingModelDto? baseline)
    {
        if (ids is null)
        {
            return;
        }

        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            string semanticType;
            string name;
            if (isRelationship)
            {
                var relationship = baseline?.Relationships.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, id, StringComparison.Ordinal));
                semanticType = relationship?.RelationshipType ?? string.Empty;
                name = DisplayName(relationship?.Name, id);
            }
            else
            {
                var element = baseline?.Elements.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, id, StringComparison.Ordinal));
                semanticType = element?.SemanticType ?? string.Empty;
                name = DisplayName(element?.Name, id);
            }

            entries.Add(new ModelingViewerListEntry
            {
                Id = id,
                IsRelationship = isRelationship,
                SemanticType = semanticType,
                Name = name,
                IsRemoved = true,
                DiffState = DiffRemoved
            });
        }
    }

    private static int IndexOfRelationship(ModelingModelDto model, ModelingRelationshipDto relationship)
    {
        for (var index = 0; index < model.Relationships.Count; index++)
        {
            if (ReferenceEquals(model.Relationships[index], relationship))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static string DisplayName(string? name, string id)
        => string.IsNullOrWhiteSpace(name) ? id : name;

    /// <summary>The heatmap value of an id, or null when the overlay has none.</summary>
    private static double? GetHeatValue(ModelingHeatmapDto? heatmap, string id)
        => heatmap is not null && heatmap.Values.TryGetValue(id, out var value) ? value : null;

    private static HashSet<string> ToSet(IReadOnlyCollection<string>? values)
        => values is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(values.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.Ordinal);

    /// <summary>
    /// The presentation styles an overlay may write, captured the FIRST time the viewer touches a
    /// style object. A later pass restores them instead of writing <c>null</c>, so the generator's own
    /// stencil paint (an edge's <c>#123456</c>, a node's inherited fill) survives an overlay that no
    /// longer covers the item. The table is weak: the entry dies with the presentation copy.
    /// </summary>
    private static readonly ConditionalWeakTable<DiagramStyle, StyleSnapshot> OriginalStyles = new();

    private sealed class StyleSnapshot
    {
        public string? Fill { get; init; }

        public string? Stroke { get; init; }

        public double? StrokeWidth { get; init; }

        public string? StrokeDasharray { get; init; }

        public double? Opacity { get; init; }

        public string? Color { get; init; }
    }

    private static void ResetOverlayStyle(DiagramStyle style)
    {
        var original = OriginalStyles.GetValue(style, static candidate => new StyleSnapshot
        {
            Fill = candidate.Fill,
            Stroke = candidate.Stroke,
            StrokeWidth = candidate.StrokeWidth,
            StrokeDasharray = candidate.StrokeDasharray,
            Opacity = candidate.Opacity,
            Color = candidate.Color
        });

        style.Fill = original.Fill;
        style.Stroke = original.Stroke;
        style.StrokeWidth = original.StrokeWidth;
        style.StrokeDasharray = original.StrokeDasharray;
        style.Opacity = original.Opacity;
        style.Color = original.Color;
    }

    /// <summary>
    /// A diff stroke: a dedicated high-contrast colour token plus a pattern cue — solid for an added
    /// item, a fine dot for a changed one and a long dash for a layout-only move. An edge has no
    /// outline to dash against a page, so it only takes the colour and the weight.
    /// </summary>
    private static void ApplyDiffStroke(DiagramStyle style, string state, bool isEdge = false)
    {
        var (token, dash, width) = state switch
        {
            DiffAdded => (TokenDiffAddedStroke, null, 3.0),
            DiffChanged => (TokenDiffChangedStroke, "2,2", 2.5),
            _ => (TokenDiffLayoutStroke, "6,3", 2.5)
        };

        ApplyStroke(style, token, width);
        style.StrokeDasharray = isEdge ? null : dash;
    }

    private static void ApplyStroke(DiagramStyle style, string token, double width = 2.5)
    {
        style.Stroke = $"var({token})";
        style.StrokeWidth = width;
    }

    private static string? ReadNodeString(DiagramNode node, string key)
        => node.Data.TryGetValue(key, out var value) ? value?.ToString() : null;
}
