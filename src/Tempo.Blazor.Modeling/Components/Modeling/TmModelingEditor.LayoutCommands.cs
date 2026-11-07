using System.Globalization;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// The phase-4 COMMAND half of the editor's layout surface (review split — the diff/sync half lives in
/// <c>TmModelingEditor.Layout.cs</c>): the undoable "Arrange" auto-layout sheet, the non-drag "Move to
/// container" sheet (WCAG 2.5.7) and the polite layout announcements (debounced for a nudge burst,
/// immediate for a hierarchy change). Both commands commit through the SAME
/// <see cref="SetModelingViewLayoutCommand"/> as a drag gesture, so a keyboard user and a pointer user
/// produce the same semantics.
/// </summary>
public partial class TmModelingEditor
{
    // ── Arrange (auto-layout LR/TB) ──────────────────────────────────────────

    /// <summary>The direction options of the arrange sheet (value + localized label).</summary>
    private IReadOnlyList<(string Value, string Label)> ArrangeDirectionChoices =>
    [
        (ArrangeDirectionLeftToRight, Loc["TmModelingEditor_ArrangeDirectionLr"]),
        (ArrangeDirectionTopToBottom, Loc["TmModelingEditor_ArrangeDirectionTb"])
    ];

    /// <summary>Opens the arrange sheet (the non-drag auto-layout entry, WCAG 2.5.7).</summary>
    internal void OpenArrangeDialog()
    {
        if (!CanEditModelLayout || _model is null || _document is null || IsMobileLayout)
        {
            return;
        }

        _arrangeDirection = ArrangeDirectionLeftToRight;
        _arrangeDialogOpen = true;
        StateHasChanged();
    }

    /// <summary>Closes the arrange sheet without arranging anything.</summary>
    internal void CloseArrangeDialog()
    {
        _arrangeDialogOpen = false;
        StateHasChanged();
    }

    internal Task HandleArrangeDirectionChangedAsync(string? direction)
    {
        _arrangeDirection = string.Equals(direction, ArrangeDirectionTopToBottom, StringComparison.OrdinalIgnoreCase)
            ? ArrangeDirectionTopToBottom
            : ArrangeDirectionLeftToRight;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Commits the arrangement as ONE undoable transaction of <c>set_view_node</c> operations
    /// (§9.4): the layout is computed from the model + the rendered stencil sizes and the
    /// relationships, cloned-validated through the engine, and regenerated so the canvas shows the
    /// result immediately. A routed connection (a stored waypoint list) has its waypoints CLEARED in
    /// the same transaction — an arranged scene must not keep the bends of the old geometry — and one
    /// undo restores both the geometry and the routing.
    /// </summary>
    internal async Task ConfirmArrangeAsync()
    {
        _arrangeDialogOpen = false;
        if (_model is null || _document is null || !CanEditModelLayout)
        {
            StateHasChanged();
            return;
        }

        var direction = string.Equals(_arrangeDirection, ArrangeDirectionTopToBottom, StringComparison.Ordinal)
            ? ModelingLayoutArranger.Direction.TopToBottom
            : ModelingLayoutArranger.Direction.LeftToRight;

        var boxes = ModelingLayoutArranger.Arrange(
            _model.Elements,
            _document.Nodes,
            _model.Relationships,
            IsContainerSemanticType,
            direction);

        var view = ModelingStructureCommandSupport.FindView(_model, OperationViewId);
        var changes = new List<ModelingViewNodeChange>();
        foreach (var box in boxes)
        {
            var element = _model.Elements.FirstOrDefault(item => string.Equals(item.Id, box.ElementId, StringComparison.Ordinal));
            if (element is null)
            {
                continue;
            }

            var viewNode = view?.Nodes.FirstOrDefault(node => string.Equals(node.ElementId, box.ElementId, StringComparison.Ordinal));
            var parentElementId = string.IsNullOrEmpty(element.ParentId) ? string.Empty : element.ParentId.Trim();
            var parentChanged = !string.Equals(viewNode?.ParentNodeId ?? string.Empty, parentElementId, StringComparison.Ordinal);
            var geometryChanged = viewNode is null
                || !NearlyEqual(viewNode.X, box.X)
                || !NearlyEqual(viewNode.Y, box.Y)
                || !NearlyEqual(viewNode.Width, box.Width)
                || !NearlyEqual(viewNode.Height, box.Height);
            if (!geometryChanged && !parentChanged)
            {
                continue;
            }

            changes.Add(new ModelingViewNodeChange
            {
                ElementId = box.ElementId,
                X = box.X,
                Y = box.Y,
                Width = box.Width,
                Height = box.Height,
                ParentNodeId = parentChanged ? parentElementId : null
            });
        }

        // A routed connection keeps the bends of the OLD geometry — clear them in the same command
        // (and the same undo step) so the arranged scene draws its own routing.
        var connectionChanges = new List<ModelingViewConnectionChange>();
        foreach (var relationship in _model.Relationships)
        {
            var viewConnection = view?.Connections.FirstOrDefault(connection =>
                string.Equals(connection.RelationshipId, relationship.Id, StringComparison.Ordinal));
            var documentEdge = _document.Edges.FirstOrDefault(edge =>
                string.Equals(edge.Id, relationship.Id, StringComparison.Ordinal));
            var routed = (viewConnection?.Waypoints.Count ?? 0) > 0 || (documentEdge?.Waypoints.Count ?? 0) > 0;
            if (routed)
            {
                connectionChanges.Add(new ModelingViewConnectionChange
                {
                    RelationshipId = relationship.Id,
                    Waypoints = []
                });
            }
        }

        if (changes.Count == 0 && connectionChanges.Count == 0)
        {
            Announce(Loc["TmModelingEditor_ArrangeUpToDate"]);
            StateHasChanged();
            return;
        }

        var command = new SetModelingViewLayoutCommand(
            ArrangeTransactionName,
            _model,
            CreateOperationContext(),
            changes,
            connectionChanges,
            [],
            OperationViewId,
            EffectiveProviderKey,
            intentViewId: TargetViewId);

        var directionLabel = direction == ModelingLayoutArranger.Direction.LeftToRight
            ? Loc["TmModelingEditor_ArrangeDirectionLr"]
            : Loc["TmModelingEditor_ArrangeDirectionTb"];

        await PushStructuralCommandsAsync(
            ArrangeTransactionName,
            [command],
            command.Batch,
            command.Intent,
            () =>
            {
                _layoutCommitCount++;
                _coalescedLayoutCommand = null;
                Announce(string.Format(
                    CultureInfo.CurrentUICulture,
                    Loc["TmModelingEditor_ArrangeAnnouncement"],
                    directionLabel.ToLower(CultureInfo.CurrentUICulture)));
            },
            seedView: true);

        // The scene moved: fit the arranged diagram into the preview (page mode), so the result is
        // visible without a manual zoom-out.
        if (_preview is not null)
        {
            await _preview.FitToViewAsync(Tempo.Blazor.Components.Diagram.DiagramFitMode.Page);
        }

        StateHasChanged();
    }

    // ── Move to container (non-drag alternative, U5) ─────────────────────────

    /// <summary>The container candidates of the move-to-container sheet: every rendered container
    /// except the element itself and its whole subtree (a container cannot contain itself), plus the
    /// root option the sheet renders.</summary>
    private IReadOnlyList<(string Value, string Label)> MoveToContainerChoices
    {
        get
        {
            if (_model is null)
            {
                return [];
            }

            return
            [
                .. ContainerElements
                    .Where(element => !_moveToContainerExcludedIds.Contains(element.Id))
                    .Select(element => (element.Id, DescribeElementOption(element)))
            ];
        }
    }

    /// <summary>Opens "Move to container" for the selected element (the keyboard/tap alternative to
    /// dragging a node into another lane, WCAG 2.5.7).</summary>
    internal Task OpenMoveToContainerDialogAsync()
    {
        if (!CanEditModelLayout || _model is null || _document is null || _selectedElement is null || IsMobileLayout)
        {
            return Task.CompletedTask;
        }

        _moveToContainerElementId = _selectedElement.Id;
        var context = LayoutSyncContext.Create(_model, _document, IsContainerSemanticType);
        _moveToContainerExcludedIds = context.DescendantElementIds(_selectedElement.Id);
        _moveToContainerId = _selectedElement.ParentId ?? MoveToRootValue;
        _moveToContainerDialogOpen = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>Closes the move-to-container sheet without moving anything.</summary>
    internal Task CloseMoveToContainerDialogAsync()
    {
        _moveToContainerDialogOpen = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    internal Task HandleMoveToContainerChangedAsync(string? containerId)
    {
        _moveToContainerId = containerId ?? MoveToRootValue;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Commits "Move to container": ONE layout transaction that places the element inside the chosen
    /// container's content area (the same slot math the generator's auto-placement uses), re-parents
    /// the view node and — only when the writer may edit STRUCTURE — the semantic element, and grows
    /// the container when needed. One undo reverts the move, the re-parent and the growth.
    /// </summary>
    internal async Task ConfirmMoveToContainerAsync()
    {
        _moveToContainerDialogOpen = false;
        _focusMoveToContainerAfterRender = true;
        if (_model is null || _document is null || !CanEditModelLayout || string.IsNullOrEmpty(_moveToContainerElementId))
        {
            StateHasChanged();
            return;
        }

        var element = _model.Elements.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, _moveToContainerElementId, StringComparison.Ordinal));
        var node = element is null
            ? null
            : _document.Nodes.FirstOrDefault(candidate =>
                string.Equals(ElementIdOfNode(candidate), element.Id, StringComparison.Ordinal));
        if (element is null || node is null)
        {
            StateHasChanged();
            return;
        }

        var targetContainerId = string.IsNullOrEmpty(_moveToContainerId) ? MoveToRootValue : _moveToContainerId.Trim();
        if (!string.IsNullOrEmpty(targetContainerId)
            && (string.Equals(targetContainerId, element.Id, StringComparison.Ordinal)
                || _moveToContainerExcludedIds.Contains(targetContainerId)))
        {
            // A stale selection (the tree changed under the sheet) must never create a parent cycle.
            StateHasChanged();
            return;
        }

        var (x, y) = ResolveMoveToContainerPosition(targetContainerId, element.Id, node);
        var view = ModelingStructureCommandSupport.FindView(_model, OperationViewId);
        var viewNode = view?.Nodes.FirstOrDefault(candidate =>
            string.Equals(candidate.ElementId, element.Id, StringComparison.Ordinal));
        var parentChanged = !string.Equals(viewNode?.ParentNodeId ?? string.Empty, targetContainerId, StringComparison.Ordinal);
        var parentMoves = CanEditModelStructure
            && !string.Equals(element.ParentId ?? string.Empty, targetContainerId, StringComparison.Ordinal)
                ? new List<(string ElementId, string ParentId)> { (element.Id, targetContainerId) }
                : [];

        var nodes = new List<ModelingViewNodeChange>
        {
            new()
            {
                ElementId = element.Id,
                X = x,
                Y = y,
                Width = node.W,
                Height = node.H,
                ParentNodeId = parentChanged ? targetContainerId : null
            }
        };

        var context = LayoutSyncContext.Create(_model, _document, IsContainerSemanticType);
        AddContainerGrowth(context, nodes, [element.Id], parentMoves);

        var command = new SetModelingViewLayoutCommand(
            MoveTransactionName,
            _model,
            CreateOperationContext(),
            nodes,
            [],
            parentMoves,
            OperationViewId,
            EffectiveProviderKey,
            intentViewId: TargetViewId);

        var elementName = GetElementName(element.Id);
        var containerName = string.IsNullOrEmpty(targetContainerId)
            ? Loc["TmModelingEditor_InsertRoot"]
            : GetElementName(targetContainerId);

        await PushStructuralCommandsAsync(
            MoveTransactionName,
            [command],
            command.Batch,
            command.Intent,
            () =>
            {
                _layoutCommitCount++;
                _coalescedLayoutCommand = null;
                Announce(string.IsNullOrEmpty(targetContainerId)
                    ? string.Format(
                        CultureInfo.CurrentUICulture,
                        Loc["TmModelingEditor_LayoutMovedToRoot"],
                        elementName)
                    : string.Format(
                        CultureInfo.CurrentUICulture,
                        Loc["TmModelingEditor_LayoutMovedInto"],
                        elementName,
                        containerName));
            },
            seedView: true);

        StateHasChanged();
    }

    /// <summary>The placement of a moved element: the container's content area (after its label band,
    /// in the first free slot) or free space below the scene for the root.</summary>
    private (double X, double Y) ResolveMoveToContainerPosition(string containerId, string elementId, DiagramNode node)
    {
        if (_document is null)
        {
            return (node.X, node.Y);
        }

        if (string.IsNullOrEmpty(containerId))
        {
            var bottom = _document.Nodes
                .Where(candidate => string.IsNullOrEmpty(candidate.ParentNodeId))
                .Select(candidate => candidate.Y + candidate.H)
                .DefaultIfEmpty(ModelingLayoutArranger.OriginY)
                .Max();
            return (ModelingLayoutArranger.OriginX, bottom + ContainerGrowPadding);
        }

        var containerNode = _document.Nodes.FirstOrDefault(candidate =>
            string.Equals(ElementIdOfNode(candidate), containerId, StringComparison.Ordinal));
        if (containerNode is null)
        {
            return (node.X, node.Y);
        }

        var view = ModelingStructureCommandSupport.FindView(_model, OperationViewId);
        var slot = view?.Nodes.Count(candidate =>
            string.Equals(candidate.ParentNodeId, containerId, StringComparison.Ordinal)
            && !string.Equals(candidate.ElementId, elementId, StringComparison.Ordinal)) ?? 0;
        return ModelingDiagramGenerator.GetContainerContentPosition(containerNode, node.W, node.H, slot);
    }

    // ── Announcements ────────────────────────────────────────────────────────

    /// <summary>
    /// Publishes the layout announcement: a re-parent (a hierarchy change the user must hear about)
    /// speaks IMMEDIATELY, a plain move waits for the nudge burst to end
    /// (<see cref="LayoutAnnouncementDebounceMilliseconds"/>) so ten arrow presses do not queue ten
    /// live-region messages (U6).
    /// </summary>
    private void ScheduleLayoutAnnouncement(string message, bool immediate)
    {
        _pendingLayoutAnnouncement = message;
        _layoutAnnouncementCancellation?.Cancel();
        _layoutAnnouncementCancellation?.Dispose();
        _layoutAnnouncementCancellation = null;
        if (immediate)
        {
            Announce(message);
            return;
        }

        var cancellation = new CancellationTokenSource();
        _layoutAnnouncementCancellation = cancellation;
        _layoutAnnouncementTask = AnnounceAfterDebounceAsync(cancellation.Token);
    }

    private async Task AnnounceAfterDebounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(LayoutAnnouncementDebounceMilliseconds),
                Clock ?? TimeProvider.System,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var message = _pendingLayoutAnnouncement;
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        try
        {
            await InvokeAsync(() =>
            {
                Announce(message!);
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // The editor was disposed while the debounce was pending.
        }
    }

    /// <summary>The polite announcement of one layout gesture: what moved, and into WHICH container
    /// when the move re-parented the element (the hierarchy change the user must hear about).</summary>
    private string DescribeLayoutCommit(LayoutCommit commit)
    {
        var moved = commit.MovedElementIds.FirstOrDefault();
        if (commit.ParentMoves.Count > 0 && commit.ParentMoves[0] is var move)
        {
            var containerName = string.IsNullOrEmpty(move.ParentId)
                ? Loc["TmModelingEditor_InsertRoot"]
                : GetElementName(move.ParentId);
            return string.Format(
                CultureInfo.CurrentUICulture,
                Loc["TmModelingEditor_LayoutMovedInto"],
                GetElementName(move.ElementId),
                containerName);
        }

        if (string.IsNullOrEmpty(moved))
        {
            return Loc["TmModelingEditor_LayoutUpdated"];
        }

        return string.Format(
            CultureInfo.CurrentUICulture,
            Loc["TmModelingEditor_LayoutMoved"],
            GetElementName(moved));
    }

    private string LayoutTransactionName => Loc["TmModelingEditor_LayoutTransaction"];

    private string MoveTransactionName => Loc["TmModelingEditor_MoveToContainer"];

    private string ArrangeTransactionName => Loc["TmModelingEditor_Arrange"];
}
