using Tempo.Blazor.Demo.Api.Data;
using Tempo.Blazor.Demo.Shared;
using Tempo.Blazor.DocumentLibrary;

namespace Tempo.Blazor.Demo.Api.Endpoints;

/// <summary>
/// REST surface over <see cref="DocumentLibraryStore"/>: folder tree, browse/search, folder
/// and document management, and per-kind document payload get/create/save (with optimistic
/// concurrency). Backs both the open dialog and MCP tooling.
/// </summary>
public static class DocumentLibraryEndpoints
{
    public static void MapDocumentLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/document-library").WithTags("Document Library");

        // ── Browse / tree ─────────────────────────────────────────────────────
        group.MapGet("/{kind}/tree", (string kind, DocumentLibraryStore store) =>
            Results.Ok(store.GetFolderTree(ParseKind(kind))));

        group.MapGet("/{kind}/browse", (
            string kind,
            DocumentLibraryStore store,
            string? folderPath,
            string? search,
            string? scope,
            DocumentLibrarySortField? sortField,
            bool? descending,
            int? skip,
            int? take) =>
        {
            // Decision DOCLIB-QUERY-SCOPE (docs/adr/0003-document-library-query-scope.md): ?scope= is parsed here so an
            // undefined value (a numeric 99, a typo) is a 400 instead of silently behaving like Folder.
            if (!TryParseScope(scope, out var parsedScope))
            {
                return Results.BadRequest(
                    $"Unknown scope '{scope}'. Expected one of: {string.Join(", ", Enum.GetNames<DocumentLibraryScope>())}.");
            }

            return Results.Ok(store.Browse(new DocumentLibraryQuery
            {
                Kind = ParseKind(kind),
                FolderPath = folderPath,
                Search = search,
                Scope = parsedScope,
                SortField = sortField ?? DocumentLibrarySortField.Name,
                Descending = descending ?? false,
                Skip = skip ?? 0,
                Take = take ?? 50
            }));
        });

        // ── Folder management ───────────────────────────────────────────────────
        group.MapPost("/{kind}/folders", (
            string kind, DocumentLibraryCreateFolderRequest request, DocumentLibraryStore store) =>
        {
            try
            {
                store.CreateFolder(ParseKind(kind), request.ParentPath, request.Name);
                return Results.Ok();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ex.Message);
            }
        });

        group.MapPut("/{kind}/folders/rename", (
            string kind, DocumentLibraryRenameFolderRequest request, DocumentLibraryStore store) =>
        {
            store.RenameFolder(ParseKind(kind), request.FolderPath, request.NewName);
            return Results.NoContent();
        });

        group.MapDelete("/{kind}/folders", (
            string kind, string folderPath, DocumentLibraryStore store) =>
        {
            store.DeleteFolder(ParseKind(kind), folderPath);
            return Results.NoContent();
        });

        // ── Document management ─────────────────────────────────────────────────
        group.MapPut("/{kind}/documents/{id:guid}/rename", (
            string kind, Guid id, DocumentLibraryRenameDocumentRequest request, DocumentLibraryStore store) =>
        {
            store.RenameDocument(ParseKind(kind), id, request.NewName);
            return Results.NoContent();
        });

        group.MapPost("/{kind}/documents/delete", (
            string kind, DocumentLibraryDeleteDocumentsRequest request, DocumentLibraryStore store) =>
        {
            store.DeleteDocuments(ParseKind(kind), request.Ids);
            return Results.NoContent();
        });

        // ── Document payload ──────────────────────────────────────────────────
        group.MapGet("/{kind}/documents/{id:guid}", (
            string kind, Guid id, DocumentLibraryStore store) =>
        {
            var doc = store.GetDocument(ParseKind(kind), id);
            return doc is null ? Results.NotFound() : Results.Ok(ToMetadata(doc));
        });

        group.MapGet("/{kind}/documents/{id:guid}/payload", (
            string kind, Guid id, DocumentLibraryStore store) =>
        {
            var doc = store.GetDocument(ParseKind(kind), id);
            return doc is null
                ? Results.NotFound()
                : Results.Content(doc.PayloadJson, "application/json");
        });

        group.MapPost("/{kind}/documents", (
            string kind, DocumentLibraryCreateRequest request, DocumentLibraryStore store) =>
        {
            var doc = store.CreateDocument(
                ParseKind(kind), request.Name, request.FolderPath, request.PayloadJson, request.PreviewSvg, request.Author);
            return Results.Ok(ToMetadata(doc));
        });

        group.MapPut("/{kind}/documents/{id:guid}/payload", (
            string kind, Guid id, DocumentLibrarySaveRequest request, DocumentLibraryStore store) =>
        {
            try
            {
                var doc = store.SaveDocument(
                    ParseKind(kind), id, request.PayloadJson, request.PreviewSvg, request.ExpectedModifiedAt, request.Name);
                return Results.Ok(ToMetadata(doc));
            }
            catch (TempoDocumentConflictException ex)
            {
                return Results.Conflict(new { conflict = true, currentModifiedAt = ex.CurrentModifiedAt });
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound();
            }
        });
    }

    private static bool TryParseScope(string? raw, out DocumentLibraryScope scope)
    {
        scope = DocumentLibraryScope.Folder;
        if (string.IsNullOrEmpty(raw))
        {
            return true;
        }

        return Enum.TryParse(raw, ignoreCase: true, out scope) && Enum.IsDefined(scope);
    }
    private static TempoDocumentKind ParseKind(string kind)
        => Enum.Parse<TempoDocumentKind>(kind, ignoreCase: true);

    private static DocumentLibraryMetadataDto ToMetadata(DocumentLibraryStore.StoredDocument doc) => new()
    {
        Id = doc.Id,
        Name = doc.Name,
        Kind = doc.Kind,
        FolderPath = doc.FolderPath,
        CreatedAt = doc.CreatedAt,
        ModifiedAt = doc.ModifiedAt,
        Author = doc.Author,
        PreviewSvg = doc.PreviewSvg
    };
}
