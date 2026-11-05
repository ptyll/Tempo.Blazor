using System.Globalization;
using System.Text;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The knobs of one <see cref="AgentGraphLayout.Build"/> pass: the pixel box of a node, the gaps between columns and
/// rows, the canvas margin, the group padding and the host's pinned positions. Every value is PIXELS, because the
/// component paints the nodes at absolute positions and an SVG curve has to line up with them exactly.
/// </summary>
internal sealed record AgentGraphLayoutOptions
{
    /// <summary>The width of one node box in pixels. Must be positive; anything else falls back to 190.</summary>
    public double NodeWidthPx { get; init; } = AgentGraphLayout.NodeWidthPx;

    /// <summary>The height of one node box in pixels. Must be positive; anything else falls back to 72.</summary>
    public double NodeHeightPx { get; init; } = AgentGraphLayout.NodeHeightPx;

    /// <summary>The horizontal gap between two columns in pixels. Must be positive; anything else falls back to 110.</summary>
    public double HorizontalGapPx { get; init; } = AgentGraphLayout.HorizontalGapPx;

    /// <summary>The vertical gap between two nodes of one column in pixels. Must be positive; anything else falls back to 26.</summary>
    public double VerticalGapPx { get; init; } = AgentGraphLayout.VerticalGapPx;

    /// <summary>The empty border of the canvas in pixels. Negative values fall back to the margin default (24).</summary>
    public double MarginPx { get; init; } = AgentGraphLayout.MarginPx;

    /// <summary>The padding a group box keeps around its members in pixels. Negative values fall back to 22.</summary>
    public double GroupPaddingPx { get; init; } = AgentGraphLayout.GroupPaddingPx;

    /// <summary>
    /// The nodes the HOST pinned, by node id. A pinned node keeps its exact coordinate instead of the column position
    /// the layout computed; everything else is still laid out around it and the canvas grows to contain it. An id that
    /// names no supplied node is ignored. Null means "no node is pinned".
    /// </summary>
    public IReadOnlyDictionary<string, AgentGraphPosition>? NodePositions { get; init; }
}

/// <summary>One node as it is painted: which column and row it occupies and the box it fills.</summary>
internal sealed record AgentGraphNodePlacement
{
    /// <summary>The node the placement paints.</summary>
    public required AgentGraphNode Node { get; init; }

    /// <summary>The 0-based column, left to right.</summary>
    public required int Layer { get; init; }

    /// <summary>The 0-based row inside the column, top to bottom.</summary>
    public required int RowIndex { get; init; }

    /// <summary>Distance in pixels from the canvas's left edge to the box's left edge.</summary>
    public required double X { get; init; }

    /// <summary>Distance in pixels from the canvas's top edge to the box's top edge.</summary>
    public required double Y { get; init; }

    /// <summary>The painted width in pixels.</summary>
    public required double WidthPx { get; init; }

    /// <summary>The painted height in pixels.</summary>
    public required double HeightPx { get; init; }

    /// <summary>Whether the host pinned this position (so a reader can tell a hand-placed node from a laid-out one).</summary>
    public required bool IsPinned { get; init; }

    /// <summary>The horizontal centre of the box in pixels.</summary>
    public double CenterX => X + (WidthPx / 2);

    /// <summary>The vertical centre of the box in pixels.</summary>
    public double CenterY => Y + (HeightPx / 2);

    /// <summary>The right edge of the box in pixels.</summary>
    public double Right => X + WidthPx;

    /// <summary>The bottom edge of the box in pixels.</summary>
    public double Bottom => Y + HeightPx;
}

/// <summary>
/// One connection as it is painted: the two boxes it joins, the cubic curve between them and the stroke width its call
/// count earned.
/// </summary>
internal sealed record AgentGraphEdgePath
{
    /// <summary>The host's edge.</summary>
    public required AgentGraphEdge Edge { get; init; }

    /// <summary>The box the call starts at.</summary>
    public required AgentGraphNodePlacement From { get; init; }

    /// <summary>The box the call arrives at.</summary>
    public required AgentGraphNodePlacement To { get; init; }

    /// <summary>The SVG path data of the cubic segment, in canvas pixels, in invariant culture.</summary>
    public required string Path { get; init; }

    /// <summary>The stroke width in pixels, derived from <see cref="AgentGraphEdge.CallCount"/> on a log scale.</summary>
    public required double StrokeWidthPx { get; init; }

    /// <summary>
    /// Whether the call travels BACKWARDS (or loops on one node), i.e. it is not a forward column-to-column
    /// connection. Such an edge is routed below the boxes instead of through them.
    /// </summary>
    public required bool IsBackEdge { get; init; }
}

/// <summary>One group as it is painted: the labelled box around every member and the members inside it.</summary>
internal sealed record AgentGraphGroupBox
{
    /// <summary>The group's name, exactly as the nodes carry it.</summary>
    public required string Group { get; init; }

    /// <summary>Distance in pixels from the canvas's left edge to the box's left edge.</summary>
    public required double X { get; init; }

    /// <summary>Distance in pixels from the canvas's top edge to the box's top edge.</summary>
    public required double Y { get; init; }

    /// <summary>The painted width in pixels.</summary>
    public required double WidthPx { get; init; }

    /// <summary>The painted height in pixels.</summary>
    public required double HeightPx { get; init; }

    /// <summary>The members of the group, in painting order.</summary>
    public required IReadOnlyList<AgentGraphNodePlacement> Nodes { get; init; }
}

/// <summary>Everything one <see cref="AgentGraphLayout.Build"/> pass computed for a single render.</summary>
internal sealed record AgentGraphLayoutResult
{
    /// <summary>The node placements, in column and row order.</summary>
    public required IReadOnlyList<AgentGraphNodePlacement> Nodes { get; init; }

    /// <summary>The edge curves, in the snapshot's edge order.</summary>
    public required IReadOnlyList<AgentGraphEdgePath> Edges { get; init; }

    /// <summary>The group boxes, in the order the groups first appear.</summary>
    public required IReadOnlyList<AgentGraphGroupBox> Groups { get; init; }

    /// <summary>The width of the canvas in pixels.</summary>
    public required double WidthPx { get; init; }

    /// <summary>The height of the canvas in pixels.</summary>
    public required double HeightPx { get; init; }

    /// <summary>How many columns the layout has.</summary>
    public required int LayerCount { get; init; }

    /// <summary>The largest <see cref="AgentGraphEdge.CallCount"/> of the snapshot, i.e. the width scale's ceiling.</summary>
    public required int MaxCallCount { get; init; }

    /// <summary>How many connections travelled backwards (or looped on one node).</summary>
    public required int BackEdgeCount { get; init; }

    /// <summary>How many edges named a node the snapshot does not contain, and were therefore not drawn.</summary>
    public required int UnresolvedEdgeCount { get; init; }

    /// <summary>Whether the graph has no nodes at all — the surface then renders its empty state.</summary>
    public bool IsEmpty => Nodes.Count == 0;

    /// <summary>Finds the placement of a node by id.</summary>
    /// <param name="nodeId">The node's id.</param>
    /// <returns>The placement, or null when the graph does not contain the node.</returns>
    public AgentGraphNodePlacement? Find(string? nodeId)
        => nodeId is null
            ? null
            : Nodes.FirstOrDefault(placement => string.Equals(placement.Node.Id, nodeId, StringComparison.Ordinal));
}

/// <summary>
/// Turns an agent graph snapshot into everything a graph surface paints: the layered columns, the ordered rows inside
/// each of them, the pixel box of every node, the group boxes, the cubic curve of every connection and the stroke width
/// its call count earned.
/// <para>
/// Pure C#: no stylesheet, no JavaScript and no clock — so the whole geometry is unit-testable and DETERMINISTIC (the
/// same snapshot always produces the same numbers, which a live surface re-running it on every paint depends on).
/// </para>
/// <para>
/// Columns follow the node KIND (a human asks on the left, an agent answers, a resource follows, a tool stands on the
/// right) and are then tightened so every edge points forward — an agent delegating to another agent pushes the target
/// one column right instead of drawing a line back across the picture. A cycle is broken deterministically by a
/// depth-first pass: the edge that closes it is still drawn, but below the boxes instead of through them. Rows inside a
/// column are ordered by a BARYCENTER heuristic (four alternating sweeps), which is what keeps the curves from
/// crossing, and members of one group are kept in one contiguous run so a group box never swallows a stranger.
/// </para>
/// </summary>
internal static class AgentGraphLayout
{
    /// <summary>
    /// The width of one node box in pixels: wide enough for an avatar, two label lines and the longest state word
    /// ("Waiting for approval") in one row, and narrow enough that a four-column graph still fits a laptop card.
    /// </summary>
    internal const double NodeWidthPx = 200;

    /// <summary>The height of one node box in pixels: an avatar, two label lines and one state row.</summary>
    internal const double NodeHeightPx = 72;

    /// <summary>The horizontal gap between two columns in pixels, so a curve has room to read.</summary>
    internal const double HorizontalGapPx = 56;

    /// <summary>The vertical gap between two nodes of one column in pixels.</summary>
    internal const double VerticalGapPx = 22;

    /// <summary>The empty border of the canvas in pixels, so a focus ring and a group box are never clipped.</summary>
    internal const double MarginPx = 24;

    /// <summary>
    /// The padding a group box keeps around its members in pixels. It leaves room for the group's LABEL above the
    /// first member (the label is painted inside the box, so a tight padding would put it on top of a node).
    /// </summary>
    internal const double GroupPaddingPx = 22;

    /// <summary>
    /// The EXTRA vertical space a group run keeps from the ungrouped node above it, in pixels. Without it the box's
    /// padding would put its top edge exactly on the neighbour's bottom edge, which reads as one box swallowing the
    /// node above it.
    /// </summary>
    internal const double GroupGapPx = 28;

    /// <summary>The stroke width of a connection that carried no calls: thin, but never invisible.</summary>
    internal const double MinEdgeWidthPx = 1.5;

    /// <summary>The stroke width of the busiest connection of the snapshot.</summary>
    internal const double MaxEdgeWidthPx = 7;

    /// <summary>How far a backwards or same-column curve dips below the boxes it joins, in pixels.</summary>
    internal const double CurveDepthPx = 56;

    /// <summary>How many alternating barycenter sweeps one pass runs: enough to settle, few enough to stay cheap.</summary>
    private const int BarycenterSweeps = 4;

    /// <summary>Builds the layout of one render.</summary>
    /// <param name="snapshot">The graph to draw. Null is treated as an empty graph.</param>
    /// <param name="options">The pixel metrics and the host's pinned positions; null uses the defaults.</param>
    /// <returns>The computed layout.</returns>
    public static AgentGraphLayoutResult Build(AgentGraphSnapshot? snapshot, AgentGraphLayoutOptions? options = null)
    {
        var settings = options ?? new AgentGraphLayoutOptions();
        var nodeWidth = Positive(settings.NodeWidthPx, NodeWidthPx);
        var nodeHeight = Positive(settings.NodeHeightPx, NodeHeightPx);
        var horizontalGap = Positive(settings.HorizontalGapPx, HorizontalGapPx);
        var verticalGap = Positive(settings.VerticalGapPx, VerticalGapPx);
        var margin = settings.MarginPx >= 0 && double.IsFinite(settings.MarginPx) ? settings.MarginPx : MarginPx;
        var groupPadding = settings.GroupPaddingPx >= 0 && double.IsFinite(settings.GroupPaddingPx)
            ? settings.GroupPaddingPx
            : GroupPaddingPx;

        var nodes = DistinctNodes(snapshot?.Nodes);
        if (nodes.Count == 0)
        {
            return new AgentGraphLayoutResult
            {
                Nodes = [],
                Edges = [],
                Groups = [],
                WidthPx = 0,
                HeightPx = 0,
                LayerCount = 0,
                MaxCallCount = 0,
                BackEdgeCount = 0,
                UnresolvedEdgeCount = 0,
            };
        }

        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < nodes.Count; index++)
        {
            indexById[nodes[index].Id] = index;
        }

        var (edges, unresolved) = ResolveEdges(snapshot?.Edges, indexById);
        var (layerOf, isBackEdge) = AssignLayers(nodes, edges, indexById);
        var columns = OrderColumns(nodes, edges, layerOf, indexById);

        var placements = PlaceNodes(nodes, columns, indexById, settings,
            nodeWidth, nodeHeight, horizontalGap, verticalGap, margin);

        var maxCallCount = 0;
        foreach (var edge in edges)
        {
            if (edge.CallCount > maxCallCount)
            {
                maxCallCount = edge.CallCount;
            }
        }

        var paths = BuildPaths(edges, placements, maxCallCount);
        var groups = BuildGroups(placements, groupPadding);

        var width = 0d;
        var height = 0d;
        foreach (var placement in placements.Values)
        {
            width = Math.Max(width, placement.Right);
            height = Math.Max(height, placement.Bottom);
        }

        return new AgentGraphLayoutResult
        {
            Nodes = columns.SelectMany(column => column.Select(id => placements[id])).ToList(),
            Edges = paths,
            Groups = groups,
            WidthPx = width + margin,
            HeightPx = height + margin,
            LayerCount = columns.Count,
            MaxCallCount = maxCallCount,
            BackEdgeCount = isBackEdge.Count,
            UnresolvedEdgeCount = unresolved,
        };
    }

    /// <summary>
    /// The stroke width a connection earns from its call count, on a LOGARITHMIC scale: ten calls out of a hundred sit
    /// at half the width range instead of at a tenth of it, so a quiet connection stays visible beside a busy one.
    /// </summary>
    /// <param name="callCount">The calls the connection carried; zero or less paints the thinnest width.</param>
    /// <param name="maxCallCount">The busiest connection of the same graph; zero or less paints the thinnest width.</param>
    /// <returns>A width in pixels between <see cref="MinEdgeWidthPx"/> and <see cref="MaxEdgeWidthPx"/>.</returns>
    public static double EdgeWidth(int callCount, int maxCallCount)
    {
        if (callCount <= 0 || maxCallCount <= 0)
        {
            return MinEdgeWidthPx;
        }

        if (callCount >= maxCallCount)
        {
            return MaxEdgeWidthPx;
        }

        var ratio = Math.Log(1d + callCount) / Math.Log(1d + maxCallCount);
        ratio = double.IsFinite(ratio) ? Math.Clamp(ratio, 0, 1) : 0;
        return MinEdgeWidthPx + ((MaxEdgeWidthPx - MinEdgeWidthPx) * ratio);
    }

    /// <summary>The nodes in painting order, with a duplicate id read once (first wins) so no box is painted twice.</summary>
    private static List<AgentGraphNode> DistinctNodes(IReadOnlyList<AgentGraphNode>? nodes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<AgentGraphNode>(nodes?.Count ?? 0);
        foreach (var node in nodes ?? [])
        {
            if (node is not null && seen.Add(node.Id))
            {
                result.Add(node);
            }
        }

        return result;
    }

    /// <summary>
    /// The connections whose two endpoints exist, with a duplicate edge id read once (first wins). An edge naming a node
    /// the snapshot does not contain is COUNTED instead of drawn.
    /// </summary>
    private static (List<AgentGraphEdge> Edges, int Unresolved) ResolveEdges(
        IReadOnlyList<AgentGraphEdge>? edges,
        Dictionary<string, int> indexById)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<AgentGraphEdge>(edges?.Count ?? 0);
        var unresolved = 0;

        foreach (var edge in edges ?? [])
        {
            if (edge is null)
            {
                continue;
            }

            if (!indexById.ContainsKey(edge.From) || !indexById.ContainsKey(edge.To))
            {
                unresolved++;
                continue;
            }

            if (seen.Add(edge.Id))
            {
                result.Add(edge);
            }
        }

        return (result, unresolved);
    }

    /// <summary>
    /// Assigns every node its column: the kind's own column, then pushed right until every non-back edge points
    /// forward, then compacted so an empty column never leaves a gap. The cycle-breaking depth-first pass runs in
    /// declaration order, so the same graph always breaks the same edge.
    /// </summary>
    private static (Dictionary<string, int> LayerOf, HashSet<string> BackEdges) AssignLayers(
        List<AgentGraphNode> nodes,
        List<AgentGraphEdge> edges,
        Dictionary<string, int> indexById)
    {
        var count = nodes.Count;
        var outgoing = new List<int>[count];
        var incoming = new List<int>[count];
        for (var index = 0; index < count; index++)
        {
            outgoing[index] = [];
            incoming[index] = [];
        }

        var edgeByIndex = new List<(int Index, AgentGraphEdge Edge)>();
        for (var index = 0; index < edges.Count; index++)
        {
            var from = indexById[edges[index].From];
            var to = indexById[edges[index].To];
            edgeByIndex.Add((index, edges[index]));
            outgoing[from].Add(index);
            incoming[to].Add(index);
        }

        var backEdges = new HashSet<string>(StringComparer.Ordinal);
        var state = new int[count];
        var stack = new Stack<(int Node, int Edge)>();
        for (var start = 0; start < count; start++)
        {
            if (state[start] != 0)
            {
                continue;
            }

            state[start] = 1;
            stack.Push((start, 0));
            while (stack.Count > 0)
            {
                var (nodeIndex, edgeIndex) = stack.Pop();
                if (edgeIndex >= outgoing[nodeIndex].Count)
                {
                    state[nodeIndex] = 2;
                    continue;
                }

                stack.Push((nodeIndex, edgeIndex + 1));

                var edge = edges[outgoing[nodeIndex][edgeIndex]];
                var target = indexById[edge.To];
                if (state[target] == 1)
                {
                    // The target is still on the stack: this edge closes a cycle and is drawn as a back edge.
                    backEdges.Add(edge.Id);
                }
                else if (state[target] == 0)
                {
                    state[target] = 1;
                    stack.Push((target, 0));
                }
            }
        }

        // Kahn's algorithm over the FORWARD edges only, picking the lowest declaration index first so the order is a
        // function of the snapshot alone.
        var indegree = new int[count];
        for (var index = 0; index < edges.Count; index++)
        {
            if (!backEdges.Contains(edges[index].Id))
            {
                indegree[indexById[edges[index].To]]++;
            }
        }

        var order = new List<int>(count);
        var placed = new bool[count];
        while (order.Count < count)
        {
            var next = -1;
            for (var candidate = 0; candidate < count; candidate++)
            {
                if (!placed[candidate] && indegree[candidate] == 0)
                {
                    next = candidate;
                    break;
                }
            }

            if (next < 0)
            {
                break;
            }

            placed[next] = true;
            order.Add(next);
            foreach (var edgeIndex in outgoing[next])
            {
                if (!backEdges.Contains(edges[edgeIndex].Id))
                {
                    indegree[indexById[edges[edgeIndex].To]]--;
                }
            }
        }

        var layers = new Dictionary<string, int>(StringComparer.Ordinal);
        var raw = new int[count];
        for (var index = 0; index < count; index++)
        {
            raw[index] = KindRank(nodes[index].Kind);
        }

        foreach (var nodeIndex in order)
        {
            foreach (var edgeIndex in incoming[nodeIndex])
            {
                if (backEdges.Contains(edges[edgeIndex].Id))
                {
                    continue;
                }

                var source = indexById[edges[edgeIndex].From];
                raw[nodeIndex] = Math.Max(raw[nodeIndex], raw[source] + 1);
            }
        }

        // Compact: only the columns that are actually used survive, in ascending order.
        var used = raw.Distinct().OrderBy(value => value).ToList();
        var compact = new Dictionary<int, int>();
        for (var index = 0; index < used.Count; index++)
        {
            compact[used[index]] = index;
        }

        for (var index = 0; index < count; index++)
        {
            layers[nodes[index].Id] = compact[raw[index]];
        }

        return (layers, backEdges);
    }

    /// <summary>The column a kind starts in: the human asks, the agent answers, a resource follows, a tool is last.</summary>
    private static int KindRank(AgentGraphNodeKind kind) => kind switch
    {
        AgentGraphNodeKind.Human => 0,
        AgentGraphNodeKind.Agent => 1,
        AgentGraphNodeKind.Resource => 2,
        AgentGraphNodeKind.Tool => 3,
        _ => 1,
    };

    /// <summary>
    /// The rows of every column: the declaration order first, then alternating down/up barycenter sweeps, then the
    /// group runs. Each step is stable, so the result is a function of the snapshot alone.
    /// </summary>
    private static List<List<string>> OrderColumns(
        List<AgentGraphNode> nodes,
        List<AgentGraphEdge> edges,
        Dictionary<string, int> layerOf,
        Dictionary<string, int> indexById)
    {
        var layerCount = layerOf.Count == 0 ? 0 : layerOf.Values.Max() + 1;
        var columns = new List<List<int>>(layerCount);
        for (var layer = 0; layer < layerCount; layer++)
        {
            columns.Add([]);
        }

        for (var index = 0; index < nodes.Count; index++)
        {
            columns[layerOf[nodes[index].Id]].Add(index);
        }

        var neighbours = new Dictionary<int, List<int>>();
        for (var index = 0; index < nodes.Count; index++)
        {
            neighbours[index] = [];
        }

        foreach (var edge in edges)
        {
            var from = indexById[edge.From];
            var to = indexById[edge.To];
            neighbours[from].Add(to);
            neighbours[to].Add(from);
        }

        for (var sweep = 0; sweep < BarycenterSweeps; sweep++)
        {
            for (var layer = 1; layer < layerCount; layer++)
            {
                Reorder(columns[layer], columns[layer - 1], neighbours);
            }

            for (var layer = layerCount - 2; layer >= 0; layer--)
            {
                Reorder(columns[layer], columns[layer + 1], neighbours);
            }
        }

        var result = new List<List<string>>(layerCount);
        foreach (var column in columns)
        {
            var groupRank = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var index in column)
            {
                var group = nodes[index].Group;
                if (!string.IsNullOrWhiteSpace(group) && !groupRank.ContainsKey(group))
                {
                    groupRank[group] = groupRank.Count;
                }
            }

            var ordered = column
                .Select((index, position) => (Index: index, Position: position))
                .OrderBy(entry => nodes[entry.Index].Group is { } group && groupRank.TryGetValue(group, out var rank)
                    ? rank
                    : -1)
                .ThenBy(entry => entry.Position)
                .Select(entry => nodes[entry.Index].Id)
                .ToList();

            result.Add(ordered);
        }

        return result;
    }

    /// <summary>
    /// Reorders one column by the barycenter of its neighbours in the reference column: a node connected to the top of
    /// the reference column moves up, and a node with no neighbour there keeps the position it already had.
    /// </summary>
    private static void Reorder(
        List<int> column,
        List<int> reference,
        Dictionary<int, List<int>> neighbours)
    {
        if (column.Count < 2 || reference.Count == 0)
        {
            return;
        }

        var positionInReference = new Dictionary<int, int>();
        for (var index = 0; index < reference.Count; index++)
        {
            positionInReference[reference[index]] = index;
        }

        var keyed = new List<(int Index, double Barycenter, int Current)>(column.Count);
        for (var index = 0; index < column.Count; index++)
        {
            var node = column[index];
            var sum = 0d;
            var count = 0;
            foreach (var neighbour in neighbours[node])
            {
                if (positionInReference.TryGetValue(neighbour, out var position))
                {
                    sum += position;
                    count++;
                }
            }

            keyed.Add((node, count == 0 ? index : sum / count, index));
        }

        var ordered = keyed
            .OrderBy(entry => entry.Barycenter)
            .ThenBy(entry => entry.Current)
            .Select(entry => entry.Index)
            .ToList();

        column.Clear();
        column.AddRange(ordered);
    }

    /// <summary>Places every node: the column grid, the vertical centring of each column and the host's pinned boxes.</summary>
    private static Dictionary<string, AgentGraphNodePlacement> PlaceNodes(
        List<AgentGraphNode> nodes,
        List<List<string>> columns,
        Dictionary<string, int> indexById,
        AgentGraphLayoutOptions settings,
        double nodeWidth,
        double nodeHeight,
        double horizontalGap,
        double verticalGap,
        double margin)
    {
        var placements = new Dictionary<string, AgentGraphNodePlacement>(StringComparer.Ordinal);

        // One pass per column: the row offsets (a group RUN keeps GroupGapPx of air from the neighbour above it, so its
        // box never looks like it swallowed that neighbour) and the column's own height.
        var offsets = new List<double[]>(columns.Count);
        var tallest = 0d;
        foreach (var column in columns)
        {
            var rowOffsets = new double[column.Count];
            var cursor = 0d;
            for (var row = 0; row < column.Count; row++)
            {
                if (row > 0 && GroupChanged(column[row - 1], column[row], nodes, indexById))
                {
                    cursor += GroupGapPx;
                }

                rowOffsets[row] = cursor;
                cursor += nodeHeight + verticalGap;
            }

            var height = column.Count == 0 ? 0 : cursor - verticalGap;
            offsets.Add(rowOffsets);
            tallest = Math.Max(tallest, height);
        }

        for (var layer = 0; layer < columns.Count; layer++)
        {
            var column = columns[layer];
            var height = column.Count == 0 ? 0 : offsets[layer][^1] + nodeHeight;
            var top = margin + ((tallest - height) / 2);

            for (var row = 0; row < column.Count; row++)
            {
                var node = nodes[indexById[column[row]]];
                var pinned = settings.NodePositions is { } positions
                             && positions.TryGetValue(node.Id, out var position)
                             && position is not null;

                placements[node.Id] = new AgentGraphNodePlacement
                {
                    Node = node,
                    Layer = layer,
                    RowIndex = row,
                    X = pinned ? settings.NodePositions![node.Id].X : margin + (layer * (nodeWidth + horizontalGap)),
                    Y = pinned ? settings.NodePositions![node.Id].Y : top + offsets[layer][row],
                    WidthPx = nodeWidth,
                    HeightPx = nodeHeight,
                    IsPinned = pinned,
                };
            }
        }

        return placements;
    }

    /// <summary>Whether two neighbouring rows of one column belong to DIFFERENT groups (either side may be ungrouped).</summary>
    private static bool GroupChanged(
        string upper,
        string lower,
        List<AgentGraphNode> nodes,
        Dictionary<string, int> indexById)
    {
        var upperGroup = nodes[indexById[upper]].Group;
        var lowerGroup = nodes[indexById[lower]].Group;
        return !string.Equals(upperGroup ?? string.Empty, lowerGroup ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>Builds the cubic curve of every connection and the width its call count earned.</summary>
    private static List<AgentGraphEdgePath> BuildPaths(
        List<AgentGraphEdge> edges,
        Dictionary<string, AgentGraphNodePlacement> placements,
        int maxCallCount)
    {
        var paths = new List<AgentGraphEdgePath>(edges.Count);
        foreach (var edge in edges)
        {
            var from = placements[edge.From];
            var to = placements[edge.To];
            var selfLoop = string.Equals(edge.From, edge.To, StringComparison.Ordinal);
            var forward = !selfLoop && to.X > from.X + 1;

            string path;
            if (selfLoop)
            {
                path = Curve(
                    from.CenterX - (from.WidthPx / 4), from.Bottom,
                    from.CenterX - CurveDepthPx, from.Bottom + CurveDepthPx,
                    from.CenterX + CurveDepthPx, from.Bottom + CurveDepthPx,
                    from.CenterX + (from.WidthPx / 4), from.Bottom);
            }
            else if (forward)
            {
                path = Curve(
                    from.Right, from.CenterY,
                    from.Right + ((to.X - from.Right) / 2), from.CenterY,
                    to.X - ((to.X - from.Right) / 2), to.CenterY,
                    to.X, to.CenterY);
            }
            else
            {
                var depth = Math.Max(from.Bottom, to.Bottom) + CurveDepthPx;
                path = Curve(
                    from.CenterX, from.Bottom,
                    from.CenterX, depth,
                    to.CenterX, depth,
                    to.CenterX, to.Bottom);
            }

            paths.Add(new AgentGraphEdgePath
            {
                Edge = edge,
                From = from,
                To = to,
                Path = path,
                StrokeWidthPx = EdgeWidth(edge.CallCount, maxCallCount),
                IsBackEdge = selfLoop || !forward,
            });
        }

        return paths;
    }

    /// <summary>One move plus one cubic segment, in invariant culture so the path parses in every locale.</summary>
    private static string Curve(
        double x0, double y0,
        double x1, double y1,
        double x2, double y2,
        double x3, double y3)
    {
        var builder = new StringBuilder("M ");
        Append(builder, x0);
        builder.Append(' ');
        Append(builder, y0);
        builder.Append(" C ");
        Append(builder, x1);
        builder.Append(' ');
        Append(builder, y1);
        builder.Append(' ');
        Append(builder, x2);
        builder.Append(' ');
        Append(builder, y2);
        builder.Append(' ');
        Append(builder, x3);
        builder.Append(' ');
        Append(builder, y3);
        return builder.ToString();
    }

    /// <summary>Appends a coordinate that never carries a locale separator.</summary>
    private static void Append(StringBuilder builder, double value)
        => builder.Append((double.IsFinite(value) ? value : 0).ToString("0.##", CultureInfo.InvariantCulture));

    /// <summary>The labelled box around every group, in the order the groups first appear while painting.</summary>
    private static List<AgentGraphGroupBox> BuildGroups(
        Dictionary<string, AgentGraphNodePlacement> placements,
        double groupPadding)
    {
        var order = new List<string>();
        var members = new Dictionary<string, List<AgentGraphNodePlacement>>(StringComparer.Ordinal);

        foreach (var placement in placements.Values
                     .OrderBy(placement => placement.Layer)
                     .ThenBy(placement => placement.Y))
        {
            if (placement.Node.Group is not { } group || string.IsNullOrWhiteSpace(group))
            {
                continue;
            }

            if (!members.TryGetValue(group, out var list))
            {
                list = [];
                members[group] = list;
                order.Add(group);
            }

            list.Add(placement);
        }

        var boxes = new List<AgentGraphGroupBox>(order.Count);
        foreach (var group in order)
        {
            var list = members[group];
            var left = list.Min(placement => placement.X) - groupPadding;
            var top = list.Min(placement => placement.Y) - groupPadding;
            var right = list.Max(placement => placement.Right) + groupPadding;
            var bottom = list.Max(placement => placement.Bottom) + groupPadding;

            boxes.Add(new AgentGraphGroupBox
            {
                Group = group,
                X = Math.Max(0, left),
                Y = Math.Max(0, top),
                WidthPx = Math.Max(1, right - Math.Max(0, left)),
                HeightPx = Math.Max(1, bottom - Math.Max(0, top)),
                Nodes = list,
            });
        }

        return boxes;
    }

    /// <summary>A positive, finite pixel value, or the fallback.</summary>
    private static double Positive(double value, double fallback)
        => double.IsFinite(value) && value > 0 ? value : fallback;
}
