namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One path an agent touched on its branch: where the file is, what happened to it, and — for a rename — where it came
/// from.
/// <para>
/// The record is a VALUE: the host composes it from its own workspace receipt (<c>ChangedPaths</c>) and never mutates
/// it. Paths are compared ORDINALLY after their separators are normalised to <c>/</c>, so the same file reported as
/// <c>src/AI/Foo.cs</c> by one agent and <c>src\AI\Foo.cs</c> by another is ONE row in the matrix instead of two rows
/// that hide a real conflict. Case is kept as the host spelled it — a case-insensitive file system would also fold
/// <c>Foo.cs</c> and <c>foo.cs</c>, but merging two genuinely distinct paths would hide a change nobody resolved.
/// </para>
/// <para>
/// A blank <see cref="Path"/> is REJECTED at construction: a path is the matrix's row key and the reader's own side
/// choice is keyed by it, so an invented one would move a decision onto the wrong file.
/// </para>
/// </summary>
public sealed record ChangedPath
{
    /// <summary>Creates a changed path.</summary>
    /// <param name="path">The path the file sits at now, relative to the repository root (<c>src/AI/Foo.cs</c>).
    /// Required.</param>
    /// <param name="kind">What the agent did to it.</param>
    /// <param name="previousPath">Where a <see cref="ChangeKind.Renamed"/> file came from. Ignored for every other
    /// kind, because a previous path only means something for a move.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or whitespace.</exception>
    public ChangedPath(string path, ChangeKind kind, string? previousPath = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "A changed path needs the path it names: it is the conflict matrix's row key and the reader's own " +
                "side choice is stored under it, so an invented one would move a decision onto the wrong file.",
                nameof(path));
        }

        Path = Normalize(path);
        Kind = kind;
        PreviousPath = kind == ChangeKind.Renamed && !string.IsNullOrWhiteSpace(previousPath)
            ? Normalize(previousPath!)
            : null;
    }

    /// <summary>The path the file sits at now, with <c>\</c> normalised to <c>/</c>.</summary>
    public string Path { get; init; }

    /// <summary>What the agent did to the file.</summary>
    public ChangeKind Kind { get; init; }

    /// <summary>
    /// Where a renamed file came from, with its separators normalised the same way as <see cref="Path"/>. Null for
    /// every other kind, and for a rename the host did not describe.
    /// </summary>
    public string? PreviousPath { get; init; }

    /// <summary>
    /// The directory the file sits in, without a trailing separator: <c>src/AI</c> for <c>src/AI/Foo.cs</c>. A file at
    /// the repository root has <see cref="string.Empty"/>, which a surface renders through its localized root word
    /// rather than as an empty heading.
    /// </summary>
    public string Directory
    {
        get
        {
            var slash = Path.LastIndexOf('/');
            return slash <= 0 ? string.Empty : Path[..slash];
        }
    }

    /// <summary>The file's own name, without its directory: <c>Foo.cs</c> for <c>src/AI/Foo.cs</c>.</summary>
    public string FileName
    {
        get
        {
            var slash = Path.LastIndexOf('/');
            return slash < 0 ? Path : Path[(slash + 1)..];
        }
    }

    /// <summary>Whether the agent moved the file (so <see cref="PreviousPath"/> may name where it came from).</summary>
    public bool IsRename => Kind == ChangeKind.Renamed;

    /// <summary>Normalises the separators of a path the host supplied.</summary>
    /// <param name="path">The path to normalise.</param>
    /// <returns>The path with <c>\</c> replaced by <c>/</c>.</returns>
    public static string Normalize(string path) => path.Replace('\\', '/').Trim();
}
