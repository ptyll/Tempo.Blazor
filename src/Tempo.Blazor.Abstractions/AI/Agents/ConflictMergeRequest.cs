namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One committed merge of the agents' branches: every file the merge touches, plus the reader's decision for each file
/// that more than one agent changed.
/// <para>
/// The request is the payload a conflict radar reports ONCE per merge action; the surface changes nothing itself. A
/// file with a single agent's change needs no decision and is listed anyway, because the host has to merge it too — the
/// request describes the whole merge, not only its conflicts.
/// </para>
/// <para>
/// <see cref="Resolutions"/> carries exactly the reader's decisions; a conflicting file with no decision is a file the
/// surface refuses to merge, so a request the host receives never leaves a conflict for it to discover itself.
/// </para>
/// </summary>
public sealed record ConflictMergeRequest
{
    /// <summary>Creates a merge request.</summary>
    /// <param name="files">Every file the merge touches, in the order the surface shows them. Null reads as an empty
    /// list; blank entries are dropped, separators are normalised to <c>/</c> (the same way
    /// <see cref="ChangedPath.Path"/> and <see cref="ConflictResolution.Path"/> are) and a path reported twice is kept
    /// ONCE — a request that named one file twice would have the host merge it twice.</param>
    /// <param name="resolutions">The reader's decisions, one per conflicting file. Null reads as an empty list.</param>
    public ConflictMergeRequest(
        IReadOnlyList<string>? files,
        IReadOnlyList<ConflictResolution>? resolutions = null)
    {
        Files = files is null
            ? []
            : [.. files
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Select(ChangedPath.Normalize)
                .Distinct(StringComparer.Ordinal)];
        Resolutions = resolutions is null ? [] : [.. resolutions.Where(resolution => resolution is not null)];
    }

    /// <summary>The paths the merge touches (conflict-free files included), in the order the surface shows them, with
    /// separators normalised to <c>/</c> and each path present once.</summary>
    public IReadOnlyList<string> Files { get; init; }

    /// <summary>The reader's decisions, one per conflicting file.</summary>
    public IReadOnlyList<ConflictResolution> Resolutions { get; init; }

    /// <summary>How many files the merge touches.</summary>
    public int FileCount => Files.Count;

    /// <summary>How many files the reader had to decide about.</summary>
    public int ResolvedConflictCount => Resolutions.Count;

    /// <summary>The decision for one path, or null when the reader did not decide about it.</summary>
    /// <param name="path">The file to look for.</param>
    /// <returns>The decision, or null.</returns>
    public ConflictResolution? ResolutionOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var resolution in Resolutions)
        {
            if (resolution.Decides(path))
            {
                return resolution;
            }
        }

        return null;
    }
}
