using System.Text.Json;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// The baseline → pending-diff half of the editor's editing contract (decisions
/// <c>MODELING-INSPECTOR-EDITING</c> + <c>MODELING-AUTHORING-CHANGESET</c>). The loaded model is
/// snapshotted into a CLONE (the baseline) and the pending change set is the DIFF of the live model
/// against it — covering content (name/description/properties), STRUCTURE (added/removed elements
/// and relationships, parent moves, re-targets) and LAYOUT (view nodes, their parent node and the
/// connection waypoints). Because it is a diff and not an accumulator, undo/redo, a discarded edit,
/// a reload or an adopted saved model keep it consistent with no field-by-field bookkeeping.
/// <para>
/// The baseline is advanced after a successful save by applying the SENT change set to a CLONE of
/// the baseline through the shared <see cref="ModelingChangeSetTranslator"/> + the ONE
/// <see cref="ModelingOperationEngine"/> — never by copying the live model — so an edit that raced
/// the save stays pending and undoable.
/// </para>
/// </summary>
public partial class TmModelingEditor
{
    /// <summary>The (model instance, provider key) pair the baseline was captured for.</summary>
    private (ModelingModelDto? Model, string? ProviderKey) _baselineKey;

    /// <summary>The loaded model as of the last load / successful save / adopted model.</summary>
    private ModelingModelDto? _baselineModel;

    /// <summary>The cached pending change set and the key it was computed for (the diff is an
    /// O(model) pass and the render path reads it several times per render).</summary>
    private ModelingModelChangeSet? _pendingChangeSetCache;

    private (int CommitVersion, ModelingModelDto? Model, string? ProviderKey) _pendingChangeSetCacheKey;

    /// <summary>
    /// Captures the baseline snapshot the pending set is diffed against: a deep CLONE of the model
    /// as loaded (or as last saved/adopted), so later in-place mutations of the live DTOs cannot
    /// leak into the comparison.
    /// </summary>
    private void CaptureBaseline(ModelingModelDto model)
    {
        _baselineModel = ModelingOperationEngine.Clone(model);
        _baselineKey = (model, EffectiveProviderKey);
        // A new baseline changes the diff inputs without bumping the commit version — the cached
        // pending set must go.
        _pendingChangeSetCache = null;
    }

    /// <summary>
    /// The pending change set: the diff of the LIVE model against the baseline. Returns null when
    /// no baseline was captured for the current model + provider pair (an EMPTY change set means
    /// "nothing differs"). Cached on the commit version + model + provider triple — a commit, an
    /// undo/redo or a baseline change invalidates it, an unrelated re-render reuses it.
    /// </summary>
    private ModelingModelChangeSet? ComputePendingChangeSet()
    {
        if (_model is null || _baselineKey.Model != _model
            || !string.Equals(_baselineKey.ProviderKey, EffectiveProviderKey, StringComparison.Ordinal)
            || _baselineModel is null)
        {
            return null;
        }

        var cacheKey = (_commitVersion, _model, EffectiveProviderKey);
        if (_pendingChangeSetCache is not null && _pendingChangeSetCacheKey.Equals(cacheKey))
        {
            return _pendingChangeSetCache;
        }

        var changeSet = BuildPendingChangeSet(_model);
        _pendingChangeSetCache = changeSet;
        _pendingChangeSetCacheKey = cacheKey;
        return changeSet;
    }

    private ModelingModelChangeSet? BuildPendingChangeSet(ModelingModelDto model)
    {
        if (_baselineModel is not { } baseline)
        {
            return null;
        }

        var changeSet = new ModelingModelChangeSet
        {
            ModelId = model.Id,
            ProviderKey = EffectiveProviderKey ?? string.Empty,
            BaseVersion = model.Metadata.SourceVersion
        };

        AppendElementChanges(changeSet, baseline, model);
        AppendRelationshipChanges(changeSet, baseline, model);
        AppendViewChanges(changeSet, baseline, model);
        return changeSet;
    }

    /// <summary>Whether a change set carries anything at all — the save gate and the pending
    /// indicator read this ONE predicate.</summary>
    private static bool HasAnyChange(ModelingModelChangeSet? changeSet)
        => changeSet is not null
            && (changeSet.Elements.Count > 0 || changeSet.Relationships.Count > 0
                || changeSet.HasStructuralChanges || changeSet.HasViewChanges);

    private static void AppendElementChanges(ModelingModelChangeSet changeSet, ModelingModelDto baseline, ModelingModelDto model)
    {
        var baselineElements = baseline.Elements.ToDictionary(element => element.Id, StringComparer.Ordinal);
        var liveElementIds = new HashSet<string>(model.Elements.Select(element => element.Id), StringComparer.Ordinal);

        foreach (var element in model.Elements)
        {
            if (!baselineElements.TryGetValue(element.Id, out var original))
            {
                changeSet.AddedElements.Add(new ModelingElementAdd
                {
                    Id = element.Id,
                    SemanticType = element.SemanticType,
                    Name = element.Name,
                    Notation = element.Notation,
                    Description = element.Description,
                    ParentId = element.ParentId,
                    Properties = new Dictionary<string, JsonElement>(element.Properties, StringComparer.Ordinal)
                });
                continue;
            }

            var change = new ModelingElementChange { ElementId = element.Id };
            if (!string.Equals(element.Name, original.Name, StringComparison.Ordinal))
            {
                change.Name = element.Name;
            }

            if (!string.Equals(element.Description, original.Description, StringComparison.Ordinal))
            {
                change.Description = element.Description;
            }

            var (setProperties, removedProperties) = DiffProperties(original.Properties, element.Properties);
            if (setProperties is not null)
            {
                change.SetProperties = setProperties;
            }

            if (removedProperties is not null)
            {
                change.RemovedProperties = removedProperties;
            }

            // null = unchanged, "" = the element became a root (a real value).
            if (!string.Equals(element.ParentId ?? string.Empty, original.ParentId ?? string.Empty, StringComparison.Ordinal))
            {
                change.ParentId = element.ParentId ?? string.Empty;
            }

            if (change.Name is not null || change.Description is not null || change.ParentId is not null
                || change.SetProperties is not null || change.RemovedProperties is not null)
            {
                changeSet.Elements.Add(change);
            }
        }

        foreach (var elementId in OrderRemovalsDescendantsFirst(
                     baseline,
                     baseline.Elements.Where(element => !liveElementIds.Contains(element.Id)).Select(element => element.Id)))
        {
            changeSet.RemovedElementIds.Add(elementId);
        }
    }

    private static void AppendRelationshipChanges(ModelingModelChangeSet changeSet, ModelingModelDto baseline, ModelingModelDto model)
    {
        var baselineRelationships = baseline.Relationships.ToDictionary(relationship => relationship.Id, StringComparer.Ordinal);
        var liveRelationshipIds = new HashSet<string>(model.Relationships.Select(relationship => relationship.Id), StringComparer.Ordinal);

        foreach (var relationship in model.Relationships)
        {
            if (!baselineRelationships.TryGetValue(relationship.Id, out var original))
            {
                changeSet.AddedRelationships.Add(new ModelingRelationshipAdd
                {
                    Id = relationship.Id,
                    RelationshipType = relationship.RelationshipType,
                    SourceElementId = relationship.SourceElementId,
                    TargetElementId = relationship.TargetElementId,
                    Name = relationship.Name,
                    Properties = new Dictionary<string, JsonElement>(relationship.Properties, StringComparer.Ordinal)
                });
                continue;
            }

            var change = new ModelingRelationshipChange { RelationshipId = relationship.Id };
            if (!string.Equals(relationship.Name, original.Name, StringComparison.Ordinal))
            {
                change.Name = relationship.Name;
            }

            // A re-target is structural (null = unchanged); a preserved endpoint stays null.
            if (!string.Equals(relationship.SourceElementId, original.SourceElementId, StringComparison.Ordinal))
            {
                change.SourceElementId = relationship.SourceElementId;
            }

            if (!string.Equals(relationship.TargetElementId, original.TargetElementId, StringComparison.Ordinal))
            {
                change.TargetElementId = relationship.TargetElementId;
            }

            var (setProperties, removedProperties) = DiffProperties(original.Properties, relationship.Properties);
            if (setProperties is not null)
            {
                change.SetProperties = setProperties;
            }

            if (removedProperties is not null)
            {
                change.RemovedProperties = removedProperties;
            }

            if (change.Name is not null || change.SourceElementId is not null || change.TargetElementId is not null
                || change.SetProperties is not null || change.RemovedProperties is not null)
            {
                changeSet.Relationships.Add(change);
            }
        }

        foreach (var relationship in baseline.Relationships.Where(relationship => !liveRelationshipIds.Contains(relationship.Id)))
        {
            changeSet.RemovedRelationshipIds.Add(relationship.Id);
        }
    }

    /// <summary>
    /// The LAYOUT section: the diff of EVERY view (phase 3 review, A9 — a node duplicated into two
    /// views must report both removals, and a save must tell the writer about all of them), one
    /// <see cref="ModelingViewChange"/> per view with a difference. A node or connection the view did
    /// not have is an upsert; one it no longer has is a removal. A model WITHOUT a view diffs to
    /// "every node and connection is new", which is exactly the seed the writer needs (decision
    /// <c>MODELING-AUTHORING-CHANGESET</c> §3.2 rule 8).
    /// </summary>
    private void AppendViewChanges(ModelingModelChangeSet changeSet, ModelingModelDto baseline, ModelingModelDto model)
    {
        var viewIds = model.Views.Select(view => view.Id)
            .Concat(baseline.Views.Select(view => view.Id))
            .Distinct(StringComparer.Ordinal);
        foreach (var viewId in viewIds)
        {
            AppendViewChange(changeSet, baseline, model, viewId);
        }
    }

    /// <summary>One view's diff (upserts + removals) appended when it carries any difference.</summary>
    private static void AppendViewChange(ModelingModelChangeSet changeSet, ModelingModelDto baseline, ModelingModelDto model, string viewId)
    {
        var liveView = FindView(model, viewId);
        if (liveView is null)
        {
            return;
        }

        var baselineView = FindView(baseline, viewId);
        var baselineNodes = (baselineView?.Nodes ?? []).ToDictionary(node => node.ElementId, StringComparer.Ordinal);
        var liveElementIds = new HashSet<string>(liveView.Nodes.Select(node => node.ElementId), StringComparer.Ordinal);
        var change = new ModelingViewChange { ViewId = viewId };

        foreach (var node in liveView.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.ElementId))
            {
                continue;
            }

            baselineNodes.TryGetValue(node.ElementId, out var original);
            var nodeChange = new ModelingViewNodeChange { ElementId = node.ElementId };
            var changed = original is null;

            if (original is null || !node.X.Equals(original.X))
            {
                nodeChange.X = node.X;
                changed = true;
            }

            if (original is null || !node.Y.Equals(original.Y))
            {
                nodeChange.Y = node.Y;
                changed = true;
            }

            if (original is null || !node.Width.Equals(original.Width))
            {
                nodeChange.Width = node.Width;
                changed = true;
            }

            if (original is null || !node.Height.Equals(original.Height))
            {
                nodeChange.Height = node.Height;
                changed = true;
            }

            if (original is null)
            {
                // A node that does not exist yet carries its parent (null = root).
                nodeChange.ParentNodeId = string.IsNullOrEmpty(node.ParentNodeId) ? null : node.ParentNodeId;
            }
            else if (!string.Equals(node.ParentNodeId ?? string.Empty, original.ParentNodeId ?? string.Empty, StringComparison.Ordinal))
            {
                nodeChange.ParentNodeId = node.ParentNodeId ?? string.Empty;
            }

            if (changed)
            {
                change.Nodes.Add(nodeChange);
            }
        }

        foreach (var original in baselineView?.Nodes ?? [])
        {
            if (!liveElementIds.Contains(original.ElementId))
            {
                change.RemovedNodeElementIds.Add(original.ElementId);
            }
        }

        var baselineConnections = (baselineView?.Connections ?? []).ToDictionary(connection => connection.RelationshipId, StringComparer.Ordinal);
        var liveConnectionIds = new HashSet<string>(liveView.Connections.Select(connection => connection.RelationshipId), StringComparer.Ordinal);

        foreach (var connection in liveView.Connections)
        {
            if (string.IsNullOrWhiteSpace(connection.RelationshipId))
            {
                continue;
            }

            baselineConnections.TryGetValue(connection.RelationshipId, out var original);
            if (original is not null && WaypointsEqual(connection.Waypoints, original.Waypoints))
            {
                continue;
            }

            change.Connections.Add(new ModelingViewConnectionChange
            {
                RelationshipId = connection.RelationshipId,
                // A NEW connection upserts with the current (usually empty) routing; an existing
                // one sends the new bend points (an empty list clears the manual routing).
                Waypoints = [.. connection.Waypoints.Select(waypoint => new ModelingViewWaypointDto { X = waypoint.X, Y = waypoint.Y })]
            });
        }

        foreach (var original in baselineView?.Connections ?? [])
        {
            if (!liveConnectionIds.Contains(original.RelationshipId))
            {
                change.RemovedConnectionRelationshipIds.Add(original.RelationshipId);
            }
        }

        if (change.Nodes.Count > 0 || change.RemovedNodeElementIds.Count > 0
            || change.Connections.Count > 0 || change.RemovedConnectionRelationshipIds.Count > 0)
        {
            changeSet.ViewChanges.Add(change);
        }
    }

    private static ModelingViewDto? FindView(ModelingModelDto model, string viewId)
        => string.IsNullOrEmpty(viewId)
            ? model.Views.FirstOrDefault()
            : model.Views.FirstOrDefault(view => string.Equals(view.Id, viewId, StringComparison.Ordinal));

    private static bool WaypointsEqual(
        IReadOnlyList<ModelingViewWaypointDto> left,
        IReadOnlyList<ModelingViewWaypointDto> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!left[index].X.Equals(right[index].X) || !left[index].Y.Equals(right[index].Y))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Orders the removed element ids DEVANCESTORS-FIRST (deepest first), so the engine's
    /// <c>still_has_children</c> check passes without a silent cascade (decision Q3 — the change set
    /// carries the expansion the editor produced). Declaration order is the stable tie-break.
    /// </summary>
    private static List<string> OrderRemovalsDescendantsFirst(ModelingModelDto baseline, IEnumerable<string> removedIds)
    {
        var parentById = baseline.Elements.ToDictionary(
            element => element.Id,
            element => element.ParentId ?? string.Empty,
            StringComparer.Ordinal);
        var ids = removedIds.ToList();
        return [.. ids.OrderByDescending(id => HierarchyDepth(parentById, id))];
    }

    private static int HierarchyDepth(IReadOnlyDictionary<string, string> parentById, string elementId)
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

    private static (Dictionary<string, JsonElement>? Set, List<string>? Removed) DiffProperties(
        IReadOnlyDictionary<string, JsonElement> baseline,
        IReadOnlyDictionary<string, JsonElement> current)
    {
        Dictionary<string, JsonElement>? set = null;
        List<string>? removed = null;

        foreach (var property in current)
        {
            if (!baseline.TryGetValue(property.Key, out var original) || !JsonElementEquals(original, property.Value))
            {
                set ??= new Dictionary<string, JsonElement>();
                set[property.Key] = property.Value.Clone();
            }
        }

        foreach (var key in baseline.Keys)
        {
            if (!current.ContainsKey(key))
            {
                removed ??= [];
                removed.Add(key);
            }
        }

        return (set, removed);
    }

    /// <summary>JSON-kind-preserving deep comparison (the .NET 8 multi-target has no
    /// <c>JsonElement.DeepEquals</c>).</summary>
    private static bool JsonElementEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.Object => left.EnumerateObject().SequenceEqual(
                right.EnumerateObject(),
                new JsonPropertyEqualityComparer()),
            JsonValueKind.Array => left.GetArrayLength() == right.GetArrayLength()
                && left.EnumerateArray().Zip(right.EnumerateArray(), JsonElementEquals).All(equal => equal),
            // Number/True/False/Null/String compare by their raw JSON text — GetString() would
            // throw on the non-string kinds.
            _ => string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal)
        };
    }

    private sealed class JsonPropertyEqualityComparer : IEqualityComparer<JsonProperty>
    {
        public bool Equals(JsonProperty x, JsonProperty y)
            => string.Equals(x.Name, y.Name, StringComparison.Ordinal) && JsonElementEquals(x.Value, y.Value);

        public int GetHashCode(JsonProperty obj) => obj.Name.GetHashCode(StringComparison.Ordinal);
    }

    /// <summary>
    /// Applies a SENT change set onto a CLONE of the BASELINE (review fix, phase 14 + the v2
    /// sections): the baseline becomes "the state the source now has" without touching the live
    /// model, so a commit that raced the save stays pending and undoable against the advanced
    /// baseline. A change set the engine rejects leaves the previous baseline in place (the sent
    /// changes stay pending instead of silently disappearing).
    /// </summary>
    private void ApplyChangeSetToBaseline(ModelingModelChangeSet changeSet)
    {
        if (_baselineModel is not { } baseline)
        {
            return;
        }

        var translation = ModelingChangeSetTranslator.ToOperationBatch(changeSet);
        if (translation.Success)
        {
            var candidate = ModelingOperationEngine.Clone(baseline);
            var outcome = ModelingOperationEngine.Apply(candidate, translation.Batch, CreateOperationContext());
            if (outcome.Success)
            {
                _baselineModel = candidate;
            }
        }

        _pendingChangeSetCache = null;
    }
}
