using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tempo.Blazor.Demo.Shared;
using Tempo.Blazor.DocumentLibrary;

namespace Tempo.Blazor.Demo.Services;

/// <summary>
/// <see cref="ITempoDocumentLibraryProvider"/> backed by the Demo.Api document-library REST
/// endpoints, so library browsing/management is shared with MCP tooling and live refresh.
/// </summary>
public sealed class ApiTempoDocumentLibraryProvider : ITempoDocumentLibraryProvider
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public ApiTempoDocumentLibraryProvider(IHttpClientFactory factory)
        => _http = factory.CreateClient("DemoApi");

    public DocumentLibraryCapabilities Capabilities => DocumentLibraryCapabilities.All;

    private static string Kind(TempoDocumentKind kind) => kind.ToString().ToLowerInvariant();

    public async Task<DocumentLibraryFolder> GetFolderTreeAsync(
        TempoDocumentKind kind, CancellationToken cancellationToken = default)
        => (await _http.GetFromJsonAsync<DocumentLibraryFolder>(
                $"/api/document-library/{Kind(kind)}/tree", Json, cancellationToken))
            ?? new DocumentLibraryFolder { Path = "/", Name = "/" };

    public async Task<DocumentLibraryPage> BrowseAsync(
        DocumentLibraryQuery query, CancellationToken cancellationToken = default)
    {
        var url =
            $"/api/document-library/{Kind(query.Kind)}/browse" +
            $"?sortField={query.SortField}&descending={query.Descending.ToString().ToLowerInvariant()}" +
            $"&skip={query.Skip}&take={query.Take}";
        if (!string.IsNullOrEmpty(query.FolderPath))
        {
            url += $"&folderPath={Uri.EscapeDataString(query.FolderPath)}";
        }
        if (!string.IsNullOrEmpty(query.Search))
        {
            url += $"&search={Uri.EscapeDataString(query.Search)}";
        }
        if (query.Scope != DocumentLibraryScope.Folder)
        {
            url += $"&scope={query.Scope}";
        }

        return (await _http.GetFromJsonAsync<DocumentLibraryPage>(url, Json, cancellationToken))
            ?? new DocumentLibraryPage();
    }

    public async Task<DocumentLibraryEntry?> GetEntryAsync(
        TempoDocumentKind kind, Guid documentId, CancellationToken cancellationToken = default)
    {
        var resp = await _http.GetAsync(
            $"/api/document-library/{Kind(kind)}/documents/{documentId}", cancellationToken);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        resp.EnsureSuccessStatusCode();
        var meta = await resp.Content.ReadFromJsonAsync<DocumentLibraryMetadataDto>(Json, cancellationToken);
        return meta is null ? null : new DocumentLibraryEntry
        {
            Id = meta.Id,
            Name = meta.Name,
            Kind = meta.Kind,
            FolderPath = meta.FolderPath,
            CreatedAt = meta.CreatedAt,
            ModifiedAt = meta.ModifiedAt,
            Author = meta.Author,
            PreviewSvg = meta.PreviewSvg
        };
    }

    public async Task<DocumentLibraryFolder> CreateFolderAsync(
        TempoDocumentKind kind, string parentPath, string name,
        CancellationToken cancellationToken = default)
    {
        var resp = await _http.PostAsJsonAsync(
            $"/api/document-library/{Kind(kind)}/folders",
            new DocumentLibraryCreateFolderRequest { ParentPath = parentPath, Name = name },
            cancellationToken);
        if (resp.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException($"Folder '{name}' already exists.");
        }
        resp.EnsureSuccessStatusCode();

        var path = parentPath == "/" ? "/" + name : parentPath + "/" + name;
        return new DocumentLibraryFolder { Path = path, Name = name };
    }

    public async Task RenameDocumentAsync(
        TempoDocumentKind kind, Guid documentId, string newName,
        CancellationToken cancellationToken = default)
    {
        var resp = await _http.PutAsJsonAsync(
            $"/api/document-library/{Kind(kind)}/documents/{documentId}/rename",
            new DocumentLibraryRenameDocumentRequest { NewName = newName },
            cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public async Task RenameFolderAsync(
        TempoDocumentKind kind, string folderPath, string newName,
        CancellationToken cancellationToken = default)
    {
        var resp = await _http.PutAsJsonAsync(
            $"/api/document-library/{Kind(kind)}/folders/rename",
            new DocumentLibraryRenameFolderRequest { FolderPath = folderPath, NewName = newName },
            cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public async Task DeleteDocumentsAsync(
        TempoDocumentKind kind, IReadOnlyList<Guid> documentIds,
        CancellationToken cancellationToken = default)
    {
        var resp = await _http.PostAsJsonAsync(
            $"/api/document-library/{Kind(kind)}/documents/delete",
            new DocumentLibraryDeleteDocumentsRequest { Ids = documentIds.ToList() },
            cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public async Task DeleteFolderAsync(
        TempoDocumentKind kind, string folderPath,
        CancellationToken cancellationToken = default)
    {
        var resp = await _http.DeleteAsync(
            $"/api/document-library/{Kind(kind)}/folders?folderPath={Uri.EscapeDataString(folderPath)}",
            cancellationToken);
        resp.EnsureSuccessStatusCode();
    }
}
