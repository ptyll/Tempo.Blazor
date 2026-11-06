using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Creates ONE semantic relationship plus its view connection (phase 3, decision
/// <c>MODELING-AUTHORING-CHANGESET</c> §9.2): a relationship is ALWAYS paired with a connection in
/// the target view, because a generator with a selected view renders edges from
/// <c>view.Connections</c> only — a relationship without a connection would vanish on reload.
/// <para>
/// The forward batch (<c>add_relationship</c> + <c>set_view_connection</c>) goes through the shared
/// <see cref="ModelingOperationEngine"/>, so the notation rules
/// (<see cref="IModelingRelationshipRulesProvider"/>) judge the new flow exactly as
/// <c>modeling_apply_operations</c> does; the editor pre-validates on a clone. <c>Undo</c> is a
/// lossless rollback of exactly what the command added.
/// </para>
/// </summary>
public sealed class AddModelingRelationshipCommand : IDiagramCommand
{
    private readonly ModelingModelDto _model;
    private readonly ModelingOperationContext _context;
    private readonly ModelingRelationshipAdd _add;
    private readonly string? _viewId;
    private readonly string? _intentViewId;
    private readonly string _providerKey;

    /// <summary>Creates the command for one semantic relationship insert.</summary>
    /// <param name="transactionName">Localized undo/redo name of the transaction.</param>
    /// <param name="model">The model to mutate in place.</param>
    /// <param name="context">Engine context (notation rules + the <c>Authoring</c> policy).</param>
    /// <param name="add">The relationship to add (id, type, endpoints, name…).</param>
    /// <param name="viewId">Target view of the OPERATIONS; null/empty = the model's default view
    /// (the engine creates it as <c>"default"</c> when the model has none).</param>
    /// <param name="providerKey">Provider key the change set is addressed to (the writer key).</param>
    /// <param name="intentViewId">The view id the change set REPORTS when it differs from the
    /// operation's (a viewless model seeds <c>"default"</c> while its operations stay view-less so
    /// the engine creates the view).</param>
    public AddModelingRelationshipCommand(
        string transactionName,
        ModelingModelDto model,
        ModelingOperationContext context,
        ModelingRelationshipAdd add,
        string? viewId = null,
        string? providerKey = null,
        string? intentViewId = null)
    {
        Name = transactionName ?? string.Empty;
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _add = add ?? throw new ArgumentNullException(nameof(add));
        _viewId = viewId;
        _intentViewId = intentViewId;
        _providerKey = providerKey ?? string.Empty;
        Batch = new ModelingOperationBatch
        {
            Operations =
            [
                ModelingStructureCommandSupport.AddRelationshipOperation(_add),
                ModelingStructureCommandSupport.SetViewConnectionOperation(_add.Id, _viewId)
            ]
        };
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Identifier of the added relationship (the client-proposed stable id).</summary>
    public string RelationshipId => _add.Id;

    /// <summary>The engine outcome of the last execution (null before the first Execute).</summary>
    public ModelingOperationResult? LastResult { get; private set; }

    /// <summary>The batch this command applies — exposed so the editor can validate it on a CLONE
    /// before pushing the command.</summary>
    public ModelingOperationBatch Batch { get; }

    /// <summary>The change-set contribution: <c>AddedRelationships</c> + the matching view connection.</summary>
    public ModelingModelChangeSet Intent => new()
    {
        ModelId = _model.Id,
        ProviderKey = _providerKey,
        BaseVersion = _model.Metadata.SourceVersion,
        AddedRelationships = [_add],
        ViewChanges =
        [
            new ModelingViewChange
            {
                ViewId = _intentViewId ?? _viewId ?? string.Empty,
                Connections = [new ModelingViewConnectionChange { RelationshipId = _add.Id, Waypoints = [] }]
            }
        ]
    };

    /// <inheritdoc />
    public void Execute()
        => LastResult = ModelingOperationEngine.Apply(_model, Batch, _context);

    /// <inheritdoc />
    public void Undo()
        => ModelingStructureCommandSupport.RemoveAddedRelationship(_model, _add.Id, _viewId);
}
