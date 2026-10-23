using Tempo.Blazor.DocumentLibrary;

namespace Tempo.Blazor.Tests.Fixtures;

/// <summary>
/// In-memory <see cref="ITempoDocumentLibraryProvider"/> used across the test suite to pin
/// the semantics the real (Demo.Api-backed) provider must honour. Documents and folders are
/// held per <see cref="TempoDocumentKind"/>; folder paths use <c>"/"</c> as the root.
/// </summary>
public sealed class InMemoryDocumentLibraryProvider : ITempoDocumentLibraryProvider
{
    private sealed record Doc(Guid Id, string Name, string FolderPath, DateTime CreatedAt, DateTime ModifiedAt, string? PreviewSvg, string? Author = null);

    private readonly Dictionary<TempoDocumentKind, HashSet<string>> _folders = new();
    private readonly Dictionary<TempoDocumentKind, List<Doc>> _docs = new();

    public InMemoryDocumentLibraryProvider(DocumentLibraryCapabilities capabilities)
    {
        Capabilities = capabilities;
        foreach (var kind in Enum.GetValues<TempoDocumentKind>())
        {
            _folders[kind] = ["/"];
            _docs[kind] = [];
        }
    }

    public DocumentLibraryCapabilities Capabilities { get; }

    // ── Seeding helpers (test-only) ───────────────────────────────────────────

    public void AddFolder(TempoDocumentKind kind, string path) => _folders[kind].Add(path);

    public Guid AddDocument(
        TempoDocumentKind kind, string name, string folderPath,
        DateTime? createdAt = null, DateTime? modifiedAt = null, string? previewSvg = null,
        Guid? id = null, string? author = null)
    {
        var docId = id ?? Guid.NewGuid();
        _docs[kind].Add(new Doc(docId, name, folderPath,
            createdAt ?? DateTime.UtcNow, modifiedAt ?? DateTime.UtcNow, previewSvg, author));
        return docId;
    }

    /// <summary>Test helper: replaces a document's preview to simulate an edit elsewhere.</summary>
    public void UpdatePreview(TempoDocumentKind kind, Guid id, string previewSvg)
    {
        var list = _docs[kind];
        var index = list.FindIndex(d => d.Id == id);
        if (index >= 0)
        {
            list[index] = list[index] with { PreviewSvg = previewSvg, ModifiedAt = DateTime.UtcNow };
        }
    }

    // ── ITempoDocumentLibraryProvider ─────────────────────────────────────────

    public Task<DocumentLibraryEntry?> GetEntryAsync(
        TempoDocumentKind kind, Guid documentId, CancellationToken cancellationToken = default)
    {
        var doc = _docs[kind].FirstOrDefault(d => d.Id == documentId);
        return Task.FromResult(doc is null ? null : new DocumentLibraryEntry
        {
            Id = doc.Id,
            Name = doc.Name,
            Kind = kind,
            FolderPath = doc.FolderPath,
            CreatedAt = doc.CreatedAt,
            ModifiedAt = doc.ModifiedAt,
            PreviewSvg = doc.PreviewSvg,
            Author = doc.Author
        });
    }

    public Task<DocumentLibraryFolder> GetFolderTreeAsync(
        TempoDocumentKind kind, CancellationToken cancellationToken = default)
    {
        var paths = _folders[kind];

        DocumentLibraryFolder Build(string path) => new()
        {
            Path = path,
            Name = path == "/" ? "/" : path[(path.LastIndexOf('/') + 1)..],
            Children = paths
                .Where(p => p != path && ParentOf(p) == path)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(Build)
                .ToList()
        };

        return Task.FromResult(Build("/"));
    }

    // Same normalisation as DocumentLibraryStore.NormalizeFolder: null/empty is the root, a trailing slash is not significant.
    private static string NormalizeFolder(string? path)
        => string.IsNullOrEmpty(path) ? "/" : path.Length > 1 ? path.TrimEnd('/') : path;

    public Task<DocumentLibraryPage> BrowseAsync(
        DocumentLibraryQuery query, CancellationToken cancellationToken = default)
    {
        IEnumerable<Doc> docs = _docs[query.Kind];

        var searching = !string.IsNullOrWhiteSpace(query.Search);
        if (searching)
        {
            docs = docs.Where(d => d.Name.Contains(query.Search!, StringComparison.OrdinalIgnoreCase));
        }

        switch (query.Scope)
        {
            case DocumentLibraryScope.Library:
                break;
            case DocumentLibraryScope.Subtree:
                var root = NormalizeFolder(query.FolderPath);
                docs = root == "/"
                    ? docs
                    : docs.Where(d => d.FolderPath == root || d.FolderPath.StartsWith(root + "/", StringComparison.Ordinal));
                break;
            default:
                if (!searching)
                {
                    var folder = NormalizeFolder(query.FolderPath);
                    docs = docs.Where(d => d.FolderPath == folder);
                }

                break;
        }

        var matched = docs.ToList();
        var total = matched.Count;

        IEnumerable<Doc> sorted = query.SortField switch
        {
            DocumentLibrarySortField.Modified => matched.OrderBy(d => d.ModifiedAt),
            DocumentLibrarySortField.Created => matched.OrderBy(d => d.CreatedAt),
            _ => matched.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
        };
        if (query.Descending)
        {
            sorted = sorted.Reverse();
        }

        var items = sorted
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(d => new DocumentLibraryEntry
            {
                Id = d.Id,
                Name = d.Name,
                Kind = query.Kind,
                FolderPath = d.FolderPath,
                CreatedAt = d.CreatedAt,
                ModifiedAt = d.ModifiedAt,
                PreviewSvg = d.PreviewSvg,
                Author = d.Author
            })
            .ToList();

        return Task.FromResult(new DocumentLibraryPage { Items = items, TotalCount = total });
    }

    public Task<DocumentLibraryFolder> CreateFolderAsync(
        TempoDocumentKind kind, string parentPath, string name,
        CancellationToken cancellationToken = default)
    {
        var path = parentPath == "/" ? "/" + name : parentPath + "/" + name;
        if (!_folders[kind].Add(path))
        {
            throw new InvalidOperationException($"Folder '{path}' already exists.");
        }

        return Task.FromResult(new DocumentLibraryFolder { Path = path, Name = name });
    }

    public Task RenameDocumentAsync(
        TempoDocumentKind kind, Guid documentId, string newName,
        CancellationToken cancellationToken = default)
    {
        var list = _docs[kind];
        var index = list.FindIndex(d => d.Id == documentId);
        if (index < 0)
        {
            throw new InvalidOperationException($"Document {documentId} not found.");
        }

        list[index] = list[index] with { Name = newName, ModifiedAt = DateTime.UtcNow };
        return Task.CompletedTask;
    }

    public Task RenameFolderAsync(
        TempoDocumentKind kind, string folderPath, string newName,
        CancellationToken cancellationToken = default)
    {
        var newPath = ParentOf(folderPath) is "/" ? "/" + newName : ParentOf(folderPath) + "/" + newName;

        var folders = _folders[kind];
        var affected = folders.Where(p => p == folderPath || p.StartsWith(folderPath + "/", StringComparison.Ordinal)).ToList();
        foreach (var p in affected)
        {
            folders.Remove(p);
            folders.Add(newPath + p[folderPath.Length..]);
        }

        var docs = _docs[kind];
        for (var i = 0; i < docs.Count; i++)
        {
            var f = docs[i].FolderPath;
            if (f == folderPath || f.StartsWith(folderPath + "/", StringComparison.Ordinal))
            {
                docs[i] = docs[i] with { FolderPath = newPath + f[folderPath.Length..] };
            }
        }

        return Task.CompletedTask;
    }

    public Task DeleteDocumentsAsync(
        TempoDocumentKind kind, IReadOnlyList<Guid> documentIds,
        CancellationToken cancellationToken = default)
    {
        _docs[kind].RemoveAll(d => documentIds.Contains(d.Id));
        return Task.CompletedTask;
    }

    public Task DeleteFolderAsync(
        TempoDocumentKind kind, string folderPath,
        CancellationToken cancellationToken = default)
    {
        _folders[kind].RemoveWhere(p =>
            p == folderPath || p.StartsWith(folderPath + "/", StringComparison.Ordinal));
        _docs[kind].RemoveAll(d =>
            d.FolderPath == folderPath || d.FolderPath.StartsWith(folderPath + "/", StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    private static string ParentOf(string path)
    {
        if (path == "/")
        {
            return "/";
        }

        var idx = path.LastIndexOf('/');
        return idx <= 0 ? "/" : path[..idx];
    }
}
