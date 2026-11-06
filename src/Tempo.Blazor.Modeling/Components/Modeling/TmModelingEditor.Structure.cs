using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// The phase-3 STRUCTURE half of <see cref="TmModelingEditor"/> (decisions
/// <c>MODELING-AUTHORING-CHANGESET</c> §9.1/§9.2 and <c>MODELING-WRITER-CAPABILITIES</c>): the
/// semantic palette insert, relationship drawing with notation validation, the cascade delete with
/// confirmation and the structural synchronization between the embedded diagram documents and the
/// semantic model.
/// <para>
/// <b>One semantics, one commit path.</b> The embedded <see cref="TmDiagramEditor"/> (the preview and
/// the "Open in editor" dialog) only knows diagram documents; the editor observes its mutations and
/// translates the STRUCTURAL ones — a node added from the notation palette, an edge drawn between
/// two nodes, a node/edge deleted — into the semantic commands of this phase. Every forward batch is
/// validated on a CLONE through the ONE <see cref="ModelingOperationEngine"/> before the command
/// enters the undo stack, so a rejected operation shows the engine's own message (the same text
/// <c>modeling_apply_operations</c> returns) and never mutates the model or the stack.
/// </para>
/// <para>
/// A content-only writer (<see cref="ModelingModelWriterCapabilities.Structure"/> absent) keeps the
/// 2.10.0 behavior: no palette, no structural action, no structural synchronization
/// (<see cref="CanEditModelStructure"/> is the ONE gate, and the razor hides every entry on it).
/// </para>
/// </summary>
public partial class TmModelingEditor
{
    /// <summary>Geometry defaults of a semantically inserted element (the view node placement). A
    /// container is born big enough for children: a pool is a tall band stack, a lane one row, a
    /// subprocess a small frame (phase 3 review, A12).</summary>
    private const double InsertedElementWidth = 160;
    private const double InsertedElementHeight = 88;
    private const double InsertedPoolWidth = 520;
    private const double InsertedPoolHeight = 260;
    private const double InsertedLaneHeight = 140;
    private const double InsertedContainerWidth = 320;
    private const double InsertedContainerHeight = 180;

    /// <summary>Notation relationship rules — the ONLY source of relationship validity (the editor
    /// never re-implements a BPMN rule). Optional: without it every notation type is a candidate and
    /// the engine's own validation decides.</summary>
    [Inject] public IModelingRelationshipRulesProvider? RelationshipRules { get; set; }

    /// <summary>Whether the editor may author the model STRUCTURE: the host opted into editing,
    /// a write path exists AND the effective writer advertises
    /// <see cref="ModelingModelWriterCapabilities.Structure"/>.</summary>
    public bool CanEditModelStructure
        => IsEditingEnabled && (EffectiveWriterCapabilities & ModelingModelWriterCapabilities.Structure) != 0;

    /// <summary>Whether a structural command was ever pushed since the last save/reload — undo and
    /// redo regenerate the documents from the model only then (a content-only undo needs no rebuild).</summary>
    private bool _hasStructuralCommands;

    /// <summary>The last structural announcement (polite live region).</summary>
    private string? _structureAnnouncement;

    /// <summary>The rejection/validation alert of the structural surface (null hides it).</summary>
    private string? _structureErrorTitle;
    private string? _structureErrorText;
    private string? _structureErrorFix;

    /// <summary>The snapshot of the last document the editor synchronized: node id → model element id
    /// and the edge ids. A mutation is structural when an id appears or disappears against it.</summary>
    private StructureSnapshot? _structureSnapshot;

    /// <summary>Test seam (InternalsVisibleTo): the last structural announcement.</summary>
    internal string? TestStructureAnnouncement => _structureAnnouncement;

    /// <summary>Test seam (InternalsVisibleTo): the rejection/validation alert title.</summary>
    internal string? TestStructureErrorTitle => _structureErrorTitle;

    /// <summary>Test seam (InternalsVisibleTo): the rejection/validation alert text.</summary>
    internal string? TestStructureErrorText => _structureErrorText;

    /// <summary>Test seam (InternalsVisibleTo): the selected element id.</summary>
    internal string? TestSelectedElementId => _selectedElement?.Id;

    /// <summary>Test seam (InternalsVisibleTo): selects an element by id (the tree's selection path).</summary>
    internal Task TestSelectElementForTestAsync(string elementId)
    {
        var element = _model?.Elements.FirstOrDefault(item => string.Equals(item.Id, elementId, StringComparison.Ordinal));
        return element is null ? Task.CompletedTask : SelectElementAsync(element);
    }

    /// <summary>Test seam (InternalsVisibleTo): the pending (content + structure + layout) change set.</summary>
    internal ModelingModelChangeSet? TestPendingChangeSet => ComputePendingChangeSet();

    /// <summary>Test seam (InternalsVisibleTo): selects a relationship by id (the inspector path).</summary>
    internal Task TestSelectRelationshipForTestAsync(string relationshipId) => SelectRelationshipAsync(relationshipId);

    /// <summary>Test seam (InternalsVisibleTo): the pending relationship type offer.</summary>
    internal IReadOnlyList<string> TestPendingRelationshipTypes => _pendingRelationship?.AllowedTypes ?? [];

    /// <summary>The last document snapshot (page, node → element, edge ids) used by the structural
    /// diff. <see cref="PageId"/> pins the diff to the page it was captured on (phase 3 review, A3):
    /// a page switch/add inside the embedded editor is navigation, never a delete of this page's
    /// nodes.</summary>
    internal sealed record StructureSnapshot(
        DiagramDocument Document,
        string? PageId,
        Dictionary<string, string> ElementByNodeId,
        HashSet<string> EdgeIds);

    /// <summary>
    /// The ONE engine context the editor uses: the notation rules, the viewpoint rules and the
    /// <c>Authoring</c> policy (view operations allowed; removals never cascaded by the engine).
    /// </summary>
    internal ModelingOperationContext CreateOperationContext() => new()
    {
        RelationshipRules = RelationshipRules,
        ViewpointRules = ViewpointRules,
        NotationProfiles = NotationProfiles,
        // The editor may render a multi-notation model under ONE notation (the host's override) —
        // the engine must judge the relationships under that SAME key or its verdict would disagree
        // with the type picker the user just saw (mandatory rule "one semantics, same errors").
        Notation = string.IsNullOrWhiteSpace(EffectiveNotation) ? null : EffectiveNotation,
        // Phase 3 review (A2): the host's viewpoint is part of the SAME semantics — a UML Include
        // offered under UseCaseDiagram must also commit (the rules judge the viewpoint), and the
        // engine never has to guess it from a view.
        ViewpointKey = string.IsNullOrWhiteSpace(EffectiveViewpointKey) ? null : EffectiveViewpointKey,
        Policy = ModelingOperationPolicy.Authoring
    };

    /// <summary>The notation the palette and the rules resolve against.</summary>
    private string EffectiveNotation => !string.IsNullOrWhiteSpace(EffectiveNotationKey)
        ? EffectiveNotationKey!
        : _model?.Notation ?? string.Empty;

    /// <summary>
    /// The view id the CHANGE SET reports for a structural command (§3.2 rule 8). A model without a
    /// view reports the <c>"default"</c> view the seed command (or the engine) creates (A1).
    /// </summary>
    private string? TargetViewId => _model is null
        ? null
        : _model.Views.Count == 0 ? SeedModelingViewCommand.SeededViewId : ResolveInitialViewId(_model);

    /// <summary>
    /// The view id the OPERATIONS carry: a viewless model stays view-less (null) so the seed
    /// command's <c>set_view_node</c> operations create the default view and every later operation
    /// targets it; with at least one view the explicit resolved id is used (multi-view correctness).
    /// </summary>
    private string? OperationViewId => _model is not null && _model.Views.Count == 0 ? null : TargetViewId;

    // ── Document ⇄ model synchronization ─────────────────────────────────────

    /// <summary>Records the document state the next mutation is diffed against.</summary>
    private void CaptureStructureSnapshot(DiagramDocument? document)
    {
        if (document is null)
        {
            _structureSnapshot = null;
            return;
        }

        var elementByNodeId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in document.Nodes)
        {
            if (node.Data is not null
                && node.Data.TryGetValue("modelElementId", out var value)
                && value?.ToString() is { Length: > 0 } elementId)
            {
                elementByNodeId[node.Id] = elementId;
            }
        }

        _structureSnapshot = new StructureSnapshot(
            document,
            document.ActivePage?.Id,
            elementByNodeId,
            [.. document.Edges.Select(edge => edge.Id)]);
    }

    /// <summary>
    /// The preview's document mutation (phase 3): the document is adopted and the STRUCTURAL part of
    /// the mutation (a palette-inserted node, a drawn edge, a deleted node/edge) is translated into
    /// the semantic commit it implies — a non-structural mutation (a move, a label edit) keeps the
    /// 2.10.0 session-only behavior.
    /// </summary>
    private async Task UpdatePreviewDocumentAsync(DiagramDocument document)
    {
        _document = document;
        await SyncStructureFromDocumentAsync(document);
    }

    /// <summary>
    /// Translates ONE document mutation into the semantic commit it implies (or ignores it when it
    /// is not structural). Called by the preview and by the open-editor dialog after the embedded
    /// editor reported a change.
    /// </summary>
    internal async Task SyncStructureFromDocumentAsync(DiagramDocument document)
    {
        if (_model is null || !CanEditModelStructure)
        {
            // 2.10.0 behavior: diagram-local edits stay diagram-local (never a silent model node).
            CaptureStructureSnapshot(document);
            return;
        }

        var snapshot = _structureSnapshot;
        if (snapshot is null || !ReferenceEquals(snapshot.Document, document))
        {
            // A regenerated / newly adopted document is a projection of the model, not a gesture.
            CaptureStructureSnapshot(document);
            return;
        }

        if (!string.Equals(snapshot.PageId, document.ActivePage?.Id, StringComparison.Ordinal))
        {
            // Phase 3 review (A3): the embedded editor switched to (or added) another page. The
            // diff would see every node of the previously captured page as "removed" and ask to
            // delete the model behind it — a page switch is navigation, not a structural gesture.
            // Re-capture the new page and return.
            CaptureStructureSnapshot(document);
            return;
        }

        var liveNodeIds = new HashSet<string>(document.Nodes.Select(node => node.Id), StringComparer.Ordinal);
        var liveEdgeIds = new HashSet<string>(document.Edges.Select(edge => edge.Id), StringComparer.Ordinal);

        var addedNodes = document.Nodes.Where(node => !snapshot.ElementByNodeId.ContainsKey(node.Id)).ToList();
        var removedNodeIds = snapshot.ElementByNodeId.Keys.Where(nodeId => !liveNodeIds.Contains(nodeId)).ToList();
        var addedEdges = document.Edges.Where(edge => !snapshot.EdgeIds.Contains(edge.Id)).ToList();
        var removedEdgeIds = snapshot.EdgeIds.Where(edgeId => !liveEdgeIds.Contains(edgeId)).ToList();

        // A reuse insert (the model tree's "Insert into preview") carries a modelElementId — it is
        // the phase-6 session-only placement feature and never a semantic insert.
        addedNodes.RemoveAll(node => node.Data is not null && node.Data.ContainsKey("modelElementId"));

        if (addedNodes.Count == 0 && addedEdges.Count == 0 && removedNodeIds.Count == 0 && removedEdgeIds.Count == 0)
        {
            CaptureStructureSnapshot(document);
            return;
        }

        // The diff lists are materialized — the snapshot can advance before the (async) commit, so
        // a second mutation raised while a modal is open cannot re-trigger the same gesture.
        CaptureStructureSnapshot(document);

        if (removedNodeIds.Count > 0 || removedEdgeIds.Count > 0)
        {
            await RequestDocumentRemovalAsync(snapshot, removedNodeIds, removedEdgeIds);
            return;
        }

        if (addedNodes.Count > 0)
        {
            await CommitPaletteInsertAsync(document, addedNodes);
            return;
        }

        await RequestDocumentRelationshipAsync(document, addedEdges[0]);
    }

    /// <summary>Expands a deleted node/edge set into the semantic removal and asks for confirmation
    /// (the cascade is shown as counts — decision Q3: the editor expands, the writer never cascades).
    /// <para>
    /// Phase 3 review (A4): only element ids that NO LIVE node still carries are removed. A model
    /// element may legitimately be rendered by several nodes (the duplicated placement the generator
    /// supports through a non-unique source id); deleting ONE of those nodes must not delete the
    /// model element behind the sibling that is still on the canvas. Edges follow the same rule.
    /// </para>
    /// </summary>
    private async Task RequestDocumentRemovalAsync(
        StructureSnapshot snapshot,
        IReadOnlyList<string> removedNodeIds,
        IReadOnlyList<string> removedEdgeIds)
    {
        var liveElementIds = new HashSet<string>(
            snapshot.Document.Nodes
                .Select(ElementIdOfNode)
                .Where(elementId => !string.IsNullOrEmpty(elementId))
                .Select(elementId => elementId!),
            StringComparer.Ordinal);

        var elementIds = removedNodeIds
            .Select(nodeId => snapshot.ElementByNodeId.TryGetValue(nodeId, out var elementId) ? elementId : null)
            .Where(elementId => !string.IsNullOrEmpty(elementId))
            .Select(elementId => elementId!)
            .Where(elementId => !liveElementIds.Contains(elementId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var liveEdgeIds = new HashSet<string>(snapshot.Document.Edges.Select(edge => edge.Id), StringComparer.Ordinal);
        var relationshipIds = removedEdgeIds
            .Where(edgeId => !liveEdgeIds.Contains(edgeId))
            .Where(edgeId => _model!.Relationships.Any(relationship => string.Equals(relationship.Id, edgeId, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (elementIds.Count == 0 && relationshipIds.Count == 0)
        {
            // A session-only node/edge disappeared (a duplicate placement whose element is still
            // rendered, or a diagram-local edge) — nothing semantic to do.
            CaptureStructureSnapshot(snapshot.Document);
            return;
        }

        await RequestDeleteAsync(ExpandRemoval(elementIds, relationshipIds));
    }

    /// <summary>The model element id a rendered node carries (<c>modelElementId</c> data), or null.</summary>
    private static string? ElementIdOfNode(DiagramNode node)
        => node.Data is not null
            && node.Data.TryGetValue("modelElementId", out var value)
            ? value?.ToString()
            : null;

    /// <summary>The full cascade of a removal: every descendant of the given elements plus every
    /// incident relationship (a writer that cannot cascade is still correct).</summary>
    internal (IReadOnlyList<string> ElementIds, IReadOnlyList<string> RelationshipIds) ExpandRemoval(
        IReadOnlyCollection<string> elementIds,
        IReadOnlyCollection<string> relationshipIds)
    {
        if (_model is null)
        {
            return (elementIds.ToList(), relationshipIds.ToList());
        }

        var elements = new HashSet<string>(elementIds, StringComparer.Ordinal);
        var pending = new Queue<string>(elements);
        while (pending.Count > 0)
        {
            var parentId = pending.Dequeue();
            foreach (var child in _model.Elements)
            {
                if (string.Equals(child.ParentId, parentId, StringComparison.Ordinal) && elements.Add(child.Id))
                {
                    pending.Enqueue(child.Id);
                }
            }
        }

        var relationships = new HashSet<string>(relationshipIds, StringComparer.Ordinal);
        foreach (var relationship in _model.Relationships)
        {
            if (elements.Contains(relationship.SourceElementId) || elements.Contains(relationship.TargetElementId))
            {
                relationships.Add(relationship.Id);
            }
        }

        // Descendants first — the order the change set and the engine batch require.
        var orderedElements = _model.Elements
            .Where(element => elements.Contains(element.Id))
            .ToList();

        return (
            ModelingStructureCommandSupport.OrderDescendantsFirst(orderedElements),
            [.. _model.Relationships.Where(relationship => relationships.Contains(relationship.Id)).Select(relationship => relationship.Id)]);
    }

    /// <summary>Opens the delete confirmation for a request (cascade counts shown).</summary>
    private Task RequestDeleteAsync((IReadOnlyList<string> ElementIds, IReadOnlyList<string> RelationshipIds) request)
    {
        if (request.ElementIds.Count == 0 && request.RelationshipIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        _pendingDelete = request;
        _deleteRestoreFocusTargetId = ResolveDeleteRestoreFocusTarget();
        _deleteConfirmationOpen = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>
    /// The id focus returns to after the delete confirmation closes (phase 3 review, U1): the tree
    /// item of the element FOLLOWING the removed primary, else the header's Delete button — never
    /// the document body. An id that stopped resolving falls back to the modal's opener.
    /// </summary>
    private string? ResolveDeleteRestoreFocusTarget()
    {
        var removed = _pendingDelete is { } pending
            ? new HashSet<string>(pending.ElementIds, StringComparer.Ordinal)
            : [];
        var primaryId = _selectedElement?.Id;

        if (_model is not null)
        {
            var ordered = _model.Elements;
            var start = primaryId is null
                ? 0
                : Math.Max(0, ordered.FindIndex(element => string.Equals(element.Id, primaryId, StringComparison.Ordinal)) + 1);
            for (var index = start; index < ordered.Count; index++)
            {
                if (!removed.Contains(ordered[index].Id))
                {
                    return TreeItemDomId(ordered[index].Id);
                }
            }
        }

        return _deleteHeaderButtonId;
    }

    /// <summary>
    /// Commits the pending removal as ONE cascade transaction and regenerates the documents. The
    /// batch is validated on a clone first; a rejection keeps the model untouched and shows the
    /// engine's message.
    /// </summary>
    internal async Task ConfirmDeleteAsync()
    {
        _deleteConfirmationOpen = false;
        var request = _pendingDelete;
        _pendingDelete = null;

        if (request is not { } deleteRequest || _model is null)
        {
            RegenerateDocumentsAfterStructureChange();
            return;
        }

        // Captured BEFORE the commit: the announcement names the user's selection, which the
        // command removes from the model (descendants-first order is not the user's selection order).
        var primaryName = ResolveDeletedPrimaryName(deleteRequest);
        var primaryRelationship = deleteRequest.ElementIds.Count == 0
            ? _model.Relationships.FirstOrDefault(relationship => deleteRequest.RelationshipIds.Contains(relationship.Id, StringComparer.Ordinal))
            : null;

        var command = new DeleteModelingSelectionCommand(
            DeleteTransactionName,
            _model,
            CreateOperationContext(),
            deleteRequest.ElementIds,
            deleteRequest.RelationshipIds,
            OperationViewId,
            EffectiveProviderKey);

        await PushStructuralCommandsAsync(
            DeleteTransactionName,
            [command],
            command.Batch,
            command.Intent,
            () =>
        {
            var removed = new HashSet<string>(deleteRequest.ElementIds, StringComparer.Ordinal);
            if (_selectedElement is not null && removed.Contains(_selectedElement.Id))
            {
                _selectedElement = null;
            }

            if (_selectedRelationship is not null
                && deleteRequest.RelationshipIds.Contains(_selectedRelationship.Id, StringComparer.Ordinal))
            {
                _selectedRelationship = null;
            }

            Announce(DescribeDelete(deleteRequest, primaryName, primaryRelationship));
        },
            seedView: false);
    }

    internal void CancelDelete()
    {
        _deleteConfirmationOpen = false;
        _pendingDelete = null;
        // The diagram already dropped the nodes — rebuild it from the model (nothing was removed).
        RegenerateDocumentsAfterStructureChange();
    }

    // ── Palette insert ───────────────────────────────────────────────────────

    /// <summary>
    /// Commits the semantic elements behind the palette nodes the embedded editor just inserted: the
    /// stencil id is mapped back to the notation's semantic type, the drop target's container
    /// (pool/lane/subprocess) becomes the element's <c>ParentId</c>, and the semantic element + its
    /// view node enter the model as ONE undo step.
    /// </summary>
    private async Task CommitPaletteInsertAsync(DiagramDocument document, IReadOnlyList<DiagramNode> addedNodes)
    {
        if (_model is null)
        {
            return;
        }

        var adds = new List<(ModelingElementAdd Add, ModelingViewNodeChange Node)>();
        foreach (var node in addedNodes)
        {
            if (!TryResolveSemanticType(node.StencilId, out var semanticType))
            {
                ShowStructureError(
                    Loc["TmModelingEditor_UnknownShapeTitle"],
                    string.Format(CultureInfo.CurrentUICulture, Loc["TmModelingEditor_UnknownShape"], node.StencilId ?? string.Empty),
                    Loc["TmModelingEditor_UnknownShapeFix"]);
                RegenerateDocumentsAfterStructureChange();
                return;
            }

            var containerId = ResolveContainerElementId(document, node);
            var elementId = Guid.NewGuid().ToString();
            adds.Add((
                new ModelingElementAdd
                {
                    Id = elementId,
                    SemanticType = semanticType,
                    Name = string.Format(CultureInfo.CurrentUICulture, Loc["TmModelingEditor_NewElementName"], GetTypeLabel(semanticType)),
                    Notation = EffectiveNotation,
                    ParentId = containerId
                },
                new ModelingViewNodeChange
                {
                    ElementId = elementId,
                    X = node.X,
                    Y = node.Y,
                    Width = node.W > 0 ? node.W : InsertedElementWidth,
                    Height = node.H > 0 ? node.H : InsertedElementHeight,
                    ParentNodeId = string.IsNullOrEmpty(containerId) ? null : containerId
                }));
        }

        if (adds.Count == 0)
        {
            RegenerateDocumentsAfterStructureChange();
            return;
        }

        var commands = new List<AddModelingElementCommand>();
        var batch = new ModelingOperationBatch();
        var intent = NewIntent();
        foreach (var (add, nodeChange) in adds)
        {
            // A viewless model stays view-less in the OPERATIONS (the seed command or the engine
            // creates the "default" view), while the intent reports that default id (A1).
            var command = new AddModelingElementCommand(
                InsertTransactionName,
                _model,
                CreateOperationContext(),
                add,
                nodeChange,
                OperationViewId,
                EffectiveProviderKey,
                intentViewId: TargetViewId);
            commands.Add(command);
            batch.Operations.AddRange(command.Batch.Operations);
            intent.AddedElements.Add(add);
            intent.ViewChanges.AddRange(command.Intent.ViewChanges);
        }

        await PushStructuralCommandsAsync(
            InsertTransactionName,
            commands,
            batch,
            intent,
            () => Announce(string.Join(
                " ",
                adds.Select(add => DescribeInsert(
                    add.Add.SemanticType,
                    add.Add.Name,
                    add.Add.ParentId)))));
    }

    /// <summary>Maps a palette stencil id back to the notation's semantic element type (the palette
    /// is built from the notation catalog, so the mapping is the catalog's inverse).</summary>
    private bool TryResolveSemanticType(string? stencilId, out string semanticType)
    {
        semanticType = string.Empty;
        if (string.IsNullOrWhiteSpace(stencilId) || NotationCatalog is null)
        {
            return false;
        }

        var entries = NotationCatalog.GetPalette(EffectiveNotation, Loc);
        var match = entries.FirstOrDefault(entry => string.Equals(entry.StencilId, stencilId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return false;
        }

        semanticType = match.SemanticType;
        return true;
    }

    /// <summary>
    /// The container (pool/lane/subprocess) the new node was dropped into: the SMALLEST container
    /// node whose bounds contain the new node's centre. Empty when the drop is on the bare canvas.
    /// </summary>
    internal string ResolveContainerElementId(DiagramDocument document, DiagramNode node)
    {
        if (_model is null)
        {
            return string.Empty;
        }

        var centerX = node.X + node.W / 2;
        var centerY = node.Y + node.H / 2;
        string bestId = string.Empty;
        var bestArea = double.MaxValue;

        foreach (var candidate in document.Nodes)
        {
            if (candidate.Data is null
                || !candidate.Data.TryGetValue("modelElementId", out var value)
                || value?.ToString() is not { Length: > 0 } elementId
                || string.Equals(elementId, node.Data?.GetValueOrDefault("modelElementId")?.ToString(), StringComparison.Ordinal))
            {
                continue;
            }

            var element = _model.Elements.FirstOrDefault(item => string.Equals(item.Id, elementId, StringComparison.Ordinal));
            if (element is null || !IsContainerSemanticType(element.SemanticType))
            {
                continue;
            }

            var contains = centerX >= candidate.X && centerX <= candidate.X + candidate.W
                && centerY >= candidate.Y && centerY <= candidate.Y + candidate.H;
            var area = candidate.W * candidate.H;
            if (contains && area < bestArea)
            {
                bestArea = area;
                bestId = elementId;
            }
        }

        return bestId;
    }

    // ── Relationship drawing ─────────────────────────────────────────────────

    /// <summary>
    /// Turns a drawn edge into a semantic relationship: the allowed types come ONLY from the notation
    /// rules provider, so an unsupported combination is REJECTED with the provider's explanation
    /// (nothing enters the undo stack and the edge is dropped from the diagram).
    /// </summary>
    private async Task RequestDocumentRelationshipAsync(DiagramDocument document, DiagramEdge edge)
    {
        var sourceElementId = ResolveElementIdForNode(document, edge.SourceNodeId);
        var targetElementId = ResolveElementIdForNode(document, edge.TargetNodeId);
        if (string.IsNullOrEmpty(sourceElementId) || string.IsNullOrEmpty(targetElementId))
        {
            ShowStructureError(
                Loc["TmModelingEditor_RelationshipRejectedTitle"],
                Loc["TmModelingEditor_RelationshipNeedsElements"],
                Loc["TmModelingEditor_RelationshipNeedsElementsFix"]);
            RegenerateDocumentsAfterStructureChange();
            return;
        }

        await OfferRelationshipAsync(sourceElementId, targetElementId, edge.ConnectorType, drawnEdge: true);
    }

    /// <summary>
    /// Computes the allowed relationship types for a pair of elements and either commits the ONE
    /// allowed type straight away, opens the type picker for several candidates, or rejects the
    /// combination with the rules provider's message.
    /// </summary>
    private async Task OfferRelationshipAsync(
        string sourceElementId,
        string targetElementId,
        string? preferredConnectorType,
        bool drawnEdge)
    {
        if (_model is null)
        {
            return;
        }

        var allowed = GetAllowedRelationshipTypes(sourceElementId, targetElementId);
        if (allowed.Count == 0)
        {
            ShowRelationshipRejection(sourceElementId, targetElementId);
            if (drawnEdge)
            {
                // The drawn edge never reaches the model — drop it from the projected document.
                RegenerateDocumentsAfterStructureChange();
            }

            return;
        }

        var preferred = ResolvePreferredRelationshipType(preferredConnectorType, allowed);
        if (preferred is not null)
        {
            await CommitRelationshipAsync(sourceElementId, targetElementId, preferred);
            return;
        }

        // A DRAWN edge implies a type (its connector type). When that implied type is NOT allowed,
        // the combination is refused with the notation rules' own explanation BEFORE the picker —
        // the user then picks one of the allowed alternatives or cancels (the edge is dropped).
        var implied = MatchCandidateType(preferredConnectorType);
        if (implied is not null && !allowed.Contains(implied, StringComparer.Ordinal))
        {
            ShowRelationshipRejectionFor(sourceElementId, targetElementId, implied);
        }

        // Several allowed types: the user picks ONE of the allowed candidates.
        _pendingRelationship = new PendingRelationship(sourceElementId, targetElementId, allowed, FromConnectDialog: !drawnEdge);
        _connectTargetId = targetElementId;
        _connectRelationshipType = allowed[0];
        _connectAllowedTypes = allowed;
        _connectRejection = null;
        _relationshipDialogOpen = true;
        StateHasChanged();
    }

    /// <summary>
    /// The relationship types the notation rules ALLOW between the two elements (the type picker's
    /// whole vocabulary). Every candidate is judged by the SAME
    /// <see cref="ModelingOperationEngine.ValidateRelationship"/> call the commit uses (phase 3
    /// review, A2) with the editor's <see cref="CreateOperationContext"/> — notation, viewpoint and
    /// elements included — so the picker can never offer a type the engine then refuses, and the
    /// editor owns no hand-built rule context. With no rules provider every notation relationship
    /// type is a candidate and the engine's validation decides at commit time.
    /// </summary>
    internal IReadOnlyList<string> GetAllowedRelationshipTypes(string sourceElementId, string targetElementId)
    {
        if (_model is null
            || !TryGetElement(sourceElementId, out _)
            || !TryGetElement(targetElementId, out _))
        {
            return [];
        }

        var candidates = GetRelationshipTypeCandidates();
        if (RelationshipRules is null)
        {
            return candidates;
        }

        var context = CreateOperationContext();
        var allowed = new List<string>();
        foreach (var relationshipType in candidates)
        {
            var outcome = ModelingOperationEngine.ValidateRelationship(
                _model,
                new ModelingRelationshipDto
                {
                    Id = "candidate",
                    RelationshipType = relationshipType,
                    SourceElementId = sourceElementId,
                    TargetElementId = targetElementId
                },
                context);

            if (outcome is null)
            {
                allowed.Add(relationshipType);
            }
        }

        return allowed;
    }

    /// <summary>The notation's relationship vocabulary, in profile order.</summary>
    private IReadOnlyList<string> GetRelationshipTypeCandidates()
    {
        var notation = EffectiveNotation;
        if (string.IsNullOrWhiteSpace(notation))
        {
            return [];
        }

        if (NotationCatalog is not null)
        {
            var entries = NotationCatalog.GetRelationshipTypes(notation, Loc);
            if (entries.Count > 0)
            {
                return [.. entries.Select(entry => entry.SemanticType)];
            }
        }

        return NotationProfiles?.GetProfile(notation)?.SupportedRelationshipTypes.ToList() ?? [];
    }

    /// <summary>The single relationship type the drawn edge implies (its connector type), else the
    /// only allowed candidate; null means "the user must pick".</summary>
    private static string? ResolvePreferredRelationshipType(string? connectorType, IReadOnlyList<string> allowed)
    {
        if (!string.IsNullOrWhiteSpace(connectorType))
        {
            var match = allowed.FirstOrDefault(type => string.Equals(type, connectorType, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return allowed.Count == 1 ? allowed[0] : null;
    }

    /// <summary>The notation relationship type a connector type names, or null when it names none.</summary>
    private string? MatchCandidateType(string? connectorType)
        => string.IsNullOrWhiteSpace(connectorType)
            ? null
            : GetRelationshipTypeCandidates()
                .FirstOrDefault(type => string.Equals(type, connectorType, StringComparison.OrdinalIgnoreCase));

    /// <summary>Shows the notation rules' own explanation for a rejected combination (the SAME
    /// message <c>modeling_apply_operations</c> returns); the reported type is the first candidate.</summary>
    private void ShowRelationshipRejection(string sourceElementId, string targetElementId)
        => ShowRelationshipRejectionFor(sourceElementId, targetElementId, GetRelationshipTypeCandidates().FirstOrDefault());

    /// <summary>
    /// The rules' explanation (message + suggested fix) for refusing ONE relationship type between
    /// the pair — resolved through <see cref="ModelingOperationEngine.ValidateRelationship"/> with
    /// the editor's context, so the text is the engine's own (phase 3 review, A2); the localized
    /// fallback covers a structure-only failure (no rules provider resolved the pair).
    /// </summary>
    internal (string Message, string? Fix) ResolveRelationshipRejection(
        string sourceElementId,
        string targetElementId,
        string? relationshipType)
    {
        if (_model is not null
            && !string.IsNullOrWhiteSpace(relationshipType)
            && ModelingOperationEngine.ValidateRelationship(
                _model,
                new ModelingRelationshipDto
                {
                    Id = "candidate",
                    RelationshipType = relationshipType!,
                    SourceElementId = sourceElementId,
                    TargetElementId = targetElementId
                },
                CreateOperationContext()) is { } error)
        {
            return (
                error.Message,
                string.IsNullOrWhiteSpace(error.SuggestedFix) ? null : error.SuggestedFix);
        }

        return (
            string.Format(
                CultureInfo.CurrentUICulture,
                Loc["TmModelingEditor_RelationshipRejected"],
                GetTypeLabel(_model?.Elements.FirstOrDefault(element => element.Id == sourceElementId)?.SemanticType ?? string.Empty),
                GetTypeLabel(_model?.Elements.FirstOrDefault(element => element.Id == targetElementId)?.SemanticType ?? string.Empty)),
            null);
    }

    /// <summary>Publishes the rejection as the editor's alert AND the polite announcement.</summary>
    private void ShowRelationshipRejectionFor(string sourceElementId, string targetElementId, string? relationshipType)
    {
        var (message, fix) = ResolveRelationshipRejection(sourceElementId, targetElementId, relationshipType);
        ShowStructureError(Loc["TmModelingEditor_RelationshipRejectedTitle"], message, fix);
    }

    /// <summary>Commits ONE semantic relationship (+ its view connection) as one undo step.</summary>
    private async Task CommitRelationshipAsync(string sourceElementId, string targetElementId, string relationshipType)
    {
        if (_model is null)
        {
            return;
        }

        var add = new ModelingRelationshipAdd
        {
            Id = Guid.NewGuid().ToString(),
            RelationshipType = relationshipType,
            SourceElementId = sourceElementId,
            TargetElementId = targetElementId,
            Name = string.Empty
        };

        var command = new AddModelingRelationshipCommand(
            ConnectTransactionName,
            _model,
            CreateOperationContext(),
            add,
            OperationViewId,
            EffectiveProviderKey,
            intentViewId: TargetViewId);

        await PushStructuralCommandsAsync(
            ConnectTransactionName,
            [command],
            command.Batch,
            command.Intent,
            () => Announce(string.Format(
                CultureInfo.CurrentUICulture,
                Loc["TmModelingEditor_StructureConnected"],
                GetElementName(sourceElementId),
                GetElementName(targetElementId),
                GetTypeLabel(relationshipType))));
    }

}