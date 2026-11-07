using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// The phase-4 LAYOUT + HIERARCHY half of <see cref="TmModelingEditor"/> (decisions
/// <c>MODELING-AUTHORING-CHANGESET</c> §9.3/§9.4 and <c>MODELING-WRITER-CAPABILITIES</c>): a node
/// move, a resize, a waypoint drag and a task dropped into another lane/pool all become ONE semantic
/// transaction that writes the view geometry (<see cref="ModelingViewNodeDto"/> X/Y/W/H and its
/// layout parent), the connection waypoints and — when the container changed — the element's
/// semantic <see cref="ModelingElementDto.ParentId"/>, so a reload renders the AUTHORED layout.
/// <para>
/// The embedded <see cref="TmDiagramEditor"/> raises <c>DocumentChanged</c> once per gesture (never
/// per pointer move), so the editor diffs the document geometry against the snapshot it captured
/// after the last render and commits exactly one <see cref="SetModelingViewLayoutCommand"/> per
/// gesture — one undo step, no debounce timer racing the pointer. A burst of keyboard nudges of the
/// SAME element set within <see cref="KeyboardNudgeCoalesceWindowMilliseconds"/> merges into the
/// command already on top of the stack (U6), so ten arrow presses are one undo step.
/// </para>
/// <para>
/// Layout authoring is gated on <see cref="ModelingModelWriterCapabilities.Layout"/>: without it the
/// moves stay session-only (2.10.0 behavior — no <c>ViewChanges</c> are ever produced, decision
/// §3.2 rule 7). A Layout-only writer (no Structure) can move geometry but never re-parents: the
/// semantic parent stays the source of truth, so a drag across a lane boundary persists X/Y only.
/// The "Arrange" action is the same gate and the same command.
/// </para>
/// </summary>
public partial class TmModelingEditor
{
    /// <summary>Tolerance of the geometry diff (the canvas rounds to two decimals).</summary>
    private const double LayoutGeometryTolerance = 0.01;

    /// <summary>Padding a grown container keeps around its furthest child.</summary>
    private const double ContainerGrowPadding = 24;

    /// <summary>Two layout steps over the same element set closer than this are ONE undo step
    /// (the keyboard-nudge burst window, phase 4 review U6).</summary>
    private const int KeyboardNudgeCoalesceWindowMilliseconds = 800;

    /// <summary>The polite layout announcement is debounced this long so a burst of arrow keys
    /// speaks once (phase 4 review U6).</summary>
    private const int LayoutAnnouncementDebounceMilliseconds = 500;

    /// <summary>Cycle guard of the ancestor walk (a malformed ParentId cycle).</summary>
    private const int MaxContainerDepth = 64;

    /// <summary>Direction values of the arrange sheet (the operation never sees them).</summary>
    internal const string ArrangeDirectionLeftToRight = "LR";

    /// <summary>Top-to-bottom arrangement value.</summary>
    internal const string ArrangeDirectionTopToBottom = "TB";

    /// <summary>The "no container" value of the move-to-container sheet.</summary>
    internal const string MoveToRootValue = "";

    /// <summary>Focus hook of the header "Move to container" action (stable, test-id
    /// independent).</summary>
    internal const string MoveToContainerActionSelector = "[data-modeling-focus='move-to-container']";

    /// <summary>Whether the editor may author LAYOUT: the host opted into editing, a write path
    /// exists AND the effective writer advertises
    /// <see cref="ModelingModelWriterCapabilities.Layout"/>.</summary>
    public bool CanEditModelLayout
        => IsEditingEnabled && (EffectiveWriterCapabilities & ModelingModelWriterCapabilities.Layout) != 0;

    /// <summary>The document geometry the next gesture is diffed against (node id → geometry).</summary>
    private Dictionary<string, LayoutNodeState>? _layoutBaseline;

    /// <summary>The connection waypoints the next gesture is diffed against (edge id → points).</summary>
    private Dictionary<string, List<DiagramPoint>>? _layoutBaselineConnections;

    /// <summary>The document the layout baseline was captured from.</summary>
    private DiagramDocument? _layoutBaselineDocument;

    /// <summary>How many layout transactions were committed (diagnostics + tests).</summary>
    private int _layoutCommitCount;

    /// <summary>Duration of the last layout DIFF + COMMIT-BUILD (the algorithmic half of a gesture —
    /// the render that follows is the renderer's, not the sync's). Guarded by the review A8 budget
    /// test.</summary>
    private TimeSpan _lastLayoutSyncDuration;

    /// <summary>The command a burst of nudges merges into (null once the burst is broken).</summary>
    private SetModelingViewLayoutCommand? _coalescedLayoutCommand;

    /// <summary>When the coalescable command was last extended (the burst window).</summary>
    private DateTimeOffset _coalescedLayoutAt;

    /// <summary>The element set the coalescable command covers (sorted, "|"-joined).</summary>
    private string _coalescedLayoutKey = string.Empty;

    /// <summary>The debounced layout announcement and its cancellation.</summary>
    private string? _pendingLayoutAnnouncement;
    private Task _layoutAnnouncementTask = Task.CompletedTask;
    private CancellationTokenSource? _layoutAnnouncementCancellation;

    /// <summary>The arrange sheet state (direction value; <c>LR</c> by default).</summary>
    private string _arrangeDirection = ArrangeDirectionLeftToRight;

    private bool _arrangeDialogOpen;

    /// <summary>The move-to-container sheet state (the element and the chosen container).</summary>
    private bool _moveToContainerDialogOpen;
    private string _moveToContainerId = MoveToRootValue;
    private string? _moveToContainerElementId;
    private HashSet<string> _moveToContainerExcludedIds = new(StringComparer.Ordinal);
    private bool _focusMoveToContainerAfterRender;

    // ── Test seams (InternalsVisibleTo) ──────────────────────────────────────

    /// <summary>Test seam: the layout capability gate.</summary>
    internal bool TestCanEditModelLayout => CanEditModelLayout;

    /// <summary>Test seam: the committed layout transaction count.</summary>
    internal int TestLayoutCommitCount => _layoutCommitCount;

    /// <summary>Test seam: the duration of the last layout diff + commit build (review A8).</summary>
    internal TimeSpan TestLastLayoutSyncDuration => _lastLayoutSyncDuration;

    /// <summary>Test seam: whether the arrange sheet is open.</summary>
    internal bool ArrangeDialogOpen => _arrangeDialogOpen;

    /// <summary>Test seam: whether the move-to-container sheet is open.</summary>
    internal bool MoveToContainerDialogOpen => _moveToContainerDialogOpen;

    /// <summary>Test seam: the selected move target of the move-to-container sheet.</summary>
    internal string TestMoveToContainerValue => _moveToContainerId;

    /// <summary>Test seam: the selectable move-to-container candidates.</summary>
    internal IReadOnlyList<(string Value, string Label)> TestMoveToContainerChoices => MoveToContainerChoices;

    /// <summary>Test seam: selects the arrange direction as the sheet would.</summary>
    internal Task TestSetArrangeDirectionAsync(string direction) => HandleArrangeDirectionChangedAsync(direction);

    /// <summary>Test seam: commits the arrange sheet as its confirm does.</summary>
    internal Task TestConfirmArrangeAsync() => ConfirmArrangeAsync();

    /// <summary>Test seam: the direction options of the arrange sheet.</summary>
    internal IReadOnlyList<(string Value, string Label)> TestArrangeDirections => ArrangeDirectionChoices;

    /// <summary>Test seam: opens the move-to-container sheet.</summary>
    internal Task TestOpenMoveToContainerAsync() => OpenMoveToContainerDialogAsync();

    /// <summary>Test seam: selects the move target as the sheet would.</summary>
    internal Task TestSetMoveToContainerAsync(string? value) => HandleMoveToContainerChangedAsync(value);

    /// <summary>Test seam: commits the move-to-container sheet as its confirm does.</summary>
    internal Task TestConfirmMoveToContainerAsync() => ConfirmMoveToContainerAsync();

    /// <summary>Test seam: awaits the pending debounced layout announcement.</summary>
    internal Task TestFlushLayoutAnnouncementAsync() => _layoutAnnouncementTask;

    /// <summary>Test seam: the layout parent of one element as the next diff would resolve it
    /// (empty when the node is parked outside every container).</summary>
    internal string TestResolveLayoutParentElementId(string elementId)
    {
        if (_model is null || _document is null)
        {
            return string.Empty;
        }

        var node = _document.Nodes.FirstOrDefault(candidate =>
            string.Equals(ElementIdOfNode(candidate), elementId, StringComparison.Ordinal));
        if (node is null)
        {
            return string.Empty;
        }

        var context = LayoutSyncContext.Create(_model, _document, IsContainerSemanticType);
        return ResolveLayoutParentElementId(context, node, context.DescendantElementIds(elementId));
    }

    /// <summary>The geometry of one rendered node as the diff sees it.</summary>
    internal sealed record LayoutNodeState(double X, double Y, double Width, double Height, string ParentElementId);

    /// <summary>One layout gesture: the view upserts plus the semantic re-parents they imply.</summary>
    internal sealed record LayoutCommit(
        List<ModelingViewNodeChange> Nodes,
        List<ModelingViewConnectionChange> Connections,
        List<(string ElementId, string ParentId)> ParentMoves,
        IReadOnlyList<string> MovedElementIds);

    // ── Per-sync lookup context (review A8) ──────────────────────────────────

    /// <summary>
    /// The immutable lookups ONE layout sync needs (review A8): the element map, the children index,
    /// the rendered CONTAINERS sorted by area (smallest first, the "smallest container wins" rule)
    /// and the node map. Built once per sync instead of re-scanning <c>Elements</c> / <c>Nodes</c>
    /// with <c>FirstOrDefault</c> inside the per-node and per-container loops, which made a 300-node
    /// model's single move quadratic.
    /// </summary>
    private sealed class LayoutSyncContext
    {
        private readonly Dictionary<string, int> _depth = new(StringComparer.Ordinal);

        private LayoutSyncContext(
            Dictionary<string, ModelingElementDto> elementsById,
            Dictionary<string, List<ModelingElementDto>> childrenByParent,
            Dictionary<string, DiagramNode> nodesById,
            Dictionary<string, DiagramNode> nodesByElementId,
            List<(ModelingElementDto Element, DiagramNode Node)> containersByArea)
        {
            ElementsById = elementsById;
            ChildrenByParent = childrenByParent;
            NodesById = nodesById;
            NodesByElementId = nodesByElementId;
            ContainersByArea = containersByArea;
        }

        /// <summary>Element id → element.</summary>
        public Dictionary<string, ModelingElementDto> ElementsById { get; }

        /// <summary>Parent element id → its declared children (declaration order).</summary>
        public Dictionary<string, List<ModelingElementDto>> ChildrenByParent { get; }

        /// <summary>Node id → its rendered node (the layout-parent fallback resolves a node id).</summary>
        public Dictionary<string, DiagramNode> NodesById { get; }

        /// <summary>Element id → its FIRST rendered node.</summary>
        public Dictionary<string, DiagramNode> NodesByElementId { get; }

        /// <summary>The rendered CONTAINER elements, smallest area first.</summary>
        public List<(ModelingElementDto Element, DiagramNode Node)> ContainersByArea { get; }

        /// <summary>Builds the context for one sync.</summary>
        /// <param name="model">The live model (null → empty context).</param>
        /// <param name="document">The document being synced.</param>
        /// <param name="isContainer">The notation profile's container predicate.</param>
        public static LayoutSyncContext Create(
            ModelingModelDto? model,
            DiagramDocument document,
            Func<string, bool> isContainer)
        {
            var elementsById = new Dictionary<string, ModelingElementDto>(StringComparer.Ordinal);
            var childrenByParent = new Dictionary<string, List<ModelingElementDto>>(StringComparer.Ordinal);
            if (model is not null)
            {
                foreach (var element in model.Elements)
                {
                    elementsById.TryAdd(element.Id, element);
                }

                foreach (var element in model.Elements)
                {
                    var parentId = element.ParentId ?? string.Empty;
                    if (string.IsNullOrEmpty(parentId) || string.Equals(parentId, element.Id, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!childrenByParent.TryGetValue(parentId, out var children))
                    {
                        children = [];
                        childrenByParent[parentId] = children;
                    }

                    children.Add(element);
                }
            }

            var nodesById = new Dictionary<string, DiagramNode>(StringComparer.Ordinal);
            var nodesByElementId = new Dictionary<string, DiagramNode>(StringComparer.Ordinal);
            foreach (var node in document.Nodes)
            {
                nodesById.TryAdd(node.Id, node);
                var elementId = ElementIdOfNode(node);
                if (!string.IsNullOrEmpty(elementId))
                {
                    nodesByElementId.TryAdd(elementId!, node);
                }
            }

            var containers = new List<(ModelingElementDto Element, DiagramNode Node)>();
            if (model is not null)
            {
                foreach (var element in model.Elements)
                {
                    if (isContainer(element.SemanticType)
                        && nodesByElementId.TryGetValue(element.Id, out var containerNode))
                    {
                        containers.Add((element, containerNode));
                    }
                }
            }

            containers.Sort(static (left, right) =>
                (left.Node.W * left.Node.H).CompareTo(right.Node.W * right.Node.H));
            return new LayoutSyncContext(elementsById, childrenByParent, nodesById, nodesByElementId, containers);
        }

        /// <summary>The declaration depth of an element (root = 0; cycles stop at the guard).</summary>
        public int DepthOf(string elementId)
        {
            if (_depth.TryGetValue(elementId, out var cached))
            {
                return cached;
            }

            var depth = 0;
            var current = elementId;
            var guard = 0;
            while (ElementsById.TryGetValue(current, out var element)
                && !string.IsNullOrEmpty(element.ParentId)
                && guard++ < MaxContainerDepth)
            {
                depth++;
                current = element.ParentId;
            }

            _depth[elementId] = depth;
            return depth;
        }

        /// <summary>The element and its whole semantic SUBTREE (a container inside it would be a
        /// parent cycle), walked through the children index — never a full model scan.</summary>
        public HashSet<string> DescendantElementIds(string? elementId)
        {
            var excluded = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(elementId))
            {
                return excluded;
            }

            excluded.Add(elementId!);
            var pending = new Queue<string>();
            pending.Enqueue(elementId!);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                if (!ChildrenByParent.TryGetValue(current, out var children))
                {
                    continue;
                }

                foreach (var child in children)
                {
                    if (excluded.Add(child.Id))
                    {
                        pending.Enqueue(child.Id);
                    }
                }
            }

            return excluded;
        }
    }

    // ── Baseline ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Records the document geometry the next gesture is diffed against. Called from
    /// <see cref="CaptureStructureSnapshot"/> (one hook for every generation, adoption and commit),
    /// so a regenerated document never reports its own projection as a user gesture.
    /// </summary>
    private void CaptureLayoutBaseline(DiagramDocument? document)
    {
        _layoutBaselineDocument = document;
        if (document is null)
        {
            _layoutBaseline = null;
            _layoutBaselineConnections = null;
            return;
        }

        var context = LayoutSyncContext.Create(_model, document, IsContainerSemanticType);
        var nodes = new Dictionary<string, LayoutNodeState>(StringComparer.Ordinal);
        foreach (var node in document.Nodes)
        {
            nodes[node.Id] = new LayoutNodeState(
                node.X,
                node.Y,
                node.W,
                node.H,
                ResolveLayoutParentElementId(context, node, null));
        }

        var connections = new Dictionary<string, List<DiagramPoint>>(StringComparer.Ordinal);
        foreach (var edge in document.Edges)
        {
            connections[edge.Id] = WaypointsOf(edge);
        }

        _layoutBaseline = nodes;
        _layoutBaselineConnections = connections;
    }

    /// <summary>The model element id a rendered node carries, resolved by node id.</summary>
    private static string? ElementIdOfNode(DiagramDocument document, string? nodeId)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            return null;
        }

        var node = document.Nodes.FirstOrDefault(candidate => string.Equals(candidate.Id, nodeId, StringComparison.Ordinal));
        return node is null ? null : ElementIdOfNode(node);
    }

    /// <summary>
    /// The container ELEMENT an element belongs to after a gesture (phase 4 §9.3): the SMALLEST
    /// rendered container whose bounds contain the node's centre — the same rule the palette insert
    /// uses (<see cref="ResolveContainerElementId"/>), so a task dropped into another lane changes
    /// its semantic <c>ParentId</c> even when the embedded editor reported no explicit parent. The
    /// document's own layout parent is the fallback (a node parked outside every container keeps the
    /// parent it had). The candidate set is the per-sync <see cref="LayoutSyncContext.ContainersByArea"/>
    /// list, so the rule is a single pass (review A8).
    /// </summary>
    private string ResolveLayoutParentElementId(
        LayoutSyncContext context,
        DiagramNode node,
        IReadOnlySet<string>? excludedElementIds)
    {
        var elementId = ElementIdOfNode(node);
        var centerX = node.X + (node.W / 2);
        var centerY = node.Y + (node.H / 2);

        foreach (var (element, candidate) in context.ContainersByArea)
        {
            // A container the node itself CONTAINS can never become its parent (a pool's centre lies
            // inside its own lane, so geometry alone would report a parent cycle). The node and its
            // whole semantic subtree are therefore excluded from the candidate set.
            if (string.Equals(element.Id, elementId, StringComparison.Ordinal)
                || (excludedElementIds is not null && excludedElementIds.Contains(element.Id)))
            {
                continue;
            }

            if (centerX >= candidate.X && centerX <= candidate.X + candidate.W
                && centerY >= candidate.Y && centerY <= candidate.Y + candidate.H)
            {
                return element.Id;
            }
        }

        // No rendered container holds the node's centre: the document's own layout parent (the
        // embedded editor's swimlane drop detection) is the fallback. Its value is a NODE id, so it
        // is mapped back to the element.
        if (string.IsNullOrEmpty(node.ParentNodeId)
            || !context.NodesById.TryGetValue(node.ParentNodeId, out var parentNode))
        {
            return string.Empty;
        }

        return ElementIdOfNode(parentNode) ?? string.Empty;
    }

    /// <summary>The effective waypoints of an edge (a manual routing or an explicit bend list).</summary>
    private static List<DiagramPoint> WaypointsOf(DiagramEdge edge)
        => edge.Waypoints.Count == 0
            ? []
            : [.. edge.Waypoints.Select(point => new DiagramPoint(point.X, point.Y))];

    // ── Document ⇄ model layout synchronization ──────────────────────────────

    /// <summary>
    /// Translates ONE document mutation into the LAYOUT transaction it implies (§9.4), or returns
    /// false when the gesture changed no geometry (a selection, a label, a z-order change). A
    /// structural mutation never reaches here — the caller handles those first and regenerates.
    /// </summary>
    internal async Task<bool> SyncLayoutFromDocumentAsync(DiagramDocument document)
    {
        if (_model is null || !CanEditModelLayout)
        {
            // 2.10.0 behavior: geometry stays session-only, no ViewChanges are produced (§3.2 rule 7).
            CaptureLayoutBaseline(document);
            return false;
        }

        if (_layoutBaseline is null
            || _layoutBaselineConnections is null
            || !ReferenceEquals(_layoutBaselineDocument, document))
        {
            // A regenerated / newly adopted document is a projection of the model, not a gesture.
            CaptureLayoutBaseline(document);
            return false;
        }

        var syncStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var context = LayoutSyncContext.Create(_model, document, IsContainerSemanticType);
        var commit = BuildLayoutCommit(context, document);
        syncStopwatch.Stop();
        _lastLayoutSyncDuration = syncStopwatch.Elapsed;
        if (commit is null)
        {
            return false;
        }

        var now = (Clock ?? TimeProvider.System).GetUtcNow();
        var elementKey = LayoutElementKey(commit.Nodes);
        if (TryCoalesceLayoutCommit(commit, elementKey, now))
        {
            unchecked { _commitVersion++; }
            ScheduleLayoutAnnouncement(DescribeLayoutCommit(commit), immediate: false);
            await AfterCommitAsync(_coalescedLayoutCommand!.Intent);
            if (ReferenceEquals(_document, document))
            {
                CaptureLayoutBaseline(document);
            }

            return true;
        }

        var transactionName = commit.ParentMoves.Count > 0 ? MoveTransactionName : LayoutTransactionName;
        var command = new SetModelingViewLayoutCommand(
            transactionName,
            _model,
            CreateOperationContext(),
            commit.Nodes,
            commit.Connections,
            commit.ParentMoves,
            OperationViewId,
            EffectiveProviderKey,
            intentViewId: TargetViewId);

        await PushStructuralCommandsAsync(
            transactionName,
            [command],
            command.Batch,
            command.Intent,
            () =>
            {
                _layoutCommitCount++;
                ScheduleLayoutAnnouncement(
                    DescribeLayoutCommit(commit),
                    immediate: commit.ParentMoves.Count > 0);
            },
            seedView: true,
            regenerate: false);

        // A REJECTED batch regenerated the documents (the node snaps back) and captured its own
        // baseline; an accepted one leaves the document — and therefore the user's gesture — as is.
        if (ReferenceEquals(_document, document))
        {
            CaptureLayoutBaseline(document);
            // Arm the nudge burst only for a plain move (no re-parent, no waypoints).
            _coalescedLayoutCommand = commit.ParentMoves.Count == 0 && commit.Connections.Count == 0
                ? command
                : null;
            _coalescedLayoutAt = now;
            _coalescedLayoutKey = elementKey;
        }

        return true;
    }

    /// <summary>The identity of one layout gesture's element set (sorted, "|"-joined).</summary>
    private static string LayoutElementKey(IEnumerable<ModelingViewNodeChange> nodes)
        => string.Join(
            "|",
            nodes.Select(node => node.ElementId).OrderBy(id => id, StringComparer.Ordinal));

    /// <summary>
    /// Merges a plain geometry step into the layout command already on top of the stack when it
    /// touches the SAME element set inside the nudge window (U6). The BEFORE snapshot of that
    /// command is untouched, so one undo still restores the geometry from before the burst.
    /// </summary>
    private bool TryCoalesceLayoutCommit(LayoutCommit commit, string elementKey, DateTimeOffset now)
    {
        if (_coalescedLayoutCommand is null
            || commit.ParentMoves.Count > 0
            || commit.Connections.Count > 0
            || !string.Equals(_coalescedLayoutKey, elementKey, StringComparison.Ordinal)
            || (now - _coalescedLayoutAt).TotalMilliseconds >= KeyboardNudgeCoalesceWindowMilliseconds)
        {
            return false;
        }

        if (!_coalescedLayoutCommand.TryMerge(commit.Nodes, commit.Connections))
        {
            return false;
        }

        _coalescedLayoutAt = now;
        return true;
    }

    /// <summary>
    /// Builds the layout transaction of one gesture: the moved/resized nodes, their LAYOUT parent,
    /// the container GROWTH a re-parented child forces (a lane stretches around its content) and the
    /// changed connection waypoints. Null when the documented geometry equals the baseline.
    /// </summary>
    private LayoutCommit? BuildLayoutCommit(LayoutSyncContext context, DiagramDocument document)
    {
        var baseline = _layoutBaseline;
        var baselineConnections = _layoutBaselineConnections;
        if (_model is null || baseline is null || baselineConnections is null)
        {
            return null;
        }

        var structuralEditing = CanEditModelStructure;

        // A duplicate placement (the same element rendered twice) is a session-only scene: the view
        // keeps ONE node per element, so only the FIRST node of an element may write layout.
        var duplicatedElementIds = new HashSet<string>(
            document.Nodes
                .Select(ElementIdOfNode)
                .Where(elementId => !string.IsNullOrEmpty(elementId))
                .Select(elementId => elementId!)
                .GroupBy(elementId => elementId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key),
            StringComparer.Ordinal);

        var nodes = new List<ModelingViewNodeChange>();
        var parentMoves = new List<(string ElementId, string ParentId)>();
        var movedElementIds = new List<string>();
        var writtenElementIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in document.Nodes)
        {
            var elementId = ElementIdOfNode(node);
            if (string.IsNullOrEmpty(elementId)
                || duplicatedElementIds.Contains(elementId!)
                || !writtenElementIds.Add(elementId!)
                || !baseline.TryGetValue(node.Id, out var original)
                || !context.ElementsById.TryGetValue(elementId!, out var element))
            {
                continue;
            }

            var positionChanged = !NearlyEqual(original.X, node.X) || !NearlyEqual(original.Y, node.Y);
            var sizeChanged = !NearlyEqual(original.Width, node.W) || !NearlyEqual(original.Height, node.H);

            // Review A4: only a node whose OWN position changed re-resolves its container. A node the
            // gesture did not move keeps the parent the baseline resolved — resizing a lane must not
            // silently re-parent the tasks inside it.
            var parentElementId = original.ParentElementId;
            if (!structuralEditing)
            {
                // Review A3: a Layout-only writer cannot change the hierarchy — the semantic parent
                // is the source of truth, so a drag into another lane persists X/Y only.
                parentElementId = element.ParentId ?? string.Empty;
            }
            else if (positionChanged)
            {
                parentElementId = ResolveLayoutParentElementId(
                    context,
                    node,
                    context.DescendantElementIds(elementId));
            }

            if (string.Equals(parentElementId, elementId, StringComparison.Ordinal))
            {
                parentElementId = string.Empty;
            }

            var geometryChanged = positionChanged || sizeChanged;
            var parentChanged = !string.Equals(original.ParentElementId, parentElementId, StringComparison.Ordinal);
            if (!geometryChanged && !parentChanged)
            {
                continue;
            }

            nodes.Add(new ModelingViewNodeChange
            {
                ElementId = elementId!,
                X = node.X,
                Y = node.Y,
                Width = node.W,
                Height = node.H,
                ParentNodeId = parentChanged ? parentElementId : null
            });
            movedElementIds.Add(elementId!);

            // §9.3: a move between two lanes/pools changes the SEMANTIC parent too — in the SAME
            // transaction, so one undo reverts both the layout and the hierarchy.
            if (structuralEditing
                && !string.Equals(element.ParentId ?? string.Empty, parentElementId, StringComparison.Ordinal))
            {
                parentMoves.Add((elementId!, parentElementId));
            }
        }

        var connections = new List<ModelingViewConnectionChange>();
        foreach (var edge in document.Edges)
        {
            if (!baselineConnections.TryGetValue(edge.Id, out var originalWaypoints)
                || !_model.Relationships.Any(relationship => string.Equals(relationship.Id, edge.Id, StringComparison.Ordinal)))
            {
                continue;
            }

            var current = WaypointsOf(edge);
            if (WaypointsEqual(originalWaypoints, current))
            {
                continue;
            }

            connections.Add(new ModelingViewConnectionChange
            {
                RelationshipId = edge.Id,
                Waypoints = [.. current.Select(point => new ModelingViewWaypointDto { X = point.X, Y = point.Y })]
            });
        }

        AddContainerGrowth(context, nodes, movedElementIds, parentMoves);

        if (nodes.Count == 0 && connections.Count == 0)
        {
            return null;
        }

        return new LayoutCommit(nodes, connections, parentMoves, movedElementIds);
    }

    /// <summary>
    /// §9.3 "a lane stretches around its content": after a child moved (or was re-parented) into a
    /// container, a container that no longer encloses it GROWS so the child is visibly inside its
    /// band, and every ANCESTOR grows too (a lane grown inside a pool pushes the pool's bottom).
    /// The growth rides in the same transaction (one undo). A container the user resized in the
    /// same gesture keeps the explicit geometry — an auto-growth never overrides a manual size.
    /// <para>
    /// Review U4: a grown BAND shifts the bands that follow it inside the same parent down by the
    /// delta, equalizes the sibling band widths and grows the ancestor pool — the pool's lanes stay
    /// flush instead of overlapping the lane that just grew.
    /// </para>
    /// </summary>
    private void AddContainerGrowth(
        LayoutSyncContext context,
        List<ModelingViewNodeChange> nodes,
        IReadOnlyList<string> movedElementIds,
        IReadOnlyList<(string ElementId, string ParentId)> parentMoves)
    {
        if (_model is null || movedElementIds.Count == 0)
        {
            return;
        }

        // The container a child lands in is the PENDING parent — the semantic move has not been
        // applied to the model yet, so a re-parented child would otherwise grow its OLD container.
        var pendingParents = parentMoves.ToDictionary(move => move.ElementId, move => move.ParentId, StringComparer.Ordinal);
        string EffectiveParent(string elementId)
            => pendingParents.TryGetValue(elementId, out var parent)
                ? parent
                : context.ElementsById.TryGetValue(elementId, out var element) ? element.ParentId ?? string.Empty : string.Empty;

        var candidates = new List<(ModelingElementDto Element, int Depth)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var movedId in movedElementIds)
        {
            var parentId = EffectiveParent(movedId);
            while (!string.IsNullOrEmpty(parentId) && seen.Add(parentId))
            {
                if (!context.ElementsById.TryGetValue(parentId, out var container)
                    || !IsContainerSemanticType(container.SemanticType)
                    || !context.NodesByElementId.ContainsKey(parentId))
                {
                    break;
                }

                candidates.Add((container, context.DepthOf(parentId)));
                parentId = EffectiveParent(parentId);
            }
        }

        // Deepest first: a lane grows before its pool is measured around the grown lane.
        foreach (var (container, _) in candidates.OrderByDescending(candidate => candidate.Depth))
        {
            var containerNode = context.NodesByElementId[container.Id];
            var explicitChange = nodes.FirstOrDefault(change => string.Equals(change.ElementId, container.Id, StringComparison.Ordinal));
            var (currentX, currentY, currentWidth, currentHeight) = EffectiveGeometry(container.Id, containerNode, nodes);

            var requiredRight = currentX + currentWidth;
            var requiredBottom = currentY + currentHeight;
            foreach (var child in context.ChildrenByParent.GetValueOrDefault(container.Id) ?? [])
            {
                if (!context.NodesByElementId.TryGetValue(child.Id, out var childNode)
                    || !string.Equals(EffectiveParent(child.Id), container.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                var (childX, childY, childWidth, childHeight) = EffectiveGeometry(child.Id, childNode, nodes);
                requiredRight = Math.Max(requiredRight, childX + childWidth + ContainerGrowPadding);
                requiredBottom = Math.Max(requiredBottom, childY + childHeight + ContainerGrowPadding);
            }

            var grownWidth = Math.Max(currentWidth, requiredRight - currentX);
            var grownHeight = Math.Max(currentHeight, requiredBottom - currentY);
            if (explicitChange is not null)
            {
                // An explicit resize of the container in the SAME gesture wins over the auto-growth.
                continue;
            }

            var grewWidth = !NearlyEqual(grownWidth, currentWidth);
            var grewHeight = !NearlyEqual(grownHeight, currentHeight);
            if (!grewWidth && !grewHeight)
            {
                continue;
            }

            UpsertGeometry(nodes, container.Id, currentX, currentY, grownWidth, grownHeight);

            // A BAND that grew inside another band (a lane inside a pool) keeps the band stack flush:
            // the following lanes move down by the delta, every sibling lane takes the grown width and
            // the POOL grows in the same transaction (it is one of the ancestors above).
            var parentId = EffectiveParent(container.Id);
            if (containerNode.SwimlaneData is null
                || string.IsNullOrEmpty(parentId)
                || !context.ElementsById.TryGetValue(parentId, out var parentElement)
                || !IsContainerSemanticType(parentElement.SemanticType))
            {
                continue;
            }

            var siblings = context.ChildrenByParent.GetValueOrDefault(parentId) ?? [];
            var containerIndex = siblings.FindIndex(sibling => string.Equals(sibling.Id, container.Id, StringComparison.Ordinal));
            var deltaHeight = grownHeight - currentHeight;
            for (var index = 0; index < siblings.Count; index++)
            {
                var sibling = siblings[index];
                if (string.Equals(sibling.Id, container.Id, StringComparison.Ordinal)
                    || index <= containerIndex
                    || !context.NodesByElementId.TryGetValue(sibling.Id, out var siblingNode)
                    || !string.Equals(EffectiveParent(sibling.Id), parentId, StringComparison.Ordinal))
                {
                    continue;
                }

                var (siblingX, siblingY, siblingWidth, siblingHeight) = EffectiveGeometry(sibling.Id, siblingNode, nodes);
                var targetY = grewHeight ? siblingY + deltaHeight : siblingY;
                var targetWidth = grewWidth ? grownWidth : siblingWidth;
                if (NearlyEqual(targetWidth, siblingWidth) && NearlyEqual(targetY, siblingY))
                {
                    continue;
                }

                UpsertGeometry(nodes, sibling.Id, siblingX, targetY, targetWidth, siblingHeight);
                if (grewHeight)
                {
                    ShiftDescendants(context, sibling.Id, deltaHeight, nodes, EffectiveParent);
                }
            }
        }
    }

    /// <summary>Shifts one element's whole rendered subtree by <paramref name="deltaY"/> (a lane and
    /// its tasks move together when the lane above it grows).</summary>
    private static void ShiftDescendants(
        LayoutSyncContext context,
        string elementId,
        double deltaY,
        List<ModelingViewNodeChange> nodes,
        Func<string, string> effectiveParent)
    {
        if (!context.ChildrenByParent.TryGetValue(elementId, out var children))
        {
            return;
        }

        foreach (var child in children)
        {
            if (!context.NodesByElementId.TryGetValue(child.Id, out var childNode)
                || !string.Equals(effectiveParent(child.Id), elementId, StringComparison.Ordinal))
            {
                continue;
            }

            var (childX, childY, childWidth, childHeight) = EffectiveGeometry(child.Id, childNode, nodes);
            UpsertGeometry(nodes, child.Id, childX, childY + deltaY, childWidth, childHeight);
            ShiftDescendants(context, child.Id, deltaY, nodes, effectiveParent);
        }
    }

    /// <summary>The geometry a container currently has in this gesture (the pending change wins).</summary>
    private static (double X, double Y, double Width, double Height) EffectiveGeometry(
        string elementId,
        DiagramNode node,
        List<ModelingViewNodeChange> nodes)
    {
        var change = nodes.FirstOrDefault(candidate => string.Equals(candidate.ElementId, elementId, StringComparison.Ordinal));
        return change is null
            ? (node.X, node.Y, node.W, node.H)
            : (change.X ?? node.X, change.Y ?? node.Y, change.Width ?? node.W, change.Height ?? node.H);
    }

    /// <summary>Writes one geometry into the pending change list (updating the existing entry).</summary>
    private static void UpsertGeometry(
        List<ModelingViewNodeChange> nodes,
        string elementId,
        double x,
        double y,
        double width,
        double height)
    {
        var existing = nodes.FirstOrDefault(candidate => string.Equals(candidate.ElementId, elementId, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.X = x;
            existing.Y = y;
            existing.Width = width;
            existing.Height = height;
            return;
        }

        nodes.Add(new ModelingViewNodeChange
        {
            ElementId = elementId,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            ParentNodeId = null
        });
    }

    private static bool NearlyEqual(double left, double right)
        => Math.Abs(left - right) <= LayoutGeometryTolerance;

    private static bool WaypointsEqual(IReadOnlyList<DiagramPoint> left, IReadOnlyList<DiagramPoint> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!NearlyEqual(left[index].X, right[index].X) || !NearlyEqual(left[index].Y, right[index].Y))
            {
                return false;
            }
        }

        return true;
    }
}
