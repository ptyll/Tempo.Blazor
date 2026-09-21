using Tempo.Blazor.EmailTemplates.Abstractions.Contracts;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;
using Tempo.Blazor.Reporting.Models;
using Tempo.Reporting.Abstractions.Data;

namespace Tempo.ReportServer.Web.Services;

/// <summary>Output format produced by a scheduled report run.</summary>
public enum ReportScheduleOutputFormat
{
    /// <summary>Portable Document Format.</summary>
    Pdf,

    /// <summary>Comma-separated values.</summary>
    Csv,

    /// <summary>OpenXML workbook.</summary>
    Xlsx,
}

/// <summary>Latest observed state of a schedule run.</summary>
public enum ReportScheduleRunStatus
{
    /// <summary>The schedule has not run yet.</summary>
    NeverRun,

    /// <summary>A render job has been queued.</summary>
    Queued,

    /// <summary>The run was delivered successfully.</summary>
    Delivered,

    /// <summary>The run failed and is waiting for retry.</summary>
    Retrying,

    /// <summary>The run failed without retry.</summary>
    Failed,
}

/// <summary>Recipient configured on a report schedule.</summary>
public sealed record ReportScheduleRecipient(string Email, string? DisplayName = null);

/// <summary>User subscription to a report schedule.</summary>
public sealed class ReportSubscription
{
    /// <summary>Subscription identifier.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Tenant identifier.</summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>Schedule identifier.</summary>
    public string ScheduleId { get; init; } = string.Empty;

    /// <summary>User identifier.</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>Delivery email address.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Whether the subscription is active.</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>Tenant-scoped scheduled report definition.</summary>
public sealed class ReportScheduleDefinition
{
    /// <summary>Schedule identifier.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Tenant identifier.</summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>User that owns this schedule.</summary>
    public string OwnerUserId { get; init; } = string.Empty;

    /// <summary>Human-readable schedule name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Report identifier to render.</summary>
    public string ReportId { get; set; } = string.Empty;

    /// <summary>Five-field cron expression in UTC.</summary>
    public string CronExpression { get; set; } = "0 8 * * 1";

    /// <summary>Output format.</summary>
    public ReportScheduleOutputFormat Format { get; set; } = ReportScheduleOutputFormat.Pdf;

    /// <summary>Email template selected from the gallery.</summary>
    public Guid EmailTemplateId { get; set; } = ReportEmailTemplateGalleryStore.ReportDigestTemplateId;

    /// <summary>Culture used for rendering and export formatting.</summary>
    public string CultureName { get; set; } = "en-US";

    /// <summary>Report parameter values.</summary>
    public Dictionary<string, ReportParameterValue> Parameters { get; set; } =
        new(StringComparer.Ordinal);

    /// <summary>Email recipients.</summary>
    public List<ReportScheduleRecipient> Recipients { get; set; } = [];

    /// <summary>Whether the schedule is enabled.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Next cron run in UTC.</summary>
    public DateTimeOffset NextRunUtc { get; set; }

    /// <summary>Last run attempt timestamp.</summary>
    public DateTimeOffset? LastRunUtc { get; set; }

    /// <summary>Last successful delivery timestamp.</summary>
    public DateTimeOffset? LastDeliveredUtc { get; set; }

    /// <summary>Retry timestamp after a failed delivery.</summary>
    public DateTimeOffset? RetryAfterUtc { get; set; }

    /// <summary>Consecutive failure count.</summary>
    public int FailureCount { get; set; }

    /// <summary>Latest status.</summary>
    public ReportScheduleRunStatus LastStatus { get; set; } = ReportScheduleRunStatus.NeverRun;

    /// <summary>Latest status message.</summary>
    public string LastStatusMessage { get; set; } = "Never run";
}

/// <summary>Clock abstraction used by schedule tests and worker code.</summary>
public interface IReportScheduleClock
{
    /// <summary>Current UTC timestamp.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>System clock implementation for production/demo runtime.</summary>
public sealed class SystemReportScheduleClock : IReportScheduleClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Mutable clock for deterministic schedule tests.</summary>
public sealed class ManualReportScheduleClock : IReportScheduleClock
{
    /// <summary>Creates a manual clock.</summary>
    public ManualReportScheduleClock(DateTimeOffset utcNow) => UtcNow = utcNow;

    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; private set; }

    /// <summary>Advances the clock.</summary>
    public void Advance(TimeSpan duration) => UtcNow += duration;
}

/// <summary>Small five-field cron parser used by the demo scheduler.</summary>
public sealed class ReportCronSchedule
{
    private readonly CronField _minute;
    private readonly CronField _hour;
    private readonly CronField _dayOfMonth;
    private readonly CronField _month;
    private readonly CronField _dayOfWeek;

    private ReportCronSchedule(
        string expression,
        CronField minute,
        CronField hour,
        CronField dayOfMonth,
        CronField month,
        CronField dayOfWeek)
    {
        Expression = expression;
        _minute = minute;
        _hour = hour;
        _dayOfMonth = dayOfMonth;
        _month = month;
        _dayOfWeek = dayOfWeek;
    }

    /// <summary>Original normalized expression.</summary>
    public string Expression { get; }

    /// <summary>Parses a five-field cron expression in UTC.</summary>
    public static ReportCronSchedule Parse(string expression)
    {
        var normalized = Normalize(expression);
        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 5)
        {
            throw new FormatException("Cron expression must contain five fields.");
        }

        return new ReportCronSchedule(
            normalized,
            CronField.Parse(parts[0], 0, 59),
            CronField.Parse(parts[1], 0, 23),
            CronField.Parse(parts[2], 1, 31),
            CronField.Parse(parts[3], 1, 12),
            CronField.Parse(parts[4], 0, 7));
    }

    /// <summary>Finds the next matching UTC minute strictly after <paramref name="afterUtc"/>.</summary>
    public DateTimeOffset GetNextOccurrence(DateTimeOffset afterUtc)
    {
        var utc = afterUtc.ToUniversalTime();
        var candidate = new DateTimeOffset(
            utc.Year,
            utc.Month,
            utc.Day,
            utc.Hour,
            utc.Minute,
            0,
            TimeSpan.Zero).AddMinutes(1);

        for (var i = 0; i < 366 * 24 * 60; i++)
        {
            if (Matches(candidate))
            {
                return candidate;
            }

            candidate = candidate.AddMinutes(1);
        }

        throw new InvalidOperationException("Cron expression did not produce an occurrence within one year.");
    }

    private static string Normalize(string expression)
        => (expression ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "@hourly" => "0 * * * *",
            "@daily" => "0 0 * * *",
            "@weekly" => "0 8 * * 1",
            var value when !string.IsNullOrWhiteSpace(value) => value,
            _ => throw new FormatException("Cron expression is required."),
        };

    private bool Matches(DateTimeOffset candidate)
    {
        var dayOfWeek = (int)candidate.DayOfWeek;
        return _minute.Contains(candidate.Minute)
            && _hour.Contains(candidate.Hour)
            && _dayOfMonth.Contains(candidate.Day)
            && _month.Contains(candidate.Month)
            && (_dayOfWeek.Contains(dayOfWeek) || (dayOfWeek == 0 && _dayOfWeek.Contains(7)));
    }

    private sealed class CronField
    {
        private readonly HashSet<int>? _values;

        private CronField(HashSet<int>? values) => _values = values;

        public static CronField Parse(string field, int min, int max)
        {
            if (field == "*")
            {
                return new CronField(null);
            }

            var values = new HashSet<int>();
            foreach (var token in field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(token, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) ||
                    value < min ||
                    value > max)
                {
                    throw new FormatException($"Cron field value '{token}' is out of range.");
                }

                values.Add(value);
            }

            if (values.Count == 0)
            {
                throw new FormatException("Cron field must not be empty.");
            }

            return new CronField(values);
        }

        public bool Contains(int value) => _values is null || _values.Contains(value);
    }
}

/// <summary>In-memory schedule and subscription store used by the report server demo.</summary>
public sealed class ReportScheduleStore
{
    private readonly IReportScheduleClock _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, ReportScheduleDefinition> _schedules = new(StringComparer.Ordinal);
    private readonly List<ReportSubscription> _subscriptions = [];

    /// <summary>Creates a seeded schedule store.</summary>
    public ReportScheduleStore(IReportScheduleClock clock)
        : this(clock, seedDemoData: true)
    {
    }

    /// <summary>Creates a schedule store with optional demo seed data.</summary>
    public ReportScheduleStore(IReportScheduleClock clock, bool seedDemoData)
    {
        _clock = clock;
        if (seedDemoData)
        {
            Seed();
        }
    }

    /// <summary>Lists schedules for a tenant.</summary>
    public IReadOnlyList<ReportScheduleDefinition> ListSchedules(string tenantId)
    {
        lock (_gate)
        {
            return _schedules.Values
                .Where(schedule => string.Equals(schedule.TenantId, tenantId, StringComparison.Ordinal))
                .OrderBy(schedule => schedule.NextRunUtc)
                .Select(Clone)
                .ToList();
        }
    }

    /// <summary>Gets a schedule by tenant and identifier.</summary>
    public ReportScheduleDefinition? GetSchedule(string tenantId, string scheduleId)
    {
        lock (_gate)
        {
            return _schedules.TryGetValue(Key(tenantId, scheduleId), out var schedule)
                ? Clone(schedule)
                : null;
        }
    }

    /// <summary>Creates or updates a schedule.</summary>
    public ReportScheduleDefinition UpsertSchedule(ReportScheduleDefinition schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var normalized = Clone(schedule);
        if (normalized.NextRunUtc == default)
        {
            normalized.NextRunUtc = ReportCronSchedule.Parse(normalized.CronExpression).GetNextOccurrence(_clock.UtcNow);
        }

        if (normalized.EmailTemplateId == Guid.Empty)
        {
            normalized.EmailTemplateId = ReportEmailTemplateGalleryStore.ReportDigestTemplateId;
        }

        lock (_gate)
        {
            _schedules[Key(normalized.TenantId, normalized.Id)] = normalized;
            return Clone(normalized);
        }
    }

    /// <summary>Enables or disables a schedule.</summary>
    public void ToggleSchedule(string tenantId, string scheduleId, bool isEnabled)
    {
        lock (_gate)
        {
            if (_schedules.TryGetValue(Key(tenantId, scheduleId), out var schedule))
            {
                schedule.IsEnabled = isEnabled;
            }
        }
    }

    /// <summary>Deletes a schedule (no-op when the id is unknown).</summary>
    public void DeleteSchedule(string tenantId, string scheduleId)
    {
        lock (_gate)
        {
            _schedules.Remove(Key(tenantId, scheduleId));
        }
    }

    /// <summary>Lists subscriptions for a tenant and user.</summary>
    public IReadOnlyList<ReportSubscription> ListSubscriptions(string tenantId, string userId)
    {
        lock (_gate)
        {
            return _subscriptions
                .Where(subscription =>
                    string.Equals(subscription.TenantId, tenantId, StringComparison.Ordinal) &&
                    string.Equals(subscription.UserId, userId, StringComparison.Ordinal))
                .Select(Clone)
                .ToList();
        }
    }

    /// <summary>Adds or replaces a user subscription.</summary>
    public void UpsertSubscription(ReportSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        lock (_gate)
        {
            _subscriptions.RemoveAll(item => string.Equals(item.Id, subscription.Id, StringComparison.Ordinal));
            _subscriptions.Add(Clone(subscription));
        }
    }

    /// <summary>Gets enabled schedules that should queue a job at the supplied time.</summary>
    public IReadOnlyList<ReportScheduleDefinition> GetDueSchedules(DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            return _schedules.Values
                .Where(schedule =>
                    schedule.IsEnabled &&
                    (schedule.RetryAfterUtc <= utcNow || schedule.NextRunUtc <= utcNow))
                .Select(Clone)
                .ToList();
        }
    }

    /// <summary>Marks a schedule as queued.</summary>
    public void MarkQueued(ScheduledReportJob job, DateTimeOffset nowUtc, DateTimeOffset? nextRunUtc, bool isRetry)
    {
        lock (_gate)
        {
            if (!_schedules.TryGetValue(Key(job.TenantId, job.ScheduleId), out var schedule))
            {
                return;
            }

            schedule.LastRunUtc = nowUtc;
            schedule.LastStatus = ReportScheduleRunStatus.Queued;
            schedule.LastStatusMessage = isRetry ? $"Retry queued at {nowUtc:HH:mm} UTC" : $"Queued at {nowUtc:HH:mm} UTC";
            schedule.RetryAfterUtc = null;
            if (!isRetry && nextRunUtc is not null)
            {
                schedule.NextRunUtc = nextRunUtc.Value;
            }
        }
    }

    /// <summary>Marks a schedule as delivered.</summary>
    public void MarkDelivered(ScheduledReportJob job, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            if (!_schedules.TryGetValue(Key(job.TenantId, job.ScheduleId), out var schedule))
            {
                return;
            }

            schedule.LastDeliveredUtc = nowUtc;
            schedule.LastStatus = ReportScheduleRunStatus.Delivered;
            schedule.LastStatusMessage = $"Delivered at {nowUtc:HH:mm} UTC";
            schedule.FailureCount = 0;
            schedule.RetryAfterUtc = null;
        }
    }

    /// <summary>Marks a schedule as failed with a future retry timestamp.</summary>
    public void MarkFailed(ScheduledReportJob job, string errorMessage, DateTimeOffset retryAfterUtc)
    {
        lock (_gate)
        {
            if (!_schedules.TryGetValue(Key(job.TenantId, job.ScheduleId), out var schedule))
            {
                return;
            }

            schedule.FailureCount++;
            schedule.RetryAfterUtc = retryAfterUtc;
            schedule.LastStatus = ReportScheduleRunStatus.Retrying;
            schedule.LastStatusMessage = $"{errorMessage}; retry at {retryAfterUtc:HH:mm} UTC";
        }
    }

    private void Seed()
    {
        var schedule = new ReportScheduleDefinition
        {
            Id = "sales-dashboard-digest",
            TenantId = "northwind",
            OwnerUserId = "pavel.author",
            Name = "Executive dashboard digest",
            ReportId = "sales-dashboard",
            CronExpression = "0 8 * * 1",
            Format = ReportScheduleOutputFormat.Pdf,
            CultureName = "en-US",
            Parameters = new Dictionary<string, ReportParameterValue>(StringComparer.Ordinal)
            {
                ["Region"] = ReportParameterValue.Scalar("EU"),
                ["MinimumTotal"] = ReportParameterValue.Scalar(0),
                ["IncludeClosed"] = ReportParameterValue.Scalar(true),
            },
            Recipients =
            [
                new ReportScheduleRecipient("finance@example.test", "Finance Ops"),
                new ReportScheduleRecipient("executive@example.test", "Executive Team"),
            ],
        };
        schedule.NextRunUtc = ReportCronSchedule.Parse(schedule.CronExpression).GetNextOccurrence(_clock.UtcNow);
        UpsertSchedule(schedule);

        UpsertSubscription(new ReportSubscription
        {
            Id = "sub-sales-dashboard-pavel",
            TenantId = "northwind",
            ScheduleId = schedule.Id,
            UserId = "pavel.author",
            Email = "pavel.author@example.test",
        });
    }

    private static string Key(string tenantId, string scheduleId) => $"{tenantId}:{scheduleId}";

    private static ReportScheduleDefinition Clone(ReportScheduleDefinition source)
        => new()
        {
            Id = source.Id,
            TenantId = source.TenantId,
            OwnerUserId = source.OwnerUserId,
            Name = source.Name,
            ReportId = source.ReportId,
            CronExpression = source.CronExpression,
            Format = source.Format,
            EmailTemplateId = source.EmailTemplateId,
            CultureName = source.CultureName,
            Parameters = new Dictionary<string, ReportParameterValue>(source.Parameters, StringComparer.Ordinal),
            Recipients = source.Recipients.ToList(),
            IsEnabled = source.IsEnabled,
            NextRunUtc = source.NextRunUtc,
            LastRunUtc = source.LastRunUtc,
            LastDeliveredUtc = source.LastDeliveredUtc,
            RetryAfterUtc = source.RetryAfterUtc,
            FailureCount = source.FailureCount,
            LastStatus = source.LastStatus,
            LastStatusMessage = source.LastStatusMessage,
        };

    private static ReportSubscription Clone(ReportSubscription source)
        => new()
        {
            Id = source.Id,
            TenantId = source.TenantId,
            ScheduleId = source.ScheduleId,
            UserId = source.UserId,
            Email = source.Email,
            IsEnabled = source.IsEnabled,
        };
}

/// <summary>Queued scheduled report render job.</summary>
public sealed class ScheduledReportJob
{
    /// <summary>Render job identifier.</summary>
    public string JobId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Schedule identifier.</summary>
    public string ScheduleId { get; init; } = string.Empty;

    /// <summary>Schedule display name.</summary>
    public string ScheduleName { get; init; } = string.Empty;

    /// <summary>Tenant identifier.</summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>User identifier.</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>Report identifier.</summary>
    public string ReportId { get; init; } = string.Empty;

    /// <summary>Output format.</summary>
    public ReportScheduleOutputFormat Format { get; init; } = ReportScheduleOutputFormat.Pdf;

    /// <summary>Email template identifier.</summary>
    public Guid EmailTemplateId { get; init; } = ReportEmailTemplateGalleryStore.ReportDigestTemplateId;

    /// <summary>Render culture.</summary>
    public string CultureName { get; init; } = "en-US";

    /// <summary>Report parameters.</summary>
    public IReadOnlyDictionary<string, ReportParameterValue> Parameters { get; init; } =
        new Dictionary<string, ReportParameterValue>(StringComparer.Ordinal);

    /// <summary>Recipients.</summary>
    public IReadOnlyList<ReportScheduleRecipient> Recipients { get; init; } = [];

    /// <summary>Timestamp when the job was queued.</summary>
    public DateTimeOffset QueuedAtUtc { get; init; }

    /// <summary>Original due timestamp.</summary>
    public DateTimeOffset DueAtUtc { get; init; }

    /// <summary>Delivery attempt number.</summary>
    public int Attempt { get; init; } = 1;
}

/// <summary>In-memory render job queue with history for the demo server.</summary>
public sealed class ReportRenderJobQueue
{
    private readonly IReportScheduleClock _clock;
    private readonly object _gate = new();
    private readonly List<ScheduledReportJob> _pending = [];
    private readonly List<ScheduledReportJob> _history = [];

    /// <summary>Creates a render job queue.</summary>
    public ReportRenderJobQueue(IReportScheduleClock clock) => _clock = clock;

    /// <summary>Pending job count.</summary>
    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>Queued job history.</summary>
    public IReadOnlyList<ScheduledReportJob> History
    {
        get
        {
            lock (_gate)
            {
                return _history.ToList();
            }
        }
    }

    /// <summary>Queues a job.</summary>
    public Task EnqueueAsync(ScheduledReportJob job, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var queued = Clone(job, job.QueuedAtUtc == default ? _clock.UtcNow : job.QueuedAtUtc);
        lock (_gate)
        {
            _pending.Add(queued);
            _history.Add(queued);
        }

        return Task.CompletedTask;
    }

    /// <summary>Dequeues the next job, optionally by identifier.</summary>
    public Task<ScheduledReportJob?> DequeueAsync(string? jobId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var index = jobId is null
                ? (_pending.Count == 0 ? -1 : 0)
                : _pending.FindIndex(job => string.Equals(job.JobId, jobId, StringComparison.Ordinal));
            if (index < 0)
            {
                return Task.FromResult<ScheduledReportJob?>(null);
            }

            var job = _pending[index];
            _pending.RemoveAt(index);
            return Task.FromResult<ScheduledReportJob?>(job);
        }
    }

    private static ScheduledReportJob Clone(ScheduledReportJob source, DateTimeOffset queuedAtUtc)
        => new()
        {
            JobId = source.JobId,
            ScheduleId = source.ScheduleId,
            ScheduleName = source.ScheduleName,
            TenantId = source.TenantId,
            UserId = source.UserId,
            ReportId = source.ReportId,
            Format = source.Format,
            EmailTemplateId = source.EmailTemplateId,
            CultureName = source.CultureName,
            Parameters = new Dictionary<string, ReportParameterValue>(source.Parameters, StringComparer.Ordinal),
            Recipients = source.Recipients.ToList(),
            QueuedAtUtc = queuedAtUtc,
            DueAtUtc = source.DueAtUtc,
            Attempt = source.Attempt,
        };
}

/// <summary>Delivery service used by scheduled report workers.</summary>
public interface IReportScheduledDeliveryService
{
    /// <summary>Renders and delivers a queued scheduled report.</summary>
    Task<DeliveredReportEmail> DeliverAsync(ScheduledReportJob job, CancellationToken cancellationToken = default);
}

/// <summary>Background-worker compatible orchestrator for scheduled reports.</summary>
public sealed class ReportScheduleWorker
{
    private readonly ReportScheduleStore _store;
    private readonly ReportRenderJobQueue _queue;
    private readonly IReportScheduledDeliveryService _delivery;
    private readonly IReportScheduleClock _clock;

    /// <summary>Creates a schedule worker.</summary>
    public ReportScheduleWorker(
        ReportScheduleStore store,
        ReportRenderJobQueue queue,
        IReportScheduledDeliveryService delivery,
        IReportScheduleClock clock)
    {
        _store = store;
        _queue = queue;
        _delivery = delivery;
        _clock = clock;
    }

    /// <summary>Queues all due schedules.</summary>
    public async Task<int> TriggerDueSchedulesAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var due = _store.GetDueSchedules(now);
        foreach (var schedule in due)
        {
            var isRetry = schedule.RetryAfterUtc <= now;
            var nextRun = isRetry ? schedule.NextRunUtc : ReportCronSchedule.Parse(schedule.CronExpression).GetNextOccurrence(now);
            var job = CreateJob(schedule, now, isRetry);
            await _queue.EnqueueAsync(job, cancellationToken).ConfigureAwait(false);
            _store.MarkQueued(job, now, nextRun, isRetry);
        }

        return due.Count;
    }

    /// <summary>Processes every queued job once.</summary>
    public async Task<int> ProcessQueuedJobsAsync(CancellationToken cancellationToken = default)
    {
        var processed = 0;
        while (await _queue.DequeueAsync(cancellationToken: cancellationToken).ConfigureAwait(false) is { } job)
        {
            await ProcessJobAsync(job, cancellationToken).ConfigureAwait(false);
            processed++;
        }

        return processed;
    }

    /// <summary>Queues and immediately processes a schedule.</summary>
    public async Task<DeliveredReportEmail?> RunScheduleNowAsync(
        string tenantId,
        string scheduleId,
        CancellationToken cancellationToken = default)
    {
        var schedule = _store.GetSchedule(tenantId, scheduleId);
        if (schedule is null)
        {
            return null;
        }

        var now = _clock.UtcNow;
        var job = CreateJob(schedule, now, isRetry: false);
        await _queue.EnqueueAsync(job, cancellationToken).ConfigureAwait(false);
        _store.MarkQueued(job, now, ReportCronSchedule.Parse(schedule.CronExpression).GetNextOccurrence(now), isRetry: false);
        var queued = await _queue.DequeueAsync(job.JobId, cancellationToken).ConfigureAwait(false);
        return queued is null ? null : await ProcessJobAsync(queued, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DeliveredReportEmail?> ProcessJobAsync(ScheduledReportJob job, CancellationToken cancellationToken)
    {
        try
        {
            var delivered = await _delivery.DeliverAsync(job, cancellationToken).ConfigureAwait(false);
            _store.MarkDelivered(job, _clock.UtcNow);
            return delivered;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var current = _store.GetSchedule(job.TenantId, job.ScheduleId);
            var failureCount = (current?.FailureCount ?? 0) + 1;
            _store.MarkFailed(job, ex.Message, _clock.UtcNow + RetryBackoff(failureCount));
            return null;
        }
    }

    private static TimeSpan RetryBackoff(int failureCount)
        => TimeSpan.FromMinutes(Math.Min(30, Math.Pow(2, Math.Max(1, failureCount))));

    private static ScheduledReportJob CreateJob(
        ReportScheduleDefinition schedule,
        DateTimeOffset now,
        bool isRetry)
        => new()
        {
            ScheduleId = schedule.Id,
            ScheduleName = schedule.Name,
            TenantId = schedule.TenantId,
            UserId = schedule.OwnerUserId,
            ReportId = schedule.ReportId,
            Format = schedule.Format,
            EmailTemplateId = schedule.EmailTemplateId,
            CultureName = schedule.CultureName,
            Parameters = new Dictionary<string, ReportParameterValue>(schedule.Parameters, StringComparer.Ordinal),
            Recipients = schedule.Recipients.ToList(),
            QueuedAtUtc = now,
            DueAtUtc = isRetry ? schedule.RetryAfterUtc ?? now : schedule.NextRunUtc,
            Attempt = isRetry ? schedule.FailureCount + 1 : 1,
        };
}

/// <summary>Email attachment captured by the report server outbox.</summary>
public sealed record ReportEmailAttachment(string FileName, string ContentType, byte[] Bytes);

/// <summary>Transport-level email snapshot captured for diagnostics and demo UX.</summary>
public sealed record EmailMessageSnapshot(IReadOnlyList<string> To, string Subject, string Transport)
{
    /// <summary>Rendered HTML body.</summary>
    public string Html { get; init; } = string.Empty;

    /// <summary>Rendered text body.</summary>
    public string Text { get; init; } = string.Empty;
}

/// <summary>Delivered scheduled report email with attachment metadata.</summary>
public sealed record DeliveredReportEmail(
    string JobId,
    string ScheduleId,
    string TenantId,
    EmailMessageSnapshot Message,
    IReadOnlyList<ReportEmailAttachment> Attachments,
    DateTimeOffset SentAtUtc);

/// <summary>In-memory outbox used by the smtp4dev demo flow.</summary>
public sealed class ReportEmailOutbox
{
    private readonly object _gate = new();
    private readonly List<DeliveredReportEmail> _messages = [];

    /// <summary>Captured delivered emails.</summary>
    public IReadOnlyList<DeliveredReportEmail> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages.ToList();
            }
        }
    }

    /// <summary>Records an email delivery.</summary>
    public void Record(DeliveredReportEmail message)
    {
        lock (_gate)
        {
            _messages.Insert(0, message);
        }
    }

    /// <summary>Clears the outbox.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _messages.Clear();
        }
    }
}

/// <summary>Demo SMTP sender that models delivery through smtp4dev.</summary>
public sealed class Smtp4DevEmailSender : IEmailSender
{
    /// <summary>Demo transport label shown in diagnostics.</summary>
    public const string Transport = "smtp4dev://localhost:2525";

    /// <summary>Messages passed to the transport.</summary>
    public List<EmailMessage> SentMessages { get; } = [];

    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SentMessages.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>Report email delivery implementation using Tempo email templates.</summary>
public sealed class ReportEmailDeliveryService : IReportScheduledDeliveryService
{
    private readonly IEmailTemplateStore _templateStore;
    private readonly IEmailTemplateRenderer _renderer;
    private readonly IEmailSender _sender;
    private readonly DemoReportSourceFactory _sourceFactory;
    private readonly ReportServerCatalogStore _catalog;
    private readonly ReportEmailOutbox _outbox;
    private readonly IReportScheduleClock _clock;

    /// <summary>Creates a report email delivery service.</summary>
    public ReportEmailDeliveryService(
        IEmailTemplateStore templateStore,
        IEmailTemplateRenderer renderer,
        IEmailSender sender,
        DemoReportSourceFactory sourceFactory,
        ReportServerCatalogStore catalog,
        ReportEmailOutbox outbox,
        IReportScheduleClock clock)
    {
        _templateStore = templateStore;
        _renderer = renderer;
        _sender = sender;
        _sourceFactory = sourceFactory;
        _catalog = catalog;
        _outbox = outbox;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<DeliveredReportEmail> DeliverAsync(ScheduledReportJob job, CancellationToken cancellationToken = default)
    {
        var export = await ExportAsync(job, cancellationToken).ConfigureAwait(false);
        var detail = await _templateStore.GetAsync(job.EmailTemplateId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Email template was not found.");
        var document = EmailTemplateSerializer.Deserialize(detail.ContentJson);
        var report = _catalog.GetCatalog(job.TenantId).Reports.FirstOrDefault(item =>
            string.Equals(item.Id, job.ReportId, StringComparison.Ordinal));
        var model = new
        {
            ReportName = report?.Name ?? job.ReportId,
            ReportId = job.ReportId,
            ScheduleName = string.IsNullOrWhiteSpace(job.ScheduleName) ? job.ScheduleId : job.ScheduleName,
            TenantName = job.TenantId,
            Format = job.Format.ToString().ToUpperInvariant(),
            AttachmentFileName = export.FileName,
            SentAt = _clock.UtcNow,
        };
        var rendered = await _renderer.RenderAsync(document, model, cancellationToken).ConfigureAwait(false);
        if (!rendered.Success)
        {
            throw new InvalidOperationException(string.Join("; ", rendered.Errors.Select(error => error.Message)));
        }

        var recipients = job.Recipients.Select(recipient => recipient.Email).Where(email => !string.IsNullOrWhiteSpace(email)).ToList();
        if (recipients.Count == 0)
        {
            throw new InvalidOperationException("At least one recipient is required.");
        }

        var message = new EmailMessage(
            From: null,
            To: recipients,
            Cc: [],
            Subject: rendered.Subject,
            Html: rendered.Html,
            Text: rendered.TextVersion);
        await _sender.SendAsync(message, cancellationToken).ConfigureAwait(false);

        var delivered = new DeliveredReportEmail(
            job.JobId,
            job.ScheduleId,
            job.TenantId,
            new EmailMessageSnapshot(message.To, message.Subject, Smtp4DevEmailSender.Transport)
            {
                Html = message.Html,
                Text = message.Text,
            },
            [new ReportEmailAttachment(export.FileName, export.ContentType, export.Bytes)],
            _clock.UtcNow);
        _outbox.Record(delivered);
        return delivered;
    }

    private async Task<ReportViewerExportResult> ExportAsync(ScheduledReportJob job, CancellationToken cancellationToken)
    {
        var source = _sourceFactory.CreateReportSource(job.ReportId);
        var request = new ReportViewerRenderRequest
        {
            TenantId = job.TenantId,
            UserId = job.UserId,
            CultureName = job.CultureName,
            Parameters = job.Parameters,
        };

        return job.Format switch
        {
            ReportScheduleOutputFormat.Csv => await source.ExportCsvAsync(request, cancellationToken).ConfigureAwait(false),
            ReportScheduleOutputFormat.Xlsx => await source.ExportXlsxAsync(request, cancellationToken).ConfigureAwait(false),
            _ => await source.ExportPdfAsync(request, cancellationToken).ConfigureAwait(false),
        };
    }
}

/// <summary>Read/write gallery store for report email templates.</summary>
public sealed class ReportEmailTemplateGalleryStore : IEmailTemplateStore
{
    /// <summary>Seeded report digest template identifier.</summary>
    public static readonly Guid ReportDigestTemplateId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly object _gate = new();
    private readonly Dictionary<Guid, EmailTemplateDocument> _documents = new();

    /// <summary>Creates a gallery store with a scheduled report template.</summary>
    public ReportEmailTemplateGalleryStore() => Seed();

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailTemplateSummaryDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<EmailTemplateSummaryDto> summaries = _documents.Values
                .OrderBy(document => document.Name, StringComparer.OrdinalIgnoreCase)
                .Select(document => EmailTemplateMapper.ToSummaryDto(document))
                .ToList();
            return Task.FromResult(summaries);
        }
    }

    /// <inheritdoc />
    public Task<EmailTemplateDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_documents.TryGetValue(id, out var document)
                ? EmailTemplateMapper.ToDetailDto(document)
                : null);
        }
    }

    /// <inheritdoc />
    public Task<EmailTemplateDetailDto> CreateAsync(CreateEmailTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var document = EmailTemplateMapper.ApplyCreate(request);
        document.Id = Guid.NewGuid();
        lock (_gate)
        {
            _documents[document.Id] = document;
        }

        return Task.FromResult(EmailTemplateMapper.ToDetailDto(document));
    }

    /// <inheritdoc />
    public Task<bool> UpdateAsync(Guid id, UpdateEmailTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var document = EmailTemplateMapper.ApplyUpdate(request);
        document.Id = id;
        lock (_gate)
        {
            if (!_documents.ContainsKey(id))
            {
                return Task.FromResult(false);
            }

            _documents[id] = document;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_documents.Remove(id));
        }
    }

    /// <inheritdoc />
    public Task<bool> IsNameAvailableAsync(string name, Guid? excludingId = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var exists = _documents.Values.Any(document =>
                document.Id != excludingId &&
                string.Equals(document.Name, name, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(!exists);
        }
    }

    private void Seed()
    {
        var document = new EmailTemplateDocument
        {
            Id = ReportDigestTemplateId,
            Name = "Scheduled report digest",
            Subject = "Scheduled report {{ report_name }} is ready",
            Preheader = "{{ attachment_file_name }} from Tempo Report Server",
            Language = "en",
            UpdatedAt = DateTime.UtcNow,
        };
        var column = new EmailColumn();
        column.Blocks.Add(new EmailTextBlock
        {
            Content = "<h1>{{ report_name }}</h1><p>{{ schedule_name }} was generated for {{ tenant_name }}.</p><p>Attached file: <strong>{{ attachment_file_name }}</strong></p><p>Format: {{ format }}</p>",
            FontSize = "15px",
            LineHeight = "1.5",
        });
        var section = new EmailSection { BackgroundColor = "#ffffff" };
        section.Columns.Add(column);
        document.Sections.Add(section);

        _documents[document.Id] = document;
    }
}
