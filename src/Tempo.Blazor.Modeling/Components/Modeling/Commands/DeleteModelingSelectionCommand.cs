using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Deletes the selected elements and relationships as ONE expanded cascade (phase 3, decision Q3):
/// the caller expands the removal into descendants + incident relationships, and this command
/// applies that expansion as one ordered batch — view connections, view nodes, relationships, then
/// elements DESCENDANTS-FIRST, so the engine's <c>still_referenced</c> / <c>still_has_children</c>
/// checks pass without ever cascading silently.
/// <para>
/// Phase 3 review: the command removes the layout entry in EVERY view (A9 — a node duplicated into
/// two views must not survive in the second one), addresses each view explicitly (<c>viewId</c>) and
/// reports one <c>ModelingViewChange</c> per affected view; <c>Undo</c> restores the removed DTOs
/// into their ORIGINAL positions (A7 — the captured index, clamped to the current count, ascending),
/// so a delete → undo pair leaves the element, relationship and view-node id sequences exactly as
/// they were. The DTOs themselves are deep copies taken BEFORE the first execution (an element keeps
/// its source triple, tags, governance and property kinds).
/// </para>
/// </summary>
public sealed class DeleteModelingSelectionCommand : IDiagramCommand
{
    private readonly ModelingModelDto _model;
    private readonly ModelingOperationContext _context;
    private readonly string? _viewId;
    private readonly string _providerKey;
    private readonly List<(int Index, ModelingElementDto Dto)> _elements;
    private readonly List<(int Index, ModelingRelationshipDto Dto)> _relationships;
    private readonly List<CapturedView> _views = [];

    /// <summary>One view's removed layout entries, with their original positions for the restore.</summary>
    private sealed record CapturedView(
        string ViewId,
        List<(int Index, ModelingViewNodeDto Dto)> Nodes,
        List<(int Index, ModelingViewConnectionDto Dto)> Connections);

    /// <summary>Creates the command for one cascade deletion.</summary>
    /// <param name="transactionName">Localized undo/redo name of the transaction.</param>
    /// <param name="model">The model to mutate in place.</param>
    /// <param name="context">Engine context (notation rules + the <c>Authoring</c> policy).</param>
    /// <param name="elementIds">Element ids to remove, INCLUDING every descendant the cascade expands to.</param>
    /// <param name="relationshipIds">Incident relationship ids to remove (before their endpoints).</param>
    /// <param name="viewId">The view the request names; it only selects the change set's primary view —
    /// the removal itself touches EVERY view (A9).</param>
    /// <param name="providerKey">Provider key the change set is addressed to (the writer key).</param>
    public DeleteModelingSelectionCommand(
        string transactionName,
        ModelingModelDto model,
        ModelingOperationContext context,
        IReadOnlyCollection<string> elementIds,
        IReadOnlyCollection<string> relationshipIds,
        string? viewId = null,
        string? providerKey = null)
    {
        Name = transactionName ?? string.Empty;
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _viewId = viewId;
        _providerKey = providerKey ?? string.Empty;

        var elementSet = new HashSet<string>(elementIds ?? [], StringComparer.Ordinal);
        var relationshipSet = new HashSet<string>(relationshipIds ?? [], StringComparer.Ordinal);
        var snapshot = ModelingOperationEngine.Clone(model);

        _elements = CaptureIndexed(snapshot.Elements, element => elementSet.Contains(element.Id), element => element.Id);
        _relationships = CaptureIndexed(snapshot.Relationships, relationship => relationshipSet.Contains(relationship.Id), relationship => relationship.Id);

        foreach (var view in snapshot.Views)
        {
            var nodes = CaptureIndexed(view.Nodes, node => elementSet.Contains(node.ElementId), node => node.ElementId);
            var connections = CaptureIndexed(view.Connections, connection => relationshipSet.Contains(connection.RelationshipId), connection => connection.RelationshipId);
            if (nodes.Count == 0 && connections.Count == 0)
            {
                continue;
            }

            _views.Add(new CapturedView(view.Id, nodes, connections));
        }

        Batch = new ModelingOperationBatch();
        foreach (var view in _views)
        {
            foreach (var connection in view.Connections)
            {
                Batch.Operations.Add(new ModelingOperation
                {
                    Op = "remove_view_connection",
                    ViewId = string.IsNullOrEmpty(view.ViewId) ? null : view.ViewId,
                    RelationshipId = connection.Dto.RelationshipId
                });
            }

            foreach (var node in view.Nodes)
            {
                Batch.Operations.Add(new ModelingOperation
                {
                    Op = "remove_view_node",
                    ViewId = string.IsNullOrEmpty(view.ViewId) ? null : view.ViewId,
                    ElementId = node.Dto.ElementId
                });
            }
        }

        foreach (var relationship in _relationships)
        {
            Batch.Operations.Add(new ModelingOperation { Op = "delete_relationship", Id = relationship.Dto.Id });
        }

        foreach (var elementId in ModelingStructureCommandSupport.OrderDescendantsFirst([.. _elements.Select(entry => entry.Dto)]))
        {
            Batch.Operations.Add(new ModelingOperation { Op = "delete_element", Id = elementId });
        }
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The element ids this command removes (descendants first) — the confirmation dialog
    /// shows their count.</summary>
    public IReadOnlyList<string> ElementIds
        => [.. ModelingStructureCommandSupport.OrderDescendantsFirst([.. _elements.Select(entry => entry.Dto)])];

    /// <summary>The relationship ids this command removes.</summary>
    public IReadOnlyList<string> RelationshipIds => [.. _relationships.Select(entry => entry.Dto.Id)];

    /// <summary>The engine outcome of the last execution (null before the first Execute).</summary>
    public ModelingOperationResult? LastResult { get; private set; }

    /// <summary>The batch this command applies — exposed for the editor's pre-validation on a CLONE.</summary>
    public ModelingOperationBatch Batch { get; }

    /// <summary>The change-set contribution: the expanded removals plus ONE view change per affected
    /// view (A9). A request that names a view the removal does not touch still reports that view
    /// (with no removals) so the change set always answers "which view did the user act in".</summary>
    public ModelingModelChangeSet Intent
    {
        get
        {
            var changeSet = new ModelingModelChangeSet
            {
                ModelId = _model.Id,
                ProviderKey = _providerKey,
                BaseVersion = _model.Metadata.SourceVersion,
                RemovedElementIds = [.. ElementIds],
                RemovedRelationshipIds = [.. RelationshipIds]
            };

            foreach (var view in _views)
            {
                changeSet.ViewChanges.Add(new ModelingViewChange
                {
                    ViewId = view.ViewId,
                    RemovedNodeElementIds = [.. view.Nodes.Select(entry => entry.Dto.ElementId)],
                    RemovedConnectionRelationshipIds = [.. view.Connections.Select(entry => entry.Dto.RelationshipId)]
                });
            }

            if (changeSet.ViewChanges.Count == 0 && _viewId is { Length: > 0 })
            {
                changeSet.ViewChanges.Add(new ModelingViewChange { ViewId = _viewId });
            }

            return changeSet;
        }
    }

    /// <inheritdoc />
    public void Execute() => LastResult = ModelingOperationEngine.Apply(_model, Batch, _context);

    /// <inheritdoc />
    public void Undo()
    {
        // A lossless restore of exactly what was captured, at the ORIGINAL position (phase 3
        // review, A7): a delete followed by an undo must leave the id sequences untouched. The
        // stored index is clamped to the current count, and the inserts run ASCENDING so two
        // neighbours removed together land in their original order.
        RestoreOrdered(_model.Elements, _elements, (list, entry) => list.Insert(Math.Min(entry.Index, list.Count), entry.Dto),
            element => element.Id, entry => entry.Dto.Id);
        RestoreOrdered(_model.Relationships, _relationships, (list, entry) => list.Insert(Math.Min(entry.Index, list.Count), entry.Dto),
            relationship => relationship.Id, entry => entry.Dto.Id);

        foreach (var captured in _views)
        {
            var view = _model.Views.FirstOrDefault(candidate => string.Equals(candidate.Id, captured.ViewId, StringComparison.Ordinal));
            if (view is null)
            {
                continue;
            }

            RestoreOrdered(view.Nodes, captured.Nodes,
                (list, entry) => list.Insert(Math.Min(entry.Index, list.Count), entry.Dto),
                node => node.ElementId, entry => entry.Dto.ElementId);
            RestoreOrdered(view.Connections, captured.Connections,
                (list, entry) => list.Insert(Math.Min(entry.Index, list.Count), entry.Dto),
                connection => connection.RelationshipId, entry => entry.Dto.RelationshipId);
        }
    }

    /// <summary>Deep-copies the matching DTOs together with their index in the source list.</summary>
    private static List<(int Index, T Dto)> CaptureIndexed<T>(IReadOnlyList<T> source, Func<T, bool> predicate, Func<T, string> idSelector)
    {
        var captured = new List<(int Index, T Dto)>();
        for (var index = 0; index < source.Count; index++)
        {
            if (predicate(source[index]))
            {
                captured.Add((index, source[index]));
            }
        }

        return captured;
    }

    /// <summary>Re-inserts the captured entries in ASCENDING original-index order, skipping ids a
    /// concurrent change already restored (undo is idempotent).</summary>
    private static void RestoreOrdered<T>(
        List<T> target,
        IReadOnlyList<(int Index, T Dto)> captured,
        Action<List<T>, (int Index, T Dto)> insert,
        Func<T, string> idSelector,
        Func<(int Index, T Dto), string> entryIdSelector)
    {
        var liveIds = new HashSet<string>(target.Select(idSelector), StringComparer.Ordinal);
        foreach (var entry in captured.OrderBy(item => item.Index))
        {
            if (liveIds.Add(entryIdSelector(entry)))
            {
                insert(target, entry);
            }
        }
    }
}
