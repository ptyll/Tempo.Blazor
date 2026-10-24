using Tempo.Blazor.EmailTemplates.Abstractions.Contracts;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;
using Tempo.Blazor.Reporting.Models;
using Tempo.Reporting.Abstractions.Data;

namespace Tempo.ReportServer.Web.Services;
/// <summary>Background-worker compatible orchestrator for scheduled reports.</summary>
public sealed class ReportScheduleWorker : IReportScheduleRunner
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

    /// <inheritdoc />
    public bool CanRun => true;

    /// <inheritdoc />
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
        var rendered = await _renderer.RenderAsync(document, model, cancellationToken: cancellationToken).ConfigureAwait(false);
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

