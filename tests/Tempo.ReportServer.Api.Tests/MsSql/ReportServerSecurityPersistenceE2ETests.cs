using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.ReportServer.Api.Security;
using Tempo.ReportServer.Api.Storage;
using Tempo.Reporting.Abstractions.Definitions;
using Tempo.Reporting.Abstractions.Dtos;
using Tempo.Reporting.Abstractions.Serialization;

namespace Tempo.ReportServer.Api.Tests.MsSql;

/// <summary>
/// End-to-end security-persistence specification against the deployable API host backed by a real
/// SQL Server database with EF-persisted API keys and audit log (decision O1 / ADR-0001):
/// generate a key -> render a report through the API with that key -> assert the key row exists in
/// the database and the render is audited in the database -> revoke the key -> the same call now
/// returns 401.
/// </summary>
/// <remarks>
/// Browser screenshots and the Web UI leg of the flow are deferred (see phase report [TODO]): they
/// require Playwright browsers plus a dual HTTPS host (API + WASM/Blazor) that is not orchestrated
/// here. This test covers the server + database legs of the E2E flow deterministically.
/// </remarks>
[Collection(MsSqlTestCollection.Name)]
public sealed class ReportServerSecurityPersistenceE2ETests : IClassFixture<MsSqlTestDatabase>
{
    private const string TenantId = "tenant-e2e";

    private readonly MsSqlTestDatabase _db;

    public ReportServerSecurityPersistenceE2ETests(MsSqlTestDatabase db) => _db = db;

    [Fact]
    public async Task GenerateKey_RenderViaApi_AuditPersisted_ThenRevoke_Yields401()
    {
        await _db.ResetAsync();
        await using var host = await PersistentSecurityHost.CreateAsync(_db.ConnectionString);

        // 1. Generate an API key (EF persisted).
        string keyId;
        string plainTextKey;
        using (var scope = host.Services.CreateScope())
        {
            var keyStore = scope.ServiceProvider.GetRequiredService<IReportApiKeyStore>();
            keyStore.Should().BeOfType<EfReportApiKeyStore>("the host must use the EF-persisted key store");
            var created = await keyStore.CreateAsync(TenantId, "e2e-embed", ReportPermission.All);
            keyId = created.KeyId;
            plainTextKey = created.PlainTextKey;
        }

        // Assert the key row is persisted in the database (hash only).
        await using (var verify = _db.CreateDbContext(TenantId))
        {
            var row = await verify.ApiKeys.SingleAsync(key => key.KeyId == keyId);
            row.KeyHash.Should().NotContain(plainTextKey);
        }

        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(ReportSecurityHeaders.ApiKey, plainTextKey);

        // 2. Render a report through the API using the key.
        var reportId = await CreateFixtureReportAsync(client);
        var render = await client.PostAsJsonAsync("/api/render", new RenderReportRequestDto
        {
            TenantId = TenantId,
            ReportId = reportId,
            Format = ReportRenderFormat.Pdf,
            CultureName = "en-US",
        });
        render.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. The render is audited by the /render endpoint itself (Fáze 11 in-handler enforcement);
        //    assert the RenderReport row lands in the DB through the host's EF audit log.
        using (var scope = host.Services.CreateScope())
        {
            var auditLog = scope.ServiceProvider.GetRequiredService<IReportAuditLog>();
            auditLog.Should().BeOfType<EfReportAuditLog>("the host must use the EF-persisted audit log");

            var events = await auditLog.QueryAsync(new ReportAuditQuery
            {
                TenantId = TenantId,
                Action = ReportAuditAction.RenderReport,
            });
            events.Should().ContainSingle(e => e.ResourceId == reportId && e.Outcome == ReportAuditOutcome.Allowed);
        }

        await using (var verify = _db.CreateDbContext(TenantId))
        {
            // The render endpoint persists exactly one RenderReport audit (the catalog seed writes
            // separate ChangeDefinition rows, which are not counted here).
            (await verify.AuditEvents.CountAsync(e =>
                e.TenantId == TenantId && e.Action == (int)ReportAuditAction.RenderReport)).Should().Be(1);
        }

        // 4. Revoke the key.
        using (var scope = host.Services.CreateScope())
        {
            var keyStore = scope.ServiceProvider.GetRequiredService<IReportApiKeyStore>();
            await keyStore.RevokeAsync(keyId, TenantId, "admin-e2e");
        }

        // 5. The same authenticated call now fails with 401.
        var afterRevoke = await client.GetAsync($"/api/folders?tenantId={TenantId}");
        afterRevoke.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<string> CreateFixtureReportAsync(HttpClient client)
    {
        var folderResponse = await client.PostAsJsonAsync("/api/folders", new CreateReportFolderRequestDto
        {
            TenantId = TenantId,
            Name = "Finance",
        });
        folderResponse.EnsureSuccessStatusCode();
        var folder = await folderResponse.Content.ReadFromJsonAsync<ReportFolderDto>();

        var reportResponse = await client.PostAsJsonAsync("/api/reports", new CreateReportRequestDto
        {
            TenantId = TenantId,
            FolderId = folder!.FolderId,
            Name = "Sales Register",
            DefinitionJson = FixtureDefinitionJson(),
        });
        reportResponse.EnsureSuccessStatusCode();
        var report = await reportResponse.Content.ReadFromJsonAsync<ReportDetailDto>();
        return report!.ReportId;
    }

    private static string FixtureDefinitionJson()
        => ReportDefinitionJsonSerializer.Serialize(new ReportDefinition
        {
            Id = "sales-register",
            Name = "Sales Register",
            DataSets = [new ReportDataSetDefinition { Name = "main" }],
            Bands = new ReportBandCollection
            {
                ReportHeader = new ReportBand
                {
                    Kind = ReportBandKind.ReportHeader,
                    Height = 60,
                    Elements =
                    [
                        new ReportTextBoxElement
                        {
                            Id = "title",
                            X = 24,
                            Y = 24,
                            Width = 320,
                            Height = 24,
                            Text = "Sales Register",
                        },
                    ],
                },
            },
        });

    private sealed class PersistentSecurityHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private PersistentSecurityHost(WebApplication app) => _app = app;

        public IServiceProvider Services => _app.Services;

        public static async Task<PersistentSecurityHost> CreateAsync(string connectionString)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddRouting();
            builder.Services.AddTempoReportServerApi(options => options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(ReportServerDbContext).Assembly.GetName().Name)));
            builder.Services.AddReportServerAuthentication(builder.Configuration);
            builder.Services.UseEfReportServerSecurityStores();

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseTempoReportServerTenantContext();
            app.MapTempoReportServerApi()
                .RequireAuthorization(ReportServerAuthenticationDefaults.ApiPolicy);

            await app.Services.EnsureTempoReportServerDatabaseAsync().ConfigureAwait(false);
            await app.StartAsync().ConfigureAwait(false);
            return new PersistentSecurityHost(app);
        }

        public HttpClient CreateClient() => _app.GetTestClient();

        public async ValueTask DisposeAsync() => await _app.DisposeAsync().ConfigureAwait(false);
    }
}
