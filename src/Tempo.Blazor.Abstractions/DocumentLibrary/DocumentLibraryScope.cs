namespace Tempo.Blazor.DocumentLibrary;

/// <summary>
/// How far a <see cref="DocumentLibraryQuery"/> reaches into the library. It replaces the ambiguous
/// "<c>FolderPath = null</c> means the root, or everywhere" convention with an explicit choice.
/// </summary>
/// <remarks>
/// A provider that cannot honour <see cref="Subtree"/> or <see cref="Library"/> may degrade them to
/// <see cref="Folder"/> (it then answers like a flat or folder-only store); callers must therefore treat a
/// wider scope as a request, not a guarantee.
/// </remarks>
public enum DocumentLibraryScope
{
    /// <summary>
    /// Only the documents directly inside <see cref="DocumentLibraryQuery.FolderPath"/>
    /// (<c>null</c> or <c>"/"</c> is the root). This is the behaviour of every query written before the scope existed.
    /// </summary>
    Folder = 0,

    /// <summary>The documents of <see cref="DocumentLibraryQuery.FolderPath"/> and of all of its sub-folders.</summary>
    Subtree = 1,

    /// <summary>The whole library of the <see cref="DocumentLibraryQuery.Kind"/>; <see cref="DocumentLibraryQuery.FolderPath"/> is ignored.</summary>
    Library = 2
}
