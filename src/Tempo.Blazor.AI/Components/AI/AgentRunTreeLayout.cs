using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// One VISIBLE row of <c>TmAgentRunTree</c>: the node, its place in the rendered tree and everything the row
/// needs to paint itself (depth, disclosure state, orphan mark and the subtree roll-up).
/// </summary>
internal sealed record AgentRunTreeRow
{
    /// <summary>The node the row renders.</summary>
    public required AgentRunNode Node { get; init; }

    /// <summary>The row's position among the VISIBLE rows; the roving-tabindex slot and a stable order key.</summary>
    public required int Index { get; init; }

    /// <summary>The tree depth; a root is 0.</summary>
    public required int Depth { get; init; }

    /// <summary>Whether the node has at least one supplied child, so the row renders a disclosure.</summary>
    public required bool HasChildren { get; init; }

    /// <summary>Whether the node's children are currently hidden by the collapse state.</summary>
    public required bool IsCollapsed { get; init; }

    /// <summary>
    /// Whether the node hangs under the root because its parent could not be resolved (missing from the
    /// list, or rejected as a cycle member) rather than because it is a genuine root.
    /// </summary>
    public required bool IsOrphan { get; init; }

    /// <summary>The node's own numbers plus every descendant's, as the row's metric cells show them.</summary>
    public required AgentRunRollupResult Rollup { get; init; }

    /// <summary>Whether <see cref="Rollup"/> covers descendants, i.e. the row shows a subtree total.</summary>
    public bool IsSummary => Rollup.IsSummary;

    /// <summary>The 1-based <c>aria-level</c> of the row.</summary>
    public int Level => Depth + 1;
}

/// <summary>
/// The result of one <see cref="AgentRunTreeLayout.Build"/> pass: the visible rows in pre-order plus the
/// counters the component reports (how much the filter or the collapse state hid, and how much of the host's
/// input was malformed).
/// </summary>
internal sealed record AgentRunTreeLayoutResult
{
    /// <summary>The visible rows in pre-order (every parent before its children).</summary>
    public IReadOnlyList<AgentRunTreeRow> Rows { get; init; } = [];

    /// <summary>How many nodes the tree has once duplicates and cycle members are excluded.</summary>
    public int TotalNodeCount { get; init; }

    /// <summary>How many nodes the filter or the collapse state keeps out of <see cref="Rows"/>.</summary>
    public int HiddenNodeCount => TotalNodeCount - Rows.Count;

    /// <summary>How many roots are orphans (their parent id names no usable node).</summary>
    public int OrphanCount { get; init; }

    /// <summary>How many nodes were rejected because they take part in a parent cycle.</summary>
    public int RejectedCycleCount => RejectedNodeIds.Count;

    /// <summary>The ids rejected as cycle members, in ordinal order (deterministic for tests and hosts).</summary>
    public IReadOnlyList<string> RejectedNodeIds { get; init; } = [];

    /// <summary>How many extra nodes carried an id another node already had and were dropped.</summary>
    public int DuplicateCount { get; init; }
}

/// <summary>
/// Turns a FLAT <see cref="AgentRunNode"/> list into the rendered forest: resolves parents, attaches orphans
/// under the root, rejects parent cycles, drops duplicate ids, applies the <see cref="AgentRunFilter"/> and
/// the collapse set, and sums every node's subtree.
/// <para>
/// The walk never recurses (a deep run is legal) and never loops on a malformed graph: every pass carries a
/// visited set, and the cycle pass colours each chain once. A filter keeps an ancestor of a matching node, so
/// "only failed" still shows the chain that led to the failure.
/// </para>
/// </summary>
internal static class AgentRunTreeLayout
{
    /// <summary>Builds the rendered forest of one render.</summary>
    /// <param name="nodes">The host's flat run. Null is treated as an empty run.</param>
    /// <param name="filter">Which nodes the reader asked to see.</param>
    /// <param name="collapsed">The ids the reader collapsed.</param>
    /// <param name="now">The current moment, used to time a node that never finished.</param>
    /// <returns>The visible rows and the counters the component reports.</returns>
    public static AgentRunTreeLayoutResult Build(
        IEnumerable<AgentRunNode>? nodes,
        AgentRunFilter filter,
        IReadOnlySet<string> collapsed,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(collapsed);

        var (unique, duplicateCount) = Unique(nodes);
        var byId = new Dictionary<string, AgentRunNode>(unique.Count, StringComparer.Ordinal);
        foreach (var node in unique)
        {
            byId[node.Id] = node;
        }

        var rejected = FindCycleMembers(unique, byId);
        var children = new Dictionary<string, List<AgentRunNode>>(StringComparer.Ordinal);
        var roots = new List<(AgentRunNode Node, bool IsOrphan)>();
        var orphanCount = 0;

        foreach (var node in unique)
        {
            if (rejected.Contains(node.Id))
            {
                continue;
            }

            var parentId = ResolveParentId(node);
            if (parentId is not null
                && byId.TryGetValue(parentId, out var parent)
                && !rejected.Contains(parent.Id)
                && !string.Equals(parent.Id, node.Id, StringComparison.Ordinal))
            {
                if (!children.TryGetValue(parent.Id, out var list))
                {
                    list = [];
                    children[parent.Id] = list;
                }

                list.Add(node);
                continue;
            }

            // A blank parent id is a genuine root; anything else that did not resolve means the parent is
            // missing from the run (or was rejected), which makes this node an orphan.
            var isOrphan = parentId is not null;
            if (isOrphan)
            {
                orphanCount++;
            }

            roots.Add((node, isOrphan));
        }

        var rollups = AgentRunRollup.ForAll(unique, now);

        // Pre-order of the whole forest, used both by the filter's keep pass and by the flattening. The
        // keep pass walks it BACKWARDS, so a child's verdict is known before its parent reads it: a parent
        // survives when it matches itself or when a surviving descendant needs it as its ancestor.
        var keep = new Dictionary<string, bool>(unique.Count, StringComparer.Ordinal);
        foreach (var id in PreOrder(roots, children).Reverse())
        {
            var node = byId[id];
            var matches = filter switch
            {
                AgentRunFilter.Running => node.Status == AgentStepStatus.Running,
                AgentRunFilter.Failed => node.Status == AgentStepStatus.Failed,
                _ => true,
            };

            var keepSelf = matches;
            if (!keepSelf && children.TryGetValue(id, out var kids))
            {
                keepSelf = kids.Any(kid => keep.TryGetValue(kid.Id, out var kept) && kept);
            }

            keep[id] = keepSelf;
        }

        var rows = new List<AgentRunTreeRow>();
        var stack = new Stack<(AgentRunNode Node, int Depth, bool IsOrphan)>();
        for (var index = roots.Count - 1; index >= 0; index--)
        {
            stack.Push((roots[index].Node, 0, roots[index].IsOrphan));
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (stack.Count > 0)
        {
            var (node, depth, isOrphan) = stack.Pop();
            if (!visited.Add(node.Id) || (keep.TryGetValue(node.Id, out var kept) && !kept))
            {
                continue;
            }

            var hasChildren = children.TryGetValue(node.Id, out var nodeChildren) && nodeChildren.Count > 0;
            var isCollapsed = hasChildren && collapsed.Contains(node.Id);

            rows.Add(new AgentRunTreeRow
            {
                Node = node,
                Index = rows.Count,
                Depth = depth,
                HasChildren = hasChildren,
                IsCollapsed = isCollapsed,
                IsOrphan = isOrphan,
                Rollup = rollups.TryGetValue(node.Id, out var rollup) ? rollup : AgentRunRollupResult.Empty,
            });

            if (!hasChildren || isCollapsed)
            {
                continue;
            }

            for (var index = nodeChildren!.Count - 1; index >= 0; index--)
            {
                var kid = nodeChildren[index];
                if (!visited.Contains(kid.Id))
                {
                    stack.Push((kid, depth + 1, false));
                }
            }
        }

        return new AgentRunTreeLayoutResult
        {
            Rows = rows,
            TotalNodeCount = unique.Count - rejected.Count,
            OrphanCount = orphanCount,
            RejectedNodeIds = rejected.OrderBy(id => id, StringComparer.Ordinal).ToList(),
            DuplicateCount = duplicateCount,
        };
    }

    /// <summary>The nodes by id, the FIRST node with an id winning, plus the number of dropped
    /// duplicates.</summary>
    private static (List<AgentRunNode> Unique, int Duplicates) Unique(IEnumerable<AgentRunNode>? nodes)
    {
        var unique = new List<AgentRunNode>();
        if (nodes is null)
        {
            return (unique, 0);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = 0;
        foreach (var node in nodes)
        {
            if (node is null || string.IsNullOrWhiteSpace(node.Id))
            {
                continue;
            }

            if (seen.Add(node.Id))
            {
                unique.Add(node);
            }
            else
            {
                duplicates++;
            }
        }

        return (unique, duplicates);
    }

    /// <summary>
    /// The ids of every node that takes part in a parent cycle. Each chain is walked once with a
    /// three-colour state map: entering a node the CURRENT walk already entered means the stack from that
    /// node onward is the cycle. Nothing recurses, so a deep run cannot overflow the stack and a cycle
    /// cannot loop.
    /// </summary>
    private static HashSet<string> FindCycleMembers(
        IReadOnlyList<AgentRunNode> nodes,
        IReadOnlyDictionary<string, AgentRunNode> byId)
    {
        const int unvisited = 0;
        const int inProgress = 1;
        const int done = 2;
        var state = new Dictionary<string, int>(nodes.Count, StringComparer.Ordinal);
        var cycles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var start in nodes)
        {
            if (state.ContainsKey(start.Id))
            {
                continue;
            }

            var path = new List<string>();
            var current = start.Id;
            while (current is not null)
            {
                if (!byId.TryGetValue(current, out var node))
                {
                    break; // a parent that does not exist terminates the walk
                }

                if (state.TryGetValue(current, out var colour))
                {
                    if (colour == inProgress)
                    {
                        var from = path.IndexOf(current);
                        for (var index = from; index >= 0 && index < path.Count; index++)
                        {
                            cycles.Add(path[index]);
                        }
                    }

                    break;
                }

                state[current] = inProgress;
                path.Add(current);
                current = ResolveParentId(node);
            }

            foreach (var id in path)
            {
                state[id] = done;
            }
        }

        return cycles;
    }

    /// <summary>The pre-order of the whole forest, without recursing.</summary>
    private static IEnumerable<string> PreOrder(
        IReadOnlyList<(AgentRunNode Node, bool IsOrphan)> roots,
        IReadOnlyDictionary<string, List<AgentRunNode>> children)
    {
        var order = new List<string>();
        var stack = new Stack<AgentRunNode>();
        for (var index = roots.Count - 1; index >= 0; index--)
        {
            stack.Push(roots[index].Node);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node.Id))
            {
                continue;
            }

            order.Add(node.Id);
            if (!children.TryGetValue(node.Id, out var nodeChildren))
            {
                continue;
            }

            for (var index = nodeChildren.Count - 1; index >= 0; index--)
            {
                stack.Push(nodeChildren[index]);
            }
        }

        return order;
    }

    /// <summary>The parent a node names, or null for a genuine root and for a self-parent.</summary>
    private static string? ResolveParentId(AgentRunNode node)
        => string.IsNullOrWhiteSpace(node.ParentId)
           || string.Equals(node.ParentId, node.Id, StringComparison.Ordinal)
            ? null
            : node.ParentId;
}
