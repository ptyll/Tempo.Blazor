namespace Tempo.Blazor.AI.Models;

/// <summary>
/// Everything ONE agent changed on its own branch — the input of the conflict radar, one entry per agent.
/// <para>
/// The record is a VALUE the host builds from its workspace receipts: the agent, the branch (or worktree) name the
/// changes live on, and every path the agent touched. It carries no opinion about conflicts: whether two agents touch
/// the same file is a question the matrix answers from these lists, so a host never has to decide what a conflict is.
/// </para>
/// <para>
/// A path reported twice by the same agent collapses to ONE entry — the LAST one wins, because a receipt that lists a
/// file twice describes the newer fact second, and two cells for one file would make the matrix claim a conflict an
/// agent does not have. The host's path order is preserved otherwise, so a diagnostic reading of the list matches the
/// host's own receipt.
/// </para>
/// </summary>
public sealed record AgentBranchChanges
{
    /// <summary>Creates one agent's branch changes.</summary>
    /// <param name="agent">The agent whose branch this is. Required.</param>
    /// <param name="branch">The branch (or worktree) the changes live on, as the host names it. Null or whitespace is
    /// stored as an empty string, which a surface renders through its localized fallback instead of an empty gap.</param>
    /// <param name="changedPaths">The paths the agent touched, in the host's order. Null reads as an empty list; a
    /// path listed twice keeps its LAST entry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is null.</exception>
    public AgentBranchChanges(AgentRef agent, string branch, IEnumerable<ChangedPath>? changedPaths = null)
    {
        Agent = agent ?? throw new ArgumentNullException(nameof(agent));
        Branch = branch ?? string.Empty;
        ChangedPaths = Collapse(changedPaths);
    }

    /// <summary>The agent the branch belongs to.</summary>
    public AgentRef Agent { get; init; }

    /// <summary>The branch (or worktree) name the host reported. Empty when the host named none.</summary>
    public string Branch { get; init; }

    /// <summary>The paths the agent touched, de-duplicated by path and in the host's order.</summary>
    public IReadOnlyList<ChangedPath> ChangedPaths { get; init; }

    /// <summary>Whether the host named the branch.</summary>
    public bool HasBranch => !string.IsNullOrWhiteSpace(Branch);

    /// <summary>Whether the agent changed anything at all (an agent with no changes is not a matrix column).</summary>
    public bool HasChanges => ChangedPaths.Count > 0;

    /// <summary>The agent's change to one path, or null when the agent did not touch it.</summary>
    /// <param name="path">The path to look for, compared as <see cref="ChangedPath.Normalize"/> normalised it.</param>
    /// <returns>The change, or null.</returns>
    public ChangedPath? Find(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = ChangedPath.Normalize(path!);
        foreach (var change in ChangedPaths)
        {
            if (string.Equals(change.Path, normalized, StringComparison.Ordinal))
            {
                return change;
            }
        }

        return null;
    }

    private static IReadOnlyList<ChangedPath> Collapse(IEnumerable<ChangedPath>? changedPaths)
    {
        if (changedPaths is null)
        {
            return [];
        }

        var collapsed = new List<ChangedPath>();
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var change in changedPaths)
        {
            if (change is null || string.IsNullOrWhiteSpace(change.Path))
            {
                continue;
            }

            if (positions.TryGetValue(change.Path, out var index))
            {
                collapsed[index] = change;
                continue;
            }

            positions[change.Path] = collapsed.Count;
            collapsed.Add(change);
        }

        return collapsed;
    }
}
