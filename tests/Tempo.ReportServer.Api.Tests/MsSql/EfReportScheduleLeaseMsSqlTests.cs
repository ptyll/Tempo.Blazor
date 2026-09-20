using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tempo.ReportServer.Api.Scheduling;
using Tempo.ReportServer.Api.Storage;
using Tempo.Reporting.Abstractions.Dtos;

namespace Tempo.ReportServer.Api.Tests.MsSql;

/// <summary>
/// Atomic lease/claim specification for <see cref="EfReportScheduleStore.TryClaimScheduleAsync"/>
/// against a real SQL Server database: with more than one worker attempting to claim the same due
/// schedule, exactly one must win, and a crashed worker's expired lease must become re-claimable.
/// </summary>
[Collection(MsSqlTestCollection.Name)]
public sealed class EfReportScheduleLeaseMsSqlTests : IClassFixture<MsSqlTestDatabase>
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-17T08:05:00Z");

    private readonly MsSqlTestDatabase _db;

    public EfReportScheduleLeaseMsSqlTests(MsSqlTestDatabase db) => _db = db;

    [Fact]
    public async Task TwoConcurrentClaims_OnSameDueSchedule_ExactlyOneWins()
    {
        await _db.ResetAsync();
        var scheduleId = await SeedScheduleAsync();

        // Two independent workers (independent contexts/connections) race to claim the same row.
        await using var contextA = _db.CreateDbContext("tenant-a");
        await using var contextB = _db.CreateDbContext("tenant-a");
        var storeA = new EfReportScheduleStore(contextA);
        var storeB = new EfReportScheduleStore(contextB);
        var leaseUntil = Now.AddMinutes(5);

        var results = await Task.WhenAll(
            storeA.TryClaimScheduleAsync("tenant-a", scheduleId, "worker-A", leaseUntil, Now),
            storeB.TryClaimScheduleAsync("tenant-a", scheduleId, "worker-B", leaseUntil, Now));

        results.Count(won => won).Should().Be(1, "exactly one worker may hold the lease");

        await using var verify = _db.CreateDbContext("tenant-a");
        var row = await verify.Schedules.SingleAsync(s => s.ScheduleId == scheduleId);
        row.LeaseOwner.Should().BeOneOf("worker-A", "worker-B");
        row.LeasedUntil.Should().Be(leaseUntil);
    }

    [Fact]
    public async Task Claim_WhileLeaseHeld_IsRejected_ThenExpiredLease_IsReclaimable()
    {
        await _db.ResetAsync();
        var scheduleId = await SeedScheduleAsync();

        await using var context = _db.CreateDbContext("tenant-a");
        var store = new EfReportScheduleStore(context);

        // First claim wins and holds the lease until Now+5m.
        (await store.TryClaimScheduleAsync("tenant-a", scheduleId, "worker-A", Now.AddMinutes(5), Now))
            .Should().BeTrue();

        // A second worker at Now+1m sees an unexpired lease and cannot claim.
        (await store.TryClaimScheduleAsync("tenant-a", scheduleId, "worker-B", Now.AddMinutes(6), Now.AddMinutes(1)))
            .Should().BeFalse();

        // After the lease elapses (worker-A crashed), the schedule becomes re-claimable at Now+6m.
        var reclaimAt = Now.AddMinutes(6);
        (await store.TryClaimScheduleAsync("tenant-a", scheduleId, "worker-B", reclaimAt.AddMinutes(5), reclaimAt))
            .Should().BeTrue();

        await using var verify = _db.CreateDbContext("tenant-a");
        var row = await verify.Schedules.SingleAsync(s => s.ScheduleId == scheduleId);
        row.LeaseOwner.Should().Be("worker-B");
    }

    [Fact]
    public async Task ApplyRunOutcome_ReleasesTheLease()
    {
        await _db.ResetAsync();
        var scheduleId = await SeedScheduleAsync();

        await using var context = _db.CreateDbContext("tenant-a");
        var store = new EfReportScheduleStore(context);
        (await store.TryClaimScheduleAsync("tenant-a", scheduleId, "worker-A", Now.AddMinutes(5), Now))
            .Should().BeTrue();

        await store.ApplyRunOutcomeAsync(
            "tenant-a",
            scheduleId,
            new ScheduleStateUpdate(
                LastRunUtc: Now,
                LastDeliveredUtc: Now,
                NextRunUtc: Now.AddDays(7),
                RetryAfterUtc: null,
                FailureCount: 0,
                LastStatus: ReportScheduleRunStatus.Delivered,
                LastStatusMessage: "Delivered",
                PendingOccurrences: []),
            [],
            CancellationToken.None);

        await using var verify = _db.CreateDbContext("tenant-a");
        var row = await verify.Schedules.SingleAsync(s => s.ScheduleId == scheduleId);
        row.LeaseOwner.Should().BeNull();
        row.LeasedUntil.Should().BeNull();
    }

    [Fact]
    public async Task TwoWorkers_ProcessingSameDueSchedule_DeliverExactlyOnce()
    {
        await _db.ResetAsync();
        var scheduleId = await SeedScheduleAsync();
        var now = DateTimeOffset.Parse("2026-07-17T08:05:00Z");
        var recorder = new ConcurrentDeliveryRecorder();

        // Two independent workers race a full processing pass over the same due schedule.
        var results = await Task.WhenAll(
            RunProcessorAsync("worker-A", now, recorder),
            RunProcessorAsync("worker-B", now, recorder));

        // Both passes may "process" the row (one delivers, the other is claimed-out and skips), but the
        // report must be delivered exactly once and only one run row may be persisted.
        recorder.Count.Should().Be(1, "the atomic claim must prevent a duplicate delivery with two workers");
        results.Sum().Should().BeGreaterThanOrEqualTo(0);

        await using var verify = _db.CreateDbContext("tenant-a");
        (await verify.ScheduleRuns.CountAsync(r => r.ScheduleId == scheduleId)).Should().Be(1);
        var row = await verify.Schedules.SingleAsync(s => s.ScheduleId == scheduleId);
        row.LastStatus.Should().Be(ReportScheduleRunStatus.Delivered.ToString());
        row.LeaseOwner.Should().BeNull();
    }

    private async Task<int> RunProcessorAsync(string instanceId, DateTimeOffset now, ConcurrentDeliveryRecorder recorder)
    {
        await using var context = _db.CreateDbContext("tenant-a");
        var store = new EfReportScheduleStore(context);
        var router = new ScheduledReportDeliveryRouter([new RecordingChannel(recorder)]);
        var processor = new ReportScheduleProcessor(
            store,
            new StubRenderer(),
            router,
            Options.Create(new ReportSchedulingOptions()),
            NullLogger<ReportScheduleProcessor>.Instance,
            new ReportSchedulingInstanceIdentity { InstanceId = instanceId });
        return await processor.ProcessDueSchedulesAsync(now);
    }

    private sealed class ConcurrentDeliveryRecorder
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Record() => Interlocked.Increment(ref _count);
    }

    private sealed class RecordingChannel : IScheduledReportDeliveryChannel
    {
        private readonly ConcurrentDeliveryRecorder _recorder;

        public RecordingChannel(ConcurrentDeliveryRecorder recorder) => _recorder = recorder;

        public ReportScheduleDeliveryKind Kind => ReportScheduleDeliveryKind.Email;

        public Task DeliverAsync(ScheduledReportDelivery delivery, CancellationToken cancellationToken = default)
        {
            _recorder.Record();
            return Task.CompletedTask;
        }
    }

    private sealed class StubRenderer : IScheduledReportRenderer
    {
        public Task<ScheduledReportArtifact> RenderAsync(ReportScheduleDto schedule, CancellationToken cancellationToken = default)
            => Task.FromResult(new ScheduledReportArtifact($"{schedule.ScheduleId}.pdf", "application/pdf", [0x25, 0x50, 0x44, 0x46]));
    }

    private async Task<string> SeedScheduleAsync()
    {
        await using var context = _db.CreateDbContext("tenant-a");
        var store = new EfReportScheduleStore(context);
        var schedule = await store.UpsertAsync(
            new UpsertReportScheduleRequestDto
            {
                TenantId = "tenant-a",
                Name = "Lease digest",
                ReportId = "sales-dashboard",
                CronExpression = "0 8 * * 5",
                Format = ReportScheduleFormat.Pdf,
                DeliveryKind = ReportScheduleDeliveryKind.Email,
                DeliveryTarget = "ops@example.test",
                MissedRunPolicy = ReportScheduleMissedRunPolicy.Skip,
            },
            DateTimeOffset.Parse("2026-07-17T07:00:00Z"));
        return schedule.ScheduleId;
    }
}
