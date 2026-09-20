using Microsoft.EntityFrameworkCore;
using Tempo.ReportServer.Api.Security;

namespace Tempo.ReportServer.Api.Tests.MsSql;

/// <summary>
/// Contract tests for <see cref="Storage.EfReportAuditLog"/> against a real SQL Server database.
/// Asserts who/when/which report/parameters/outcome are persisted and that filtered queries work.
/// </summary>
[Collection(MsSqlTestCollection.Name)]
public sealed class EfReportAuditLogMsSqlTests : IClassFixture<MsSqlTestDatabase>
{
    private readonly MsSqlTestDatabase _db;

    public EfReportAuditLogMsSqlTests(MsSqlTestDatabase db) => _db = db;

    [Fact]
    public async Task Write_PersistsActorReportParametersAndOutcome()
    {
        await _db.ResetAsync();
        var now = DateTimeOffset.Parse("2026-07-01T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var (context, log) = _db.CreateAuditLog();
        await using (context)
        {
            await log.WriteAsync(new ReportAuditEvent
            {
                TenantId = "tenant-a",
                ActorId = "api:embedded-app",
                Action = ReportAuditAction.RenderReport,
                ResourceKind = ReportResourceKind.Render,
                ResourceId = "orders",
                Outcome = ReportAuditOutcome.Allowed,
                Timestamp = now,
                Details = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["from"] = "2026-01-01",
                    ["to"] = "2026-06-30",
                },
            });

            var events = await log.ListAsync("tenant-a");
            events.Should().ContainSingle();
            var only = events[0];
            only.ActorId.Should().Be("api:embedded-app");
            only.Action.Should().Be(ReportAuditAction.RenderReport);
            only.ResourceId.Should().Be("orders");
            only.Outcome.Should().Be(ReportAuditOutcome.Allowed);
            only.Details.Should().ContainKey("from").WhoseValue.Should().Be("2026-01-01");
            only.Details.Should().ContainKey("to").WhoseValue.Should().Be("2026-06-30");
        }

        await using var verify = _db.CreateDbContext("tenant-a");
        var row = await verify.AuditEvents.SingleAsync();
        row.ActorId.Should().Be("api:embedded-app");
        row.DetailsJson.Should().Contain("2026-06-30");
    }

    [Fact]
    public async Task Query_FiltersByActionOutcomeActorAndTimeRange()
    {
        await _db.ResetAsync();
        var start = DateTimeOffset.Parse("2026-07-01T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var (context, log) = _db.CreateAuditLog();
        await using (context)
        {
            await log.WriteAsync(ReportAuditEvent.Allowed("tenant-a", "author-1", ReportAuditAction.RenderReport, ReportResourceKind.Render, "orders", start));
            await log.WriteAsync(ReportAuditEvent.Denied("tenant-a", "author-2", ReportAuditAction.ChangeAcl, ReportResourceKind.Acl, "finance", start.AddMinutes(5)));
            await log.WriteAsync(ReportAuditEvent.Allowed("tenant-a", "author-1", ReportAuditAction.ExportReport, ReportResourceKind.Export, "orders", start.AddMinutes(10)));
            await log.WriteAsync(ReportAuditEvent.Allowed("tenant-b", "author-9", ReportAuditAction.RenderReport, ReportResourceKind.Render, "orders", start.AddMinutes(15)));

            // Tenant isolation.
            (await log.QueryAsync(new ReportAuditQuery { TenantId = "tenant-a" })).Should().HaveCount(3);

            // Filter by outcome.
            var denied = await log.QueryAsync(new ReportAuditQuery { TenantId = "tenant-a", Outcome = ReportAuditOutcome.Denied });
            denied.Should().ContainSingle(e => e.Action == ReportAuditAction.ChangeAcl);

            // Filter by actor.
            var author1 = await log.QueryAsync(new ReportAuditQuery { TenantId = "tenant-a", ActorId = "author-1" });
            author1.Should().HaveCount(2);

            // Filter by action.
            var renders = await log.QueryAsync(new ReportAuditQuery { TenantId = "tenant-a", Action = ReportAuditAction.RenderReport });
            renders.Should().ContainSingle();

            // Filter by time range (exclude the first event).
            var afterStart = await log.QueryAsync(new ReportAuditQuery { TenantId = "tenant-a", From = start.AddMinutes(1) });
            afterStart.Should().HaveCount(2);

            // Ordered most recent first.
            var ordered = await log.QueryAsync(new ReportAuditQuery { TenantId = "tenant-a" });
            ordered[0].Timestamp.Should().Be(start.AddMinutes(10));
        }
    }
}
