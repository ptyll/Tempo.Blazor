using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// The non-drag authoring surfaces of the phase-3 structure half (WCAG 2.5.7, decision
/// <c>MODELING-AUTHORING-CHANGESET</c> §9.1/§9.2): "Insert element &gt; type &gt; container", "Connect
/// to &gt; target &gt; type" and the cascade delete confirmation. Every drag gesture of the embedded
/// editor has a keyboard/tap equivalent here, and both paths raise the SAME commit contract —
/// the alternative is a second entry point, never a second implementation.
/// </summary>
public partial class TmModelingEditor
{
    private bool _insertElementDialogOpen;
    private string _insertSemanticType = string.Empty;
    private string _insertContainerId = string.Empty;
    private string _insertName = string.Empty;

    private bool _relationshipDialogOpen;
    private PendingRelationship? _pendingRelationship;
    private string _connectTargetId = string.Empty;
    private string _connectRelationshipType = string.Empty;
    private IReadOnlyList<string> _connectAllowedTypes = [];
    private string? _connectRejection;
    private string? _connectRejectionFix;

    private bool _deleteConfirmationOpen;

    /// <summary>
    /// The delete confirmation's TITLE (phase 3 review, U4): the named element ("Delete “Sales”?"),
    /// or the relationship's endpoints when only a relationship is being removed. Grammatical
    /// counts live in the summary — a title never counts.
    /// </summary>
    internal string DeleteTitleText
    {
        get
        {
            if (_pendingDelete is not { } pending)
            {
                return Loc["TmModelingEditor_DeleteTitle"];
            }

            if (pending.ElementIds.Count == 0)
            {
                var relationship = _model?.Relationships.FirstOrDefault(candidate => pending.RelationshipIds.Contains(candidate.Id, StringComparer.Ordinal));
                return relationship is null
                    ? Loc["TmModelingEditor_DeleteTitle"]
                    : string.Format(
                        CultureInfo.CurrentUICulture,
                        Loc["TmModelingEditor_DeleteRelationshipTitle"],
                        GetElementName(relationship.SourceElementId),
                        GetElementName(relationship.TargetElementId));
            }

            return string.Format(
                CultureInfo.CurrentUICulture,
                Loc["TmModelingEditor_DeleteTitle"],
                ResolveDeletedPrimaryName(pending));
        }
    }

    /// <summary>The localized cascade summary of the pending deletion: the grammatical plural counts
    /// of the nested elements and the relationships (phase 3 review, U4). Empty for a
    /// relationship-only confirmation — its title already names the whole removal.</summary>
    internal string DeleteSummaryText
    {
        get
        {
            if (_pendingDelete is not { } pending || pending.ElementIds.Count == 0)
            {
                return string.Empty;
            }

            return string.Format(
                CultureInfo.CurrentUICulture,
                Loc["TmModelingEditor_DeleteSummary"],
                Loc.Plural("TmModelingEditor_DeleteNestedCount", Math.Max(0, pending.ElementIds.Count - 1)),
                Loc.Plural("TmModelingEditor_DeleteRelCount", pending.RelationshipIds.Count));
        }
    }

    /// <summary>
    /// Where focus goes when the delete confirmation closes (phase 3 review, U1): the tree item of
    /// the element FOLLOWING the deleted one, else the header's Delete button (still rendered,
    /// aria-disabled once nothing is selected) — never the document body. Computed when the
    /// confirmation OPENS, because the selection (and possibly the tree row) is gone by the time it
    /// closes; the focus-trap falls back to the opener when the id no longer resolves.
    /// </summary>
    private string? _deleteRestoreFocusTargetId;

    internal string? DeleteRestoreFocusTargetId => _deleteRestoreFocusTargetId;

    /// <summary>The name shown as the relationship source (the fixed pending source, else the selection).</summary>
    private string RelationshipSourceLabel
        => GetElementName(_pendingRelationship?.SourceElementId ?? _selectedElement?.Id ?? string.Empty);

    /// <summary>Whether the relationship dialog offers a target picker: a DRAWN edge fixes its two
    /// endpoints, while "Connect to…" lets the user choose the target.</summary>
    private bool RelationshipTargetVisible => _pendingRelationship is null || _pendingRelationship.FromConnectDialog;

    /// <summary>The palette of the active notation as dialog options.</summary>
    private IReadOnlyList<(string Value, string Label)> InsertTypeChoices
        => [.. InsertTypeOptions.Select(entry => (entry.SemanticType, entry.Label))];

    /// <summary>The container candidates as dialog options (the root option is rendered by the
    /// child). Phase 3 review, U7: every option is "name (type)" so two same-named containers stay
    /// distinguishable.</summary>
    private IReadOnlyList<(string Value, string Label)> InsertContainerChoices
        => [.. InsertContainerOptions.Select(element => (element.Id, DescribeElementOption(element)))];

    /// <summary>The "Connect to" target candidates as dialog options ("name (type)", U7).</summary>
    private IReadOnlyList<(string Value, string Label)> ConnectTargetChoices
        => [.. ConnectTargetOptions.Select(element => (element.Id, DescribeElementOption(element)))];

    /// <summary>The dialog option label of an element: its display name plus the localized type in
    /// parentheses, so equal names never collide in a picker.</summary>
    internal string DescribeElementOption(ModelingElementDto element)
    {
        var name = string.IsNullOrWhiteSpace(element.Name) ? element.Id : element.Name;
        return string.IsNullOrWhiteSpace(element.SemanticType)
            ? name
            : $"{name} ({GetTypeLabel(element.SemanticType)})";
    }

    /// <summary>The ALLOWED relationship types of the current pair as dialog options.</summary>
    private IReadOnlyList<(string Value, string Label)> ConnectTypeChoices
        => [.. ConnectTypeOptions.Select(type => (type, GetTypeLabel(type)))];
    private (IReadOnlyList<string> ElementIds, IReadOnlyList<string> RelationshipIds)? _pendingDelete;

    // ── Insert element ───────────────────────────────────────────────────────

    /// <summary>The palette entries of the active notation (type picker of the insert dialog).</summary>
    internal IReadOnlyList<ModelingNotationCatalogEntry> InsertTypeOptions
        => NotationCatalog?.GetPalette(EffectiveNotation, Loc) ?? [];

    /// <summary>Container candidates of the insert dialog: the root plus every element whose
    /// notation profile type CONTAINS other elements (A6 — the profile owns the vocabulary).</summary>
    internal IReadOnlyList<ModelingElementDto> InsertContainerOptions => ContainerElements;

    /// <summary>Opens "Insert element": the first notation type and the bare canvas are preselected.</summary>
    internal Task OpenInsertElementDialogAsync()
    {
        if (!CanEditModelStructure || IsMobileLayout)
        {
            return Task.CompletedTask;
        }

        var types = InsertTypeOptions;
        _insertSemanticType = types.Count > 0 ? types[0].SemanticType : string.Empty;
        // Phase 3 review (U12): the container closest to the user is preselected — the selected
        // element itself when it IS a container, else its parent when THAT is one.
        _insertContainerId = ResolveDefaultInsertContainerId();
        _insertName = string.Empty;
        _insertElementDialogOpen = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>Closes the insert sheet without committing (the test seam drives the same path).</summary>
    internal Task CloseInsertElementDialogAsync()
    {
        _insertElementDialogOpen = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task HandleInsertTypeChangedAsync(string value)
    {
        _insertSemanticType = value ?? string.Empty;
        return Task.CompletedTask;
    }

    private Task HandleInsertContainerChangedAsync(string value)
    {
        _insertContainerId = value ?? string.Empty;
        return Task.CompletedTask;
    }

    private Task HandleInsertNameChangedAsync(string value)
    {
        _insertName = value ?? string.Empty;
        return Task.CompletedTask;
    }

    /// <summary>
    /// The preselected container of the insert dialog (phase 3 review, U12): the selected element
    /// when its type contains others, else its PARENT element when that one does, else the root.
    /// </summary>
    internal string ResolveDefaultInsertContainerId()
    {
        if (_selectedElement is null)
        {
            return string.Empty;
        }

        if (IsContainerSemanticType(_selectedElement.SemanticType))
        {
            return _selectedElement.Id;
        }

        var parent = _model?.Elements.FirstOrDefault(element =>
            string.Equals(element.Id, _selectedElement.ParentId, StringComparison.Ordinal));
        return parent is not null && IsContainerSemanticType(parent.SemanticType) ? parent.Id : string.Empty;
    }

    /// <summary>
    /// Commits "Insert element": mints the semantic element + its view node inside the chosen
    /// container (phase 3 review, A12 — the container's bounds, not the viewport, decide where the
    /// new element lands; the container GROWS in the same command when it no longer fits) with the
    /// chosen container as its ParentId. One undo removes the element, its node and the growth.
    /// </summary>
    internal async Task ConfirmInsertElementAsync()
    {
        _insertElementDialogOpen = false;
        if (_model is null || string.IsNullOrWhiteSpace(_insertSemanticType))
        {
            return;
        }

        var containerId = string.IsNullOrWhiteSpace(_insertContainerId) ? string.Empty : _insertContainerId;
        var (x, y, width, height, containerResize) = ResolveInsertPlacement(containerId);
        var typeLabel = GetTypeLabel(_insertSemanticType);
        var name = string.IsNullOrWhiteSpace(_insertName)
            ? string.Format(CultureInfo.CurrentUICulture, Loc["TmModelingEditor_NewElementName"], typeLabel)
            : _insertName.Trim();

        var add = new ModelingElementAdd
        {
            Id = Guid.NewGuid().ToString(),
            SemanticType = _insertSemanticType,
            Name = name,
            Notation = EffectiveNotation,
            ParentId = containerId
        };
        var node = new ModelingViewNodeChange
        {
            ElementId = add.Id,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            ParentNodeId = string.IsNullOrEmpty(containerId) ? null : containerId
        };

        var command = new AddModelingElementCommand(
            InsertTransactionName,
            _model,
            CreateOperationContext(),
            add,
            node,
            OperationViewId,
            EffectiveProviderKey,
            intentViewId: TargetViewId,
            containerResize: containerResize);

        var elementId = add.Id;
        var insertedType = _insertSemanticType;
        var insertedName = name;
        await PushStructuralCommandsAsync(
            InsertTransactionName,
            [command],
            command.Batch,
            command.Intent,
            () =>
            {
                _selectedElement = _model.Elements.FirstOrDefault(element => string.Equals(element.Id, elementId, StringComparison.Ordinal));
                _selectedRelationship = null;
                Announce(DescribeInsert(insertedType, insertedName, containerId));
            });
    }

    /// <summary>
    /// Where a dialog-created element lands (phase 3 review, A12). With a container: inside the
    /// container's rendered bounds (a 24px collision nudge clamped to those bounds), with the
    /// lane-in-pool band rule (a lane spans its pool's width below the last lane). Without one: the
    /// preview's viewport centre, else the active page centre (the pre-review behaviour). The
    /// returned container resize is non-null when the container must GROW to hold the element.
    /// </summary>
    internal (double X, double Y, double Width, double Height, ModelingViewNodeChange? ContainerResize) ResolveInsertPlacement(string containerId)
    {
        // A CONTAINER is born big enough to hold children: a pool is a band stack, a lane a row,
        // a subprocess a small frame. Without this the first lane of a fresh pool could never fit
        // its own task (A12) and the authored scene would start as a pile of overlapping boxes.
        var width = ResolveInsertedWidth(_insertSemanticType);
        var height = ResolveInsertedHeight(_insertSemanticType);
        if (_model is null || string.IsNullOrWhiteSpace(containerId))
        {
            // ResolveInsertionPoint returns the TOP-LEFT for the DEFAULT element size; re-centre it
            // for the real (container) size so a pool grows around the same point.
            var (defaultX, defaultY) = ResolveInsertionPoint();
            var centerX = defaultX + InsertedElementWidth / 2;
            var centerY = defaultY + InsertedElementHeight / 2;
            var (rootX, rootY) = ResolveRootPlacement(centerX, centerY, width, height);
            return (rootX, rootY, width, height, null);
        }

        var view = ResolveTargetView();
        var containerNode = view?.Nodes.FirstOrDefault(node => string.Equals(node.ElementId, containerId, StringComparison.Ordinal));
        if (view is null || containerNode is null || containerNode.Width <= 0 || containerNode.Height <= 0)
        {
            var (defaultX, defaultY) = ResolveInsertionPoint();
            var (rootX, rootY) = ResolveRootPlacement(
                defaultX + InsertedElementWidth / 2,
                defaultY + InsertedElementHeight / 2,
                width,
                height);
            return (rootX, rootY, width, height, null);
        }

        double x;
        double y;
        if (IsLaneSemanticType(_insertSemanticType))
        {
            // A lane is a horizontal BAND of its pool: the pool's inner width, below the last lane.
            x = containerNode.X + 30;
            width = Math.Max(InsertedElementWidth, containerNode.Width - 30);
            height = InsertedLaneHeight;
            var laneBottoms = view.Nodes
                .Where(node => string.Equals(node.ParentNodeId, containerId, StringComparison.Ordinal)
                    && IsLaneElement(node.ElementId))
                .Select(node => node.Y + node.Height)
                .ToList();
            y = laneBottoms.Count == 0 ? containerNode.Y + 12 : laneBottoms.Max() + 12;
        }
        else
        {
            // A grid of candidate slots inside the container: the first free one wins, so two
            // elements dropped into the same lane sit SIDE BY SIDE instead of on top of each
            // other. No slot fits a container that is too small — the fallback returns the first
            // slot and the container GROWS below.
            var startX = containerNode.X + 30;
            var startY = containerNode.Y + 12;
            x = startX;
            y = startY;
            var placed = false;
            for (var row = 0; row < 10 && !placed; row++)
            {
                for (var column = 0; column < 10 && !placed; column++)
                {
                    var candidateX = startX + column * (width + 24);
                    var candidateY = startY + row * (height + 24);
                    if (candidateX + width + 8 > containerNode.X + containerNode.Width
                        || candidateY + height + 8 > containerNode.Y + containerNode.Height)
                    {
                        // This row/column band cannot hold the node — later ones cannot either.
                        break;
                    }

                    if (!CollidesWithSibling(view, containerId, candidateX, candidateY, width, height))
                    {
                        x = candidateX;
                        y = candidateY;
                        placed = true;
                    }
                }
            }

            if (!placed)
            {
                x = startX;
                y = startY;
            }
        }

        ModelingViewNodeChange? resize = null;
        var requiredWidth = x + width + 8 - containerNode.X;
        var requiredHeight = y + height + 8 - containerNode.Y;
        if (requiredWidth > containerNode.Width || requiredHeight > containerNode.Height)
        {
            resize = new ModelingViewNodeChange
            {
                ElementId = containerId,
                Width = Math.Max(containerNode.Width, requiredWidth),
                Height = Math.Max(containerNode.Height, requiredHeight)
            };
        }

        return (x, y, width, height, resize);
    }

    /// <summary>The width a freshly inserted type gets: containers are born with room for children.</summary>
    private double ResolveInsertedWidth(string? semanticType)
        => IsPoolSemanticType(semanticType) ? InsertedPoolWidth
            : IsContainerSemanticType(semanticType) ? InsertedContainerWidth
            : InsertedElementWidth;

    /// <summary>The height a freshly inserted type gets (pools are tall, lanes one row).</summary>
    private double ResolveInsertedHeight(string? semanticType)
        => IsPoolSemanticType(semanticType) ? InsertedPoolHeight
            : IsLaneSemanticType(semanticType) ? InsertedLaneHeight
            : IsContainerSemanticType(semanticType) ? InsertedContainerHeight
            : InsertedElementHeight;

    /// <summary>Centres a root-level insert of the given size (the viewport centre, else the page centre).</summary>
    internal (double X, double Y) ResolveRootPlacement(double centerX, double centerY, double width, double height)
    {
        var x = centerX - width / 2;
        var y = centerY - height / 2;
        var view = ResolveTargetView();
        if (view is not null)
        {
            // A root insert only collides with other ROOT nodes — the containers' own children
            // must not push it away.
            for (var attempt = 0; attempt < 20 && CollidesWithSibling(view, string.Empty, x, y, width, height); attempt++)
            {
                x += 24;
                y += 24;
            }
        }

        return (x, y);
    }

    /// <summary>Whether the semantic type is the BPMN pool (the lane container's band owner).</summary>
    private static bool IsPoolSemanticType(string? semanticType)
        => string.Equals(semanticType?.Trim(), "Pool", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the candidate rectangle overlaps a rendered SIBLING — a node parented by the same
    /// container (<paramref name="containerId"/>; empty = the root level). The container itself and
    /// its ancestors are NOT siblings: counting them made every insert "collide" with the box it
    /// was dropped into and nudged it to the container's far edge (phase 3 review, A12).
    /// </summary>
    private static bool CollidesWithSibling(ModelingViewDto view, string containerId, double x, double y, double width, double height)
        => view.Nodes.Any(node => string.Equals(node.ParentNodeId ?? string.Empty, containerId, StringComparison.Ordinal)
            && node.Width > 0 && node.Height > 0
            && x < node.X + node.Width
            && x + width > node.X
            && y < node.Y + node.Height
            && y + height > node.Y);

    /// <summary>The view nodes live in: the resolved target view, else the model's first view.</summary>
    private ModelingViewDto? ResolveTargetView()
    {
        if (_model is null)
        {
            return null;
        }

        var viewId = TargetViewId;
        return viewId is null
            ? _model.Views.FirstOrDefault()
            : _model.Views.FirstOrDefault(view => string.Equals(view.Id, viewId, StringComparison.Ordinal)) ?? _model.Views.FirstOrDefault();
    }

    /// <summary>Whether the semantic type is the BPMN band element laid out as a pool row.</summary>
    private static bool IsLaneSemanticType(string? semanticType)
        => string.Equals(semanticType?.Trim(), "Lane", StringComparison.OrdinalIgnoreCase);

    private bool IsLaneElement(string? elementId)
        => IsLaneSemanticType(_model?.Elements.FirstOrDefault(element => string.Equals(element.Id, elementId, StringComparison.Ordinal))?.SemanticType);

    /// <summary>
    /// The insertion point of a dialog-created element: the preview's viewport centre, else the
    /// active page centre (SSR and bUnit have no viewport). An OCCUPIED spot is nudged diagonally
    /// (the same collision rule the palette drop uses) so repeated non-drag inserts never stack on
    /// top of each other — the user always sees the new element.
    /// </summary>
    internal (double X, double Y) ResolveInsertionPoint()
    {
        double centerX;
        double centerY;
        if (_preview is not null && _preview.TryGetInsertionCenter(out var point))
        {
            centerX = point.X;
            centerY = point.Y;
        }
        else
        {
            var page = _document?.ActivePage;
            centerX = page is null ? InsertedElementWidth / 2 : page.Width / 2d;
            centerY = page is null ? InsertedElementHeight / 2 : page.Height / 2d;
        }

        var x = centerX - InsertedElementWidth / 2;
        var y = centerY - InsertedElementHeight / 2;
        var view = TargetViewId is { } viewId
            ? _model?.Views.FirstOrDefault(candidate => string.Equals(candidate.Id, viewId, StringComparison.Ordinal))
            : _model?.Views.FirstOrDefault();

        if (view is not null)
        {
            for (var attempt = 0; attempt < 20 && view.Nodes.Any(node => Overlaps(node, x, y)); attempt++)
            {
                x += 24;
                y += 24;
            }
        }

        return (x, y);
    }

    private static bool Overlaps(ModelingViewNodeDto node, double x, double y)
        => node.Width > 0 && node.Height > 0
            && x < node.X + node.Width
            && x + InsertedElementWidth > node.X
            && y < node.Y + node.Height
            && y + InsertedElementHeight > node.Y;

    // ── Connect to (keyboard alternative to drawing an edge) ─────────────────

    /// <summary>Target candidates of "Connect to": every OTHER element of the model.</summary>
    internal IReadOnlyList<ModelingElementDto> ConnectTargetOptions
        => _model is null || _selectedElement is null
            ? []
            : [.. _model.Elements.Where(element => !string.Equals(element.Id, _selectedElement.Id, StringComparison.Ordinal))];

    /// <summary>The labels of the currently allowed relationship types.</summary>
    internal IReadOnlyList<string> ConnectTypeOptions => _connectAllowedTypes;

    /// <summary>The inline explanation when the selected pair allows no relationship at all.</summary>
    internal string? ConnectRejection => _connectRejection;

    /// <summary>The rules' suggested fix of the inline rejection.</summary>
    internal string? ConnectRejectionFix => _connectRejectionFix;

    internal bool RelationshipDialogOpen => _relationshipDialogOpen;

    /// <summary>Opens "Connect to" for the selected element (the non-drag alternative to drawing).</summary>
    internal Task OpenConnectDialogAsync()
    {
        if (!CanEditModelStructure || IsMobileLayout || _selectedElement is null)
        {
            return Task.CompletedTask;
        }

        var targets = ConnectTargetOptions;
        _connectTargetId = targets.Count > 0 ? targets[0].Id : string.Empty;
        RecomputeConnectTypes();
        _relationshipDialogOpen = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>Closes the relationship dialog; a DRAWN edge that was waiting for its type is
    /// dropped by regenerating the document from the model.</summary>
    internal Task CloseRelationshipDialogAsync()
    {
        var wasDrawnEdge = _pendingRelationship is { } pending && !pending.FromConnectDialog;
        _relationshipDialogOpen = false;
        _pendingRelationship = null;
        _connectRejection = null;
        _connectRejectionFix = null;
        if (wasDrawnEdge)
        {
            RegenerateDocumentsAfterStructureChange();
        }

        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task HandleConnectTargetChangedAsync(string value)
    {
        _connectTargetId = value ?? string.Empty;
        RecomputeConnectTypes();
        return Task.CompletedTask;
    }

    private Task HandleConnectTypeChangedAsync(string value)
    {
        _connectRelationshipType = value ?? string.Empty;
        return Task.CompletedTask;
    }

    /// <summary>Test seam (InternalsVisibleTo): drives the "Connect to" target pick.</summary>
    internal Task SelectConnectTargetForTestAsync(string targetId) => HandleConnectTargetChangedAsync(targetId);

    /// <summary>Test seam (InternalsVisibleTo): drives the "Connect to" relationship-type pick.</summary>
    internal Task SelectConnectTypeForTestAsync(string relationshipType) => HandleConnectTypeChangedAsync(relationshipType);

    /// <summary>Recomputes the allowed types (and the rejection text) for the current pair.</summary>
    private void RecomputeConnectTypes()
    {
        _connectRejection = null;
        _connectRejectionFix = null;
        if (_selectedElement is null || string.IsNullOrWhiteSpace(_connectTargetId))
        {
            _connectAllowedTypes = [];
            _connectRelationshipType = string.Empty;
            return;
        }

        var allowed = GetAllowedRelationshipTypes(_selectedElement.Id, _connectTargetId);
        _connectAllowedTypes = allowed;
        if (allowed.Count == 0)
        {
            // The dialog explains the refusal INLINE — it must not also raise the editor-wide alert
            // while the user is still choosing inside the sheet.
            _connectRelationshipType = string.Empty;
            var (message, fix) = ResolveRelationshipRejection(
                _selectedElement.Id,
                _connectTargetId,
                GetRelationshipTypeCandidates().FirstOrDefault());
            _connectRejection = message;
            _connectRejectionFix = fix;
            return;
        }

        if (!allowed.Contains(_connectRelationshipType, StringComparer.Ordinal))
        {
            _connectRelationshipType = allowed[0];
        }

        // The sheet re-renders with the recomputed type list, so the confirm button's enabled state
        // and the visible selection always describe the pair the user just picked.
        StateHasChanged();
    }

    /// <summary>Commits the dialog's relationship once the user confirms the type.</summary>
    internal async Task ConfirmConnectAsync()
    {
        var pending = _pendingRelationship;
        var sourceId = pending?.SourceElementId ?? _selectedElement?.Id;
        var targetId = pending?.TargetElementId ?? _connectTargetId;
        var relationshipType = string.IsNullOrWhiteSpace(_connectRelationshipType)
            ? _connectAllowedTypes.FirstOrDefault()
            : _connectRelationshipType;

        _relationshipDialogOpen = false;
        _pendingRelationship = null;

        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId) || string.IsNullOrWhiteSpace(relationshipType))
        {
            return;
        }

        await CommitRelationshipAsync(sourceId, targetId, relationshipType);
    }

    // ── Delete (Delete key / context menu / inspector action) ────────────────

    internal bool DeleteConfirmationOpen => _deleteConfirmationOpen;

    /// <summary>Element count of the pending cascade (the confirmation's neutral count).</summary>
    internal int PendingDeleteElementCount => _pendingDelete is { } pending ? pending.ElementIds.Count : 0;

    /// <summary>Relationship count of the pending cascade.</summary>
    internal int PendingDeleteRelationshipCount => _pendingDelete is { } pending ? pending.RelationshipIds.Count : 0;

    /// <summary>
    /// The inspector header's Delete action: expands the cascade for the current selection and asks
    /// for confirmation (the same modal the diagram's own Delete gesture feeds).
    /// </summary>
    internal async Task RequestDeleteSelectedAsync()
    {
        // The mobile band is G2 (viewing + light edits): no structural entry at all, and the
        // inspector's Delete row is not rendered there — the guard keeps every entry honest.
        if (!CanEditModelStructure || _model is null || IsMobileLayout)
        {
            return;
        }

        if (_selectedRelationship is not null)
        {
            await RequestDeleteAsync(([], [_selectedRelationship.Id]));
            return;
        }

        if (_selectedElement is null)
        {
            return;
        }

        await RequestDeleteAsync(ExpandRemoval([_selectedElement.Id], []));
    }
}
