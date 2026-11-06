using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The conflict radar's MODEL: every path the agents touched as one row, every agent as one column, and a cell where
/// the two meet when that agent changed that file.
/// <para>
/// The matrix is pure C# and fully testable without a renderer, which is the point: a conflict is a FACT ("two or more
/// agents changed the same path"), not a colour a template decides. One agent changing a file is work; two agents
/// changing it is a decision somebody has to make, so the row is marked a conflict and the surface can never disagree
/// with the count it prints.
/// </para>
/// <para>
/// Ordering answers the reader's first question. Conflicting rows come FIRST and, inside both groups, paths are sorted
/// ordinally, so the same input always paints the same picture. Directories are derived from that order — the directory
/// holding the first conflict is the first group — which keeps "group by directory" and "conflicts on top" from fighting
/// each other: the group order follows the rows rather than the other way round.
/// </para>
/// <para>
/// A conflict is judged on the path an agent leaves the file AT: a rename's <see cref="ChangedPath.PreviousPath"/> is
/// shown in the cell ("was src/Old.cs") but does not create a second row, because a reader decides about one file, not
/// about a path that no longer exists.
/// </para>
/// </summary>
public sealed class ConflictMatrix
{
    /// <summary>An empty matrix: no agents, no rows, nothing to decide.</summary>
    public static ConflictMatrix Empty { get; } = new(null);

    private readonly Dictionary<string, ConflictMatrixRow> _rowsByPath = new(StringComparer.Ordinal);

    /// <summary>Builds the matrix from the agents' branch changes.</summary>
    /// <param name="branches">One entry per agent, in any order (the host's order is preserved for the columns). Null,
    /// empty, a null entry and an agent that changed nothing are all skipped: a column with no cells would only widen
    /// the table.</param>
    public ConflictMatrix(IReadOnlyList<AgentBranchChanges?>? branches)
    {
        var columns = new List<AgentBranchChanges>();
        if (branches is not null)
        {
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var branch in branches)
            {
                if (branch is null || !branch.HasChanges)
                {
                    continue;
                }

                if (positions.TryGetValue(branch.Agent.Id, out var index))
                {
                    // The same agent reported twice (two worktrees, two receipts): one column, with both lists of
                    // changes merged — dropping the second branch would silently lose the files it names.
                    columns[index] = columns[index] with
                    {
                        ChangedPaths = Merge(columns[index].ChangedPaths, branch.ChangedPaths),
                    };
                    continue;
                }

                positions[branch.Agent.Id] = columns.Count;
                columns.Add(branch);
            }
        }

        Columns = columns;

        var paths = new List<string>();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            foreach (var change in column.ChangedPaths)
            {
                if (seenPaths.Add(change.Path))
                {
                    paths.Add(change.Path);
                }
            }
        }

        var rows = new List<ConflictMatrixRow>(paths.Count);
        foreach (var path in paths)
        {
            var cells = new List<ConflictMatrixCell?>(columns.Count);
            foreach (var column in columns)
            {
                var change = column.Find(path);
                cells.Add(change is null ? null : new ConflictMatrixCell(column.Agent, change));
            }

            rows.Add(new ConflictMatrixRow(path, cells));
        }

        rows.Sort(CompareRows);
        Rows = rows;

        foreach (var row in rows)
        {
            _rowsByPath[row.Path] = row;
        }

        Conflicts = [.. rows.Where(row => row.IsConflict)];
        Directories = GroupByDirectory(rows);
    }

    /// <summary>The agents that changed something, in the host's order — one column each.</summary>
    public IReadOnlyList<AgentBranchChanges> Columns { get; }

    /// <summary>Every changed path, conflicting ones first and each group in ordinal path order.</summary>
    public IReadOnlyList<ConflictMatrixRow> Rows { get; }

    /// <summary>The rows that more than one agent changed, in the same order as <see cref="Rows"/>.</summary>
    public IReadOnlyList<ConflictMatrixRow> Conflicts { get; }

    /// <summary>The rows grouped by the directory their file sits in, following the row order.</summary>
    public IReadOnlyList<ConflictMatrixDirectory> Directories { get; }

    /// <summary>How many agents changed something (how many columns the grid shows).</summary>
    public int ColumnCount => Columns.Count;

    /// <summary>How many paths the agents touched between them (how many rows the grid shows).</summary>
    public int RowCount => Rows.Count;

    /// <summary>How many paths more than one agent changed.</summary>
    public int ConflictCount => Conflicts.Count;

    /// <summary>Whether any path is claimed by more than one agent.</summary>
    public bool HasConflicts => ConflictCount > 0;

    /// <summary>Whether the agents changed nothing at all (the surface renders its empty state).</summary>
    public bool IsEmpty => RowCount == 0;

    /// <summary>The row for one path, or null when no agent touched it.</summary>
    /// <param name="path">The path to look for.</param>
    /// <returns>The row, or null.</returns>
    public ConflictMatrixRow? Find(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return _rowsByPath.TryGetValue(ChangedPath.Normalize(path!), out var row) ? row : null;
    }

    /// <summary>
    /// The rows a reader still has to decide about: every conflicting row whose decision is missing, or whose decision
    /// names an agent that does not change that file (a decision that would keep a version nobody wrote).
    /// </summary>
    /// <param name="resolutions">The reader's decisions, in any order. Null reads as no decisions at all.</param>
    /// <returns>The unresolved rows, in row order.</returns>
    public IReadOnlyList<ConflictMatrixRow> Unresolved(IEnumerable<ConflictResolution?>? resolutions)
    {
        if (!HasConflicts)
        {
            return [];
        }

        var decided = new Dictionary<string, ConflictResolution>(StringComparer.Ordinal);
        if (resolutions is not null)
        {
            foreach (var resolution in resolutions)
            {
                if (resolution is not null)
                {
                    decided[resolution.Path] = resolution;
                }
            }
        }

        var unresolved = new List<ConflictMatrixRow>();
        foreach (var row in Conflicts)
        {
            if (!decided.TryGetValue(row.Path, out var resolution) || !row.Settles(resolution))
            {
                unresolved.Add(row);
            }
        }

        return unresolved;
    }

    /// <summary>Whether every conflict carries a decision the matrix accepts (a conflict-free merge is always ready).</summary>
    /// <param name="resolutions">The reader's decisions, in any order.</param>
    /// <returns>Whether the merge may be committed.</returns>
    public bool IsReadyToMerge(IEnumerable<ConflictResolution?>? resolutions) => Unresolved(resolutions).Count == 0;

    /// <summary>The decision for one path, if the matrix accepts it.</summary>
    /// <param name="path">The file to look for.</param>
    /// <param name="resolutions">The reader's decisions, in any order.</param>
    /// <returns>The accepted decision, or null.</returns>
    public ConflictResolution? AcceptedResolution(string? path, IEnumerable<ConflictResolution?>? resolutions)
    {
        var row = Find(path);
        if (row is null || !row.IsConflict || resolutions is null)
        {
            return null;
        }

        foreach (var resolution in resolutions)
        {
            if (resolution is not null && resolution.Decides(row.Path) && row.Settles(resolution))
            {
                return resolution;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the merge request a surface reports when the reader commits: every changed path, plus the accepted
    /// decision for each conflicting file.
    /// </summary>
    /// <param name="resolutions">The reader's decisions, in any order.</param>
    /// <returns>The request; conflicts with no accepted decision are simply left without one (callers gate on
    /// <see cref="IsReadyToMerge"/>).</returns>
    public ConflictMergeRequest BuildMergeRequest(IEnumerable<ConflictResolution?>? resolutions)
    {
        var accepted = new List<ConflictResolution>(Conflicts.Count);
        foreach (var row in Conflicts)
        {
            if (AcceptedResolution(row.Path, resolutions) is { } resolution)
            {
                accepted.Add(resolution);
            }
        }

        return new ConflictMergeRequest([.. Rows.Select(row => row.Path)], accepted);
    }

    /// <summary>Merges a second receipt of the same agent's changes, keeping the host's first-seen path order.</summary>
    private static IReadOnlyList<ChangedPath> Merge(
        IReadOnlyList<ChangedPath> first,
        IReadOnlyList<ChangedPath> second)
    {
        var merged = new List<ChangedPath>(first);
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < merged.Count; index++)
        {
            positions[merged[index].Path] = index;
        }

        foreach (var change in second)
        {
            if (positions.TryGetValue(change.Path, out var index))
            {
                merged[index] = change;
                continue;
            }

            positions[change.Path] = merged.Count;
            merged.Add(change);
        }

        return merged;
    }

    private static int CompareRows(ConflictMatrixRow left, ConflictMatrixRow right)    {
        var conflict = right.IsConflict.CompareTo(left.IsConflict);
        return conflict != 0
            ? conflict
            : string.CompareOrdinal(left.Path, right.Path);
    }

    private static IReadOnlyList<ConflictMatrixDirectory> GroupByDirectory(IReadOnlyList<ConflictMatrixRow> rows)
    {
        var directories = new List<ConflictMatrixDirectory>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!index.TryGetValue(row.Directory, out var position))
            {
                position = directories.Count;
                index[row.Directory] = position;
                directories.Add(new ConflictMatrixDirectory(row.Directory, []));
            }

            directories[position].Rows.Add(row);
        }

        return directories;
    }
}

/// <summary>
/// One row of the conflict matrix: a file, the agents' changes to it and whether more than one agent claimed it.
/// <para>
/// <see cref="Cells"/> is aligned with <see cref="ConflictMatrix.Columns"/> — index <c>i</c> is the column of the agent
/// at index <c>i</c>, and a null cell means "this agent did not touch this file". The alignment is the point: a grid
/// that looks cells up by agent id would re-order itself the moment a host sorted its agents differently from its
/// matrix.
/// </para>
/// </summary>
public sealed class ConflictMatrixRow
{
    /// <summary>Creates a row.</summary>
    /// <param name="path">The changed path. Required.</param>
    /// <param name="cells">One entry per matrix column (null where the agent did not touch the file).</param>
    public ConflictMatrixRow(string path, IReadOnlyList<ConflictMatrixCell?> cells)
    {
        Path = ChangedPath.Normalize(path);
        Cells = cells ?? [];
        Changed = [.. Cells.Where(cell => cell is not null).Select(cell => cell!)];
    }

    /// <summary>The file the row is about, with separators normalised to <c>/</c>.</summary>
    public string Path { get; }

    /// <summary>One entry per matrix column, null where that agent did not touch the file.</summary>
    public IReadOnlyList<ConflictMatrixCell?> Cells { get; }

    /// <summary>The cells that carry a change, in column order.</summary>
    public IReadOnlyList<ConflictMatrixCell> Changed { get; }

    /// <summary>The directory the file sits in (empty for a file at the repository root).</summary>
    public string Directory
    {
        get
        {
            var slash = Path.LastIndexOf('/');
            return slash <= 0 ? string.Empty : Path[..slash];
        }
    }

    /// <summary>The file's own name, without its directory.</summary>
    public string FileName
    {
        get
        {
            var slash = Path.LastIndexOf('/');
            return slash < 0 ? Path : Path[(slash + 1)..];
        }
    }

    /// <summary>How many agents changed the file.</summary>
    public int AgentCount => Changed.Count;

    /// <summary>Whether more than one agent changed the file — the definition of a conflict here.</summary>
    public bool IsConflict => Changed.Count > 1;

    /// <summary>The agents that changed the file, in column order.</summary>
    public IReadOnlyList<AgentRef> ConflictingAgents => [.. Changed.Select(cell => cell.Agent)];

    /// <summary>Whether a decision settles this row (only an agent that really changed the file may win it).</summary>
    /// <param name="resolution">The reader's decision.</param>
    /// <returns>Whether the row is decided by it.</returns>
    public bool Settles(ConflictResolution resolution)
    {
        if (resolution is null)
        {
            return false;
        }

        if (!resolution.Decides(Path))
        {
            return false;
        }

        return resolution.IsManual
               || Changed.Any(cell => string.Equals(cell.Agent.Id, resolution.AgentId, StringComparison.Ordinal));
    }
}

/// <summary>
/// One directory group of the conflict matrix: the folder and the rows that sit directly in it.
/// <para>
/// The group is FLAT by design — a nested tree would hide the conflicts of a deep folder behind three disclosures a
/// reader has to open. The path of every row is printed in full by the surface, so one level of grouping is enough to
/// shorten the scan without losing where a file lives.
/// </para>
/// </summary>
public sealed class ConflictMatrixDirectory
{
    /// <summary>Creates a directory group.</summary>
    /// <param name="directory">The directory the rows sit in (empty for the repository root).</param>
    /// <param name="rows">The rows of the group, in matrix order. The list is owned by the matrix and filled while it
    /// is built.</param>
    public ConflictMatrixDirectory(string directory, List<ConflictMatrixRow> rows)
    {
        Directory = directory ?? string.Empty;
        Rows = rows ?? [];
    }

    /// <summary>The directory the rows sit in; empty for a file at the repository root.</summary>
    public string Directory { get; }

    /// <summary>The group's rows, in matrix order (conflicts first).</summary>
    public List<ConflictMatrixRow> Rows { get; }

    /// <summary>How many rows the group holds.</summary>
    public int RowCount => Rows.Count;

    /// <summary>How many of the group's rows are conflicts.</summary>
    public int ConflictCount => Rows.Count(row => row.IsConflict);

    /// <summary>Whether the group holds at least one conflict.</summary>
    public bool HasConflicts => ConflictCount > 0;
}

/// <summary>
/// One cell of the conflict matrix: the agent, the change it made and — for a rename — where the file came from.
/// <para>
/// A cell exists only where an agent really changed the file; the matrix stores null instead of an "unchanged" cell so
/// no surface can paint a change that did not happen.
/// </para>
/// </summary>
public sealed class ConflictMatrixCell
{
    /// <summary>Creates a cell.</summary>
    /// <param name="agent">The agent the column belongs to. Required.</param>
    /// <param name="change">The agent's change to the row's file. Required.</param>
    public ConflictMatrixCell(AgentRef agent, ChangedPath change)
    {
        Agent = agent ?? throw new ArgumentNullException(nameof(agent));
        Change = change ?? throw new ArgumentNullException(nameof(change));
    }

    /// <summary>The agent the cell's column belongs to.</summary>
    public AgentRef Agent { get; }

    /// <summary>The agent's change to the row's file.</summary>
    public ChangedPath Change { get; }

    /// <summary>What the agent did to the file.</summary>
    public ChangeKind Kind => Change.Kind;

    /// <summary>Where a renamed file came from, or null when the change is not a rename.</summary>
    public string? PreviousPath => Change.PreviousPath;

    /// <summary>Whether the cell is a rename (so the surface prints where the file came from).</summary>
    public bool IsRename => Change.IsRename;
}
