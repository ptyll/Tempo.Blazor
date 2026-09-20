using Microsoft.EntityFrameworkCore;
using Tempo.Reporting.Abstractions;
using Tempo.Reporting.Abstractions.Definitions;
using Tempo.Reporting.Abstractions.Dtos;
using Tempo.Reporting.Abstractions.Serialization;
using Tempo.ReportServer.Api.Rendering;
using Tempo.ReportServer.Api.Storage;

namespace Tempo.ReportServer.Api.Tests.MsSql;

/// <summary>
/// Atomic multi-node claim specification for <see cref="EfReportRenderJobQueue"/> against a real SQL
/// Server database: with more than one render node calling <see cref="EfReportRenderJobQueue.ProcessNextAsync"/>
/// on the same queued job, exactly one must claim and render it (the other double-renders nothing), and a
/// crashed node's expired lease must let the job be re-claimed and rendered — exactly once overall.
/// </summary>
[Collection(MsSqlTestCollection.Name)]
public sealed class EfReportRenderJobQueueMsSqlTests : IClassFixture<MsSqlTestDatabase>
{
    private readonly MsSqlTestDatabase _db;

    public EfReportRenderJobQueueMsSqlTests(MsSqlTestDatabase db) => _db = db;

    [Fact]
    public async Task TwoNodes_ProcessingSameQueuedJob_RenderExactlyOnce()
    {
        await _db.ResetAsync();
        var reportId = await SeedReportAsync();
        var renderer = new CountingRenderer(new ReportServerRenderer(new EmptyReportDataProvider()));

        var (enqCtx, enqQueue) = CreateQueue("enqueuer", renderer);
        var job = await enqQueue.EnqueueAsync(RenderRequest(reportId));
        await enqCtx.DisposeAsync();

        // Two independent nodes (independent contexts/connections) race a full processing pass.
        var (ctxA, nodeA) = CreateQueue("node-A", renderer);
        var (ctxB, nodeB) = CreateQueue("node-B", renderer);
        var results = await Task.WhenAll(nodeA.ProcessNextAsync(), nodeB.ProcessNextAsync());
        await ctxA.DisposeAsync();
        await ctxB.DisposeAsync();

        renderer.Count.Should().Be(1, "the atomic claim must prevent a double render across nodes");
        results.Count(job => job is { Status: RenderJobStatus.Completed }).Should().Be(1, "exactly one node completes the job");
        results.Count(job => job is null).Should().Be(1, "the losing node finds nothing left to claim");

        await using var verify = _db.CreateDbContext("tenant-a");
        var row = await verify.RenderJobs.SingleAsync(j => j.JobId == job.JobId);
        row.Status.Should().Be("Completed");
        row.LeaseOwner.Should().BeNull();
        row.LeasedUntilTicks.Should().BeNull();
        row.DownloadUrl.Should().Be($"api/render/jobs/{job.JobId}/result");
    }

    [Fact]
    public async Task HeldLease_BlocksReclaim_ThenExpiredLease_IsReclaimedAndRenderedOnce()
    {
        await _db.ResetAsync();
        var reportId = await SeedReportAsync();
        var renderer = new CountingRenderer(new ReportServerRenderer(new EmptyReportDataProvider()));

        var (enqCtx, enqQueue) = CreateQueue("enqueuer", renderer);
        var job = await enqQueue.EnqueueAsync(RenderRequest(reportId));
        await enqCtx.DisposeAsync();

        // Simulate a node that claimed the job (Running) and still holds an unexpired lease.
        await using (var crash = _db.CreateDbContext("tenant-a"))
        {
            await crash.RenderJobs
                .Where(j => j.JobId == job.JobId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(j => j.Status, "Running")
                    .SetProperty(j => j.LeaseOwner, "node-A")
                    .SetProperty(j => j.LeasedUntilTicks, DateTimeOffset.UtcNow.AddMinutes(5).UtcTicks));
        }

        // A second node cannot claim the job while the lease is held.
        var (heldCtx, heldNode) = CreateQueue("node-B", renderer);
        (await heldNode.ProcessNextAsync()).Should().BeNull("an unexpired lease blocks re-claim");
        await heldCtx.DisposeAsync();
        renderer.Count.Should().Be(0);

        // The crashed node's lease elapses.
        await using (var expire = _db.CreateDbContext("tenant-a"))
        {
            await expire.RenderJobs
                .Where(j => j.JobId == job.JobId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(j => j.LeasedUntilTicks, DateTimeOffset.UtcNow.AddMinutes(-1).UtcTicks));
        }

        // The second node now re-claims and renders the job — exactly once overall.
        var (reclaimCtx, reclaimNode) = CreateQueue("node-B2", renderer);
        var reclaimed = await reclaimNode.ProcessNextAsync();
        await reclaimCtx.DisposeAsync();

        reclaimed.Should().NotBeNull();
        reclaimed!.Status.Should().Be(RenderJobStatus.Completed);
        renderer.Count.Should().Be(1);

        await using var verify = _db.CreateDbContext("tenant-a");
        var row = await verify.RenderJobs.SingleAsync(j => j.JobId == job.JobId);
        row.Status.Should().Be("Completed");
        row.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public async Task Enqueue_Process_CompletesRoundTrip_OnSqlServer()
    {
        await _db.ResetAsync();
        var reportId = await SeedReportAsync();
        var renderer = new CountingRenderer(new ReportServerRenderer(new EmptyReportDataProvider()));

        var (ctx, queue) = CreateQueue("node", renderer);
        var job = await queue.EnqueueAsync(RenderRequest(reportId));
        var processed = await queue.ProcessNextAsync();
        await ctx.DisposeAsync();

        processed!.Status.Should().Be(RenderJobStatus.Completed);
        renderer.Count.Should().Be(1);

        await using var verify = _db.CreateDbContext("tenant-a");
        var row = await verify.RenderJobs.SingleAsync(j => j.JobId == job.JobId);
        row.Status.Should().Be("Completed");
        row.StartedAt.Should().NotBeNull();
        row.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Claim_InterleavesTenantsRoundRobin_NotGlobalFifo()
    {
        await _db.ResetAsync();
        var reportA = await SeedReportAsync("tenant-a");
        var reportB = await SeedReportAsync("tenant-b");
        var renderer = new CountingRenderer(new ReportServerRenderer(new EmptyReportDataProvider()));
        // Strictly increasing clock so QueuedSequence is deterministic regardless of wall-clock resolution.
        var clock = new MonotonicTimeProvider(DateTimeOffset.Parse("2026-07-19T00:00:00Z"));

        var (ctx, queue) = CreateQueue("node", renderer, clock);

        // tenant A enqueues 3 jobs; tenant B enqueues 1 after A's first.
        var a1 = await queue.EnqueueAsync(RenderRequest(reportA, "tenant-a"));
        var b1 = await queue.EnqueueAsync(RenderRequest(reportB, "tenant-b"));
        var a2 = await queue.EnqueueAsync(RenderRequest(reportA, "tenant-a"));
        var a3 = await queue.EnqueueAsync(RenderRequest(reportA, "tenant-a"));

        var claimedOrder = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var processed = await queue.ProcessNextAsync();
            claimedOrder.Add(processed!.JobId);
        }

        await ctx.DisposeAsync();

        // Round-robin interleave (A1, B1, A2, A3) — NOT global FIFO (A1, A2, A3, B1): tenant B's single job
        // is served in the first fairness round rather than behind tenant A's whole backlog.
        claimedOrder.Should().Equal(a1.JobId, b1.JobId, a2.JobId, a3.JobId);
    }

    private (ReportServerDbContext Context, EfReportRenderJobQueue Queue) CreateQueue(
        string nodeId,
        IReportServerRenderer renderer,
        TimeProvider? timeProvider = null)
    {
        var requestContext = new ReportServerRequestContext();
        requestContext.Set(new ReportExecutionContext("tenant-a", "render-worker", "en-US"));
        var options = new DbContextOptionsBuilder<ReportServerDbContext>()
            .UseSqlServer(
                _db.ConnectionString,
                sql => sql.MigrationsAssembly(typeof(ReportServerDbContext).Assembly.GetName().Name))
            .Options;
        var context = new ReportServerDbContext(options, requestContext);
        var store = new EfReportServerStore(context);
        var queue = new EfReportRenderJobQueue(
            context,
            requestContext,
            store,
            renderer,
            new ReportRenderJobQueueOptions(),
            timeProvider ?? TimeProvider.System,
            new ReportRenderNodeIdentity { NodeId = nodeId });
        return (context, queue);
    }

    private Task<string> SeedReportAsync() => SeedReportAsync("tenant-a");

    private async Task<string> SeedReportAsync(string tenantId)
    {
        await using var context = _db.CreateDbContext(tenantId);
        var store = new EfReportServerStore(context);
        var folder = await store.CreateFolderAsync(new CreateReportFolderRequestDto { TenantId = tenantId, Name = "Finance" });
        var report = await store.CreateReportAsync(
            new CreateReportRequestDto
            {
                TenantId = tenantId,
                FolderId = folder.FolderId,
                Name = "Sales Register",
                DefinitionJson = DefinitionJson(),
            },
            "seed-user");
        return report.ReportId;
    }

    private static RenderReportRequestDto RenderRequest(string reportId, string tenantId = "tenant-a")
        => new()
        {
            TenantId = tenantId,
            ReportId = reportId,
            Format = ReportRenderFormat.Snapshot,
            CultureName = "en-US",
        };

    private static string DefinitionJson()
        => ReportDefinitionJsonSerializer.Serialize(new ReportDefinition
        {
            Id = "sales-register",
            Name = "Sales Register",
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
                            Width = 240,
                            Height = 24,
                            Text = "Sales",
                        },
                    ],
                },
            },
        });

    /// <summary>
    /// A clock that advances one second on every read, so successive enqueues get strictly increasing
    /// <c>QueuedSequence</c> values regardless of wall-clock tick resolution — making the fairness
    /// ordering deterministic.
    /// </summary>
    private sealed class MonotonicTimeProvider : TimeProvider
    {
        private long _ticks;

        public MonotonicTimeProvider(DateTimeOffset start) => _ticks = start.UtcTicks;

        public override DateTimeOffset GetUtcNow()
            => new(Interlocked.Add(ref _ticks, TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }

    /// <summary>
    /// Wraps the real renderer and counts render invocations across all nodes so a test can prove a job
    /// is rendered exactly once even when multiple nodes race it.
    /// </summary>
    private sealed class CountingRenderer : IReportServerRenderer
    {
        private readonly IReportServerRenderer _inner;
        private int _count;

        public CountingRenderer(IReportServerRenderer inner) => _inner = inner;

        public int Count => Volatile.Read(ref _count);

        public Task<IReadOnlyList<ReportParameterMetadataDto>> GetParametersAsync(ReportDetailDto report, CancellationToken cancellationToken = default)
            => _inner.GetParametersAsync(report, cancellationToken);

        public Task<RenderReportResultDto> RenderAsync(
            ReportDetailDto report,
            RenderReportRequestDto request,
            ReportExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return _inner.RenderAsync(report, request, context, cancellationToken);
        }
    }
}
