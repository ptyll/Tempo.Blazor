namespace Tempo.Blazor.DocumentLibrary;

/// <summary>
/// Describes a single page of a browse/search request against the document library.
/// </summary>
public sealed class DocumentLibraryQuery
{
    /// <summary>Which kind of documents to list.</summary>
    public required TempoDocumentKind Kind { get; set; }

    /// <summary>
    /// Folder to list. Null or <c>"/"</c> is the root. Ignored by flat stores and when <see cref="Scope"/> is
    /// <see cref="DocumentLibraryScope.Library"/>.
    /// </summary>
    public string? FolderPath { get; set; }

    /// <summary>
    /// How far the query reaches: <see cref="DocumentLibraryScope.Folder"/> (default, the direct content of
    /// <see cref="FolderPath"/>), <see cref="DocumentLibraryScope.Subtree"/> (the folder and all of its sub-folders) or
    /// <see cref="DocumentLibraryScope.Library"/> (the whole library; <see cref="FolderPath"/> is ignored). A library-wide
    /// query (the open dialog's "Recent" node, a search) states <see cref="DocumentLibraryScope.Library"/> instead of
    /// relying on a <c>null</c> <see cref="FolderPath"/>. A provider without support for the wider scopes may degrade
    /// <see cref="DocumentLibraryScope.Subtree"/> and <see cref="DocumentLibraryScope.Library"/> to
    /// <see cref="DocumentLibraryScope.Folder"/>.
    /// </summary>
    public DocumentLibraryScope Scope { get; set; } = DocumentLibraryScope.Folder;

    /// <summary>
    /// Free-text search across document names. Null or empty returns the folder contents
    /// unfiltered. Only honoured when the provider advertises
    /// <see cref="DocumentLibraryCapabilities.Search"/>.
    /// </summary>
    public string? Search { get; set; }

    /// <summary>Field to order results by. Defaults to <see cref="DocumentLibrarySortField.Name"/>.</summary>
    public DocumentLibrarySortField SortField { get; set; } = DocumentLibrarySortField.Name;

    /// <summary>Whether to sort descending. Defaults to ascending.</summary>
    public bool Descending { get; set; }

    /// <summary>Number of items to skip (pagination offset).</summary>
    public int Skip { get; set; }

    /// <summary>Maximum number of items to return. Defaults to 50.</summary>
    public int Take { get; set; } = 50;

    /// <summary>
    /// Optional application scope (GUID string). Multi-app hosts (e.g. a single API key granting
    /// access to several apps) set this so stateless callers such as MCP tools list documents from
    /// the intended app. Single-app stores may ignore it.
    /// </summary>
    public string? ScopeAppId { get; set; }
}
