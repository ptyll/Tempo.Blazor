using Tempo.Blazor.DocumentLibrary;

namespace Tempo.Blazor.Tests.Fixtures;

/// <summary>
/// Wraps another provider but holds read operations until a gate task completes — used to
/// assert the dialog's loading state.
/// </summary>
public sealed class GatedLibraryProvider(ITempoDocumentLibraryProvider inner, Task gate)
    : ITempoDocumentLibraryProvider
{
    public DocumentLibraryCapabilities Capabilities => inner.Capabilities;

    public async Task<DocumentLibraryFolder> GetFolderTreeAsync(
        TempoDocumentKind kind, CancellationToken cancellationToken = default)
    {
        await gate.ConfigureAwait(false);
        return await inner.GetFolderTreeAsync(kind, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentLibraryPage> BrowseAsync(
        DocumentLibraryQuery query, CancellationToken cancellationToken = default)
    {
        await gate.ConfigureAwait(false);
        return await inner.BrowseAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentLibraryEntry?> GetEntryAsync(
        TempoDocumentKind kind, Guid documentId, CancellationToken cancellationToken = default)
    {
        await gate.ConfigureAwait(false);
        return await inner.GetEntryAsync(kind, documentId, cancellationToken).ConfigureAwait(false);
    }

    public Task<DocumentLibraryFolder> CreateFolderAsync(
        TempoDocumentKind kind, string parentPath, string name,
        CancellationToken cancellationToken = default)
        => inner.CreateFolderAsync(kind, parentPath, name, cancellationToken);

    public Task RenameDocumentAsync(
        TempoDocumentKind kind, Guid documentId, string newName,
        CancellationToken cancellationToken = default)
        => inner.RenameDocumentAsync(kind, documentId, newName, cancellationToken);

    public Task RenameFolderAsync(
        TempoDocumentKind kind, string folderPath, string newName,
        CancellationToken cancellationToken = default)
        => inner.RenameFolderAsync(kind, folderPath, newName, cancellationToken);

    public Task DeleteDocumentsAsync(
        TempoDocumentKind kind, IReadOnlyList<Guid> documentIds,
        CancellationToken cancellationToken = default)
        => inner.DeleteDocumentsAsync(kind, documentIds, cancellationToken);

    public Task DeleteFolderAsync(
        TempoDocumentKind kind, string folderPath,
        CancellationToken cancellationToken = default)
        => inner.DeleteFolderAsync(kind, folderPath, cancellationToken);
}

/// <summary>Throws on every read — used to assert the dialog's error state.</summary>
public sealed class ThrowingLibraryProvider : ITempoDocumentLibraryProvider
{
    public DocumentLibraryCapabilities Capabilities => DocumentLibraryCapabilities.All;

    public Task<DocumentLibraryFolder> GetFolderTreeAsync(
        TempoDocumentKind kind, CancellationToken cancellationToken = default)
        => Task.FromException<DocumentLibraryFolder>(new InvalidOperationException("boom"));

    public Task<DocumentLibraryPage> BrowseAsync(
        DocumentLibraryQuery query, CancellationToken cancellationToken = default)
        => Task.FromException<DocumentLibraryPage>(new InvalidOperationException("boom"));

    public Task<DocumentLibraryEntry?> GetEntryAsync(
        TempoDocumentKind kind, Guid documentId, CancellationToken cancellationToken = default)
        => Task.FromException<DocumentLibraryEntry?>(new InvalidOperationException("boom"));

    public Task<DocumentLibraryFolder> CreateFolderAsync(
        TempoDocumentKind kind, string parentPath, string name,
        CancellationToken cancellationToken = default)
        => Task.FromException<DocumentLibraryFolder>(new InvalidOperationException("boom"));

    public Task RenameDocumentAsync(
        TempoDocumentKind kind, Guid documentId, string newName,
        CancellationToken cancellationToken = default)
        => Task.FromException(new InvalidOperationException("boom"));

    public Task RenameFolderAsync(
        TempoDocumentKind kind, string folderPath, string newName,
        CancellationToken cancellationToken = default)
        => Task.FromException(new InvalidOperationException("boom"));

    public Task DeleteDocumentsAsync(
        TempoDocumentKind kind, IReadOnlyList<Guid> documentIds,
        CancellationToken cancellationToken = default)
        => Task.FromException(new InvalidOperationException("boom"));

    public Task DeleteFolderAsync(
        TempoDocumentKind kind, string folderPath,
        CancellationToken cancellationToken = default)
        => Task.FromException(new InvalidOperationException("boom"));
}

/// <summary>
/// Wraps another provider and records every browse query, so a test can assert exactly what the
/// dialog asked for (the virtual "Recent" node issues a very specific query).
/// </summary>
public sealed class RecordingLibraryProvider(ITempoDocumentLibraryProvider inner) : ITempoDocumentLibraryProvider
{
    /// <summary>A snapshot of one browse query (the dialog reuses nothing, but a snapshot is immune to later mutation).</summary>
    public sealed record BrowseCall(
        string? FolderPath, string? Search, DocumentLibrarySortField SortField, bool Descending, int Skip, int Take,
        DocumentLibraryScope Scope = DocumentLibraryScope.Folder);

    /// <summary>Every browse call in order.</summary>
    public List<BrowseCall> Calls { get; } = [];

    public DocumentLibraryCapabilities Capabilities => inner.Capabilities;

    public Task<DocumentLibraryFolder> GetFolderTreeAsync(
        TempoDocumentKind kind, CancellationToken cancellationToken = default)
        => inner.GetFolderTreeAsync(kind, cancellationToken);

    public Task<DocumentLibraryPage> BrowseAsync(
        DocumentLibraryQuery query, CancellationToken cancellationToken = default)
    {
        Calls.Add(new BrowseCall(query.FolderPath, query.Search, query.SortField, query.Descending, query.Skip, query.Take, query.Scope));
        return inner.BrowseAsync(query, cancellationToken);
    }

    public Task<DocumentLibraryEntry?> GetEntryAsync(
        TempoDocumentKind kind, Guid documentId, CancellationToken cancellationToken = default)
        => inner.GetEntryAsync(kind, documentId, cancellationToken);

    public Task<DocumentLibraryFolder> CreateFolderAsync(
        TempoDocumentKind kind, string parentPath, string name, CancellationToken cancellationToken = default)
        => inner.CreateFolderAsync(kind, parentPath, name, cancellationToken);

    public Task RenameDocumentAsync(
        TempoDocumentKind kind, Guid documentId, string newName, CancellationToken cancellationToken = default)
        => inner.RenameDocumentAsync(kind, documentId, newName, cancellationToken);

    public Task RenameFolderAsync(
        TempoDocumentKind kind, string folderPath, string newName, CancellationToken cancellationToken = default)
        => inner.RenameFolderAsync(kind, folderPath, newName, cancellationToken);

    public Task DeleteDocumentsAsync(
        TempoDocumentKind kind, IReadOnlyList<Guid> documentIds, CancellationToken cancellationToken = default)
        => inner.DeleteDocumentsAsync(kind, documentIds, cancellationToken);

    public Task DeleteFolderAsync(
        TempoDocumentKind kind, string folderPath, CancellationToken cancellationToken = default)
        => inner.DeleteFolderAsync(kind, folderPath, cancellationToken);
}