namespace Tempo.Blazor.AI.Models;

/// <summary>
/// The reader's decision about ONE conflicting file: keep one agent's version, or merge the file by hand.
/// <para>
/// The record is a VALUE and the file's path is its key, which is why a surface bound to a list of these can have at
/// most one decision per file: two entries for one path would let the merge request claim two winners for one file.
/// A decision for a path that is not in conflict is ignored by the radar (nothing to decide) but is still carried to
/// the host unchanged, so a host that keeps its own list never loses data on a re-render.
/// </para>
/// <para>
/// <see cref="ConflictResolutionKind.Agent"/> requires <see cref="AgentId"/>; a blank one is REJECTED at construction
/// because "keep the agent's side" without an agent would pick a winner by accident.
/// </para>
/// </summary>
public sealed record ConflictResolution
{
    /// <summary>Creates a resolution.</summary>
    /// <param name="path">The conflicting file's path. Required.</param>
    /// <param name="kind">Whether one agent wins or the reader merges by hand.</param>
    /// <param name="agentId">The winning agent's id — required for <see cref="ConflictResolutionKind.Agent"/>, unused
    /// for <see cref="ConflictResolutionKind.Manual"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank, or an agent resolution names no
    /// agent.</exception>
    public ConflictResolution(string path, ConflictResolutionKind kind, string? agentId = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "A resolution names the file it decides: the path is its key, and two entries under an invented path " +
                "would claim a winner for a file nobody chose.",
                nameof(path));
        }

        if (kind == ConflictResolutionKind.Agent && string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException(
                "An agent resolution must name the agent whose version wins — 'keep the agent's side' without an " +
                "agent would pick a winner by accident.",
                nameof(agentId));
        }

        Path = ChangedPath.Normalize(path);
        Kind = kind;
        AgentId = kind == ConflictResolutionKind.Agent ? agentId : null;
    }

    /// <summary>The conflicting file's path, normalised the same way <see cref="ChangedPath.Path"/> is.</summary>
    public string Path { get; init; }

    /// <summary>Whether one agent wins or the reader merges the file by hand.</summary>
    public ConflictResolutionKind Kind { get; init; }

    /// <summary>The winning agent's id, or null for a manual resolution.</summary>
    public string? AgentId { get; init; }

    /// <summary>Whether this decision picks an agent's version.</summary>
    public bool IsAgentSide => Kind == ConflictResolutionKind.Agent;

    /// <summary>Whether this decision hands the file to the reader.</summary>
    public bool IsManual => Kind == ConflictResolutionKind.Manual;

    /// <summary>Whether a decision stored under <paramref name="path"/> settles that file.</summary>
    /// <param name="path">The path to test.</param>
    /// <returns>Whether the paths name the same file.</returns>
    public bool Decides(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && string.Equals(Path, ChangedPath.Normalize(path!), StringComparison.Ordinal);
}
