namespace Tempo.Blazor.AI.Models;

/// <summary>
/// What one agent did to one path on its own branch — the fact a conflict radar paints in the cell where the file and
/// the agent meet.
/// <para>
/// The enum carries no text: a surface renders its own localized word and icon for each value, so the same change reads
/// "Modified" in English and "Změněno" in Czech. <see cref="Renamed"/> is the only value that also needs a second path
/// (where the file came from), which <see cref="ChangedPath.PreviousPath"/> carries.
/// </para>
/// <para>
/// The four values are deliberately the whole vocabulary: a real Git status also reports modes, copies and merges, but
/// none of them changes what a reader has to decide here — WHO wins the file — and inventing a value per porcelain
/// token would leave most cells with a state no surface can explain.
/// </para>
/// </summary>
public enum ChangeKind
{
    /// <summary>The agent created the path on its branch.</summary>
    Added,

    /// <summary>The agent changed the contents (or the mode) of a path that already existed on the base branch.</summary>
    Modified,

    /// <summary>The agent removed the path from its branch.</summary>
    Deleted,

    /// <summary>The agent moved the path. <see cref="ChangedPath.PreviousPath"/> names where it came from.</summary>
    Renamed,
}
