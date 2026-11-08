using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tempo.Blazor.Modeling;

/// <summary>
/// Headless model diff (phase 7 of the full BPMN authoring plan): compares two <see cref="ModelingModelDto"/>
/// versions and returns the overlay <c>TmModelingViewer</c> renders, so a host or an MCP agent can
/// compute it WITHOUT referencing the Blazor package.
/// <para>
/// The result is a pure function of the two models — no ids are resolved against any registry and
/// nothing is mutated. Classification rules:
/// </para>
/// <list type="bullet">
/// <item><description><b>Added / removed</b> — an id present in only one of the two models. Added ids are
/// listed in the CURRENT model's order, removed ids in the BASELINE's order, so the overlay is stable
/// for a given pair of models.</description></item>
/// <item><description><b>Changed</b> — an id present in both whose semantic fields differ
/// (<c>Name</c>, <c>Description</c>, <c>SemanticType</c>, <c>Notation</c>, <c>ParentId</c>,
/// <c>Tags</c>, <c>Properties</c>, <c>Governance</c> and the source triple for an element;
/// <c>RelationshipType</c>, endpoints, <c>Name</c>, <c>Tags</c>, <c>Properties</c> and the source
/// triple for a relationship).</description></item>
/// <item><description><b>Layout-only</b> — an element present in both whose semantics are IDENTICAL but
/// whose stored view geometry (<see cref="ModelingViewNodeDto.X"/>, <c>Y</c>, <c>Width</c>,
/// <c>Height</c>, <c>ParentNodeId</c>) or view-node MEMBERSHIP differs in at least one view. A
/// relationship's layout (waypoints / a view connection appearing or disappearing) is not part of the
/// overlay — the overlay distinguishes relationships only as added, removed or changed.</description></item>
/// <item><description>A change beats a layout change: an element whose semantics changed is reported in
/// <see cref="ModelingDiffOverlayDto.ChangedElementIds"/> and never also in
/// <see cref="ModelingDiffOverlayDto.LayoutOnlyElementIds"/>.</description></item>
/// </list>
/// </summary>
public static class ModelingModelDiff
{
    /// <summary>
    /// Computes the diff overlay between <paramref name="baseline"/> and <paramref name="current"/>.
    /// </summary>
    /// <param name="baseline">The "before" model. <c>null</c> means an empty model: everything in
    /// <paramref name="current"/> is reported as added and <see cref="ModelingDiffOverlayDto.BaselineModel"/>
    /// stays <c>null</c> (there is nothing to name a removed item from).</param>
    /// <param name="current">The "after" model. <c>null</c> means an empty model: everything in
    /// <paramref name="baseline"/> is reported as removed.</param>
    /// <returns>A fully populated <see cref="ModelingDiffOverlayDto"/>; never <c>null</c>.</returns>
    public static ModelingDiffOverlayDto Compute(ModelingModelDto? baseline, ModelingModelDto? current)
    {
        var hasBaseline = baseline is not null;
        baseline ??= new ModelingModelDto();
        current ??= new ModelingModelDto();

        var addedElements = new List<string>();
        var removedElements = new List<string>();
        var changedElements = new List<string>();
        var layoutElements = new List<string>();
        var addedRelationships = new List<string>();
        var removedRelationships = new List<string>();
        var changedRelationships = new List<string>();

        DiffElements(baseline, current, addedElements, removedElements, changedElements, layoutElements);
        DiffRelationships(baseline, current, addedRelationships, removedRelationships, changedRelationships);

        return new ModelingDiffOverlayDto
        {
            AddedElementIds = addedElements,
            RemovedElementIds = removedElements,
            ChangedElementIds = changedElements,
            LayoutOnlyElementIds = layoutElements,
            AddedRelationshipIds = addedRelationships,
            RemovedRelationshipIds = removedRelationships,
            ChangedRelationshipIds = changedRelationships,
            BaselineModel = hasBaseline && !ReferenceEquals(baseline, current) ? baseline : null
        };
    }

    private static void DiffElements(
        ModelingModelDto baseline,
        ModelingModelDto current,
        List<string> added,
        List<string> removed,
        List<string> changed,
        List<string> layout)
    {
        var baselineById = new Dictionary<string, ModelingElementDto>(StringComparer.Ordinal);
        foreach (var element in baseline.Elements)
        {
            baselineById[element.Id] = element;
        }

        var currentIds = new HashSet<string>(StringComparer.Ordinal);
        var layoutCandidates = new List<string>();
        foreach (var element in current.Elements)
        {
            currentIds.Add(element.Id);
            if (!baselineById.TryGetValue(element.Id, out var before))
            {
                added.Add(element.Id);
                continue;
            }

            if (!SemanticEquals(before, element))
            {
                changed.Add(element.Id);
                continue;
            }

            layoutCandidates.Add(element.Id);
        }

        foreach (var element in baseline.Elements)
        {
            if (!currentIds.Contains(element.Id))
            {
                removed.Add(element.Id);
            }
        }

        if (layoutCandidates.Count == 0 || ViewGeometryEqual(baseline, current, layoutCandidates))
        {
            return;
        }

        foreach (var elementId in layoutCandidates)
        {
            if (!ElementLayoutEquals(baseline, current, elementId))
            {
                layout.Add(elementId);
            }
        }
    }

    private static void DiffRelationships(
        ModelingModelDto baseline,
        ModelingModelDto current,
        List<string> added,
        List<string> removed,
        List<string> changed)
    {
        var baselineById = new Dictionary<string, ModelingRelationshipDto>(StringComparer.Ordinal);
        foreach (var relationship in baseline.Relationships)
        {
            baselineById[relationship.Id] = relationship;
        }

        var currentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relationship in current.Relationships)
        {
            currentIds.Add(relationship.Id);
            if (!baselineById.TryGetValue(relationship.Id, out var before))
            {
                added.Add(relationship.Id);
                continue;
            }

            if (!SemanticEquals(before, relationship))
            {
                changed.Add(relationship.Id);
            }
        }

        foreach (var relationship in baseline.Relationships)
        {
            if (!currentIds.Contains(relationship.Id))
            {
                removed.Add(relationship.Id);
            }
        }
    }

    /// <summary>Cheap gate: when NO candidate's geometry moved anywhere, the per-element pass is skipped.</summary>
    private static bool ViewGeometryEqual(
        ModelingModelDto baseline,
        ModelingModelDto current,
        List<string> candidates)
    {
        var candidateIds = new HashSet<string>(candidates, StringComparer.Ordinal);
        return NodeIndex(baseline, candidateIds).SequenceEqual(NodeIndex(current, candidateIds));
    }

    /// <summary>
    /// Every view-node placement of the candidate ids as a comparable sequence
    /// (<c>viewId</c>, <c>elementId</c>, geometry), ordered by view order then node order. A view that
    /// only one model has yields a shorter sequence, so membership changes are caught too.
    /// </summary>
    private static IEnumerable<string> NodeIndex(ModelingModelDto model, HashSet<string> elementIds)
    {
        foreach (var view in model.Views)
        {
            foreach (var node in view.Nodes)
            {
                if (elementIds.Contains(node.ElementId))
                {
                    yield return string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"{view.Id}\u001f{node.ElementId}\u001f{node.X:R}\u001f{node.Y:R}\u001f{node.Width:R}\u001f{node.Height:R}\u001f{node.ParentNodeId ?? string.Empty}");
                }
            }
        }
    }

    private static bool ElementLayoutEquals(
        ModelingModelDto baseline,
        ModelingModelDto current,
        string elementId)
    {
        var ids = new HashSet<string>([elementId], StringComparer.Ordinal);
        var before = NodeIndex(baseline, ids).ToList();
        var after = NodeIndex(current, ids).ToList();
        return before.Count == after.Count && before.SequenceEqual(after);
    }

    private static bool SemanticEquals(ModelingElementDto before, ModelingElementDto after)
        => string.Equals(before.SourceId, after.SourceId, StringComparison.Ordinal)
           && string.Equals(before.SourceType, after.SourceType, StringComparison.Ordinal)
           && string.Equals(before.SourcePath, after.SourcePath, StringComparison.Ordinal)
           && string.Equals(before.Notation, after.Notation, StringComparison.Ordinal)
           && string.Equals(before.SemanticType, after.SemanticType, StringComparison.Ordinal)
           && string.Equals(before.Name, after.Name, StringComparison.Ordinal)
           && string.Equals(before.Description, after.Description, StringComparison.Ordinal)
           && string.Equals(before.ParentId, after.ParentId, StringComparison.Ordinal)
           && string.Equals(before.Governance.TrustLevel, after.Governance.TrustLevel, StringComparison.Ordinal)
           && string.Equals(before.Governance.ReviewState, after.Governance.ReviewState, StringComparison.Ordinal)
           && string.Equals(before.Governance.SyncState, after.Governance.SyncState, StringComparison.Ordinal)
           && string.Equals(before.Governance.DataSource, after.Governance.DataSource, StringComparison.Ordinal)
           && TagsEqual(before.Tags, after.Tags)
           && JsonEquals(before.Properties, after.Properties);

    private static bool SemanticEquals(ModelingRelationshipDto before, ModelingRelationshipDto after)
        => string.Equals(before.SourceId, after.SourceId, StringComparison.Ordinal)
           && string.Equals(before.SourceType, after.SourceType, StringComparison.Ordinal)
           && string.Equals(before.SourceElementId, after.SourceElementId, StringComparison.Ordinal)
           && string.Equals(before.TargetElementId, after.TargetElementId, StringComparison.Ordinal)
           && string.Equals(before.RelationshipType, after.RelationshipType, StringComparison.Ordinal)
           && string.Equals(before.Name, after.Name, StringComparison.Ordinal)
           && TagsEqual(before.Tags, after.Tags)
           && JsonEquals(before.Properties, after.Properties);

    /// <summary>
    /// Compares two property values by JSON VALUE: a different JSON kind is a change, while a
    /// reformatted-but-equal document (whitespace, object member order) is not.
    /// </summary>
    private static bool JsonValueEquals(JsonElement before, JsonElement after)
        => before.ValueKind == after.ValueKind
           && JsonNode.DeepEquals(JsonNode.Parse(before.GetRawText()), JsonNode.Parse(after.GetRawText()));

    private static bool TagsEqual(List<string> before, List<string> after)
        => before.Count == after.Count && before.SequenceEqual(after, StringComparer.Ordinal);

    /// <summary>
    /// Property maps compare by VALUE, not by insertion order: two models that describe the same values
    /// in a different order are not a change. A value's JSON kind is part of the comparison
    /// (<c>1</c> and <c>"1"</c> differ).
    /// </summary>
    private static bool JsonEquals(
        Dictionary<string, JsonElement> before,
        Dictionary<string, JsonElement> after)
    {
        if (before.Count != after.Count)
        {
            return false;
        }

        foreach (var (key, value) in before)
        {
            if (!after.TryGetValue(key, out var other))
            {
                return false;
            }

            if (!JsonValueEquals(value, other))
            {
                return false;
            }
        }

        return true;
    }
}
