using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Seeds the ONE view a viewless model needs before the first structural authoring step (phase 3
/// review, A1): the preview already renders every element at its grid fallback and every
/// relationship as an edge, but the model has no view — committing only the inserted node/connection
/// would make the update <c>ViewChanges</c> describe a partial scene and the pre-existing elements
/// would silently lose their rendered position on the next reload.
/// <para>
/// The command is PREPENDED to the same <see cref="DiagramCommandTransaction"/> as the insert/connect
/// (so one undo removes both), takes one <c>set_view_node</c> per RENDERED document node (element id
/// from the node's <c>modelElementId</c> data, its current X/Y/W/H and its semantic parent as
/// <c>parentNodeId</c>) plus one <c>set_view_connection</c> per rendered edge whose id IS a
/// relationship id, and reports those as the transaction's <c>ViewChanges</c> for the
/// <c>"default"</c> view the engine creates. <c>Undo</c> removes exactly the view it created.
/// </para>
/// </summary>
internal sealed class SeedModelingViewCommand : IDiagramCommand
{
    /// <summary>The id the engine gives the view it creates for a model without one.</summary>
    internal const string SeededViewId = "default";

    private readonly ModelingModelDto _model;
    private readonly ModelingOperationContext _context;
    private readonly HashSet<string> _preExistingViewIds;
    private readonly ModelingViewChange _viewChange = new() { ViewId = SeededViewId };

    /// <summary>Records the seed of the document's rendered scene.</summary>
    /// <param name="transactionName">Localized undo/redo name of the containing transaction.</param>
    /// <param name="model">The (viewless) model to seed.</param>
    /// <param name="context">Engine context (notation rules + the <c>Authoring</c> policy).</param>
    /// <param name="document">The rendered document the seed geometry is read from.</param>
    /// <param name="relationshipIds">The model's relationship ids (an edge is seeded only when its id IS one).</param>
    public SeedModelingViewCommand(
        string transactionName,
        ModelingModelDto model,
        ModelingOperationContext context,
        DiagramDocument? document,
        IReadOnlyCollection<string> relationshipIds)
    {
        Name = transactionName ?? string.Empty;
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _preExistingViewIds = new HashSet<string>(model.Views.Select(view => view.Id), StringComparer.Ordinal);
        Batch = new ModelingOperationBatch();

        if (document is null)
        {
            return;
        }

        var elementById = model.Elements
            .GroupBy(element => element.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var relationships = new HashSet<string>(relationshipIds, StringComparer.Ordinal);

        foreach (var node in document.Nodes)
        {
            if (node.Data is null
                || !node.Data.TryGetValue("modelElementId", out var value)
                || value?.ToString() is not { Length: > 0 } elementId
                || !elementById.TryGetValue(elementId, out var element))
            {
                continue;
            }

            var width = node.W > 0 ? node.W : 0;
            var height = node.H > 0 ? node.H : 0;
            var parentNodeId = string.IsNullOrEmpty(element.ParentId) ? null : element.ParentId;
            Batch.Operations.Add(new ModelingOperation
            {
                Op = "set_view_node",
                ElementId = elementId,
                X = node.X,
                Y = node.Y,
                Width = width,
                Height = height,
                ParentNodeId = parentNodeId
            });
            _viewChange.Nodes.Add(new ModelingViewNodeChange
            {
                ElementId = elementId,
                X = node.X,
                Y = node.Y,
                Width = width,
                Height = height,
                ParentNodeId = parentNodeId
            });
        }

        foreach (var edge in document.Edges)
        {
            if (!relationships.Contains(edge.Id))
            {
                continue;
            }

            Batch.Operations.Add(new ModelingOperation
            {
                Op = "set_view_connection",
                RelationshipId = edge.Id,
                Waypoints = []
            });
            _viewChange.Connections.Add(new ModelingViewConnectionChange
            {
                RelationshipId = edge.Id,
                Waypoints = []
            });
        }
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The batch that creates the view — exposed for the editor's clone validation.</summary>
    public ModelingOperationBatch Batch { get; }

    /// <summary>The engine outcome of the last execution (null before the first Execute).</summary>
    public ModelingOperationResult? LastResult { get; private set; }

    /// <summary>Whether the seed carries anything at all (a document with no rendered node/edge
    /// needs no view op — the insert/connect creates the view itself).</summary>
    public bool HasOperations => Batch.Operations.Count > 0;

    /// <summary>The change-set contribution: the seeded view's nodes and connections.</summary>
    public ModelingModelChangeSet Intent => new()
    {
        ModelId = _model.Id,
        ProviderKey = string.Empty,
        BaseVersion = _model.Metadata.SourceVersion,
        ViewChanges = [new ModelingViewChange
        {
            ViewId = SeededViewId,
            Nodes = [.. _viewChange.Nodes],
            Connections = [.. _viewChange.Connections]
        }]
    };

    /// <inheritdoc />
    public void Execute() => LastResult = ModelingOperationEngine.Apply(_model, Batch, _context);

    /// <inheritdoc />
    public void Undo()
    {
        // Exactly the view this command created goes away — a view that already existed (the
        // command should never run then) stays untouched.
        foreach (var view in _model.Views.Where(view => !_preExistingViewIds.Contains(view.Id)).ToArray())
        {
            _model.Views.Remove(view);
        }
    }
}
