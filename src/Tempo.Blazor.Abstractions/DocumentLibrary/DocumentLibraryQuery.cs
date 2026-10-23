namespace Tempo.Blazor.DocumentLibrary;

/// <summary>
/// Describes a single page of a browse/search request against the document library.
/// </summary>
public sealed class DocumentLibraryQuery
{
    /// <summary>Which kind of documents to list.</summary>
    public required TempoDocumentKind Kind { get; set; }

    /// <summary>
    /// Folder to list. Null or <c>"/"</c> lists the root. Ignored by flat stores.
    /// </summary>
    /// <remarks>
    /// <b>Null is ambiguous.</b> The contract says "null lists the root", yet a library-wide query (a search, or the open
    /// dialog's virtual "Recent" node) also sends <c>null</c> because there is no explicit scope member. A provider
    /// that wants those to span every folder must treat <c>null</c> as "everywhere" (and then the root is only reachable
    /// as <c>"/"</c>); a provider following the letter of the contract returns the root documents only. The built-in demo
    /// store follows the contract. An explicit scope (e.g. <c>IncludeSubfolders</c> / <c>DocumentLibraryScope</c>) is the planned way
    /// to remove the ambiguity.
    /// </remarks>
    public string? FolderPath { get; set; }

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
