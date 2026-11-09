using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>Options controlling the headless <see cref="BpmnAutoLayout"/>.</summary>
public sealed class BpmnAutoLayoutOptions
{
    /// <summary>The id of the generated view. Defaults to <c>"auto"</c>.</summary>
    public string ViewId { get; set; } = "auto";

    /// <summary>
    /// The display name of the generated view. Empty (the default) falls back to the model title
    /// and then to <c>"Auto layout"</c>.
    /// </summary>
    public string ViewName { get; set; } = string.Empty;
}

/// <summary>
/// Deterministic, headless layered left-to-right layout for BPMN models that carry no (or
/// incomplete) BPMNDI diagram data. Pools render as stacked horizontal bands with lanes stacked
/// inside them; the token flow runs from left to right in longest-path layers; containers are
/// sized to fit their contents. Pure functions over <see cref="ModelingModelDto"/> — no JS interop,
/// no UI dependency, no randomness and no clock: the same model always produces the same view, so
/// the output is safe for golden tests and server-side use.
/// The layout runs in <c>O(V + E)</c> time and space (V = elements, E = relationships): the
/// relationship indices are built once, flow nodes are bucketed by layer in a single pass, and the
/// longest-path layering is an ITERATIVE Kahn topological sort over the non-back edges — no
/// recursion, so a deeply chained model cannot overflow the stack. Layer members stack in
/// declaration order from <c>y = 0</c> and every layer block is centred vertically on the tallest
/// layer, so a plain chain renders as ONE horizontal line. A boundary event joins the layer of the
/// activity it is attached to (a weight-0 edge) and stacks on a separate row inside the host
/// activity's column. Generated connections carry NO waypoints — the diagram generator routes
/// them (see docs/modeling-bpmn-xml.md §5.4).
/// </summary>
public static class BpmnAutoLayout
{
    // Geometry constants — the conventional BPMN tool sizes the DI corpus uses (a 100x80 task,
    // a 36x36 event, a 50x50 gateway), so an auto-laid-out diagram reads like tool output.
    private const double OriginX = 40;
    private const double OriginY = 40;
    private const double TaskWidth = 100;
    private const double TaskHeight = 80;
    private const double EventSize = 36;
    private const double GatewaySize = 50;
    private const double DataObjectWidth = 36;
    private const double DataObjectHeight = 50;
    private const double DataStoreWidth = 50;
    private const double DataStoreHeight = 60;
    private const double AnnotationWidth = 140;
    private const double AnnotationHeight = 60;
    private const double GroupWidth = 220;
    private const double GroupHeight = 120;
    private const double SubProcessMinWidth = 100;
    private const double SubProcessMinHeight = 80;
    private const double PoolLabelBand = 30;
    private const double PoolMinHeight = 120;
    private const double LaneMinHeight = 100;
    private const double ContainerPadding = 20;
    private const double SubProcessPadding = 20;
    private const double GapX = 40;
    private const double RowGapY = 20;
    private const double PoolGapY = 40;
    private const double BlockGapY = 40;
    private const double FreeGapX = 40;
    private const double FreeRowGapY = 40;

    private static readonly string[] FlowSemanticTypes =
    [
        BpmnSemanticTypes.Task, BpmnSemanticTypes.UserTask, BpmnSemanticTypes.ServiceTask,
        BpmnSemanticTypes.SendTask, BpmnSemanticTypes.ReceiveTask, BpmnSemanticTypes.ScriptTask,
        BpmnSemanticTypes.BusinessRuleTask, BpmnSemanticTypes.ManualTask,
        BpmnSemanticTypes.SubProcess, BpmnSemanticTypes.EventSubProcess, BpmnSemanticTypes.CallActivity,
        BpmnSemanticTypes.StartEvent, BpmnSemanticTypes.EndEvent,
        BpmnSemanticTypes.IntermediateCatchEvent, BpmnSemanticTypes.IntermediateThrowEvent,
        BpmnSemanticTypes.BoundaryEvent, BpmnSemanticTypes.ExclusiveGateway,
        BpmnSemanticTypes.ParallelGateway, BpmnSemanticTypes.InclusiveGateway,
        BpmnSemanticTypes.ComplexGateway, BpmnSemanticTypes.EventBasedGateway,
    ];

    /// <summary>
    /// Lays out <paramref name="model"/> into a new <see cref="ModelingViewDto"/> (notation
    /// <c>bpmn2</c>) covering every element. Element order in the view follows the model
    /// declaration order; connections follow the relationship declaration order.
    /// </summary>
    /// <param name="model">The imported model to lay out.</param>
    /// <param name="options">Layout options; <c>null</c> uses the defaults.</param>
    /// <returns>The generated view; never <c>null</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <c>null</c>.</exception>
    public static ModelingViewDto Layout(ModelingModelDto model, BpmnAutoLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new BpmnAutoLayoutOptions();

        var context = new LayoutContext(model);
        context.PlaceScope(context.RootChildren, OriginX, OriginY);

        var view = new ModelingViewDto
        {
            Id = string.IsNullOrWhiteSpace(options.ViewId) ? "auto" : options.ViewId,
            Name = !string.IsNullOrWhiteSpace(options.ViewName)
                ? options.ViewName
                : string.IsNullOrWhiteSpace(model.Title) ? "Auto layout" : model.Title,
            Notation = BpmnConstants.Bpmn2Notation,
        };

        foreach (var element in model.Elements)
        {
            if (!context.Positions.TryGetValue(element.Id, out var position))
            {
                continue;
            }

            string? parentNodeId = null;
            if (!string.IsNullOrWhiteSpace(element.ParentId)
                && !string.Equals(element.ParentId, element.Id, StringComparison.Ordinal)
                && context.Positions.ContainsKey(element.ParentId))
            {
                parentNodeId = element.ParentId;
            }

            view.Nodes.Add(new ModelingViewNodeDto
            {
                ElementId = element.Id,
                X = position.X,
                Y = position.Y,
                Width = position.Width,
                Height = position.Height,
                ParentNodeId = parentNodeId,
            });
        }

        foreach (var relationship in model.Relationships)
        {
            if (!context.Positions.TryGetValue(relationship.SourceElementId, out _)
                || !context.Positions.TryGetValue(relationship.TargetElementId, out _))
            {
                continue;
            }

            view.Connections.Add(new ModelingViewConnectionDto
            {
                RelationshipId = relationship.Id,
                SourceNodeId = relationship.SourceElementId,
                TargetNodeId = relationship.TargetElementId,
                // No waypoints: the stored view is the layout authority for node geometry only —
                // the diagram generator auto-routes the edge (docs/modeling-bpmn-xml.md §5.4).
                Waypoints = [],
            });
        }

        return view;
    }

    /// <summary>
    /// Computes the two endpoint waypoints of a connection: the exit point faces the target, the
    /// entry point faces the source (edge midpoints), so flow reads left-to-right. Shared by the
    /// auto-layout and the partial-BPMNDI fill, which routes connections across DI-positioned
    /// nodes.
    /// </summary>
    internal static List<ModelingViewWaypointDto> Route(
        IReadOnlyDictionary<string, Placement> positions, string sourceId, string targetId)
    {
        var source = positions[sourceId];
        var target = positions[targetId];
        var sourceCenterX = source.X + source.Width / 2;
        var sourceCenterY = source.Y + source.Height / 2;
        var targetCenterX = target.X + target.Width / 2;
        var targetCenterY = target.Y + target.Height / 2;

        (double X, double Y) exit;
        (double X, double Y) entry;
        if (string.Equals(sourceId, targetId, StringComparison.Ordinal))
        {
            exit = (source.X + source.Width, sourceCenterY);
            entry = (sourceCenterX, source.Y + source.Height);
        }
        else if (targetCenterX >= sourceCenterX)
        {
            exit = (source.X + source.Width, sourceCenterY);
            entry = (target.X, targetCenterY);
        }
        else
        {
            exit = (source.X, sourceCenterY);
            entry = (target.X + target.Width, targetCenterY);
        }

        return
        [
            new ModelingViewWaypointDto { X = exit.X, Y = exit.Y },
            new ModelingViewWaypointDto { X = entry.X, Y = entry.Y },
        ];
    }

    private static bool IsFlowNode(ModelingElementDto element) =>
        FlowSemanticTypes.Contains(element.SemanticType, StringComparer.Ordinal);

    private static bool IsPool(ModelingElementDto element) =>
        string.Equals(element.SemanticType, BpmnSemanticTypes.Pool, StringComparison.Ordinal);

    private static bool IsLane(ModelingElementDto element) =>
        string.Equals(element.SemanticType, BpmnSemanticTypes.Lane, StringComparison.Ordinal);

    private static bool IsSubProcess(ModelingElementDto element) =>
        string.Equals(element.SemanticType, BpmnSemanticTypes.SubProcess, StringComparison.Ordinal)
        || string.Equals(element.SemanticType, BpmnSemanticTypes.EventSubProcess, StringComparison.Ordinal);

    private static (double Width, double Height) DefaultSize(ModelingElementDto element) =>
        element.SemanticType switch
        {
            BpmnSemanticTypes.StartEvent or BpmnSemanticTypes.EndEvent
                or BpmnSemanticTypes.IntermediateCatchEvent or BpmnSemanticTypes.IntermediateThrowEvent
                or BpmnSemanticTypes.BoundaryEvent => (EventSize, EventSize),
            BpmnSemanticTypes.ExclusiveGateway or BpmnSemanticTypes.ParallelGateway
                or BpmnSemanticTypes.InclusiveGateway or BpmnSemanticTypes.ComplexGateway
                or BpmnSemanticTypes.EventBasedGateway => (GatewaySize, GatewaySize),
            BpmnSemanticTypes.DataObject => (DataObjectWidth, DataObjectHeight),
            BpmnSemanticTypes.DataStore => (DataStoreWidth, DataStoreHeight),
            BpmnSemanticTypes.TextAnnotation => (AnnotationWidth, AnnotationHeight),
            BpmnSemanticTypes.Group => (GroupWidth, GroupHeight),
            _ => (TaskWidth, TaskHeight),
        };

    /// <summary>A placed node: absolute view coordinates plus size.</summary>
    internal readonly record struct Placement(double X, double Y, double Width, double Height);

    /// <summary>The relative layout of one scope's direct children.</summary>
    private sealed class ScopeContent
    {
        public required List<(ModelingElementDto Element, double X, double Y, double Width, double Height)> Placements { get; init; }

        public double Width { get; init; }

        public double Height { get; init; }
    }

    private sealed class LayoutContext
    {
        private readonly Dictionary<string, int> _elementOrdinal;
        private readonly Dictionary<string, List<ModelingElementDto>> _childrenByParent = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (double Width, double Height)> _measuredSizes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _measuring = new(StringComparer.Ordinal);

        /// <summary>Source element id → outgoing relationships in declaration order (built once).</summary>
        private readonly Dictionary<string, List<ModelingRelationshipDto>> _relationshipsBySource = new(StringComparer.Ordinal);

        /// <summary>Target element id → incoming relationships in declaration order (built once).</summary>
        private readonly Dictionary<string, List<ModelingRelationshipDto>> _relationshipsByTarget = new(StringComparer.Ordinal);

        public LayoutContext(ModelingModelDto model)
        {
            _elementOrdinal = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index < model.Elements.Count; index++)
            {
                var element = model.Elements[index];
                if (!string.IsNullOrWhiteSpace(element.Id))
                {
                    _elementOrdinal[element.Id] = index;
                }
            }

            foreach (var element in model.Elements)
            {
                var parentKey = ParentKeyOf(element);
                if (!_childrenByParent.TryGetValue(parentKey, out var siblings))
                {
                    siblings = [];
                    _childrenByParent[parentKey] = siblings;
                }

                siblings.Add(element);
            }

            foreach (var relationship in model.Relationships)
            {
                if (!string.IsNullOrWhiteSpace(relationship.SourceElementId))
                {
                    if (!_relationshipsBySource.TryGetValue(relationship.SourceElementId, out var outgoing))
                    {
                        outgoing = [];
                        _relationshipsBySource[relationship.SourceElementId] = outgoing;
                    }

                    outgoing.Add(relationship);
                }

                if (!string.IsNullOrWhiteSpace(relationship.TargetElementId))
                {
                    if (!_relationshipsByTarget.TryGetValue(relationship.TargetElementId, out var incoming))
                    {
                        incoming = [];
                        _relationshipsByTarget[relationship.TargetElementId] = incoming;
                    }

                    incoming.Add(relationship);
                }
            }
        }

        public Dictionary<string, Placement> Positions { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<ModelingElementDto> RootChildren =>
            _childrenByParent.TryGetValue(string.Empty, out var children) ? children : [];

        private string ParentKeyOf(ModelingElementDto element)
        {
            var parentId = element.ParentId ?? string.Empty;
            if (parentId.Length == 0
                || string.Equals(parentId, element.Id, StringComparison.Ordinal)
                || !_elementOrdinal.ContainsKey(parentId))
            {
                return string.Empty;
            }

            return parentId;
        }

        private IReadOnlyList<ModelingElementDto> ChildrenOf(string containerId) =>
            _childrenByParent.TryGetValue(containerId, out var children) ? children : [];

        /// <summary>The size of an element: measured for containers, conventional defaults otherwise.</summary>
        public (double Width, double Height) SizeOf(ModelingElementDto element)
        {
            if (!IsPool(element) && !IsLane(element) && !IsSubProcess(element))
            {
                return DefaultSize(element);
            }

            if (_measuredSizes.TryGetValue(element.Id, out var cached))
            {
                return cached;
            }

            // Importers declare a container before its children; a hand-built model that violates
            // that gets the conventional default instead of overflowing the stack.
            if (!_measuring.Add(element.Id))
            {
                return (TaskWidth, TaskHeight);
            }

            var content = ArrangeScope(ChildrenOf(element.Id));
            (double Width, double Height) size = element.SemanticType switch
            {
                _ when IsPool(element) => (
                    PoolLabelBand + content.Width + 2 * ContainerPadding,
                    Math.Max(PoolMinHeight, content.Height + 2 * ContainerPadding)),
                _ when IsLane(element) => (
                    content.Width + 2 * ContainerPadding,
                    Math.Max(LaneMinHeight, content.Height + 2 * ContainerPadding)),
                _ => (
                    Math.Max(SubProcessMinWidth, content.Width + 2 * SubProcessPadding),
                    Math.Max(SubProcessMinHeight, content.Height + 2 * SubProcessPadding)),
            };
            _measuring.Remove(element.Id);
            _measuredSizes[element.Id] = size;
            return size;
        }

        /// <summary>Arranges the direct children of one scope into relative coordinates.</summary>
        public ScopeContent ArrangeScope(IReadOnlyList<ModelingElementDto> children)
        {
            var pools = children.Where(IsPool).ToList();
            var lanes = children.Where(IsLane).ToList();
            var flow = children.Where(element => !IsPool(element) && !IsLane(element) && IsFlowNode(element)).ToList();
            var free = children.Where(element => !IsPool(element) && !IsLane(element) && !IsFlowNode(element)).ToList();

            var placements = new List<(ModelingElementDto Element, double X, double Y, double Width, double Height)>();

            // Each block stacks vertically; non-empty blocks are separated by BlockGapY.
            var blockCursorY = 0.0;
            var width = 0.0;

            void AddBlock(List<(ModelingElementDto Element, double X, double Y, double Width, double Height)> block, double blockWidth, double blockHeight)
            {
                if (block.Count == 0)
                {
                    return;
                }

                if (placements.Count > 0)
                {
                    blockCursorY += BlockGapY;
                }

                foreach (var placement in block)
                {
                    placements.Add((placement.Element, placement.X, placement.Y + blockCursorY, placement.Width, placement.Height));
                }

                blockCursorY += blockHeight;
                width = Math.Max(width, blockWidth);
            }

            // Pools: stacked horizontal bands, top to bottom in declaration order.
            var poolBlock = new List<(ModelingElementDto Element, double X, double Y, double Width, double Height)>();
            var poolY = 0.0;
            var poolWidth = 0.0;
            foreach (var pool in pools)
            {
                var size = SizeOf(pool);
                poolBlock.Add((pool, 0, poolY, size.Width, size.Height));
                poolY += size.Height + PoolGapY;
                poolWidth = Math.Max(poolWidth, size.Width);
            }

            if (poolBlock.Count > 0)
            {
                poolY -= PoolGapY;
            }

            AddBlock(poolBlock, poolWidth, poolY);

            // Lanes: flush bands inside their scope (a pool carries them; a lane-less process can
            // hold them at the root when no collaboration declared a pool).
            var laneBlock = new List<(ModelingElementDto Element, double X, double Y, double Width, double Height)>();
            var laneY = 0.0;
            var laneWidth = 0.0;
            foreach (var lane in lanes)
            {
                var size = SizeOf(lane);
                laneBlock.Add((lane, 0, laneY, size.Width, size.Height));
                laneY += size.Height;
                laneWidth = Math.Max(laneWidth, size.Width);
            }

            AddBlock(laneBlock, laneWidth, laneY);

            // Flow nodes: layered left-to-right; free-standing artifacts one row below the flow.
            var flowContent = ArrangeFlow(flow);
            var flowBlock = flowContent.Placements;
            var freeBlock = new List<(ModelingElementDto Element, double X, double Y, double Width, double Height)>();
            var freeX = 0.0;
            var freeHeight = 0.0;
            foreach (var element in free)
            {
                var size = DefaultSize(element);
                freeBlock.Add((element, freeX, 0, size.Width, size.Height));
                freeX += size.Width + FreeGapX;
                freeHeight = Math.Max(freeHeight, size.Height);
            }

            var freeWidth = freeBlock.Count > 0 ? freeX - FreeGapX : 0;
            AddBlock(flowBlock, flowContent.Width, flowContent.Height);
            AddBlock(freeBlock, freeWidth, freeHeight);

            return new ScopeContent { Placements = placements, Width = width, Height = blockCursorY };
        }

        private ScopeContent ArrangeFlow(IReadOnlyList<ModelingElementDto> flow)
        {
            if (flow.Count == 0)
            {
                return new ScopeContent { Placements = [], Width = 0, Height = 0 };
            }

            var layers = ComputeLayers(flow);
            var stepX = flow.Max(element => SizeOf(element).Width) + GapX;

            // Bucket the flow nodes by layer in ONE pass (linear, no per-layer rescans).
            var maxLayer = 0;
            for (var index = 0; index < layers.Length; index++)
            {
                if (layers[index] > maxLayer)
                {
                    maxLayer = layers[index];
                }
            }

            var membersByLayer = new List<ModelingElementDto>[maxLayer + 1];
            for (var layer = 0; layer <= maxLayer; layer++)
            {
                membersByLayer[layer] = [];
            }

            for (var index = 0; index < flow.Count; index++)
            {
                membersByLayer[layers[index]].Add(flow[index]);
            }

            // Each layer stacks its own members from y = 0 in declaration order; the block height
            // is the TALLEST layer stack and every other layer is centred on it — a single-member
            // chain therefore renders as one horizontal line.
            var layerHeights = new double[maxLayer + 1];
            var maxHeight = 0.0;
            for (var layer = 0; layer <= maxLayer; layer++)
            {
                var height = 0.0;
                var members = membersByLayer[layer];
                for (var index = 0; index < members.Count; index++)
                {
                    height += SizeOf(members[index]).Height;
                    if (index + 1 < members.Count)
                    {
                        height += RowGapY;
                    }
                }

                layerHeights[layer] = height;
                if (height > maxHeight)
                {
                    maxHeight = height;
                }
            }

            var placements = new List<(ModelingElementDto Element, double X, double Y, double Width, double Height)>();
            var width = 0.0;
            for (var layer = 0; layer <= maxLayer; layer++)
            {
                var y = (maxHeight - layerHeights[layer]) / 2;
                var members = membersByLayer[layer];
                for (var index = 0; index < members.Count; index++)
                {
                    var element = members[index];
                    var size = SizeOf(element);
                    placements.Add((element, layer * stepX, y, size.Width, size.Height));
                    if (layer * stepX + size.Width > width)
                    {
                        width = layer * stepX + size.Width;
                    }

                    y += size.Height + RowGapY;
                }
            }

            return new ScopeContent { Placements = placements, Width = width, Height = maxHeight };
        }

        /// <summary>
        /// Longest-path layering over the flow relationships of the scope, computed by an
        /// ITERATIVE Kahn topological sort (no recursion — a 100 000-node chain cannot overflow
        /// the stack). Cycles are broken by ignoring DFS back edges; boundary events join the
        /// layer of the activity they attach to (weight-0 edge). Everything is driven by
        /// declaration order, so the result is stable. Runs in O(V + E) over the scope.
        /// </summary>
        private int[] ComputeLayers(IReadOnlyList<ModelingElementDto> flow)
        {
            var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index < flow.Count; index++)
            {
                indexById[flow[index].Id] = index;
            }

            // Edges in a deterministic order: each flow node's outgoing relationships in
            // declaration order, then the boundary attachments as weight-0 edges.
            var edges = new List<(int From, int To, int Weight)>();
            for (var index = 0; index < flow.Count; index++)
            {
                if (!_relationshipsBySource.TryGetValue(flow[index].Id, out var outgoing))
                {
                    continue;
                }

                foreach (var relationship in outgoing)
                {
                    if (indexById.TryGetValue(relationship.TargetElementId, out var to))
                    {
                        edges.Add((index, to, 1));
                    }
                }
            }

            for (var index = 0; index < flow.Count; index++)
            {
                var element = flow[index];
                if (!string.Equals(element.SemanticType, BpmnSemanticTypes.BoundaryEvent, StringComparison.Ordinal)
                    || !element.Properties.TryGetValue("attachedToRef", out var attached)
                    || attached.ValueKind != System.Text.Json.JsonValueKind.String
                    || !indexById.TryGetValue(attached.GetString() ?? string.Empty, out var host))
                {
                    continue;
                }

                edges.Add((host, index, 0));
            }

            // Forward adjacency for cycle detection AND the Kahn relaxation — both in edge
            // declaration order.
            var forward = new List<int>[flow.Count];
            for (var node = 0; node < flow.Count; node++)
            {
                forward[node] = [];
            }

            for (var edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
            {
                forward[edges[edgeIndex].From].Add(edgeIndex);
            }

            // Back-edge detection: iterative DFS over the forward graph in declaration order. An
            // edge whose target is already on the stack closes a cycle and is ignored for
            // layering (a BPMN rework loop must not push its nodes to ever deeper columns).
            var state = new int[flow.Count]; // 0 = unvisited, 1 = on stack, 2 = done
            var isBackEdge = new bool[edges.Count];
            for (var start = 0; start < flow.Count; start++)
            {
                if (state[start] != 0)
                {
                    continue;
                }

                var stack = new Stack<(int Node, int NextAdjacency)>();
                state[start] = 1;
                stack.Push((start, 0));
                while (stack.Count > 0)
                {
                    var (node, nextAdjacency) = stack.Pop();
                    if (nextAdjacency < forward[node].Count)
                    {
                        stack.Push((node, nextAdjacency + 1));
                        var edgeIndex = forward[node][nextAdjacency];
                        var target = edges[edgeIndex].To;
                        if (state[target] == 1)
                        {
                            isBackEdge[edgeIndex] = true;
                        }
                        else if (state[target] == 0)
                        {
                            state[target] = 1;
                            stack.Push((target, 0));
                        }
                    }
                    else
                    {
                        state[node] = 2;
                    }
                }
            }

            // Kahn's algorithm over the DAG (back edges removed): relax layers[to] =
            // max(layers[to], layers[from] + weight) in topological order. Nodes of a residual
            // cycle (impossible after back-edge removal, kept defensive) stay in layer 0.
            var layers = new int[flow.Count];
            var indegree = new int[flow.Count];
            for (var edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
            {
                if (!isBackEdge[edgeIndex])
                {
                    indegree[edges[edgeIndex].To]++;
                }
            }

            var queue = new Queue<int>();
            for (var node = 0; node < flow.Count; node++)
            {
                if (indegree[node] == 0)
                {
                    queue.Enqueue(node);
                }
            }

            while (queue.Count > 0)
            {
                var from = queue.Dequeue();
                foreach (var edgeIndex in forward[from])
                {
                    if (isBackEdge[edgeIndex])
                    {
                        continue;
                    }

                    var to = edges[edgeIndex].To;
                    var candidate = layers[from] + edges[edgeIndex].Weight;
                    if (candidate > layers[to])
                    {
                        layers[to] = candidate;
                    }

                    if (--indegree[to] == 0)
                    {
                        queue.Enqueue(to);
                    }
                }
            }

            return layers;
        }

        /// <summary>
        /// Positions a scope's children at the absolute origin and recurses into the containers
        /// among them (pools, lanes, subprocesses).
        /// </summary>
        public void PlaceScope(IReadOnlyList<ModelingElementDto> children, double originX, double originY)
        {
            var content = ArrangeScope(children);
            foreach (var (element, x, y, width, height) in content.Placements)
            {
                var absoluteX = originX + x;
                var absoluteY = originY + y;
                Positions[element.Id] = new Placement(absoluteX, absoluteY, width, height);

                if (IsPool(element))
                {
                    PlaceScope(ChildrenOf(element.Id), absoluteX + PoolLabelBand + ContainerPadding, absoluteY + ContainerPadding);
                }
                else if (IsLane(element))
                {
                    PlaceScope(ChildrenOf(element.Id), absoluteX + ContainerPadding, absoluteY + ContainerPadding);
                }
                else if (IsSubProcess(element))
                {
                    PlaceScope(ChildrenOf(element.Id), absoluteX + SubProcessPadding, absoluteY + SubProcessPadding);
                }
            }
        }
    }
}
