namespace Tempo.Blazor.AI.Models;

/// <summary>
/// Subtree arithmetic over a flat <see cref="AgentRunNode"/> list: the cost, the tokens and the elapsed time
/// of one node and everything under it.
/// <para>
/// The helper is deliberately public and PURE: <c>TmAgentRunTree</c> uses it for its parent rows, and a host
/// uses it for a detail panel, a header total or a server-side report — so the number a reader sees on the
/// screen and the number a host computes cannot drift.
/// </para>
/// <para>
/// The input is treated as untrusted. A duplicate id is read once (the first node with that id wins, exactly
/// like the tree's duplicate rule), a node whose parent is missing still counts under its own root, and a
/// parent CYCLE cannot loop: every walk carries a visited set, so a malformed run yields a finite answer
/// instead of a hang.
/// </para>
/// </summary>
public static class AgentRunRollup
{
    /// <summary>
    /// The roll-up of one node: its own numbers plus every descendant reachable through
    /// <see cref="AgentRunNode.ParentId"/>, each node counted once.
    /// </summary>
    /// <param name="nodeId">The <see cref="AgentRunNode.Id"/> to sum. An id that names no node yields
    /// <see cref="AgentRunRollupResult.Empty"/>.</param>
    /// <param name="nodes">The flat run. A null argument throws.</param>
    /// <param name="now">The current moment, used to time a node that never finished.</param>
    /// <returns>The subtree's sums; nothing reported stays <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="nodes"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="nodeId"/> is null, empty or whitespace.</exception>
    public static AgentRunRollupResult For(string nodeId, IEnumerable<AgentRunNode> nodes, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(nodes);

        var byId = UniqueById(nodes);
        if (!byId.ContainsKey(nodeId))
        {
            return AgentRunRollupResult.Empty;
        }

        var children = ChildrenById(byId);

        var cost = 0L;
        var tokens = 0L;
        var duration = TimeSpan.Zero;
        var costReported = false;
        var tokensReported = false;
        var durationReported = false;
        var count = 0;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(nodeId);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!visited.Add(id) || !byId.TryGetValue(id, out var node))
            {
                continue;
            }

            count++;
            if (node.CostMicroUnits is { } nodeCost)
            {
                cost += nodeCost;
                costReported = true;
            }

            if (node.Tokens is { } nodeTokens)
            {
                tokens += nodeTokens;
                tokensReported = true;
            }

            if (node.Elapsed(now) is { } elapsed)
            {
                duration += elapsed;
                durationReported = true;
            }

            if (children.TryGetValue(id, out var kids))
            {
                for (var index = kids.Count - 1; index >= 0; index--)
                {
                    if (!visited.Contains(kids[index].Id))
                    {
                        stack.Push(kids[index].Id);
                    }
                }
            }
        }

        return new AgentRunRollupResult
        {
            CostMicroUnits = costReported ? cost : null,
            Tokens = tokensReported ? tokens : null,
            Duration = durationReported ? duration : null,
            NodeCount = count,
        };
    }

    /// <summary>
    /// One roll-up per supplied node, computed in a single pass over the forest (children before their
    /// parent), so a 500-node run costs one walk rather than one walk per row.
    /// </summary>
    /// <param name="nodes">The flat run. A null argument throws.</param>
    /// <param name="now">The current moment, used to time a node that never finished.</param>
    /// <returns>A map from <see cref="AgentRunNode.Id"/> to the node's subtree roll-up.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="nodes"/> is null.</exception>
    public static IReadOnlyDictionary<string, AgentRunRollupResult> ForAll(
        IEnumerable<AgentRunNode> nodes,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var byId = UniqueById(nodes);
        var children = ChildrenById(byId);

        // A spanning pre-order over the forest. Roots come first in input order; a node no root reaches
        // (a cycle member) starts its own walk, so every node ends up in `order` exactly once.
        var order = new List<string>(byId.Count);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var roots = byId.Values.Where(node => ResolveParentId(node) is null).ToList();

        void Walk(string startId)
        {
            var stack = new Stack<string>();
            stack.Push(startId);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (!visited.Add(id))
                {
                    continue;
                }

                order.Add(id);
                if (!children.TryGetValue(id, out var kids))
                {
                    continue;
                }

                for (var index = kids.Count - 1; index >= 0; index--)
                {
                    var kid = kids[index].Id;
                    if (!visited.Contains(kid))
                    {
                        stack.Push(kid);
                    }
                }
            }
        }

        foreach (var root in roots)
        {
            if (!visited.Contains(root.Id))
            {
                Walk(root.Id);
            }
        }

        foreach (var node in byId.Values)
        {
            if (!visited.Contains(node.Id))
            {
                Walk(node.Id);
            }
        }

        // A pre-order list visits a parent before its children, so walking it BACKWARDS means every
        // child's sums are complete when its parent reads them.
        var results = new Dictionary<string, AgentRunRollupResult>(byId.Count, StringComparer.Ordinal);
        foreach (var id in Enumerable.Reverse(order))
        {
            var node = byId[id];
            long? cost = node.CostMicroUnits;
            long? tokens = node.Tokens;
            var duration = node.Elapsed(now);
            var count = 1;

            if (children.TryGetValue(id, out var kids))
            {
                foreach (var kid in kids)
                {
                    if (!results.TryGetValue(kid.Id, out var child))
                    {
                        continue;
                    }

                    count += child.NodeCount;
                    cost = Sum(cost, child.CostMicroUnits);
                    tokens = Sum(tokens, child.Tokens);
                    duration = Sum(duration, child.Duration);
                }
            }

            results[id] = new AgentRunRollupResult
            {
                CostMicroUnits = cost,
                Tokens = tokens,
                Duration = duration,
                NodeCount = count,
            };
        }

        return results;
    }

    private static long? Sum(long? left, long? right)
        => left is { } a && right is { } b ? a + b : left ?? right;

    private static TimeSpan? Sum(TimeSpan? left, TimeSpan? right)
        => left is { } a && right is { } b ? a + b : left ?? right;

    /// <summary>The node a node hangs under, or null for a genuine root and for a self-parent.</summary>
    private static string? ResolveParentId(AgentRunNode node)
        => string.IsNullOrWhiteSpace(node.ParentId)
           || string.Equals(node.ParentId, node.Id, StringComparison.Ordinal)
            ? null
            : node.ParentId;

    /// <summary>
    /// The nodes by id, the FIRST node with an id winning: a second node with the same id would give the
    /// list two rows with one <c>@key</c> and the sums two readings of one fact.
    /// </summary>
    private static Dictionary<string, AgentRunNode> UniqueById(IEnumerable<AgentRunNode> nodes)
    {
        var byId = new Dictionary<string, AgentRunNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            // A null entry (a default(AgentRunNode) in a host's list) and a blank id are not addressable
            // nodes; skipping them keeps every lookup total.
            if (node is null || string.IsNullOrWhiteSpace(node.Id))
            {
                continue;
            }

            byId.TryAdd(node.Id, node);
        }

        return byId;
    }

    private static Dictionary<string, List<AgentRunNode>> ChildrenById(Dictionary<string, AgentRunNode> byId)
    {
        var children = new Dictionary<string, List<AgentRunNode>>(StringComparer.Ordinal);
        foreach (var node in byId.Values)
        {
            if (ResolveParentId(node) is not { } parentId || !byId.ContainsKey(parentId))
            {
                continue;
            }

            if (!children.TryGetValue(parentId, out var list))
            {
                list = [];
                children[parentId] = list;
            }

            list.Add(node);
        }

        return children;
    }
}
