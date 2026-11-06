using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Creates ONE semantic element plus its view node (phase 3, decision
/// <c>MODELING-AUTHORING-CHANGESET</c> §9.1): the palette insert of the "Open in editor" dialog and
/// the non-drag "Insert element" path both mint the semantic element and its placement in the SAME
/// command, so one undo/redo step removes/adds both — never a diagram node without a model element.
/// <para>
/// The forward batch (<c>add_element</c> + <c>set_view_node</c>) is applied by the shared
/// <see cref="ModelingOperationEngine"/>, so a notation rule or an invalid container fails exactly
/// as it would through <c>modeling_apply_operations</c>. The editor validates the same batch on a
/// clone BEFORE pushing, so a rejected insert never enters the undo stack. <c>Undo</c> is a
/// lossless rollback of exactly what the command added.
/// </para>
/// </summary>
public sealed class AddModelingElementCommand : IDiagramCommand
{
    private readonly ModelingModelDto _model;
    private readonly ModelingOperationContext _context;
    private readonly ModelingElementAdd _add;
    private readonly ModelingViewNodeChange _node;
    private readonly ModelingViewNodeChange? _containerResize;
    private readonly ModelingViewNodeDto? _previousContainerGeometry;
    private readonly string? _viewId;
    private readonly string? _intentViewId;
    private readonly string _providerKey;

    /// <summary>Creates the command for one semantic insert.</summary>
    /// <param name="transactionName">Localized undo/redo name of the transaction.</param>
    /// <param name="model">The model to mutate in place.</param>
    /// <param name="context">Engine context (notation rules + the <c>Authoring</c> policy).</param>
    /// <param name="add">The semantic element to add (id, type, name, parent…).</param>
    /// <param name="node">The view placement of the element (X/Y are mandatory).</param>
    /// <param name="viewId">Target view of the OPERATIONS; null/empty = the model's default view
    /// (the engine creates it as <c>"default"</c> when the model has none).</param>
    /// <param name="providerKey">Provider key the change set is addressed to (the writer key).</param>
    /// <param name="intentViewId">The view id the change set REPORTS when it differs from the
    /// operation's (a viewless model seeds <c>"default"</c> while its operations stay view-less so
    /// the engine creates the view).</param>
    /// <param name="containerResize">Phase 3 review (A12): the container's GROWN geometry when the
    /// new node no longer fits inside it. Applied in the same command (one undo step) and reported
    /// as a second view-node change of the same view.</param>
    public AddModelingElementCommand(
        string transactionName,
        ModelingModelDto model,
        ModelingOperationContext context,
        ModelingElementAdd add,
        ModelingViewNodeChange node,
        string? viewId = null,
        string? providerKey = null,
        string? intentViewId = null,
        ModelingViewNodeChange? containerResize = null)
    {
        Name = transactionName ?? string.Empty;
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _add = add ?? throw new ArgumentNullException(nameof(add));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _viewId = viewId;
        _intentViewId = intentViewId;
        _containerResize = containerResize;
        _providerKey = providerKey ?? string.Empty;
        Batch = new ModelingOperationBatch
        {
            Operations =
            [
                ModelingStructureCommandSupport.AddElementOperation(_add),
                ModelingStructureCommandSupport.SetViewNodeOperation(_node, _viewId)
            ]
        };

        if (_containerResize is not null)
        {
            Batch.Operations.Add(ModelingStructureCommandSupport.SetViewNodeOperation(_containerResize, _viewId));
            // Undo restores the container exactly: the grown geometry is a side effect of the
            // insert, never a user edit that outlives it (phase 3 review, A12/A7).
            var view = ModelingStructureCommandSupport.FindView(model, viewId);
            var container = view?.Nodes.FirstOrDefault(node =>
                string.Equals(node.ElementId, _containerResize.ElementId, StringComparison.Ordinal));
            _previousContainerGeometry = container is null
                ? null
                : new ModelingViewNodeDto
                {
                    ElementId = container.ElementId,
                    X = container.X,
                    Y = container.Y,
                    Width = container.Width,
                    Height = container.Height,
                    ParentNodeId = container.ParentNodeId
                };
        }
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Identifier of the added element (the client-proposed stable id).</summary>
    public string ElementId => _add.Id;

    /// <summary>The engine outcome of the last execution (null before the first Execute).</summary>
    public ModelingOperationResult? LastResult { get; private set; }

    /// <summary>The change-set contribution of this command: <c>AddedElements</c> + the matching
    /// <c>ViewChanges</c> node — exactly what <c>OnModelChanged</c> and the writer receive.</summary>
    public ModelingModelChangeSet Intent => new()
    {
        ModelId = _model.Id,
        ProviderKey = _providerKey,
        BaseVersion = _model.Metadata.SourceVersion,
        AddedElements = [_add],
        ViewChanges =
        [
            new ModelingViewChange
            {
                ViewId = _intentViewId ?? _viewId ?? string.Empty,
                Nodes = _containerResize is null ? [_node] : [_node, _containerResize]
            }
        ]
    };

    /// <summary>The batch this command applies — exposed so the editor can validate it on a CLONE
    /// before pushing the command (a rejected insert never enters the undo stack).</summary>
    public ModelingOperationBatch Batch { get; }

    /// <inheritdoc />
    public void Execute()
        => LastResult = ModelingOperationEngine.Apply(_model, Batch, _context);

    /// <inheritdoc />
    public void Undo()
    {
        ModelingStructureCommandSupport.RemoveAddedElement(_model, _add.Id, _viewId);

        if (_previousContainerGeometry is not null && _containerResize is not null)
        {
            var view = ModelingStructureCommandSupport.FindView(_model, _viewId);
            var container = view?.Nodes.FirstOrDefault(node =>
                string.Equals(node.ElementId, _containerResize.ElementId, StringComparison.Ordinal));
            if (container is not null)
            {
                container.X = _previousContainerGeometry.X;
                container.Y = _previousContainerGeometry.Y;
                container.Width = _previousContainerGeometry.Width;
                container.Height = _previousContainerGeometry.Height;
                container.ParentNodeId = _previousContainerGeometry.ParentNodeId;
            }
        }
    }
}
