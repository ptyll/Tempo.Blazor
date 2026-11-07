using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// The deterministic arrangement behind the editor's "Arrange" action (phase 4, decision
/// <c>MODELING-AUTHORING-CHANGESET</c> §9.4): it computes view geometry for the RENDERED elements of
/// one view and returns it as plain <see cref="Box"/> values — the editor commits them as ONE
/// undoable <c>set_view_node</c> transaction, so the arrangement is a normal, reversible edit.
/// <para>
/// The rules follow the BPMN visual convention instead of a general graph layout: a POOL and a LANE
/// are horizontal BANDS (their name in a left label band) stacked in sibling order, and the flow
/// nodes INSIDE a lane are ranked by their relationships — the source of a flow sits before its
/// target along the direction axis, and the targets of one branching node share a rank (so a
/// gateway's branches line up). A run with no relationship at all is packed into a square-ish grid
/// (<c>ceil(sqrt(n))</c> columns) so 300 unrelated nodes never stretch into one long strip. Ties
/// keep the model's declaration order, so the result is deterministic. A container is therefore
/// never moved out of its parent, and a lane keeps its vertical order inside the pool.
/// </para>
/// <para>
/// A pool's lanes are FLUSH (the BPMN convention): no content margin, no gap between lanes, and a
/// lane spans the pool's content width (pool width − label band), so the pool's bands form one
/// continuous grid.
/// </para>
/// <para>
/// General graph layout (dagre/force) is the embedded diagram editor's own feature and stays in the
/// browser; this helper only owns the band arrangement a pool/lane hierarchy needs, which the
/// browser cannot do because the modeling preview is a read-only projection.
/// </para>
/// </summary>
internal static class ModelingLayoutArranger
{
    /// <summary>Origin of an arranged scene.</summary>
    internal const double OriginX = 40;
    internal const double OriginY = 40;

    /// <summary>Width of the left label band of a pool/lane (BPMN convention).</summary>
    internal const double LabelBandWidth = 30;

    /// <summary>Margin between a container edge and its content.</summary>
    internal const double ContentMargin = 20;

    /// <summary>Gap between two flow nodes laid along the direction axis. Wide enough that a
    /// relationship LABEL (BPMN edge names are long) fits between two nodes.</summary>
    internal const double ChildGap = 80;

    /// <summary>Gap between two stacked container bands (lanes of one pool). Zero: BPMN lanes are
    /// FLUSH inside their pool, so the bands read as one grid.</summary>
    internal const double BandGap = 0;

    /// <summary>Gap between two ROOT-level bands (pools of the scene).</summary>
    internal const double RootBandGap = 16;

    private const double MinimumNodeWidth = 80;
    private const double MinimumNodeHeight = 50;
    private const double MinimumContainerWidth = 240;
    private const double MinimumLaneHeight = 90;
    private const double MinimumPoolHeight = 120;

    /// <summary>The axis along which the flow nodes of one container are laid out.</summary>
    internal enum Direction
    {
        /// <summary>Left to right (BPMN default): the flow nodes of a lane form a row.</summary>
        LeftToRight,

        /// <summary>Top to bottom: the flow nodes of a lane form a column.</summary>
        TopToBottom
    }

    /// <summary>The arranged geometry of ONE element.</summary>
    /// <param name="ElementId">Model element id.</param>
    /// <param name="X">Left position.</param>
    /// <param name="Y">Top position.</param>
    /// <param name="Width">Width.</param>
    /// <param name="Height">Height.</param>
    internal sealed record Box(string ElementId, double X, double Y, double Width, double Height);

    /// <summary>
    /// Arranges the rendered elements of one view.
    /// </summary>
    /// <param name="elements">The model's elements (declaration order is the sibling order and the
    /// tie-breaker of a rank).</param>
    /// <param name="documentNodes">The generated nodes of the view — the source of the leaf sizes
    /// and of the pool/lane band knowledge (<see cref="DiagramNode.SwimlaneData"/>).</param>
    /// <param name="relationships">The model's relationships — they rank the flow nodes of a run
    /// (a relationship whose endpoints are not siblings of the same run is ignored).</param>
    /// <param name="isContainer">Which element types CONTAIN others (the notation profile's
    /// <see cref="IModelingNotationProfile.ContainerElementTypes"/> — the editor owns no vocabulary).</param>
    /// <param name="direction">Layout direction of the flow nodes inside a container.</param>
    /// <returns>One box per rendered element, containers first (parents before children).</returns>
    internal static IReadOnlyList<Box> Arrange(
        IReadOnlyList<ModelingElementDto> elements,
        IReadOnlyList<DiagramNode> documentNodes,
        IReadOnlyList<ModelingRelationshipDto> relationships,
        Func<string, bool> isContainer,
        Direction direction)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(documentNodes);
        ArgumentNullException.ThrowIfNull(relationships);
        ArgumentNullException.ThrowIfNull(isContainer);

        var nodeByElementId = new Dictionary<string, DiagramNode>(StringComparer.Ordinal);
        foreach (var node in documentNodes)
        {
            var elementId = ElementIdOf(node);
            if (!string.IsNullOrEmpty(elementId) && !nodeByElementId.ContainsKey(elementId!))
            {
                nodeByElementId[elementId!] = node;
            }
        }

        var childrenByParent = new Dictionary<string, List<ModelingElementDto>>(StringComparer.Ordinal);
        var roots = new List<ModelingElementDto>();
        foreach (var element in elements)
        {
            if (!nodeByElementId.ContainsKey(element.Id))
            {
                continue;
            }

            var parentId = element.ParentId ?? string.Empty;
            if (!string.IsNullOrEmpty(parentId)
                && !string.Equals(parentId, element.Id, StringComparison.Ordinal)
                && nodeByElementId.ContainsKey(parentId))
            {
                if (!childrenByParent.TryGetValue(parentId, out var siblings))
                {
                    siblings = [];
                    childrenByParent[parentId] = siblings;
                }

                siblings.Add(element);
                continue;
            }

            roots.Add(element);
        }

        var context = new ArrangeContext(nodeByElementId, childrenByParent, relationships, isContainer, direction);
        var boxes = new List<Box>(nodeByElementId.Count);
        context.PlaceContent(roots, OriginX, OriginY, 0, flushBands: false, boxes);
        return boxes;
    }

    private static string? ElementIdOf(DiagramNode node)
        => node.Data is not null
            && node.Data.TryGetValue("modelElementId", out var value)
            ? value?.ToString()
            : null;

    /// <summary>Per-call state of one arrangement (the measured sizes cache keeps it O(n)).</summary>
    private sealed class ArrangeContext(
        Dictionary<string, DiagramNode> nodeByElementId,
        Dictionary<string, List<ModelingElementDto>> childrenByParent,
        IReadOnlyList<ModelingRelationshipDto> relationships,
        Func<string, bool> isContainer,
        Direction direction)
    {
        private readonly Dictionary<string, (double Width, double Height)> _measured = new(StringComparer.Ordinal);

        /// <summary>Whether the element RENDERS as a swimlane band (a pool/lane) — the document node
        /// carries swimlane data only for those stencils.</summary>
        private bool IsBand(string elementId)
            => nodeByElementId.TryGetValue(elementId, out var node) && node.SwimlaneData is not null;

        private bool IsContainerElement(string elementId)
            => isContainer(elementId) || childrenByParent.ContainsKey(elementId);

        /// <summary>The natural size of one element (a container is measured from its content).</summary>
        private (double Width, double Height) Measure(string elementId)
        {
            if (_measured.TryGetValue(elementId, out var cached))
            {
                return cached;
            }

            var size = MeasureCore(elementId);
            _measured[elementId] = size;
            return size;
        }

        private (double Width, double Height) MeasureCore(string elementId)
        {
            var node = nodeByElementId[elementId];
            var leafWidth = Math.Max(MinimumNodeWidth, node.W);
            var leafHeight = Math.Max(MinimumNodeHeight, node.H);

            if (!IsContainerElement(elementId))
            {
                return (leafWidth, leafHeight);
            }

            var children = childrenByParent.GetValueOrDefault(elementId) ?? [];
            var bands = children.Where(child => IsBand(child.Id)).ToList();
            var axis = children.Where(child => !IsBand(child.Id)).ToList();

            if (IsBand(elementId))
            {
                // A POOL of lanes is a flush band stack: no margin, no gap, and the pool is as wide
                // as its label band plus the widest lane (a lane spans the pool's content width).
                if (bands.Count > 0)
                {
                    var (stackWidth, stackHeight) = MeasureFlushStack(bands);
                    return (LabelBandWidth + stackWidth, Math.Max(MinimumPoolHeight, stackHeight));
                }

                // A LANE holds a flow-node run after its own label band and content margin.
                var (axisWidth, axisHeight) = MeasureRun(axis);
                return (
                    Math.Max(MinimumContainerWidth, LabelBandWidth + (2 * ContentMargin) + axisWidth),
                    Math.Max(MinimumLaneHeight, (2 * ContentMargin) + axisHeight));
            }

            var (runWidth, runHeight) = MeasureRun(axis);
            var (bandWidth, bandHeight) = MeasureStack(bands, BandGap);
            var gap = axis.Count > 0 && bands.Count > 0 ? ChildGap : 0;
            return (
                Math.Max(leafWidth, Math.Max(runWidth, bandWidth)),
                Math.Max(MinimumNodeHeight, runHeight + gap + bandHeight));
        }

        /// <summary>The extent of a FLUSH band stack (the lanes of a pool): zero gap.</summary>
        private (double Width, double Height) MeasureFlushStack(IReadOnlyList<ModelingElementDto> bands)
        {
            var widest = 0d;
            var total = 0d;
            foreach (var band in bands)
            {
                var (width, height) = Measure(band.Id);
                widest = Math.Max(widest, width);
                total += height;
            }

            return (widest, total);
        }

        /// <summary>
        /// The extent of one run of non-band siblings. The nodes are RANKED by their relationships
        /// (source before target, ties in declaration order); a run without a single relationship is
        /// packed into a square-ish grid so it never grows into one long strip.
        /// </summary>
        private (double Width, double Height) MeasureRun(IReadOnlyList<ModelingElementDto> children)
        {
            var slots = BuildRunSlots(children);
            if (slots.Count == 0)
            {
                return (0, 0);
            }

            var (columnWidths, rowHeights) = MeasureSlots(slots);
            var width = columnWidths.Sum() + (ChildGap * Math.Max(0, columnWidths.Count - 1));
            var height = rowHeights.Sum() + (ChildGap * Math.Max(0, rowHeights.Count - 1));
            return (width, height);
        }

        private (List<double> ColumnWidths, List<double> RowHeights) MeasureSlots(IReadOnlyList<RunSlot> slots)
        {
            var columns = slots.Max(slot => slot.Column) + 1;
            var rows = slots.Max(slot => slot.Row) + 1;
            var columnWidths = Enumerable.Repeat(0d, columns).ToList();
            var rowHeights = Enumerable.Repeat(0d, rows).ToList();
            foreach (var slot in slots)
            {
                var (width, height) = Measure(slot.Element.Id);
                columnWidths[slot.Column] = Math.Max(columnWidths[slot.Column], width);
                rowHeights[slot.Row] = Math.Max(rowHeights[slot.Row], height);
            }

            return (columnWidths, rowHeights);
        }

        /// <summary>
        /// The extent of stacked bands (the lanes of a pool, or the root-level pools), always
        /// vertical.
        /// </summary>
        private (double Width, double Height) MeasureStack(IReadOnlyList<ModelingElementDto> bands, double gap)
        {
            if (bands.Count == 0)
            {
                return (0, 0);
            }

            var total = gap * (bands.Count - 1);
            var widest = 0d;
            foreach (var band in bands)
            {
                var (width, height) = Measure(band.Id);
                widest = Math.Max(widest, width);
                total += height;
            }

            return (widest, total);
        }

        /// <summary>
        /// Places ONE container's content (or the scene's roots): the flow-node run along the
        /// direction axis first, then the bands stacked below it in sibling order. A pool's lanes are
        /// FLUSH (no margin, no gap, the full content width); the root scene and a non-band container
        /// keep the band gap.
        /// </summary>
        internal void PlaceContent(
            IReadOnlyList<ModelingElementDto> children,
            double x,
            double y,
            double availableWidth,
            bool flushBands,
            List<Box> boxes)
        {
            if (children.Count == 0)
            {
                return;
            }

            var bands = children.Where(child => IsBand(child.Id)).ToList();
            var axis = children.Where(child => !IsBand(child.Id)).ToList();
            var (axisWidth, axisHeight) = PlaceRun(axis, x, y, boxes);
            var bandsY = y + (axis.Count > 0 ? axisHeight + ChildGap : 0);

            // A container's bands fill its content width; the SCENE root (no width yet — every root
            // band is a pool with its own measured size) falls back to the widest band, so a root
            // pool never collapses to a zero-width rectangle.
            var stackWidth = MeasureStack(bands, flushBands ? BandGap : RootBandGap).Width;
            var bandWidth = availableWidth > 0
                ? Math.Max(axisWidth, Math.Max(stackWidth, availableWidth))
                : Math.Max(axisWidth, stackWidth);
            PlaceStack(bands, x, bandsY, bandWidth, flushBands ? BandGap : RootBandGap, boxes);
        }

        /// <summary>Places a run of siblings at (<paramref name="x"/>, <paramref name="y"/>) along
        /// the direction axis.</summary>
        private (double Width, double Height) PlaceRun(
            IReadOnlyList<ModelingElementDto> children,
            double x,
            double y,
            List<Box> boxes)
        {
            var slots = BuildRunSlots(children);
            if (slots.Count == 0)
            {
                return (0, 0);
            }

            var (columnWidths, rowHeights) = MeasureSlots(slots);
            var columnOffsets = Offsets(columnWidths, ChildGap);
            var rowOffsets = Offsets(rowHeights, ChildGap);
            foreach (var slot in slots)
            {
                var (slotWidth, slotHeight) = Measure(slot.Element.Id);
                Place(
                    slot.Element.Id,
                    x + columnOffsets[slot.Column],
                    y + rowOffsets[slot.Row],
                    slotWidth,
                    slotHeight,
                    boxes);
            }

            return (
                columnOffsets[^1] + columnWidths[^1],
                rowOffsets[^1] + rowHeights[^1]);
        }

        /// <summary>Places stacked bands below the flow-node run of their container.</summary>
        private (double Width, double Height) PlaceStack(
            IReadOnlyList<ModelingElementDto> bands,
            double x,
            double y,
            double width,
            double gap,
            List<Box> boxes)
        {
            if (bands.Count == 0)
            {
                return (0, 0);
            }

            var cursorY = y;
            var total = gap * (bands.Count - 1);
            foreach (var band in bands)
            {
                var (_, height) = Measure(band.Id);
                Place(band.Id, x, cursorY, width, height, boxes);
                cursorY += height + gap;
                total += height;
            }

            return (width, total);
        }

        /// <summary>Emits one element's box and recurses into its content.</summary>
        private void Place(string elementId, double x, double y, double width, double height, List<Box> boxes)
        {
            boxes.Add(new Box(elementId, x, y, width, height));

            if (!childrenByParent.TryGetValue(elementId, out var children) || children.Count == 0)
            {
                return;
            }

            var isBand = IsBand(elementId);
            var band = isBand ? LabelBandWidth : 0;
            var flushBands = isBand && children.Any(child => IsBand(child.Id));
            var contentX = x + band + (flushBands ? 0 : ContentMargin);
            var contentY = y + (flushBands ? 0 : ContentMargin);
            var contentWidth = Math.Max(0, width - band - (flushBands ? 0 : 2 * ContentMargin));
            PlaceContent(children, contentX, contentY, contentWidth, flushBands, boxes);
        }

        // ── Rank assignment ──────────────────────────────────────────────────

        /// <summary>Where one child of a run sits in the run's grid (column along the direction
        /// axis, row on the cross axis — swapped for <see cref="Direction.TopToBottom"/>).</summary>
        private sealed record RunSlot(ModelingElementDto Element, int Column, int Row);

        /// <summary>
        /// Assigns a rank to every child of one run: a LONGEST-PATH rank over the relationships whose
        /// two endpoints are siblings of this run. Back edges (a cycle) are ignored, so a malformed
        /// model still arranges; equal ranks keep the declaration order (the caller's order).
        /// A run with no relationship is packed into <c>ceil(sqrt(n))</c> columns.
        /// </summary>
        private List<RunSlot> BuildRunSlots(IReadOnlyList<ModelingElementDto> children)
        {
            if (children.Count == 0)
            {
                return [];
            }

            if (children.Count == 1)
            {
                return [new RunSlot(children[0], 0, 0)];
            }

            var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index < children.Count; index++)
            {
                indexById[children[index].Id] = index;
            }

            var outgoing = new List<int>[children.Count];
            for (var index = 0; index < children.Count; index++)
            {
                outgoing[index] = [];
            }

            var hasRelationship = false;
            foreach (var relationship in relationships)
            {
                if (!indexById.TryGetValue(relationship.SourceElementId ?? string.Empty, out var source)
                    || !indexById.TryGetValue(relationship.TargetElementId ?? string.Empty, out var target)
                    || source == target)
                {
                    continue;
                }

                outgoing[source].Add(target);
                hasRelationship = true;
            }

            if (!hasRelationship)
            {
                // No flow knowledge: pack the run into a square-ish grid (ceil(sqrt(n)) along the
                // direction axis) — 300 unrelated nodes stay a block, never a 300-node strip.
                var columns = (int)Math.Ceiling(Math.Sqrt(children.Count));
                var slots = new List<RunSlot>(children.Count);
                for (var index = 0; index < children.Count; index++)
                {
                    var column = index % columns;
                    var row = index / columns;
                    slots.Add(direction == Direction.LeftToRight
                        ? new RunSlot(children[index], column, row)
                        : new RunSlot(children[index], row, column));
                }

                return slots;
            }

            var ranks = LongestPathRanks(outgoing);
            var orderInRank = new Dictionary<int, int>();
            var ranked = new List<RunSlot>(children.Count);
            for (var index = 0; index < children.Count; index++)
            {
                var rank = ranks[index];
                var position = orderInRank.TryGetValue(rank, out var used) ? used : 0;
                orderInRank[rank] = position + 1;
                ranked.Add(direction == Direction.LeftToRight
                    ? new RunSlot(children[index], rank, position)
                    : new RunSlot(children[index], position, rank));
            }

            return ranked;
        }

        /// <summary>
        /// Longest-path ranks of one run. A DFS post-order yields a topological order of the DAG that
        /// remains after the BACK edges (an edge into the current path) are removed, so a malformed
        /// model with a cycle still arranges; the ranks are then relaxed in that order —
        /// <c>rank(target) = max(rank(target), rank(source) + 1)</c>. Declaration order breaks ties
        /// (the caller keeps the input order inside one rank).
        /// </summary>
        private static int[] LongestPathRanks(IReadOnlyList<List<int>> outgoing)
        {
            var count = outgoing.Count;
            var ranks = new int[count];
            var state = new int[count]; // 0 = new, 1 = on the current path, 2 = finished
            var backEdges = new HashSet<(int Source, int Target)>();
            var postOrder = new List<int>(count);

            for (var start = 0; start < count; start++)
            {
                if (state[start] != 0)
                {
                    continue;
                }

                var stack = new Stack<(int Node, int Edge)>();
                stack.Push((start, 0));
                state[start] = 1;
                while (stack.Count > 0)
                {
                    var (node, edge) = stack.Pop();
                    if (edge < outgoing[node].Count)
                    {
                        stack.Push((node, edge + 1));
                        var target = outgoing[node][edge];
                        if (state[target] == 1)
                        {
                            // The edge closes a cycle: ignoring it keeps the arrangement finite.
                            backEdges.Add((node, target));
                        }
                        else if (state[target] == 0)
                        {
                            state[target] = 1;
                            stack.Push((target, 0));
                        }

                        continue;
                    }

                    state[node] = 2;
                    postOrder.Add(node);
                }
            }

            postOrder.Reverse();
            foreach (var node in postOrder)
            {
                foreach (var target in outgoing[node])
                {
                    if (backEdges.Contains((node, target)))
                    {
                        continue;
                    }

                    ranks[target] = Math.Max(ranks[target], ranks[node] + 1);
                }
            }

            return ranks;
        }

        private static List<double> Offsets(IReadOnlyList<double> sizes, double gap)
        {
            var offsets = new List<double>(sizes.Count);
            var cursor = 0d;
            for (var index = 0; index < sizes.Count; index++)
            {
                offsets.Add(cursor);
                cursor += sizes[index] + gap;
            }

            return offsets;
        }
    }
}
