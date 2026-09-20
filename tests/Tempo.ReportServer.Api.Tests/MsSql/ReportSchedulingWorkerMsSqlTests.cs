using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tempo.ReportServer.Api.Scheduling;
using Tempo.ReportServer.Api.Storage;
using Tempo.Reporting.Abstractions.Dtos;

namespace Tempo.ReportServer.Api.Tests.MsSql;

/// <summary>
/// Integration tests for the server-tier scheduling worker against a real SQL Server database.
/// They assert both the persisted schedule/run rows and the deliveries captured by a recording
/// channel, driving the processor with an explicit UTC instant so timing is deterministic.
/// </summary>
[Collection(MsSqlTestCollection.Name)]
public sealed class ReportSchedulingWorkerMsSqlTests : IClassFixture<MsSqlTestDatabase>
{
    private static readonly DateTimeOffset Friday0700 = DateTimeOffset.Parse("2026-07-17T07:00:00Z");

    private readonly MsSqlTestDatabase _db;

    public ReportSchedulingWorkerMsSqlTests(MsSqlTestDatabase db) => _db = db;

    [Fact]
    public async Task DueSchedule_RendersDelivers_AndPersistsRunAtomically()
    {
        await _db.ResetAsync();
        var scheduleId = await SeedScheduleAsync(cron: "0 8 * * 5", policy: ReportScheduleMissedRunPolicy.Skip);

        var channel = new RecordingDeliveryChannel();

        // Not yet due at 07:30 (next run is 08:00).
        (await ProcessAsync(DateTimeOffset.Parse("2026-07-17T07:30:00Z"), channel)).Should().Be(0);
        channel.Deliveries.Should().BeEmpty();

        // Due at 08:05.
        var processed = await ProcessAsync(DateTimeOffset.Parse("2026-07-17T08:05:00Z"), channel);

        processed.Should().Be(1);
        channel.Deliveries.Should().ContainSingle();
        channel.Deliveries[0].Artifact.Bytes.Should().NotBeEmpty();
        channel.Deliveries[0].Artifact.ContentType.Should().Be("application/pdf");

        await using var verify = _db.CreateDbContext("tenant-a");
        var schedule = await verify.Schedules.SingleAsync(s => s.ScheduleId == scheduleId);
        schedule.LastStatus.Should().Be(ReportScheduleRunStatus.Delivered.ToString());
        schedule.LastDeliveredUtc.Should().NotBeNull();
        schedule.FailureCount.Should().Be(0);
        schedule.PendingOccurrencesJson.Should().BeNull();
        // Next run advances to the following Friday.
        schedule.NextRunUtc.Should().Be(DateTimeOffset.Parse("2026-07-24T08:00:00Z"));

        var run = await verify.ScheduleRuns.SingleAsync(r => r.ScheduleId == scheduleId);
        run.Status.Should().Be(ReportScheduleRunStatus.Delivered.ToString());
        run.ArtifactByteCount.Should().BeGreaterThan(0);
        run.OccurrenceUtc.Should().Be(DateTimeOffset.Parse("2026-07-17T08:00:00Z"));
    }

    [Fact]
    public async Task FailedDelivery_SchedulesRetryWithBackoff_ThenRecovers()
    {
        await _db.ResetAsync();
        var scheduleId = await SeedScheduleAsync(cron: "0 8 * * 5", policy: ReportScheduleMissedRunPolicy.Skip);

        var channel = new ScriptedDeliveryChannel { ShouldFail = true };

        // First attempt at 08:05 fails => retry scheduled 1 minute later (base backoff).
        await ProcessAsync(DateTimeOffset.Parse("2026-07-17T08:05:00Z"), channel);

        await using (var verify = _db.CreateDbContext("tenant-a"))
        {
            var schedule = await verify.Schedules.SingleAsync(s => s.ScheduleId == scheduleId);
            schedule.LastStatus.Should().Be(ReportScheduleRunStatus.Retrying.ToString());
            schedule.FailureCount.Should().Be(1);
            schedule.RetryAfterUtc.Should().Be(DateTimeOffset.Parse("2026-07-17T08:06:00Z"));
            schedule.PendingOccurrencesJson.Should().NotBeNull();
        }

        // Retry succeeds at 08:06.
        channel.ShouldFail = false;
        var processed = await ProcessAsync(DateTimeOffset.Parse("2026-07-17T08:06:00Z"), channel);

        processed.Should().Be(1);
        channel.Deliveries.Should().ContainSingle();
        channel.Deliveries[0].OccurrenceUtc.Should().Be(DateTimeOffset.Parse("2026-07-17T08:00:00Z"));

        await using var verifyAfter = _db.CreateDbContext("tenant-a");
        var recovered = await verifyAfter.Schedules.SingleAsync(s => s.ScheduleId == scheduleId);
        recovered.LastStatus.Should().Be(ReportScheduleRunStatus.Delivered.ToString());
        recovered.FailureCount.Should().Be(0);
        recovered.RetryAfterUtc.Should().BeNull();
        recovered.PendingOccurrencesJson.Should().BeNull();
        (await verifyAfter.ScheduleRuns.CountAsync(r => r.ScheduleId == scheduleId)).Should().Be(2);
    }

    [Fact]
    public async Task CatchUpPolicy_BackfillsEveryMissedOccurrence()
    {
        await _db.ResetAsync();
        var scheduleId = await SeedScheduleAsync(cron: "0 * * * *", policy: ReportScheduleMissedRunPolicy.CatchUp);

        // Simulate a schedule that last ran at 08:00 but the worker only wakes at 12:10.
        await using (var arrange = _db.CreateDbContext("tenant-a"))
        {
            var row = await arrange.Schedules.SingleAsync(s => s.ScheduleId == scheduleId);
            row.LastRunUtc = DateTimeOffset.Parse("2026-07-17T08:00:00Z");
            row.NextRunUtc = DateTimeOffset.Parse("2026-07-17T09:00:00Z");
            await arrange.SaveChangesAsync();
        }

        var channel = new RecordingDeliveryChannel();
        await ProcessAsync(DateTimeOffset.Parse("2026-07-17T12:10:00Z"), channel);

        // Occurrences 09,10,11,12 are backfilled.
        channel.Deliveries.Select(d => d.OccurrenceUtc).Should().Equal(
            DateTimeOffset.Parse("2026-07-17T09:00:00Z"),
            DateTimeOffset.Parse("2026-07-17T10:00:00Z"),
            DateTimeOffset.Parse("2026-07-17T11:00:00Z"),
            DateTimeOffset.Parse("2026-07-17T12:00:00Z"));

        await using var verify = _db.CreateDbContext("tenant-a");
        (await verify.ScheduleRuns.CountAsync(r => r.ScheduleId == scheduleId)).Should().Be(4);
        var schedule = await verify.Schedules.SingleAsync(s => s.ScheduleId == scheduleId);
        schedule.NextRunUtc.Should().Be(DateTimeOffset.Parse("2026-07-17T13:00:00Z"));
    }

    [Fact]
    public async Task ApplyRunOutcome_OnStaleRowVersion_ThrowsConcurrencyException()
    {
        await _db.ResetAsync();
        var scheduleId = await SeedScheduleAsync(cron: "0 8 * * 5", policy: ReportScheduleMissedRunPolicy.Skip);

        // Second worker loads (and tracks) the schedule row at RowVersion v1.
        await using var loser = _db.CreateDbContext("tenant-a");
        _ = await loser.Schedules.SingleAsync(s => s.ScheduleId == scheduleId);
        var loserStore = new EfReportScheduleStore(loser);

        // Winning worker advances the same row to v2.
        await using (var winner = _db.CreateDbContext("tenant-a"))
        {
            await new EfReportScheduleStore(winner)
                .ApplyRunOutcomeAsync("tenant-a", scheduleId, DeliveredUpdate(), [], CancellationToken.None);
        }

        // The loser now writes against the stale v1 token and must lose the optimistic-concurrency race.
        var act = () => loserStore.ApplyRunOutcomeAsync("tenant-a", scheduleId, DeliveredUpdate(), [], CancellationToken.None);

        (await act.Should().ThrowAsync<ReportScheduleConcurrencyException>())
            .Which.ScheduleId.Should().Be(scheduleId);
    }

    private static ScheduleStateUpdate DeliveredUpdate()
        => new(
            LastRunUtc: Friday0700,
            LastDeliveredUtc: Friday0700,
            NextRunUtc: Friday0700.AddDays(7),
            RetryAfterUtc: null,
            FailureCount: 0,
            LastStatus: ReportScheduleRunStatus.Delivered,
            LastStatusMessage: "Delivered",
            PendingOccurrences: []);

    private async Task<string> SeedScheduleAsync(string cron, ReportScheduleMissedRunPolicy policy)
    {
        await using var context = _db.CreateDbContext("tenant-a");
        var store = new EfReportScheduleStore(context);
        var schedule = await store.UpsertAsync(
            new UpsertReportScheduleRequestDto
            {
                TenantId = "tenant-a",
                Name = "Executive digest",
                ReportId = "sales-dashboard",
                CronExpression = cron,
                Format = ReportScheduleFormat.Pdf,
                DeliveryKind = ReportScheduleDeliveryKind.Email,
                DeliveryTarget = "ops@example.test",
                MissedRunPolicy = policy,
            },
            Friday0700);
        return schedule.ScheduleId;
    }

    private async Task<int> ProcessAsync(DateTimeOffset now, IScheduledReportDeliveryChannel channel)
    {
        await using var context = _db.CreateDbContext("tenant-a");
        var store = new EfReportScheduleStore(context);
        var router = new ScheduledReportDeliveryRouter([channel]);
        var processor = new ReportScheduleProcessor(
            store,
            new StubScheduledReportRenderer(),
            router,
            Options.Create(new ReportSchedulingOptions()),
            NullLogger<ReportScheduleProcessor>.Instance);
        return await processor.ProcessDueSchedulesAsync(now);
    }

    private sealed class StubScheduledReportRenderer : IScheduledReportRenderer
    {
        public Task<ScheduledReportArtifact> RenderAsync(ReportScheduleDto schedule, CancellationToken cancellationToken = default)
            => Task.FromResult(new ScheduledReportArtifact(
                $"{schedule.ScheduleId}.pdf",
                "application/pdf",
                [0x25, 0x50, 0x44, 0x46])); // "%PDF"
    }

    private sealed class RecordingDeliveryChannel : IScheduledReportDeliveryChannel
    {
        public List<ScheduledReportDelivery> Deliveries { get; } = [];

        public ReportScheduleDeliveryKind Kind => ReportScheduleDeliveryKind.Email;

        public Task DeliverAsync(ScheduledReportDelivery delivery, CancellationToken cancellationToken = default)
        {
            Deliveries.Add(delivery);
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedDeliveryChannel : IScheduledReportDeliveryChannel
    {
        public List<ScheduledReportDelivery> Deliveries { get; } = [];

        public bool ShouldFail { get; set; }

        public ReportScheduleDeliveryKind Kind => ReportScheduleDeliveryKind.Email;

        public Task DeliverAsync(ScheduledReportDelivery delivery, CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                throw new InvalidOperationException("Simulated delivery failure.");
            }

            Deliveries.Add(delivery);
            return Task.CompletedTask;
        }
    }
}
