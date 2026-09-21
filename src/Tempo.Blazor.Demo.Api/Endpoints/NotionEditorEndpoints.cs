using Microsoft.AspNetCore.Mvc;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Demo.Api.Data;
using Tempo.Blazor.Demo.Api.Hubs;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Demo.Api.Endpoints;

public static class NotionEditorEndpoints
{
    private static readonly SemaphoreSlim E2ESeedGate = new(1, 1);

    public static void MapNotionEditorEndpoints(this IEndpointRouteBuilder app)
    {
        var pageGroup = app.MapGroup("/api/notion/pages").WithTags("Notion Editor");
        var taskGroup = app.MapGroup("/api/notion/tasks").WithTags("Notion Editor");
        var reactionGroup = app.MapGroup("/api/notion/reactions").WithTags("Notion Editor");
        var templateGroup = app.MapGroup("/api/notion/templates").WithTags("Notion Editor");
        var blogGroup = app.MapGroup("/api/notion/blog").WithTags("Notion Editor");
        var watchGroup = app.MapGroup("/api/notion/watches").WithTags("Notion Editor");
        var spaceGroup = app.MapGroup("/api/notion/spaces").WithTags("Notion Editor");
        var notificationGroup = app.MapGroup("/api/notion/notifications").WithTags("Notion Editor");
        var permissionGroup = app.MapGroup("/api/notion/permissions").WithTags("Notion Editor");
        var auditGroup = app.MapGroup("/api/notion/audit").WithTags("Notion Editor");
        var publicShareGroup = app.MapGroup("/api/notion/public-shares").WithTags("Notion Editor");
        var historyGroup = app.MapGroup("/api/notion/history").WithTags("Notion Editor");
        var workItemGroup = app.MapGroup("/api/notion/work-items").WithTags("Notion Editor");
        var bookmarkGroup = app.MapGroup("/api/notion/bookmarks").WithTags("Notion Editor");
        var smartLinkGroup = app.MapGroup("/api/notion/smart-links").WithTags("Notion Editor");
        var syncedBlockGroup = app.MapGroup("/api/notion/synced-blocks").WithTags("Notion Editor");
        var aggregateGroup = app.MapGroup("/api/notion/aggregate").WithTags("Notion Editor");

        aggregateGroup.MapGet("/pages/{pageId:guid}", async (
            Guid pageId,
            DemoNotionAggregateStore store) =>
        {
            var result = await store.LoadPageAsync(pageId);
            return Results.Json(
                result,
                NotionAggregateJson.Options,
                statusCode: result.Found ? StatusCodes.Status200OK : StatusCodes.Status404NotFound);
        });
        aggregateGroup.MapGet("/blocks/{blockId:guid}", async (
            Guid blockId,
            DemoNotionAggregateStore store) =>
        {
            var result = await store.LoadBlockAsync(blockId);
            return Results.Json(
                result,
                NotionAggregateJson.Options,
                statusCode: result.Found ? StatusCodes.Status200OK : StatusCodes.Status404NotFound);
        });
        aggregateGroup.MapPost("/save", async (
            HttpRequest request,
            DemoNotionAggregateStore store,
            DemoNotionWatchProvider watchProvider,
            DemoNotionNotificationStore notificationStore,
            CancellationToken cancellationToken) =>
        {
            var payload = await request.ReadFromJsonAsync<NotionAggregateSaveRequest>(
                NotionAggregateJson.Options,
                cancellationToken);
            if (payload is null)
            {
                return Results.BadRequest();
            }
            var result = store.Save(payload);
            if (result.Success)
            {
                // A committed aggregate save IS a page edit — notify watchers exactly like the
                // removed granular block endpoint did. The actor comes from the page snapshot the
                // editor stamped (LastEditedByUserId), falling back to the audit user header.
                var fallbackActor = GetAuditUser(request).UserId;
                foreach (var pageSave in payload.Pages)
                {
                    var actor = string.IsNullOrWhiteSpace(pageSave.Snapshot.Page.LastEditedByUserId)
                        ? fallbackActor
                        : pageSave.Snapshot.Page.LastEditedByUserId!;
                    await NotifyPageWatchersAsync(
                        pageSave.Snapshot.Page.Id,
                        actor,
                        watchProvider,
                        notificationStore,
                        cancellationToken);
                }
            }
            return Results.Json(
                result,
                NotionAggregateJson.Options,
                statusCode: result.Conflict
                    ? StatusCodes.Status409Conflict
                    : result.Success
                        ? StatusCodes.Status200OK
                        : StatusCodes.Status400BadRequest);
        });
        aggregateGroup.MapPost("/e2e/advance-token/{pageId:guid}", (
            Guid pageId,
            DemoNotionAggregateStore store) =>
            store.AdvanceConcurrencyToken(pageId)
                ? Results.NoContent()
                : Results.NotFound());

        app.MapPost("/api/notion/page-properties/report", (PagePropertiesReportQuery query, MockNotionDataStore dataStore, MockNotionBlockStore blockStore) =>
            Results.Ok(BuildPagePropertiesReport(query, dataStore, blockStore)));

        // ── Provider APIs ─────────────────────────────────────────────────────

        workItemGroup.MapGet("/{providerKey}/{externalId}", (string providerKey, string externalId, DemoWorkItemStore store) =>
        {
            var workItem = store.GetById(providerKey, externalId);
            return workItem is null ? Results.NotFound() : Results.Ok(workItem);
        });

        workItemGroup.MapPost("/query", (TmWorkItemQuery query, DemoWorkItemStore store) =>
            Results.Ok(store.Search(query)));

        bookmarkGroup.MapPost("/resolve", (BookmarkResolveRequest request, MockNotionBookmarkStore store) =>
            Results.Ok(store.Resolve(request.Url)));

        smartLinkGroup.MapPost("/resolve", (SmartLinkResolveRequest request, MockNotionBookmarkStore store) =>
        {
            try
            {
                return Results.Ok(store.ResolveSmartLink(request.Url));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (UriFormatException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        syncedBlockGroup.MapGet("/{syncId:guid}/children", (Guid syncId, MockNotionBlockStore blockStore) =>
            Results.Ok(blockStore.GetSyncedChildBlocks(syncId)));

        syncedBlockGroup.MapPut("/{syncId:guid}/children", (Guid syncId, List<PageBlock> children, MockNotionBlockStore blockStore) =>
        {
            try
            {
                blockStore.UpdateSyncedChildBlocks(syncId, children);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        syncedBlockGroup.MapGet("/{syncId:guid}/refs", (Guid syncId, MockNotionBlockStore blockStore) =>
            Results.Ok(blockStore.GetSyncedRefs(syncId)));

        syncedBlockGroup.MapPost("/{syncId:guid}/refs", (Guid syncId, CreateSyncRefRequest request, MockNotionBlockStore blockStore) =>
        {
            if (!Guid.TryParse(request.TargetPageId, out var targetPageId))
                return Results.BadRequest(new { error = "TargetPageId must be a valid GUID." });

            Guid? afterBlockId = null;
            if (!string.IsNullOrWhiteSpace(request.AfterBlockId))
            {
                if (!Guid.TryParse(request.AfterBlockId, out var parsedAfterBlockId))
                    return Results.BadRequest(new { error = "AfterBlockId must be a valid GUID." });

                afterBlockId = parsedAfterBlockId;
            }

            try
            {
                return Results.Ok(blockStore.CreateSyncedRef(syncId, targetPageId, afterBlockId));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        syncedBlockGroup.MapPost("/refs/{blockId:guid}/unsync", (Guid blockId, MockNotionBlockStore blockStore) =>
        {
            try
            {
                return Results.Ok(blockStore.UnsyncSyncedRef(blockId));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        permissionGroup.MapGet("/pages/{pageId:guid}", async (Guid pageId, DemoNotionPermissionProvider permissionProvider, CancellationToken cancellationToken) =>
            Results.Ok(await permissionProvider.GetRestrictionsAsync(pageId, cancellationToken)));

        permissionGroup.MapPut("/pages/{pageId:guid}", async (
            Guid pageId,
            PageRestrictionDto restrictions,
            HttpRequest httpRequest,
            DemoNotionPermissionProvider permissionProvider,
            DemoNotionAuditProvider auditProvider,
            CancellationToken cancellationToken) =>
        {
            restrictions.PageId = pageId;
            await permissionProvider.SetRestrictionsAsync(restrictions, cancellationToken);
            await LogAuditAsync(httpRequest, auditProvider, DemoNotionAuditProvider.ActionRestrict, pageId, new Dictionary<string, string>
            {
                ["mode"] = restrictions.Mode.ToString(),
                ["entries"] = restrictions.Entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }, cancellationToken);
            return Results.NoContent();
        });

        permissionGroup.MapGet("/pages/{pageId:guid}/effective/{userId}", async (
            Guid pageId,
            string userId,
            string? groups,
            DemoNotionPermissionProvider permissionProvider,
            CancellationToken cancellationToken) =>
        {
            var groupIds = string.IsNullOrWhiteSpace(groups)
                ? Array.Empty<string>()
                : groups.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return Results.Ok(await permissionProvider.GetEffectivePermissionAsync(pageId, userId, groupIds, cancellationToken));
        });

        auditGroup.MapPost("/entries", async (TmActivityEntry entry, DemoNotionAuditProvider auditProvider, CancellationToken cancellationToken) =>
        {
            var created = await auditProvider.AppendAsync(entry, cancellationToken);
            return Results.Ok(created);
        });

        auditGroup.MapGet("/entries", async (
            string? userId,
            string? action,
            string? targetType,
            string? targetId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? correlationId,
            int? skip,
            int? take,
            DemoNotionAuditProvider auditProvider,
            CancellationToken cancellationToken) =>
        {
            var query = new TmActivityQuery
            {
                SearchText = userId,
                Action = action,
                EntityType = targetType,
                EntityId = targetId,
                CorrelationId = correlationId,
                From = from,
                To = to,
                Skip = Math.Max(0, skip.GetValueOrDefault()),
                Take = Math.Clamp(take.GetValueOrDefault(10), 1, 100)
            };
            return Results.Ok(await auditProvider.QueryAsync(query, cancellationToken));
        });

        publicShareGroup.MapPost("/pages/{pageId:guid}", async (
            Guid pageId,
            PublicShareOptions options,
            DemoNotionPublicShareProvider publicShareProvider,
            CancellationToken cancellationToken) =>
            Results.Ok(await publicShareProvider.CreateShareAsync(pageId, options, cancellationToken)));

        publicShareGroup.MapGet("/pages/{pageId:guid}", async (
            Guid pageId,
            DemoNotionPublicShareProvider publicShareProvider,
            CancellationToken cancellationToken) =>
        {
            var share = await publicShareProvider.GetShareAsync(pageId, cancellationToken);
            return share is null ? Results.NotFound() : Results.Ok(share);
        });

        publicShareGroup.MapDelete("/pages/{pageId:guid}", async (
            Guid pageId,
            DemoNotionPublicShareProvider publicShareProvider,
            CancellationToken cancellationToken) =>
        {
            await publicShareProvider.RevokeAsync(pageId, cancellationToken);
            return Results.NoContent();
        });

        publicShareGroup.MapGet("/tokens/{token}", async (
            string token,
            DemoNotionPublicShareProvider publicShareProvider,
            CancellationToken cancellationToken) =>
        {
            var share = await publicShareProvider.ResolveByTokenAsync(token, cancellationToken);
            return share is null ? Results.NotFound() : Results.Ok(share);
        });

        // ── Page CRUD ─────────────────────────────────────────────────────────

        pageGroup.MapGet("/", (MockNotionDataStore store) =>
            Results.Ok(store.GetAllPages()));

        // Literal routes must be registered before parameterised {pageId}
        pageGroup.MapGet("/root/children", (MockNotionDataStore store) =>
            Results.Ok(store.GetChildPagesAsync(null).Result));

        pageGroup.MapGet("/favorites", (MockNotionDataStore store) =>
            Results.Ok(store.GetFavoritesAsync().Result));

        pageGroup.MapGet("/recent/{count}", (int count, MockNotionDataStore store) =>
            Results.Ok(store.GetRecentPagesAsync(count).Result));

        pageGroup.MapGet("/trash", (MockNotionDataStore store) =>
            Results.Ok(store.GetTrashAsync().Result));

        pageGroup.MapGet("/labels", (MockNotionDataStore store) =>
            Results.Ok(store.GetAllLabelsAsync().Result));

        pageGroup.MapGet("/labels/{label}", (string label, MockNotionDataStore store) =>
            Results.Ok(store.GetPagesByLabelAsync(label).Result));

        pageGroup.MapPost("/bulk/move", async (
            BulkMovePagesRequest request,
            HttpRequest httpRequest,
            MockNotionDataStore store,
            DemoNotionAuditProvider auditProvider,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await store.MovePagesAsync(request.PageIds, request.NewParentId, cancellationToken);
                foreach (var pageId in request.PageIds)
                {
                    if (Guid.TryParse(pageId, out var id))
                    {
                        await LogAuditAsync(httpRequest, auditProvider, DemoNotionAuditProvider.ActionMove, id, new Dictionary<string, string>
                        {
                            ["newParentId"] = request.NewParentId ?? string.Empty
                        }, cancellationToken);
                    }
                }

                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException)
            {
                return Results.BadRequest();
            }
            catch (FormatException)
            {
                return Results.BadRequest();
            }
        });

        pageGroup.MapPost("/bulk/delete", async (
            BulkDeletePagesRequest request,
            HttpRequest httpRequest,
            MockNotionDataStore store,
            DemoNotionAuditProvider auditProvider,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await store.DeletePagesAsync(request.PageIds, cancellationToken);
                foreach (var pageId in request.PageIds)
                {
                    if (Guid.TryParse(pageId, out var id))
                        await LogAuditAsync(httpRequest, auditProvider, DemoNotionAuditProvider.ActionDelete, id, new Dictionary<string, string>(), cancellationToken);
                }

                return Results.NoContent();
            }
            catch (FormatException)
            {
                return Results.BadRequest();
            }
        });

        pageGroup.MapGet("/{pageId}", (string pageId, MockNotionDataStore store) =>
        {
            try   { return Results.Ok(store.GetPageAsync(pageId).Result); }
            catch { return Results.NotFound(); }
        });

        pageGroup.MapGet("/{parentId}/children", (string parentId, MockNotionDataStore store) =>
            Results.Ok(store.GetChildPagesAsync(parentId).Result));

        pageGroup.MapPost("/", async (CreatePageRequest request, HttpRequest httpRequest, MockNotionDataStore store, DemoNotionAuditProvider auditProvider, CancellationToken cancellationToken) =>
        {
            var page = await store.CreatePageAsync(request.ParentId, request.Title);
            await LogAuditAsync(httpRequest, auditProvider, DemoNotionAuditProvider.ActionCreate, page.Id, new Dictionary<string, string>
            {
                ["title"] = page.Title
            }, cancellationToken);
            return Results.Created($"/api/notion/pages/{page.Id}", page);
        });

        pageGroup.MapPut("/{pageId}", async (string pageId, UpdatePageRequest request, HttpRequest httpRequest, MockNotionDataStore store, DemoNotionAuditProvider auditProvider, CancellationToken cancellationToken) =>
        {
            try
            {
                var page = await store.GetPageAsync(pageId);
                if (page is NotionPage notionPage)
                {
                    notionPage.Title        = request.Title        ?? notionPage.Title;
                    notionPage.Description  = request.Description  ?? notionPage.Description;
                    notionPage.IconEmoji    = request.IconEmoji    ?? notionPage.IconEmoji;
                    notionPage.IsFullWidth  = request.IsFullWidth  ?? notionPage.IsFullWidth;
                    notionPage.IsSmallText  = request.IsSmallText  ?? notionPage.IsSmallText;
                    notionPage.IsLocked     = request.IsLocked     ?? notionPage.IsLocked;
                    await store.UpdatePageAsync(notionPage);
                    await LogAuditAsync(httpRequest, auditProvider, DemoNotionAuditProvider.ActionEdit, notionPage.Id, new Dictionary<string, string>
                    {
                        ["title"] = notionPage.Title
                    }, cancellationToken);
                    return Results.Ok(notionPage);
                }
                return Results.BadRequest();
            }
            catch { return Results.NotFound(); }
        });

        pageGroup.MapPut("/{pageId}/labels", (Guid pageId, IReadOnlyList<string> labels, MockNotionDataStore store) =>
        {
            try
            {
                store.SetPageLabelsAsync(pageId, labels).Wait();
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        pageGroup.MapDelete("/{pageId}", async (string pageId, HttpRequest httpRequest, MockNotionDataStore store, DemoNotionAuditProvider auditProvider, CancellationToken cancellationToken) =>
        {
            await store.DeletePageAsync(pageId);
            if (Guid.TryParse(pageId, out var id))
                await LogAuditAsync(httpRequest, auditProvider, DemoNotionAuditProvider.ActionDelete, id, new Dictionary<string, string>(), cancellationToken);
            return Results.NoContent();
        });

        pageGroup.MapDelete("/{pageId}/permanent", async (string pageId, HttpRequest httpRequest, MockNotionDataStore store, DemoNotionAuditProvider auditProvider, CancellationToken cancellationToken) =>
        {
            await store.PermanentlyDeletePageAsync(pageId);
            if (Guid.TryParse(pageId, out var id))
                await LogAuditAsync(httpRequest, auditProvider, DemoNotionAuditProvider.ActionDelete, id, new Dictionary<string, string>
                {
                    ["permanent"] = bool.TrueString
                }, cancellationToken);
            return Results.NoContent();
        });

        pageGroup.MapPost("/{pageId}/restore", (string pageId, MockNotionDataStore store) =>
        {
            store.RestorePageAsync(pageId).Wait();
            return Results.NoContent();
        });

        pageGroup.MapPost("/{pageId}/move", async (string pageId, MovePageRequest req, HttpRequest httpRequest, MockNotionDataStore store, DemoNotionAuditProvider auditProvider, CancellationToken cancellationToken) =>
        {
            await store.MovePageAsync(pageId, req.NewParentId);
            if (Guid.TryParse(pageId, out var id))
                await LogAuditAsync(httpRequest, auditProvider, DemoNotionAuditProvider.ActionMove, id, new Dictionary<string, string>
                {
                    ["newParentId"] = req.NewParentId ?? string.Empty
                }, cancellationToken);
            return Results.NoContent();
        });

        pageGroup.MapPost("/{pageId}/duplicate", (string pageId, MockNotionDataStore store) =>
        {
            try
            {
                var dup = store.DuplicatePageAsync(pageId).Result;
                return Results.Created($"/api/notion/pages/{dup.Id}", dup);
            }
            catch { return Results.NotFound(); }
        });

        pageGroup.MapPost("/{pageId}/copy-tree", async (
            string pageId,
            CopyPageTreeRequest request,
            HttpRequest httpRequest,
            MockNotionDataStore store,
            DemoNotionAuditProvider auditProvider,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await store.CopyPageTreeAsync(pageId, request.NewParentId, cancellationToken);
                await LogAuditAsync(httpRequest, auditProvider, DemoNotionAuditProvider.ActionCreate, result.RootPage.Id, new Dictionary<string, string>
                {
                    ["sourcePageId"] = pageId,
                    ["newParentId"] = request.NewParentId ?? string.Empty
                }, cancellationToken);

                return Results.Created($"/api/notion/pages/{result.RootPage.Id}", result.RootPage);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (FormatException)
            {
                return Results.BadRequest();
            }
        });

        pageGroup.MapPost("/{pageId}/favorite/{isFavorite}", (string pageId, bool isFavorite, MockNotionDataStore store) =>
        {
            store.ToggleFavoriteAsync(pageId, isFavorite).Wait();
            return Results.NoContent();
        });

        pageGroup.MapGet("/{pageId}/export/{format}", async (
            string pageId,
            string format,
            bool? includeSubpages,
            DemoNotionImportExportProvider importExportProvider,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<NotionExportFormat>(format, ignoreCase: true, out var exportFormat))
                return Results.BadRequest();

            try
            {
                var artifact = await importExportProvider.ExportPageArtifactAsync(
                    pageId,
                    exportFormat,
                    includeSubpages.GetValueOrDefault(),
                    cancellationToken);

                return Results.File(artifact.Content, artifact.ContentType, artifact.FileName);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (NotSupportedException)
            {
                return Results.BadRequest();
            }
        });

        pageGroup.MapPost("/import", async (
            HttpRequest request,
            DemoNotionImportExportProvider importExportProvider,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest();

            var form = await request.ReadFormAsync(cancellationToken);
            if (!Enum.TryParse<NotionImportFormat>(form["format"].FirstOrDefault(), ignoreCase: true, out var importFormat))
                return Results.BadRequest();

            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest();

            await using var stream = file.OpenReadStream();
            try
            {
                var page = await importExportProvider.ImportPageArtifactAsync(
                    stream,
                    importFormat,
                    form["targetParentPageId"].FirstOrDefault(),
                    file.FileName,
                    cancellationToken);

                return Results.Created($"/api/notion/pages/{page.Id}", page);
            }
            catch (InvalidDataException)
            {
                return Results.BadRequest();
            }
            catch (NotSupportedException)
            {
                return Results.BadRequest();
            }
            catch (Exception) when (importFormat == NotionImportFormat.Word)
            {
                return Results.BadRequest();
            }
        });

        // ── Spreadsheet documents ─────────────────────────────────────────────

        var spreadsheetGroup = app.MapGroup("/api/notion/spreadsheets").WithTags("Notion Editor");

        spreadsheetGroup.MapPost("/", (MockSpreadsheetDocumentStore store) =>
        {
            var (id, workbook) = store.Create();
            return Results.Ok(new SpreadsheetDocumentCreateResult(id, PrepareSpreadsheetWorkbookForTransport(workbook)));
        });

        spreadsheetGroup.MapGet("/{id}", (Guid id, MockSpreadsheetDocumentStore store) =>
        {
            var workbook = store.Get(id);
            return workbook is null ? Results.NotFound() : Results.Ok(PrepareSpreadsheetWorkbookForTransport(workbook));
        });

        spreadsheetGroup.MapPut("/{id}", (Guid id, SpreadsheetWorkbook workbook, MockSpreadsheetDocumentStore store) =>
            Results.Ok(PrepareSpreadsheetWorkbookForTransport(store.Save(id, NormalizeSpreadsheetWorkbook(workbook)))));

        // ── Search ───────────────────────────────────────────────────────────
        app.MapPost(
            "/api/notion/search",
            async (NotionSearchRequest request, DemoNotionSearchService searchService, CancellationToken cancellationToken) =>
                Results.Ok(await searchService.SearchAsync(request, cancellationToken)))
            .WithTags("Notion Editor");

        // ── Analytics ────────────────────────────────────────────────────────
        var analyticsGroup = app.MapGroup("/api/notion/analytics").WithTags("Notion Editor");

        analyticsGroup.MapGet("/pages/{pageId:guid}", async (Guid pageId, MockNotionAnalyticsStore analyticsStore, CancellationToken cancellationToken) =>
        {
            var analytics = await analyticsStore.GetPageAnalyticsAsync(pageId, cancellationToken);
            return analytics is null ? Results.NotFound() : Results.Ok(analytics);
        });

        analyticsGroup.MapPost("/pages/{pageId:guid}/views", async (Guid pageId, RecordPageViewRequest request, MockNotionAnalyticsStore analyticsStore, CancellationToken cancellationToken) =>
        {
            await analyticsStore.RecordViewAsync(pageId, request.UserId, cancellationToken);
            return Results.NoContent();
        });

        analyticsGroup.MapGet("/spaces/{spaceId}/top-pages", async (
            string spaceId,
            int? take,
            DateOnly? from,
            DateOnly? to,
            MockNotionAnalyticsStore analyticsStore,
            CancellationToken cancellationToken) =>
        {
            var analytics = await analyticsStore.GetTopPagesAsync(spaceId, new NotionAnalyticsRange
            {
                Take = take.GetValueOrDefault(10),
                From = from,
                To = to
            }, cancellationToken);

            return Results.Ok(analytics);
        });

        // ── Tasks ────────────────────────────────────────────────────────────
        taskGroup.MapPost("/query", async (
            TmWorkItemQuery query,
            DemoNotionTaskProvider taskProvider,
            CancellationToken cancellationToken) =>
        {
            var result = await taskProvider.SearchAsync(query, cancellationToken);
            return Results.Ok(result);
        });

        taskGroup.MapPut("/{taskId}/completed", async (
            string taskId,
            TaskCompletionRequest request,
            DemoNotionTaskProvider taskProvider,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await taskProvider.SetCompletedAsync(taskId, request.Completed, cancellationToken);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        historyGroup.MapGet("/pages/{pageId}/diff", (
            string pageId,
            string fromVersionId,
            string toVersionId,
            [FromServices] DemoNotionHistoryStore historyStore) =>
        {
            var diff = historyStore.GetDiff(pageId, fromVersionId, toVersionId);
            return diff is null ? Results.NotFound() : Results.Ok(diff);
        });

        historyGroup.MapGet("/pages/{pageId}/versions", (
            string pageId,
            int? page,
            int? pageSize,
            [FromServices] DemoNotionHistoryStore historyStore) =>
            Results.Ok(historyStore.GetVersions(pageId, page.GetValueOrDefault(1), pageSize.GetValueOrDefault(20))));

        historyGroup.MapGet("/pages/{pageId}/versions/{versionId}", (
            string pageId,
            string versionId,
            [FromServices] DemoNotionHistoryStore historyStore) =>
        {
            var version = historyStore.GetVersion(pageId, versionId);
            return version is null ? Results.NotFound() : Results.Ok(version);
        });

        historyGroup.MapGet("/versions/{versionId}", (
            string versionId,
            [FromServices] DemoNotionHistoryStore historyStore) =>
        {
            var version = historyStore.FindVersion(versionId);
            return version is null ? Results.NotFound() : Results.Ok(version);
        });

        historyGroup.MapPost("/pages/{pageId}/versions/{versionId}/restore", (
            string pageId,
            string versionId,
            MockNotionBlockStore blockStore,
            DemoNotionAggregateStore aggregateStore,
            [FromServices] DemoNotionHistoryStore historyStore) =>
        {
            var restoredBlocks = historyStore.RestoreVersion(pageId, versionId);
            if (restoredBlocks is null || !Guid.TryParse(pageId, out var pageGuid))
                return Results.NotFound();

            blockStore.ReplacePageBlocks(pageGuid, restoredBlocks);
            // The restore writes blocks directly (no aggregate save) — drop the cached snapshot so
            // the next aggregate load rebuilds from the block store instead of serving the
            // pre-restore page (same bypass as task completion, CF4).
            aggregateStore.InvalidatePageSnapshot(pageGuid);
            return Results.NoContent();
        });

        // ── Page reactions ──────────────────────────────────────────────────
        reactionGroup.MapGet("/pages/{pageId:guid}", (Guid pageId, MockNotionReactionStore reactionStore) =>
            Results.Ok(reactionStore.GetReactions(pageId)));

        reactionGroup.MapPost("/pages/{pageId:guid}/like", (
            Guid pageId,
            PageReactionToggleRequest request,
            MockNotionReactionStore reactionStore) =>
            Results.Ok(reactionStore.ToggleLike(pageId, request.UserId)));

        reactionGroup.MapPost("/pages/{pageId:guid}/reaction", (
            Guid pageId,
            PageReactionToggleRequest request,
            MockNotionReactionStore reactionStore) =>
            Results.Ok(reactionStore.ToggleReaction(pageId, request.Reaction, request.UserId)));

        // ── Templates ────────────────────────────────────────────────────────
        templateGroup.MapGet("/", async (DemoNotionTemplateStore templateStore, CancellationToken cancellationToken) =>
            Results.Ok(await templateStore.GetTemplatesAsync(cancellationToken)));

        templateGroup.MapGet("/{templateId}", async (
            string templateId,
            DemoNotionTemplateStore templateStore,
            CancellationToken cancellationToken) =>
        {
            var template = await templateStore.GetByIdAsync(templateId, cancellationToken);
            return template is null ? Results.NotFound() : Results.Ok(template);
        });

        // ── Spaces ───────────────────────────────────────────────────────────
        spaceGroup.MapGet("/", async (MockNotionDataStore store, CancellationToken cancellationToken) =>
            Results.Ok(await store.GetSpacesAsync(cancellationToken)));

        spaceGroup.MapGet("/{spaceId}", async (string spaceId, MockNotionDataStore store, CancellationToken cancellationToken) =>
        {
            var space = await store.GetSpaceAsync(spaceId, cancellationToken);
            return space is null ? Results.NotFound() : Results.Ok(space);
        });

        spaceGroup.MapPost("/", async (NotionSpaceDto space, MockNotionDataStore store, CancellationToken cancellationToken) =>
            Results.Created($"/api/notion/spaces/{space.Id}", await store.CreateSpaceAsync(space, cancellationToken)));

        spaceGroup.MapGet("/{spaceId}/pages", async (string spaceId, MockNotionDataStore store, CancellationToken cancellationToken) =>
            Results.Ok(await store.GetPagesInSpaceAsync(spaceId, cancellationToken)));

        spaceGroup.MapPost("/pages/{pageId}/move", async (
            string pageId,
            MovePageToSpaceRequest request,
            MockNotionDataStore store,
            CancellationToken cancellationToken) =>
        {
            await store.MovePageToSpaceAsync(pageId, request.SpaceId, cancellationToken);
            return Results.NoContent();
        });

        // ── Blog ─────────────────────────────────────────────────────────────
        blogGroup.MapGet("/spaces/{spaceId}/posts", async (
            string spaceId,
            int? skip,
            int? take,
            bool? includeDrafts,
            DemoNotionBlogProvider blogProvider,
            CancellationToken cancellationToken) =>
            Results.Ok(await blogProvider.GetPostsAsync(spaceId, new NotionBlogQuery
            {
                Skip = Math.Max(0, skip.GetValueOrDefault()),
                Take = Math.Clamp(take.GetValueOrDefault(5), 1, 100),
                IncludeDrafts = includeDrafts.GetValueOrDefault()
            }, cancellationToken)));

        blogGroup.MapGet("/posts/{postId}", async (
            string postId,
            DemoNotionBlogProvider blogProvider,
            CancellationToken cancellationToken) =>
        {
            var post = await blogProvider.GetPostAsync(postId, cancellationToken);
            return post is null ? Results.NotFound() : Results.Ok(post);
        });

        blogGroup.MapPost("/posts", async (
            CreateNotionBlogPostRequest request,
            DemoNotionBlogProvider blogProvider,
            CancellationToken cancellationToken) =>
            Results.Ok(await blogProvider.CreatePostAsync(request, cancellationToken)));

        blogGroup.MapPost("/posts/{postId}/publish", async (
            string postId,
            PublishNotionBlogPostRequest request,
            DemoNotionBlogProvider blogProvider,
            CancellationToken cancellationToken) =>
            Results.Ok(await blogProvider.PublishAsync(postId, request, cancellationToken)));

        // ── Watches and notifications ────────────────────────────────────────
        watchGroup.MapPut("/pages/{pageId}", async (
            string pageId,
            NotionWatchRequest request,
            DemoNotionWatchProvider watchProvider,
            CancellationToken cancellationToken) =>
        {
            await watchProvider.WatchAsync(pageId, request.UserId, request.IncludeChildren, cancellationToken);
            return Results.NoContent();
        });

        watchGroup.MapDelete("/pages/{pageId}/users/{userId}", async (
            string pageId,
            string userId,
            DemoNotionWatchProvider watchProvider,
            CancellationToken cancellationToken) =>
        {
            await watchProvider.UnwatchAsync(pageId, userId, cancellationToken);
            return Results.NoContent();
        });

        watchGroup.MapGet("/pages/{pageId}", async (
            string pageId,
            DemoNotionWatchProvider watchProvider,
            CancellationToken cancellationToken) =>
            Results.Ok(await watchProvider.GetWatchersAsync(pageId, cancellationToken)));

        watchGroup.MapGet("/pages/{pageId}/users/{userId}", async (
            string pageId,
            string userId,
            DemoNotionWatchProvider watchProvider,
            CancellationToken cancellationToken) =>
            Results.Ok(await watchProvider.IsWatchingAsync(pageId, userId, cancellationToken)));

        notificationGroup.MapPost("/", async (
            TmNotification notification,
            DemoNotionNotificationStore notificationStore,
            CancellationToken cancellationToken) =>
        {
            var created = await notificationStore.PublishAsync(notification, cancellationToken);
            return Results.Ok(created);
        });

        notificationGroup.MapGet("/users/{userId}", (
            string userId,
            int? skip,
            int? take,
            bool? includeRead,
            DemoNotionNotificationStore notificationStore) =>
            Results.Ok(notificationStore.GetNotifications(new TmNotificationQuery
            {
                RecipientUserId = userId,
                Skip = skip.GetValueOrDefault(0),
                Take = take.GetValueOrDefault(20),
                IncludeRead = includeRead.GetValueOrDefault(true)
            })));

        notificationGroup.MapGet("/users/{userId}/unread-count", async (
            string userId,
            DemoNotionNotificationStore notificationStore,
            CancellationToken cancellationToken) =>
            Results.Ok(await notificationStore.GetUnreadCountAsync(userId, cancellationToken)));

        notificationGroup.MapPost("/users/{userId}/{notificationId}/read", async (
            string userId,
            string notificationId,
            DemoNotionNotificationStore notificationStore,
            CancellationToken cancellationToken) =>
        {
            await notificationStore.MarkAsReadAsync(notificationId, userId, cancellationToken);
            return Results.NoContent();
        });

        notificationGroup.MapPost("/users/{userId}/read-all", async (
            string userId,
            DemoNotionNotificationStore notificationStore,
            CancellationToken cancellationToken) =>
        {
            await notificationStore.MarkAllAsReadAsync(userId, cancellationToken);
            return Results.NoContent();
        });

        notificationGroup.MapDelete("/", (DemoNotionNotificationStore notificationStore) =>
        {
            notificationStore.Clear();
            return Results.NoContent();
        });

        // ── Reset (for E2E tests) ─────────────────────────────────────────────
        app.MapPost("/api/notion/reset", (
            MockNotionDataStore dataStore,
            MockNotionBlockStore blockStore,
            DemoNotionAggregateStore aggregateStore,
            DemoNotionNotificationStore notificationStore,
            DemoNotionHistoryStore historyStore) =>
        {
            dataStore.Reset();
            blockStore.Reset();
            aggregateStore.Reset();
            historyStore.Reset();
            notificationStore.Clear();
            return Results.NoContent();
        });

        app.MapPost(
            "/api/notion/e2e/seed/{scenario}",
            async (
                string scenario,
                MockNotionDataStore dataStore,
                MockNotionBlockStore blockStore,
                DemoNotionAggregateStore aggregateStore,
                MockNotionAnalyticsStore analyticsStore,
                MockNotionReactionStore reactionStore,
                DemoNotionAuditProvider auditProvider,
                DemoNotionBlogProvider blogProvider,
                DemoNotionPermissionProvider permissionProvider,
                DemoNotionPublicShareProvider publicShareProvider,
                DemoNotionWatchProvider watchProvider,
                DemoNotionNotificationStore notificationStore,
                [FromServices] DemoNotionHistoryStore historyStore,
                CancellationToken cancellationToken) =>
            {
                await E2ESeedGate.WaitAsync(cancellationToken);
                try
                {
                    ResetNotionE2EState(
                        dataStore,
                        blockStore,
                        analyticsStore,
                        reactionStore,
                        auditProvider,
                        blogProvider,
                        permissionProvider,
                        publicShareProvider,
                        watchProvider,
                        notificationStore,
                        historyStore);
                    aggregateStore.Reset();

                    switch (scenario.Trim())
                    {
                        case "seedEmptyPage":
                            dataStore.SeedE2ESimplePage("Empty Notion Page", "Empty page for editor insertion scenarios.");
                            blockStore.SeedE2EEmptyPage();
                            break;
                        case "seedTextFormattingPage":
                            dataStore.SeedE2ESimplePage("EB10 Text Formatting", "Text formatting seed page.");
                            blockStore.SeedE2ETextFormattingPage();
                            break;
                        case "seedListTodoPage":
                            dataStore.SeedE2ESimplePage("EB2 Lists, Toggle, Todo", "List, toggle, and todo seed page.");
                            blockStore.SeedE2EListTodoPage();
                            break;
                        case "seedInlineToolbarPage":
                            dataStore.SeedE2ESimplePage("EB4 Inline Toolbar", "Inline toolbar seed page.");
                            blockStore.SeedE2EInlineToolbarPage();
                            break;
                        case "action-items":
                        case "seedActionItemsPage":
                            dataStore.SeedE2ESimplePage("CF3 Action Items", "Action items seed page.");
                            blockStore.SeedE2EActionItemsPage();
                            break;
                        case "seedMentionTokenPage":
                            dataStore.SeedE2EMentionTokenPage();
                            blockStore.SeedE2EMentionTokenPage();
                            break;
                        case "seedTasksPage":
                            dataStore.SeedE2ESimplePage("CF4 Release Follow-up", "Task aggregation seed page.");
                            blockStore.SeedE2ETasksPage();
                            break;
                        case "seedEmptyTasksPage":
                            dataStore.SeedE2ESimplePage("CF4 Empty Tasks", "Empty task seed page.");
                            blockStore.SeedE2EEmptyTasksPage();
                            break;
                        case "seedManyTasksPage":
                            dataStore.SeedE2ESimplePage("CF4 Many Tasks", "Many task seed page.");
                            blockStore.SeedE2EManyTasksPage();
                            break;
                        case "seedWorkItemsPage":
                            dataStore.SeedE2ESimplePage("CF5 Work Items", "Work item provider seed page.");
                            blockStore.SeedE2EWorkItemsPage();
                            break;
                        case "seedSmartLinksPage":
                            dataStore.SeedE2ESimplePage("CF8 Smart Links", "Smart link paste seed page.");
                            blockStore.SeedE2ESmartLinksPage();
                            break;
                        case "seedLabelsPage":
                            dataStore.SeedE2ELabelsPage();
                            blockStore.SeedE2EEmptyPageInfoPage();
                            break;
                        case "seedContentByLabelPage":
                            dataStore.SeedE2EContentByLabelPage();
                            blockStore.SeedE2EContentByLabelPage();
                            break;
                        case "seedCollaborationPage":
                            dataStore.SeedE2ESimplePage("EB14 Collaboration", "Collaboration seed page.");
                            blockStore.SeedE2ECollaborationPage();
                            break;
                        case "seedSpecialBlocksPage":
                            dataStore.SeedE2ESimplePage("EB15 Special Blocks", "Special block screenshot recovery page.");
                            blockStore.SeedE2ESpecialBlocksPage();
                            break;
                        case "seedDragDropPage":
                            dataStore.SeedE2ESimplePage("EB16 Drag and Drop", "Drag and drop screenshot recovery page.");
                            blockStore.SeedE2EDragDropPage();
                            break;
                        case "showCollaborationNoUsers":
                        case "showCollaborationOneCursor":
                        case "showCollaborationManyCursors":
                        case "showCollaborationLongNames":
                        case "showCollaborationOverlappingCursors":
                            dataStore.SeedE2ESimplePage("EB14 Collaboration", "Collaboration seed page.");
                            blockStore.SeedE2ECollaborationPage();
                            NotionCollaborationHub.SeedE2ECursors(scenario.Trim(), MockNotionDataStore.Page1Id.ToString("D"));
                            break;
                        case "seedLayoutPage":
                            dataStore.SeedE2ESimplePage("EB8 Layout Blocks", "Layout seed page.");
                            blockStore.SeedE2ELayoutPage();
                            break;
                        case "seedEmptyTocPage":
                            dataStore.SeedE2ESimplePage("EB8 Empty Table of Contents", "Empty table of contents seed page.");
                            blockStore.SeedE2EEmptyTocPage();
                            break;
                        case "seedTablePage":
                            dataStore.SeedE2ESimplePage("EB7 Table Blocks", "Table seed page.");
                            blockStore.SeedE2ETablePage();
                            break;
                        case "seedAtomicTablePage":
                            dataStore.SeedE2ESimplePage(
                                "F6 Atomic Notion Table",
                                "Atomic table authoring and conflict recovery seed page.");
                            blockStore.SeedE2EAtomicTablePage();
                            break;
                        case "seedKrFidelityPage":
                            dataStore.SeedE2ESimplePage(
                                "F7 KR DOCX Fidelity",
                                "KR.docx table fidelity seed page.");
                            blockStore.SeedE2EKrFidelityPage();
                            break;
                        case "seedMediaPage":
                            dataStore.SeedE2ESimplePage("EB6 Media Blocks", "Media seed page.");
                            blockStore.SeedE2EMediaPage();
                            break;
                        case "seedCommentsPage":
                            dataStore.SeedE2ESimplePage("EB10 Comments Workspace", "Comment recovery seed page.");
                            blockStore.SeedE2ECommentsPage();
                            break;
                        case "seedPageSettingsPage":
                            dataStore.SeedE2EPageSettingsPage();
                            blockStore.SeedE2EPageSettingsPage();
                            break;
                        case "seedHistoryEmptyPage":
                            dataStore.SeedE2ESimplePage("EB13 Empty History", "History empty state recovery page.");
                            blockStore.SeedE2EHistoryPage();
                            historyStore.SeedEmptyHistory();
                            break;
                        case "seedHistoryManyPage":
                            dataStore.SeedE2ESimplePage("EB13 Version History", "History timeline recovery page.");
                            blockStore.SeedE2EHistoryPage();
                            historyStore.SeedManyHistory();
                            break;
                        case "seedHistoryDiffPage":
                            dataStore.SeedE2ESimplePage("CF23 Version Diff", "History diff comparison recovery page.");
                            blockStore.SeedE2EHistoryPage();
                            historyStore.SeedDiffHistory();
                            break;
                        case "seedCommentlessPage":
                            dataStore.SeedE2ESimplePage("EB10 Commentless Workspace", "Comment provider edge seed page.");
                            blockStore.SeedE2EEmptyPage();
                            break;
                        case "seedIncludePagePage":
                            dataStore.SeedE2EIncludePage();
                            blockStore.SeedE2EIncludePage();
                            break;
                        case "seedChildrenDisplayPage":
                            dataStore.SeedE2EChildrenDisplayPage();
                            blockStore.SeedE2EChildrenDisplayPage();
                            break;
                        case "seedExcerptPage":
                            dataStore.SeedE2EExcerptPage();
                            blockStore.SeedE2EExcerptPage();
                            break;
                        case "seedPagePropertiesPage":
                            dataStore.SeedE2EPagePropertiesPage();
                            blockStore.SeedE2EPagePropertiesPage();
                            break;
                        case "seedSearchPage":
                            dataStore.SeedE2ESearchPage();
                            blockStore.SeedE2ESearchPage();
                            break;
                        case "seedBulkPages":
                            dataStore.SeedE2EBulkPages();
                            blockStore.SeedE2EBulkPages();
                            break;
                        case "export":
                        case "seedExportPage":
                            dataStore.SeedE2EExportPage();
                            blockStore.SeedE2EExportPage();
                            break;
                        case "seedRestrictionsPage":
                            dataStore.SeedE2ERestrictionsPage();
                            blockStore.SeedE2ERestrictionsPage();
                            permissionProvider.SeedE2ERestrictions();
                            break;
                        case "seedSidebarEmptyPage":
                            dataStore.SeedE2ESidebarEmptyNavigation();
                            blockStore.SeedE2EEmptyPage();
                            break;
                        case "seedSidebarDeepPage":
                            dataStore.SeedE2ESidebarDeepNavigation();
                            blockStore.SeedE2EEmptyPage();
                            break;
                        case "seedSidebarTrashPage":
                            dataStore.SeedE2ESidebarTrashNavigation();
                            blockStore.SeedE2EEmptyPage();
                            break;
                        case "seedPageInfoPage":
                            dataStore.SeedE2EPageInfoPage();
                            blockStore.SeedE2EPageInfoLikePage();
                            analyticsStore.SeedE2EPageInfoPage();
                            break;
                        case "seedEmptyPageInfoPage":
                            dataStore.SeedE2EEmptyPageInfoPage();
                            blockStore.SeedE2EEmptyPageInfoPage();
                            analyticsStore.SeedE2EEmptyPageInfoPage();
                            break;
                        case "seedAnalyticsPage":
                            dataStore.SeedE2EAnalyticsPage();
                            blockStore.SeedE2EPageInfoLikePage();
                            analyticsStore.SeedE2EAnalyticsPage();
                            break;
                        case "seedEmptyAnalyticsPage":
                            dataStore.SeedE2EEmptyPageInfoPage();
                            blockStore.SeedE2EEmptyPageInfoPage();
                            analyticsStore.SeedE2EEmptyAnalyticsPage();
                            break;
                        case "seedPageReactionsEmptyPage":
                            dataStore.SeedE2ESimplePage("CF17 Page Reactions", "Page reactions seed page.");
                            blockStore.SeedE2EPageReactionsPage();
                            reactionStore.SeedE2EEmptyPage();
                            break;
                        case "seedPageReactionsManyPage":
                            dataStore.SeedE2ESimplePage("CF17 Page Reactions", "Page reactions seed page.");
                            blockStore.SeedE2EPageReactionsPage();
                            reactionStore.SeedE2EManyPage();
                            break;
                        case "seedAuditPage":
                            dataStore.SeedE2EAuditPage();
                            blockStore.SeedE2EPageInfoLikePage();
                            auditProvider.SeedE2EAuditPage();
                            break;
                        case "seedEmptyAuditPage":
                            dataStore.SeedE2EAuditPage();
                            blockStore.SeedE2EPageInfoLikePage();
                            auditProvider.SeedE2EEmptyAuditPage();
                            break;
                        case "seedManyAuditEntriesPage":
                            dataStore.SeedE2EAuditPage();
                            blockStore.SeedE2EPageInfoLikePage();
                            auditProvider.SeedE2EManyAuditEntries();
                            break;
                        case "seedBlogPage":
                            dataStore.SeedE2ESpacesPage();
                            blogProvider.SeedE2EBlog();
                            break;
                        case "seedEmptyBlogPage":
                            dataStore.SeedE2ESpacesPage();
                            blogProvider.SeedE2EEmptyBlog();
                            break;
                        case "seedManyBlogPostsPage":
                            dataStore.SeedE2ESpacesPage();
                            blogProvider.SeedE2EManyBlogPosts();
                            break;
                        case "seedSpacesPage":
                            dataStore.SeedE2ESpacesPage();
                            blockStore.SeedE2EPageInfoLikePage();
                            break;
                        case "seedManySpacesPage":
                            dataStore.SeedE2EManySpacesPage();
                            blockStore.SeedE2EPageInfoLikePage();
                            break;
                        case "seedExpiredPublicSharePage":
                            dataStore.SeedE2EAuditPage();
                            blockStore.SeedE2EPageInfoLikePage();
                            publicShareProvider.SeedE2EExpiredShare();
                            break;
                        case "seedPublicSharePage":
                            dataStore.SeedE2EPublicSharePage();
                            blockStore.SeedE2EPublicSharePage();
                            break;
                        case "seedWatchPage":
                            await watchProvider.SeedE2EWatchAsync(cancellationToken);
                            break;
                        case "history-diff":
                            dataStore.SeedE2ESimplePage("CF23 Version Diff", "History diff comparison recovery page.");
                            blockStore.SeedE2EHistoryPage();
                            historyStore.SeedDiffHistory();
                            break;
                    }
                }
                finally
                {
                    E2ESeedGate.Release();
                }

                return Results.NoContent();
            });
    }

    private static void ResetNotionE2EState(
        MockNotionDataStore dataStore,
        MockNotionBlockStore blockStore,
        MockNotionAnalyticsStore analyticsStore,
        MockNotionReactionStore reactionStore,
        DemoNotionAuditProvider auditProvider,
        DemoNotionBlogProvider blogProvider,
        DemoNotionPermissionProvider permissionProvider,
        DemoNotionPublicShareProvider publicShareProvider,
        DemoNotionWatchProvider watchProvider,
        DemoNotionNotificationStore notificationStore,
        DemoNotionHistoryStore historyStore)
    {
        dataStore.Reset();
        blockStore.Reset();
        analyticsStore.Reset();
        reactionStore.Reset();
        auditProvider.Reset();
        blogProvider.Reset();
        permissionProvider.Reset();
        publicShareProvider.Reset();
        watchProvider.Reset();
        historyStore.Reset();
        notificationStore.Clear();
        NotionCollaborationHub.ClearE2ESeeds();
    }

    private static async Task LogAuditAsync(
        HttpRequest request,
        DemoNotionAuditProvider auditProvider,
        string action,
        Guid pageId,
        IReadOnlyDictionary<string, string> details,
        CancellationToken cancellationToken)
    {
        var user = GetAuditUser(request);
        await auditProvider.AppendAsync(new TmActivityEntry
        {
            Actor = new TmUserRef { Id = user.UserId, DisplayName = user.DisplayName },
            Action = action,
            EntityRef = TmEntityRef.Create("page", pageId.ToString("D")),
            Metadata = details.ToDictionary(pair => pair.Key, pair => (object)pair.Value, StringComparer.OrdinalIgnoreCase)
        }, cancellationToken);
    }

    private static (string UserId, string DisplayName) GetAuditUser(HttpRequest request)
    {
        var userId = request.Headers.TryGetValue("x-tempo-userid", out var idValues) && !string.IsNullOrWhiteSpace(idValues.FirstOrDefault())
            ? idValues.First()!
            : "demo";
        var displayName = request.Headers.TryGetValue("x-tempo-userdisplayname", out var nameValues) && !string.IsNullOrWhiteSpace(nameValues.FirstOrDefault())
            ? nameValues.First()!
            : userId;
        return (userId.Trim(), displayName.Trim());
    }

    private static SpreadsheetWorkbook PrepareSpreadsheetWorkbookForTransport(SpreadsheetWorkbook workbook)
    {
        var clone = NormalizeSpreadsheetWorkbook(workbook).Clone();
        foreach (var sheet in clone.Sheets)
            sheet.Workbook = null;

        return clone;
    }

    private static SpreadsheetWorkbook NormalizeSpreadsheetWorkbook(SpreadsheetWorkbook? workbook)
    {
        var normalized = workbook ?? new SpreadsheetWorkbook();
        if (normalized.Sheets.Count == 0)
            normalized.AddSheet("Sheet1");

        if (normalized.ActiveSheetIndex < 0 || normalized.ActiveSheetIndex >= normalized.Sheets.Count)
            normalized.ActiveSheetIndex = 0;

        for (var i = 0; i < normalized.Sheets.Count; i++)
        {
            normalized.Sheets[i].Workbook = normalized;
            normalized.Sheets[i].SheetIndexInWorkbook = i;
        }

        return normalized;
    }

    private static IReadOnlyList<PagePropertiesReportRow> BuildPagePropertiesReport(
        PagePropertiesReportQuery query,
        MockNotionDataStore dataStore,
        MockNotionBlockStore blockStore)
    {
        var requiredLabels = NormalizeList(query.Labels);
        var rows = new List<PagePropertiesReportRow>();

        foreach (var page in dataStore.GetAllPages()
                     .Where(page => !page.IsDeleted)
                     .OrderBy(page => page.Title, StringComparer.OrdinalIgnoreCase))
        {
            if (requiredLabels.Count > 0 && !requiredLabels.All(label =>
                    page.Labels.Any(existing => string.Equals(existing.Trim(), label, StringComparison.OrdinalIgnoreCase))))
            {
                continue;
            }

            var properties = blockStore.GetBlocksAsync(page.Id.ToString("D")).Result
                .Where(block => block.Type == BlockType.PageProperties)
                .OrderBy(block => block.Order)
                .Select(block => block.Content as IPagePropertiesBlockContent)
                .FirstOrDefault(content => content?.Rows.Count > 0);

            if (properties is null)
            {
                continue;
            }

            rows.Add(new PagePropertiesReportRow
            {
                PageId = page.Id,
                Title = page.Title,
                IconEmoji = page.IconEmoji,
                Labels = page.Labels,
                Properties = properties.Rows
                    .Where(row => !string.IsNullOrWhiteSpace(row.Key))
                    .GroupBy(row => row.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Last().ValueHtml,
                        StringComparer.OrdinalIgnoreCase)
            });
        }

        return rows;
    }

    private static IReadOnlyList<string> NormalizeList(IEnumerable<string> values)
        => values
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static async Task NotifyPageWatchersAsync(
        Guid pageId,
        string actorUserId,
        DemoNotionWatchProvider watchProvider,
        DemoNotionNotificationStore notificationStore,
        CancellationToken cancellationToken)
    {
        var watchers = await watchProvider.GetWatchersAsync(pageId.ToString("D"), cancellationToken);
        foreach (var watcher in watchers.Where(w => !string.Equals(w.UserId, actorUserId, StringComparison.OrdinalIgnoreCase)))
        {
            await notificationStore.PublishAsync(new TmNotification
            {
                Type = TmNotificationTypes.PageEdited,
                RecipientUserId = watcher.UserId,
                Actor = new TmUserRef { Id = actorUserId, DisplayName = actorUserId },
                Title = "Page edited",
                ActionUrl = $"/notion-editor?page={pageId:D}",
                EntityRef = TmEntityRef.Create("page", pageId.ToString("D")),
                CreatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
    }
}

public record CreatePageRequest(string Title, string? ParentId = null);
public record UpdatePageRequest(string? Title, string? Description, string? IconEmoji,
    bool? IsFullWidth = null, bool? IsSmallText = null, bool? IsLocked = null);
public record TaskCompletionRequest(bool Completed);
public record MovePageToSpaceRequest(string SpaceId);
