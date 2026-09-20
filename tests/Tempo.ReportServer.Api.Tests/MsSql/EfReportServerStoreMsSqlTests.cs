using Microsoft.EntityFrameworkCore;
using Tempo.Reporting.Abstractions.Dtos;

namespace Tempo.ReportServer.Api.Tests.MsSql;

/// <summary>
/// Repository contract tests for <c>EfReportServerStore</c> executed against a real SQL Server
/// database (decision O1 / ADR-0001). These assert both the store's public behaviour and the
/// persisted rows in the catalog tables.
/// </summary>
[Collection(MsSqlTestCollection.Name)]
public sealed class EfReportServerStoreMsSqlTests : IClassFixture<MsSqlTestDatabase>
{
    private readonly MsSqlTestDatabase _db;

    public EfReportServerStoreMsSqlTests(MsSqlTestDatabase db) => _db = db;

    [Fact]
    public async Task CreateReport_PersistsReportRowAndInitialRevision()
    {
        await _db.ResetAsync();
        var (context, store) = _db.CreateStore("tenant-a");
        await using (context)
        {
            var folder = await store.CreateFolderAsync(new CreateReportFolderRequestDto { TenantId = "tenant-a", Name = "Finance" });
            folder.Path.Should().Be("/Finance");

            var report = await store.CreateReportAsync(new CreateReportRequestDto
            {
                TenantId = "tenant-a",
                FolderId = folder.FolderId,
                Name = "Sales Register",
                Description = "Orders and totals.",
                DefinitionJson = "{\"id\":\"sales\"}",
            }, "author-1");

            report.LatestRevisionId.Should().NotBeNullOrEmpty();
        }

        // Direct DB assertion: exactly one report row and one revision row exist for the tenant.
        await using var verify = _db.CreateDbContext("tenant-a");
        (await verify.Reports.CountAsync()).Should().Be(1);
        var revision = await verify.Revisions.SingleAsync();
        revision.RevisionNumber.Should().Be(1);
        revision.IsPublished.Should().BeTrue();
        revision.CreatedByUserId.Should().Be("author-1");
    }

    [Fact]
    public async Task AddRevision_IncrementsNumber_AndEnforcesOptimisticConcurrency()
    {
        await _db.ResetAsync();
        var (context, store) = _db.CreateStore("tenant-a");
        await using (context)
        {
            var folder = await store.CreateFolderAsync(new CreateReportFolderRequestDto { TenantId = "tenant-a", Name = "Finance" });
            var report = await store.CreateReportAsync(new CreateReportRequestDto
            {
                TenantId = "tenant-a",
                FolderId = folder.FolderId,
                Name = "Sales Register",
                DefinitionJson = "{\"v\":1}",
            }, "author-1");

            var second = await store.AddRevisionAsync(new UpdateReportDefinitionRequestDto
            {
                TenantId = "tenant-a",
                ReportId = report.ReportId,
                ExpectedRevisionId = report.LatestRevisionId,
                DefinitionJson = "{\"v\":2}",
                Comment = "draft",
            }, "author-2");
            second.Should().NotBeNull();
            second!.RevisionNumber.Should().Be(2);

            // A stale ExpectedRevisionId must be rejected (returns null, no new revision).
            var stale = await store.AddRevisionAsync(new UpdateReportDefinitionRequestDto
            {
                TenantId = "tenant-a",
                ReportId = report.ReportId,
                ExpectedRevisionId = report.LatestRevisionId, // now stale
                DefinitionJson = "{\"v\":3}",
            }, "author-3");
            stale.Should().BeNull();
        }

        await using var verify = _db.CreateDbContext("tenant-a");
        (await verify.Revisions.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task PublishAndRollback_UpdatePublishStateAndAppendRevision()
    {
        await _db.ResetAsync();
        var (context, store) = _db.CreateStore("tenant-a");
        await using (context)
        {
            var folder = await store.CreateFolderAsync(new CreateReportFolderRequestDto { TenantId = "tenant-a", Name = "Finance" });
            var report = await store.CreateReportAsync(new CreateReportRequestDto
            {
                TenantId = "tenant-a",
                FolderId = folder.FolderId,
                Name = "Sales Register",
                DefinitionJson = "{\"v\":1}",
            }, "author-1");

            var second = await store.AddRevisionAsync(new UpdateReportDefinitionRequestDto
            {
                TenantId = "tenant-a",
                ReportId = report.ReportId,
                DefinitionJson = "{\"v\":2}",
            }, "author-1");

            var published = await store.PublishRevisionAsync("tenant-a", report.ReportId, new PublishReportRevisionRequestDto
            {
                RevisionId = second!.RevisionId,
            });
            published!.IsPublished.Should().BeTrue();

            var rolledBack = await store.RollbackAsync("tenant-a", report.ReportId, new RollbackReportRevisionRequestDto
            {
                RevisionId = report.LatestRevisionId!,
                Comment = "rollback",
            }, "author-1");
            rolledBack!.RevisionNumber.Should().Be(3);
        }

        await using var verify = _db.CreateDbContext("tenant-a");
        (await verify.Revisions.CountAsync(r => r.IsPublished)).Should().Be(1);
        (await verify.Revisions.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task DataSources_UpsertGetDelete_RoundTrip()
    {
        await _db.ResetAsync();
        var (context, store) = _db.CreateStore("tenant-a");
        await using (context)
        {
            var created = await store.UpsertDataSourceAsync(new UpsertReportDataSourceRequestDto
            {
                TenantId = "tenant-a",
                Name = "orders-db",
                Kind = "sql",
                Connection = "Server=erp;Database=Reporting;",
            });

            // Upsert on the same name updates in place (no duplicate row).
            var updated = await store.UpsertDataSourceAsync(new UpsertReportDataSourceRequestDto
            {
                TenantId = "tenant-a",
                Name = "orders-db",
                Kind = "sql",
                Connection = "Server=erp2;Database=Reporting;",
            });
            updated.DataSourceId.Should().Be(created.DataSourceId);

            (await store.GetDataSourceAsync("tenant-a", created.DataSourceId))!.Connection.Should().Contain("erp2");
            (await store.DeleteDataSourceAsync("tenant-a", created.DataSourceId)).Should().BeTrue();
            (await store.GetDataSourceAsync("tenant-a", created.DataSourceId)).Should().BeNull();
        }

        await using var verify = _db.CreateDbContext("tenant-a");
        (await verify.DataSources.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Tenants_AreIsolated_ByStoreAndByGlobalQueryFilter()
    {
        await _db.ResetAsync();

        var (contextA, storeA) = _db.CreateStore("tenant-a");
        await using (contextA)
        {
            var folderA = await storeA.CreateFolderAsync(new CreateReportFolderRequestDto { TenantId = "tenant-a", Name = "Finance" });
            await storeA.CreateReportAsync(new CreateReportRequestDto
            {
                TenantId = "tenant-a",
                FolderId = folderA.FolderId,
                Name = "A Report",
                DefinitionJson = "{}",
            }, "author-a");
        }

        var (contextB, storeB) = _db.CreateStore("tenant-b");
        ReportDetailDto reportB;
        await using (contextB)
        {
            var folderB = await storeB.CreateFolderAsync(new CreateReportFolderRequestDto { TenantId = "tenant-b", Name = "Finance" });
            reportB = await storeB.CreateReportAsync(new CreateReportRequestDto
            {
                TenantId = "tenant-b",
                FolderId = folderB.FolderId,
                Name = "B Report",
                DefinitionJson = "{}",
            }, "author-b");
        }

        // tenant-a search must never see tenant-b reports.
        var (contextSearch, storeSearch) = _db.CreateStore("tenant-a");
        await using (contextSearch)
        {
            var results = await storeSearch.SearchReportsAsync(new ReportSearchRequestDto { TenantId = "tenant-a" });
            results.Should().ContainSingle(r => r.Name == "A Report");
            results.Should().NotContain(r => r.ReportId == reportB.ReportId);
            (await storeSearch.GetReportAsync("tenant-a", reportB.ReportId)).Should().BeNull();
        }
    }
}
