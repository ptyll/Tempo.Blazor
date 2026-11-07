using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// The phase-4 LAYOUT commit of <see cref="TmModelingEditor"/> (decision
/// <c>MODELING-AUTHORING-CHANGESET</c> §9.3/§9.4): it writes view geometry (node X/Y/W/H and the
/// layout parent), connection waypoints and — when the gesture also re-parents an element (a task
/// dropped into another lane/pool) — the SEMANTIC <c>ParentId</c>, all in ONE command, so one undo
/// step reverts the move, the re-parent and the container growth together.
/// <para>
/// The forward batch (<c>update_element parentId</c> + <c>set_view_node</c> +
/// <c>set_view_connection</c>) is an ordinary engine batch that the editor validates on a CLONE
/// before pushing, so an invalid re-parent (a SequenceFlow that would now cross a pool) is refused
/// with the engine's own <c>invalid_relationship</c> message and never enters the undo stack.
/// <c>Undo</c> restores exactly what the command overwrote — including deleting a view node or a
/// view the command created for a model that had none.
/// </para>
/// </summary>
internal sealed class SetModelingViewLayoutCommand : IDiagramCommand
{
    private readonly ModelingModelDto _model;
    private readonly ModelingOperationContext _context;
    private readonly string? _viewId;
    private readonly string? _intentViewId;
    private readonly string _providerKey;
    private readonly List<ModelingViewNodeChange> _nodes;
    private readonly List<ModelingViewConnectionChange> _connections;
    private readonly List<(string ElementId, string ParentId)> _parentMoves;
    private readonly Dictionary<string, ModelingViewNodeDto?> _previousNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ModelingViewConnectionDto?> _previousConnections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _previousParents = new(StringComparer.Ordinal);
    private bool _createdView;

    /// <summary>Creates the layout command.</summary>
    /// <param name="transactionName">Localized undo/redo name of the transaction.</param>
    /// <param name="model">The model to mutate in place.</param>
    /// <param name="context">Engine context (notation + viewpoint rules, <c>Authoring</c> policy).</param>
    /// <param name="nodes">View-node upserts (X/Y are always present — a new node needs them).</param>
    /// <param name="connections">View-connection upserts (waypoints; an empty list clears routing).</param>
    /// <param name="parentMoves">Semantic parent changes that belong to the same gesture.</param>
    /// <param name="viewId">Target view of the OPERATIONS; null/empty = the model's default view
    /// (the engine creates <c>"default"</c> when the model has none).</param>
    /// <param name="providerKey">Provider key the change set is addressed to.</param>
    /// <param name="intentViewId">The view id the change set REPORTS (the seeded <c>"default"</c>
    /// while the operations stay view-less).</param>
    public SetModelingViewLayoutCommand(
        string transactionName,
        ModelingModelDto model,
        ModelingOperationContext context,
        IEnumerable<ModelingViewNodeChange> nodes,
        IEnumerable<ModelingViewConnectionChange> connections,
        IEnumerable<(string ElementId, string ParentId)> parentMoves,
        string? viewId = null,
        string? providerKey = null,
        string? intentViewId = null)
    {
        Name = transactionName ?? string.Empty;
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _nodes = [.. nodes ?? []];
        _connections = [.. connections ?? []];
        _parentMoves = [.. parentMoves ?? []];
        _viewId = viewId;
        _intentViewId = intentViewId;
        _providerKey = providerKey ?? string.Empty;

        Batch = new ModelingOperationBatch();
        foreach (var (elementId, parentId) in _parentMoves)
        {
            Batch.Operations.Add(new ModelingOperation
            {
                Op = "update_element",
                Id = elementId,
                ParentId = parentId
            });
        }

        foreach (var node in _nodes)
        {
            Batch.Operations.Add(ModelingStructureCommandSupport.SetViewNodeOperation(node, _viewId));
        }

        foreach (var connection in _connections)
        {
            Batch.Operations.Add(ModelingStructureCommandSupport.SetViewConnectionOperation(
                connection.RelationshipId,
                _viewId,
                connection.Waypoints));
        }

        CapturePreviousState();
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The engine batch this command applies (exposed for the editor's clone validation).</summary>
    public ModelingOperationBatch Batch { get; }

    /// <summary>The change-set contribution: the semantic re-parents plus the layout upserts.</summary>
    public ModelingModelChangeSet Intent => new()
    {
        ModelId = _model.Id,
        ProviderKey = _providerKey,
        BaseVersion = _model.Metadata.SourceVersion,
        Elements = [.. _parentMoves.Select(move => new ModelingElementChange
        {
            ElementId = move.ElementId,
            ParentId = move.ParentId ?? string.Empty
        })],
        ViewChanges =
        [
            new ModelingViewChange
            {
                ViewId = _intentViewId ?? _viewId ?? string.Empty,
                Nodes = [.. _nodes],
                Connections = [.. _connections]
            }
        ]
    };

    /// <summary>Whether the gesture changed view geometry (as opposed to only a semantic parent).</summary>
    public bool HasViewChanges => _nodes.Count > 0 || _connections.Count > 0;

    /// <summary>Whether this command can absorb the NEXT gesture into itself (phase 4 review U6):
    /// it must be a parent-move-free, waypoint-free layout step over the SAME element set, so one
    /// undo step covers a burst of keyboard nudges.</summary>
    /// <param name="elementIds">The element ids of the incoming gesture.</param>
    internal bool CanCoalesceWith(IReadOnlyCollection<string> elementIds)
    {
        if (_parentMoves.Count > 0 || _connections.Count > 0 || _nodes.Count == 0)
        {
            return false;
        }

        if (elementIds.Count != _nodes.Count)
        {
            return false;
        }

        var current = new HashSet<string>(_nodes.Select(node => node.ElementId), StringComparer.Ordinal);
        return elementIds.All(current.Contains);
    }

    /// <summary>
    /// Merges the NEXT geometry step into this command (U6). The BEFORE snapshot is untouched — an
    /// undo of the burst still restores the geometry from before the first nudge — while the forward
    /// geometry and the engine batch are rewritten to the latest values and applied to the model. The
    /// caller re-raises the commit contract afterwards.
    /// </summary>
    /// <param name="nodes">The merged node geometry (same element set as this command).</param>
    /// <param name="connections">Waypoint changes (empty for a pure nudge).</param>
    /// <returns><c>true</c> when the merge applied.</returns>
    internal bool TryMerge(IReadOnlyList<ModelingViewNodeChange> nodes, IReadOnlyList<ModelingViewConnectionChange> connections)
    {
        if (connections.Count > 0 || !CanCoalesceWith([.. nodes.Select(node => node.ElementId)]))
        {
            return false;
        }

        var merged = new Dictionary<string, ModelingViewNodeChange>(StringComparer.Ordinal);
        foreach (var change in _nodes)
        {
            merged[change.ElementId] = change;
        }

        foreach (var change in nodes)
        {
            if (!merged.TryGetValue(change.ElementId, out var target))
            {
                return false;
            }

            target.X = change.X ?? target.X;
            target.Y = change.Y ?? target.Y;
            target.Width = change.Width ?? target.Width;
            target.Height = change.Height ?? target.Height;
            if (change.ParentNodeId is not null)
            {
                target.ParentNodeId = change.ParentNodeId;
            }
        }

        Batch.Operations.Clear();
        foreach (var node in _nodes)
        {
            Batch.Operations.Add(ModelingStructureCommandSupport.SetViewNodeOperation(node, _viewId));
        }

        Execute();
        return true;
    }

    /// <inheritdoc />
    public void Execute()
    {
        foreach (var (elementId, parentId) in _parentMoves)
        {
            var element = _model.Elements.FirstOrDefault(item => string.Equals(item.Id, elementId, StringComparison.Ordinal));
            if (element is not null)
            {
                element.ParentId = parentId ?? string.Empty;
            }
        }

        if (_nodes.Count == 0 && _connections.Count == 0)
        {
            return;
        }

        var view = ModelingStructureCommandSupport.FindView(_model, _viewId);
        if (view is null)
        {
            view = new ModelingViewDto
            {
                Id = SeedModelingViewCommand.SeededViewId,
                Name = string.Empty,
                Notation = _model.Notation
            };
            _model.Views.Add(view);
            _createdView = true;
        }

        foreach (var change in _nodes)
        {
            var node = view.Nodes.FirstOrDefault(item => string.Equals(item.ElementId, change.ElementId, StringComparison.Ordinal));
            if (node is null)
            {
                node = new ModelingViewNodeDto { ElementId = change.ElementId };
                view.Nodes.Add(node);
            }

            if (change.X is { } x)
            {
                node.X = x;
            }

            if (change.Y is { } y)
            {
                node.Y = y;
            }

            if (change.Width is { } width)
            {
                node.Width = width;
            }

            if (change.Height is { } height)
            {
                node.Height = height;
            }

            if (change.ParentNodeId is not null)
            {
                node.ParentNodeId = change.ParentNodeId;
            }
        }

        foreach (var change in _connections)
        {
            var connection = view.Connections.FirstOrDefault(item =>
                string.Equals(item.RelationshipId, change.RelationshipId, StringComparison.Ordinal));
            if (connection is null)
            {
                var relationship = _model.Relationships.FirstOrDefault(item =>
                    string.Equals(item.Id, change.RelationshipId, StringComparison.Ordinal));
                connection = new ModelingViewConnectionDto
                {
                    RelationshipId = change.RelationshipId,
                    SourceNodeId = relationship?.SourceElementId ?? string.Empty,
                    TargetNodeId = relationship?.TargetElementId ?? string.Empty
                };
                view.Connections.Add(connection);
            }

            if (change.Waypoints is { } waypoints)
            {
                connection.Waypoints = [.. waypoints.Select(point => new ModelingViewWaypointDto { X = point.X, Y = point.Y })];
            }
        }
    }

    /// <inheritdoc />
    public void Undo()
    {
        foreach (var (elementId, _) in _parentMoves)
        {
            var element = _model.Elements.FirstOrDefault(item => string.Equals(item.Id, elementId, StringComparison.Ordinal));
            if (element is not null && _previousParents.TryGetValue(elementId, out var previousParent))
            {
                element.ParentId = previousParent ?? string.Empty;
            }
        }

        var view = ModelingStructureCommandSupport.FindView(_model, _viewId);
        if (view is null)
        {
            return;
        }

        foreach (var change in _nodes)
        {
            var node = view.Nodes.FirstOrDefault(item => string.Equals(item.ElementId, change.ElementId, StringComparison.Ordinal));
            if (_previousNodes.TryGetValue(change.ElementId, out var previous))
            {
                if (previous is null)
                {
                    if (node is not null)
                    {
                        view.Nodes.Remove(node);
                    }

                    continue;
                }

                if (node is null)
                {
                    node = new ModelingViewNodeDto { ElementId = change.ElementId };
                    view.Nodes.Add(node);
                }

                node.ElementId = previous.ElementId;
                node.X = previous.X;
                node.Y = previous.Y;
                node.Width = previous.Width;
                node.Height = previous.Height;
                node.ParentNodeId = previous.ParentNodeId;
            }
        }

        foreach (var change in _connections)
        {
            var connection = view.Connections.FirstOrDefault(item =>
                string.Equals(item.RelationshipId, change.RelationshipId, StringComparison.Ordinal));
            if (!_previousConnections.TryGetValue(change.RelationshipId, out var previous))
            {
                continue;
            }

            if (previous is null)
            {
                if (connection is not null)
                {
                    view.Connections.Remove(connection);
                }

                continue;
            }

            if (connection is null)
            {
                connection = new ModelingViewConnectionDto { RelationshipId = change.RelationshipId };
                view.Connections.Add(connection);
            }

            connection.SourceNodeId = previous.SourceNodeId;
            connection.TargetNodeId = previous.TargetNodeId;
            connection.Waypoints = [.. previous.Waypoints.Select(point => new ModelingViewWaypointDto { X = point.X, Y = point.Y })];
        }

        if (_createdView && _model.Views.Count > 1)
        {
            _model.Views.Remove(view);
        }
    }

    /// <summary>Snapshots the state the command overwrites, so UNDO is lossless (nothing is
    /// re-derived from the current state — a second undo must see the SAME snapshot).</summary>
    private void CapturePreviousState()
    {
        foreach (var (elementId, _) in _parentMoves)
        {
            var element = _model.Elements.FirstOrDefault(item => string.Equals(item.Id, elementId, StringComparison.Ordinal));
            if (element is not null)
            {
                _previousParents[elementId] = element.ParentId;
            }
        }

        var view = ModelingStructureCommandSupport.FindView(_model, _viewId);
        if (view is null)
        {
            return;
        }

        foreach (var change in _nodes)
        {
            var node = view.Nodes.FirstOrDefault(item => string.Equals(item.ElementId, change.ElementId, StringComparison.Ordinal));
            _previousNodes[change.ElementId] = node is null
                ? null
                : new ModelingViewNodeDto
                {
                    ElementId = node.ElementId,
                    X = node.X,
                    Y = node.Y,
                    Width = node.Width,
                    Height = node.Height,
                    ParentNodeId = node.ParentNodeId
                };
        }

        foreach (var change in _connections)
        {
            var connection = view.Connections.FirstOrDefault(item =>
                string.Equals(item.RelationshipId, change.RelationshipId, StringComparison.Ordinal));
            _previousConnections[change.RelationshipId] = connection is null
                ? null
                : new ModelingViewConnectionDto
                {
                    RelationshipId = connection.RelationshipId,
                    SourceNodeId = connection.SourceNodeId,
                    TargetNodeId = connection.TargetNodeId,
                    Waypoints = [.. connection.Waypoints.Select(point => new ModelingViewWaypointDto { X = point.X, Y = point.Y })]
                };
        }
    }
}
