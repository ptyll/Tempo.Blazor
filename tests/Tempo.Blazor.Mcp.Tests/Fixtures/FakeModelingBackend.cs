using Tempo.Blazor.DocumentLibrary;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Mcp.Tests.Fixtures;

/// <summary>
/// In-memory backend implementing both the document library and the modeling model document
/// provider, so the modeling MCP tools can be exercised end to end without a server.
/// </summary>
public sealed class FakeModelingBackend : ITempoDocumentLibraryProvider, IModelingModelDocumentProvider
{
    private sealed class Entry
    {
        public required ModelingModelDto Model { get; set; }
        public required string Name { get; set; }
        public required string FolderPath { get; set; }
        public DateTime CreatedAt { get; init; }
        public DateTime ModifiedAt { get; set; }
    }

    private readonly Dictionary<Guid, Entry> _docs = new();

    /// <summary>Last scopeAppId received by <see cref="CreateModelingModelDocumentAsync(string, string?)"/>.</summary>
    public string? LastCreateScopeAppId { get; private set; }

    /// <summary>Last scopeAppId received by <see cref="BrowseAsync"/>.</summary>
    public string? LastBrowseScopeAppId { get; private set; }

    /// <summary>Number of times a model was saved (asserting no-write on invalid batches).</summary>
    public int SaveCount { get; private set; }

    /// <summary>When set, <see cref="SaveModelingModelDocumentAsync"/> throws for the document
    /// created with this title (exercising the import tool's per-document save failure path).</summary>
    public string? FailSaveForTitle { get; set; }

    public DocumentLibraryCapabilities Capabilities => DocumentLibraryCapabilities.All;

    // ── Seeding helper ──────────────────────────────────────────────────────────

    public Guid Add(string name, string folderPath, ModelingModelDto model)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        model.Title = name;
        _docs[id] = new Entry
        {
            Model = model,
            Name = name,
            FolderPath = folderPath,
            CreatedAt = now,
            ModifiedAt = now
        };
        return id;
    }

    public DateTime ModifiedAtOf(Guid id) => _docs[id].ModifiedAt;

    public ModelingModelDto ModelOf(Guid id) => _docs[id].Model;

    // ── IModelingModelDocumentProvider ───────────────────────────────────────────

    public Task<ModelingModelDto?> GetModelingModelDocumentAsync(Guid documentId)
        => Task.FromResult(_docs.TryGetValue(documentId, out var e) ? e.Model : null);

    public Task<ModelingModelDto> SaveModelingModelDocumentAsync(Guid documentId, ModelingModelDto model)
    {
        if (FailSaveForTitle is not null && _docs.TryGetValue(documentId, out var failing)
            && string.Equals(failing.Name, FailSaveForTitle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("boom");
        }

        SaveCount++;
        if (_docs.TryGetValue(documentId, out var e))
        {
            e.Model = model;
            e.ModifiedAt = DateTime.UtcNow.AddMilliseconds(1);
        }
        return Task.FromResult(model);
    }

    public Task<(Guid Id, ModelingModelDto Document)> CreateModelingModelDocumentAsync(string title)
        => CreateModelingModelDocumentAsync(title, null);

    public Task<(Guid Id, ModelingModelDto Document)> CreateModelingModelDocumentAsync(string title, string? scopeAppId)
    {
        LastCreateScopeAppId = scopeAppId;
        var model = new ModelingModelDto { Title = string.IsNullOrWhiteSpace(title) ? "Untitled model" : title };
        var id = Add(model.Title, "/", model);
        return Task.FromResult((id, model));
    }

    // ── ITempoDocumentLibraryProvider ────────────────────────────────────────────

    public Task<DocumentLibraryFolder> GetFolderTreeAsync(TempoDocumentKind kind, CancellationToken ct = default)
        => Task.FromResult(new DocumentLibraryFolder { Path = "/", Name = "/" });

    public Task<DocumentLibraryPage> BrowseAsync(DocumentLibraryQuery query, CancellationToken ct = default)
    {
        LastBrowseScopeAppId = query.ScopeAppId;
        IEnumerable<KeyValuePair<Guid, Entry>> docs = _docs;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            docs = docs.Where(d => d.Value.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase));
        }
        else if (!string.IsNullOrEmpty(query.FolderPath))
        {
            docs = docs.Where(d => d.Value.FolderPath == query.FolderPath);
        }

        var matched = docs.ToList();
        var items = matched
            .OrderBy(d => d.Value.Name, StringComparer.OrdinalIgnoreCase)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(d => ToEntry(d.Key, d.Value))
            .ToList();

        return Task.FromResult(new DocumentLibraryPage { Items = items, TotalCount = matched.Count });
    }

    public Task<DocumentLibraryEntry?> GetEntryAsync(TempoDocumentKind kind, Guid documentId, CancellationToken ct = default)
        => Task.FromResult(_docs.TryGetValue(documentId, out var e) ? ToEntry(documentId, e) : null);

    private static DocumentLibraryEntry ToEntry(Guid id, Entry e) => new()
    {
        Id = id,
        Name = e.Name,
        Kind = TempoDocumentKind.Modeling,
        FolderPath = e.FolderPath,
        CreatedAt = e.CreatedAt,
        ModifiedAt = e.ModifiedAt
    };

    public Task<DocumentLibraryFolder> CreateFolderAsync(TempoDocumentKind kind, string parentPath, string name, CancellationToken ct = default)
        => Task.FromResult(new DocumentLibraryFolder { Path = parentPath == "/" ? "/" + name : parentPath + "/" + name, Name = name });

    public Task RenameDocumentAsync(TempoDocumentKind kind, Guid documentId, string newName, CancellationToken ct = default)
    {
        if (_docs.TryGetValue(documentId, out var e)) { e.Name = newName; e.ModifiedAt = DateTime.UtcNow; }
        return Task.CompletedTask;
    }

    public Task RenameFolderAsync(TempoDocumentKind kind, string folderPath, string newName, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteDocumentsAsync(TempoDocumentKind kind, IReadOnlyList<Guid> documentIds, CancellationToken ct = default)
    {
        foreach (var id in documentIds) _docs.Remove(id);
        return Task.CompletedTask;
    }

    public Task DeleteFolderAsync(TempoDocumentKind kind, string folderPath, CancellationToken ct = default)
        => Task.CompletedTask;
}
