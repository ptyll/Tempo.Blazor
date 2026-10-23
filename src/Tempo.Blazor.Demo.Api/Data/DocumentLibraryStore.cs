using Tempo.Blazor.DocumentLibrary;

namespace Tempo.Blazor.Demo.Api.Data;

/// <summary>
/// In-process store backing the document library for all editor kinds. Holds document
/// payloads plus their metadata and a virtual folder tree, enforces optimistic concurrency
/// on save, and publishes <see cref="TempoDocumentChange"/> notifications so open editors
/// and embedded blocks can refresh.
/// </summary>
public sealed class DocumentLibraryStore
{
    /// <summary>A stored document: metadata plus its serialised payload.</summary>
    public sealed class StoredDocument
    {
        public required Guid Id { get; init; }
        public required TempoDocumentKind Kind { get; init; }
        public required string Name { get; set; }
        public required string FolderPath { get; set; }
        public DateTime CreatedAt { get; init; }
        public DateTime ModifiedAt { get; set; }
        public string? Author { get; set; }
        public string? PreviewSvg { get; set; }
        public required string PayloadJson { get; set; }
    }

    private readonly ITempoDocumentChangePublisher _publisher;
    private readonly object _gate = new();
    private readonly Dictionary<TempoDocumentKind, HashSet<string>> _folders = new();
    private readonly Dictionary<TempoDocumentKind, List<StoredDocument>> _docs = new();

    public DocumentLibraryStore(ITempoDocumentChangePublisher publisher)
    {
        _publisher = publisher;
        foreach (var kind in Enum.GetValues<TempoDocumentKind>())
        {
            _folders[kind] = ["/"];
            _docs[kind] = [];
        }
    }

    // ── Documents ────────────────────────────────────────────────────────────

    public StoredDocument CreateDocument(
        TempoDocumentKind kind, string name, string folderPath, string payloadJson,
        string? previewSvg, string? author = null)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var doc = new StoredDocument
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                Name = name,
                FolderPath = string.IsNullOrEmpty(folderPath) ? "/" : folderPath,
                CreatedAt = now,
                ModifiedAt = now,
                Author = author,
                PreviewSvg = previewSvg,
                PayloadJson = payloadJson
            };
            _docs[kind].Add(doc);
            _folders[kind].Add(doc.FolderPath);
            Publish(kind, doc.Id, TempoDocumentChangeType.Saved, doc.ModifiedAt);
            return doc;
        }
    }

    public StoredDocument? GetDocument(TempoDocumentKind kind, Guid id)
    {
        lock (_gate)
        {
            return _docs[kind].FirstOrDefault(d => d.Id == id);
        }
    }

    public StoredDocument SaveDocument(
        TempoDocumentKind kind, Guid id, string payloadJson, string? previewSvg,
        DateTime? expectedModifiedAt = null, string? name = null)
    {
        lock (_gate)
        {
            var doc = _docs[kind].FirstOrDefault(d => d.Id == id)
                ?? throw new InvalidOperationException($"Document {id} not found.");

            if (expectedModifiedAt is { } expected
                && Math.Abs((doc.ModifiedAt - expected).TotalMilliseconds) > 1)
            {
                throw new TempoDocumentConflictException(kind, id, doc.ModifiedAt);
            }

            doc.PayloadJson = payloadJson;
            if (previewSvg is not null)
            {
                doc.PreviewSvg = previewSvg;
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                doc.Name = name;
            }
            doc.ModifiedAt = DateTime.UtcNow;
            Publish(kind, id, TempoDocumentChangeType.Saved, doc.ModifiedAt);
            return doc;
        }
    }

    public void RenameDocument(TempoDocumentKind kind, Guid id, string newName)
    {
        lock (_gate)
        {
            var doc = _docs[kind].FirstOrDefault(d => d.Id == id)
                ?? throw new InvalidOperationException($"Document {id} not found.");
            doc.Name = newName;
            doc.ModifiedAt = DateTime.UtcNow;
            Publish(kind, id, TempoDocumentChangeType.Renamed, doc.ModifiedAt);
        }
    }

    public void DeleteDocuments(TempoDocumentKind kind, IReadOnlyList<Guid> ids)
    {
        lock (_gate)
        {
            foreach (var id in ids)
            {
                if (_docs[kind].RemoveAll(d => d.Id == id) > 0)
                {
                    Publish(kind, id, TempoDocumentChangeType.Deleted, DateTime.UtcNow);
                }
            }
        }
    }

    // ── Browse / folders ───────────────────────────────────────────────────────

    public DocumentLibraryPage Browse(DocumentLibraryQuery query)
    {
        lock (_gate)
        {
            IEnumerable<StoredDocument> docs = _docs[query.Kind];

            var searching = !string.IsNullOrWhiteSpace(query.Search);
            if (searching)
            {
                docs = docs.Where(d => d.Name.Contains(query.Search!, StringComparison.OrdinalIgnoreCase));
            }

            // Scope.Library reaches every folder (FolderPath is ignored); Subtree is the folder plus its descendants;
            // Folder is the direct content. A Folder-scope query with a search keeps the pre-Scope behaviour: the
            // search spans every folder (the dialog now says so with Library instead).
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

            IEnumerable<StoredDocument> sorted = query.SortField switch
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
                .Select(ToEntry)
                .ToList();

            return new DocumentLibraryPage { Items = items, TotalCount = total };
        }
    }

    public DocumentLibraryFolder GetFolderTree(TempoDocumentKind kind)
    {
        lock (_gate)
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

            return Build("/");
        }
    }

    public void CreateFolder(TempoDocumentKind kind, string parentPath, string name)
    {
        lock (_gate)
        {
            var path = parentPath == "/" ? "/" + name : parentPath + "/" + name;
            if (!_folders[kind].Add(path))
            {
                throw new InvalidOperationException($"Folder '{path}' already exists.");
            }
        }
    }

    public void RenameFolder(TempoDocumentKind kind, string folderPath, string newName)
    {
        lock (_gate)
        {
            var newPath = ParentOf(folderPath) is "/" ? "/" + newName : ParentOf(folderPath) + "/" + newName;

            var folders = _folders[kind];
            foreach (var p in folders.Where(p => p == folderPath || p.StartsWith(folderPath + "/", StringComparison.Ordinal)).ToList())
            {
                folders.Remove(p);
                folders.Add(newPath + p[folderPath.Length..]);
            }

            foreach (var d in _docs[kind])
            {
                if (d.FolderPath == folderPath || d.FolderPath.StartsWith(folderPath + "/", StringComparison.Ordinal))
                {
                    d.FolderPath = newPath + d.FolderPath[folderPath.Length..];
                }
            }
        }
    }

    public void DeleteFolder(TempoDocumentKind kind, string folderPath)
    {
        lock (_gate)
        {
            _folders[kind].RemoveWhere(p =>
                p == folderPath || p.StartsWith(folderPath + "/", StringComparison.Ordinal));

            var removed = _docs[kind]
                .Where(d => d.FolderPath == folderPath || d.FolderPath.StartsWith(folderPath + "/", StringComparison.Ordinal))
                .Select(d => d.Id)
                .ToList();
            _docs[kind].RemoveAll(d => removed.Contains(d.Id));
            foreach (var id in removed)
            {
                Publish(kind, id, TempoDocumentChangeType.Deleted, DateTime.UtcNow);
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DocumentLibraryEntry ToEntry(StoredDocument d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Kind = d.Kind,
        FolderPath = d.FolderPath,
        CreatedAt = d.CreatedAt,
        ModifiedAt = d.ModifiedAt,
        Author = d.Author,
        PreviewSvg = d.PreviewSvg
    };

    private static string NormalizeFolder(string? path)
        => string.IsNullOrEmpty(path) ? "/" : path.Length > 1 ? path.TrimEnd('/') : path;

    private static string ParentOf(string path)
    {
        if (path == "/")
        {
            return "/";
        }

        var idx = path.LastIndexOf('/');
        return idx <= 0 ? "/" : path[..idx];
    }

    private void Publish(TempoDocumentKind kind, Guid id, TempoDocumentChangeType type, DateTime modifiedAt)
    {
        _ = _publisher.PublishAsync(new TempoDocumentChange
        {
            Kind = kind,
            DocumentId = id,
            ChangeType = type,
            ModifiedAt = modifiedAt,
            Origin = "store"
        });
    }
}
