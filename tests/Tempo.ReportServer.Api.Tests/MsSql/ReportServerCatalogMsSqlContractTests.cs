using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Reporting.Abstractions.Dtos;

namespace Tempo.ReportServer.Api.Tests.MsSql;

/// <summary>
/// Runs the report server catalog HTTP contract against a real SQL Server database, proving that
/// the EF <c>IReportServerStore</c> implementation honours the same behaviour the in-memory/SQLite
/// specification (<c>ReportServerF10ApiTests</c>) asserts, and that the operations land as real rows.
/// </summary>
[Collection(MsSqlTestCollection.Name)]
public sealed class ReportServerCatalogMsSqlContractTests : IClassFixture<MsSqlTestDatabase>
{
    private readonly MsSqlTestDatabase _db;

    public ReportServerCatalogMsSqlContractTests(MsSqlTestDatabase db) => _db = db;

    [Fact]
    public async Task Catalog_IsolatesTenants_TracksRevisions_AndPersistsRows()
    {
        await _db.ResetAsync();
        await using var app = await CreateHostAsync();
        var client = new TempoReportServerClient(app.Client);

        var folderA = await client.CreateFolderAsync(new CreateReportFolderRequestDto { TenantId = "tenant-a", Name = "Finance" });
        var folderB = await client.CreateFolderAsync(new CreateReportFolderRequestDto { TenantId = "tenant-b", Name = "Finance" });

        var reportA = await client.CreateReportAsync(new CreateReportRequestDto
        {
            TenantId = "tenant-a",
            FolderId = folderA.FolderId,
            Name = "Sales Register",
            DefinitionJson = "{\"id\":\"sales\"}",
        });
        var reportB = await client.CreateReportAsync(new CreateReportRequestDto
        {
            TenantId = "tenant-b",
            FolderId = folderB.FolderId,
            Name = "Private Register",
            DefinitionJson = "{\"id\":\"private\"}",
        });

        var tenantAReports = await client.SearchReportsAsync(new ReportSearchRequestDto { TenantId = "tenant-a", Query = "Register" });
        tenantAReports.Should().ContainSingle(r => r.ReportId == reportA.ReportId);
        tenantAReports.Should().NotContain(r => r.ReportId == reportB.ReportId);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetReportAsync(reportB.ReportId, "tenant-a"));

        var second = await client.UpdateReportDefinitionAsync(new UpdateReportDefinitionRequestDto
        {
            TenantId = "tenant-a",
            ReportId = reportA.ReportId,
            ExpectedRevisionId = reportA.LatestRevisionId,
            DefinitionJson = "{\"id\":\"sales\",\"v\":2}",
            Comment = "draft update",
        });
        second.RevisionNumber.Should().Be(2);

        var published = await client.PublishRevisionAsync(reportA.ReportId, "tenant-a", new PublishReportRevisionRequestDto
        {
            RevisionId = second.RevisionId,
        });
        published.IsPublished.Should().BeTrue();

        var rolledBack = await client.RollbackRevisionAsync(reportA.ReportId, "tenant-a", new RollbackReportRevisionRequestDto
        {
            RevisionId = reportA.LatestRevisionId!,
            Comment = "rollback",
        });
        rolledBack.RevisionNumber.Should().Be(3);

        var source = await client.UpsertDataSourceAsync(new UpsertReportDataSourceRequestDto
        {
            TenantId = "tenant-a",
            Name = "orders-db",
            Kind = "sql",
            Connection = "Server=erp;Database=Reporting;",
        });
        (await client.GetDataSourcesAsync("tenant-a")).Should().ContainSingle(s => s.DataSourceId == source.DataSourceId);

        // DB assertion: the catalog tables hold exactly the rows the API produced.
        await using var verifyA = _db.CreateDbContext("tenant-a");
        (await verifyA.Reports.CountAsync()).Should().Be(1);
        (await verifyA.Revisions.CountAsync()).Should().Be(3);
        (await verifyA.Revisions.CountAsync(r => r.IsPublished)).Should().Be(1);
        (await verifyA.DataSources.CountAsync()).Should().Be(1);

        await using var verifyB = _db.CreateDbContext("tenant-b");
        (await verifyB.Reports.CountAsync()).Should().Be(1);
    }

    private async Task<CatalogHost> CreateHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddTempoReportServerApi(options => options.UseSqlServer(
            _db.ConnectionString,
            sql => sql.MigrationsAssembly(typeof(Storage.ReportServerDbContext).Assembly.GetName().Name)));
        // Catalog contract host has no authentication gate; allow anonymous operations so the
        // in-handler ACL enforcement does not fail closed (401) for principal-less requests.
        builder.Services.Configure<ReportServerApiOptions>(o => o.AllowAnonymousOperations = true);
        var app = builder.Build();
        app.UseTempoReportServerTenantContext();
        app.MapTempoReportServerApi();
        await app.Services.EnsureTempoReportServerDatabaseAsync().ConfigureAwait(false);
        await app.StartAsync().ConfigureAwait(false);
        return new CatalogHost(app);
    }

    private sealed class CatalogHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        public CatalogHost(WebApplication app)
        {
            _app = app;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync().ConfigureAwait(false);
        }
    }
}
