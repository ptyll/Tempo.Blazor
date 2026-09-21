#pragma warning disable MA0048

using Tempo.Blazor.Reporting.Interop;
using Tempo.Blazor.Reporting.Models;
using Tempo.Reporting.Abstractions.Data;
using Tempo.Reporting.Abstractions.Definitions;
using Tempo.Reporting.Abstractions.Dtos;
using Tempo.Reporting.Abstractions.Serialization;
using DtoScheduleRunStatus = Tempo.Reporting.Abstractions.Dtos.ReportScheduleRunStatus;

namespace Tempo.ReportServer.Web.Services;

/// <summary>
/// In-memory <see cref="ITempoReportServerClient"/> used when <c>Api:BaseUrl</c> is not configured —
/// the self-contained demo mode where the Web host runs without the Report Server Api. It serves the
/// full client surface (catalog, revisions, permissions, data sources, schedules, API keys, audit,
/// favorites, render runs, resolve) from seeded demo state so every portal page works offline, on both
/// InteractiveAuto legs.
/// </summary>
/// <remarks>
/// Schedules delegate to <see cref="ReportScheduleStore"/> and schedule-run history is derived from
/// <see cref="ReportEmailOutbox"/>, so the run-now button (<see cref="ReportScheduleWorker.RunScheduleNowAsync"/>)
/// renders/delivers through the same path the schedules page lists. Rendering and parameter metadata go
/// through <see cref="DemoReportSourceFactory"/>, which keeps the hand-authored demo definitions
/// (sales-register, sales-dashboard, …) as the single source of truth.
/// </remarks>
public sealed class DemoTempoReportServerClient : ITempoReportServerClient
{
    private readonly object _gate = new();
    private readonly DemoReportSourceFactory _sourceFactory;
    private readonly ReportScheduleStore _schedules;
    private readonly ReportEmailOutbox _outbox;
    private readonly IReportScheduleClock _clock;

    private readonly List<ReportFolderDto> _folders;
    private readonly List<ReportSummaryDto> _reports;
    private readonly List<ReportRevisionDto> _revisions;
    private readonly List<ReportDataSourceDto> _dataSources;
    private readonly List<ReportFolderAclEntryDto> _acls;
    private readonly List<ReportApiKeyDto> _apiKeys;
    private readonly List<ReportAuditEventDto> _audit = [];
    private readonly List<ReportFavoriteDto> _favorites = [];
    private readonly List<RenderRunDto> _renderRuns = [];
    private readonly Dictionary<string, RenderJobDto> _renderJobs = new(StringComparer.Ordinal);

    // Per-report stored definition JSON (created/edited reports). Seeded demo reports resolve their
    // definition lazily from DemoReportSourceFactory so the designer edits the real document.
    private readonly Dictionary<string, string> _definitionByReportId = new(StringComparer.Ordinal);
    private int _idCounter;

    /// <summary>Creates the seeded self-contained demo client.</summary>
    public DemoTempoReportServerClient(
        DemoReportSourceFactory sourceFactory,
        ReportScheduleStore schedules,
        ReportEmailOutbox outbox,
        IReportScheduleClock clock)
    {
        _sourceFactory = sourceFactory;
        _schedules = schedules;
        _outbox = outbox;
        _clock = clock;

        var seedTime = DateTimeOffset.Parse("2026-06-20T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        _folders =
        [
            Folder("northwind", "folder-finance", null, "Finance", "/finance"),
            Folder("northwind", "folder-finance-month-end", "folder-finance", "Month End", "/finance/month-end"),
            Folder("northwind", "folder-operations", null, "Operations", "/operations"),
            Folder("northwind", "folder-executive", null, "Executive", "/executive"),
            Folder("contoso", "folder-contoso-operations", null, "Operations", "/operations"),
            Folder("contoso", "folder-contoso-finance", null, "Finance", "/finance"),
        ];

        _reports =
        [
            Summary("northwind", "sales-register", "folder-finance", "Sales Register", "Sales orders, totals and payment status.", 12, seedTime.AddHours(-5)),
            Summary("northwind", "invoice-aging", "folder-finance", "Invoice Aging", "Open receivables grouped by due date.", 7, seedTime.AddDays(-1)),
            Summary("northwind", "sales-dashboard", "folder-executive", "Dashboard prodejů", "Executive sales dashboard with three charts and an order table.", 3, seedTime.AddHours(-7)),
            Summary("northwind", "margin-watch", "folder-executive", "Margin Watch", "Gross margin trend by product family.", 4, seedTime.AddDays(-2)),
            Summary("northwind", "fulfillment-sla", "folder-operations", "Fulfillment SLA", "Warehouse SLA by region and carrier.", 9, seedTime.AddDays(-3)),
            Summary("contoso", "sales-register", "folder-contoso-operations", "Sales Register", "Contoso order register with regional filters.", 5, seedTime.AddDays(-1)),
            Summary("contoso", "fulfillment-sla", "folder-contoso-operations", "Fulfillment SLA", "Carrier and warehouse SLA report.", 8, seedTime.AddDays(-2)),
        ];

        _revisions =
        [
            Revision("sales-register-r12", "northwind", "sales-register", 12, "Pavel Author", seedTime.AddHours(-2), "Added IncludeClosed parameter; changed Sales dataset timeout.", isCurrent: true),
            Revision("sales-register-r11", "northwind", "sales-register", 11, "Pavel Author", seedTime.AddDays(-2), "Updated totals textbox format and footer metadata."),
            Revision("sales-dashboard-r3", "northwind", "sales-dashboard", 3, "Pavel Author", seedTime.AddHours(-7), "Added engine-drawn chart dashboard fixture.", isCurrent: true),
            Revision("invoice-aging-r7", "northwind", "invoice-aging", 7, "Eva Finance", seedTime.AddDays(-1), "Changed aging buckets from 15 to 30 days.", isCurrent: true),
            Revision("margin-watch-r4", "northwind", "margin-watch", 4, "Pavel Author", seedTime.AddDays(-2), "Added product-family margin trend.", isCurrent: true),
            Revision("fulfillment-sla-r9", "northwind", "fulfillment-sla", 9, "Dana Ops", seedTime.AddDays(-3), "Added carrier filter and SLA breach badge.", isCurrent: true),
            Revision("sales-register-r5", "contoso", "sales-register", 5, "Ops Analytics", seedTime.AddDays(-1), "Regional filters added.", isCurrent: true),
            Revision("fulfillment-sla-r8", "contoso", "fulfillment-sla", 8, "Ops Analytics", seedTime.AddDays(-2), "Carrier SLA badges.", isCurrent: true),
        ];

        _dataSources =
        [
            new ReportDataSourceDto { TenantId = "northwind", DataSourceId = "erp-sql", Name = "ERP SQL", Kind = "SQL", Connection = "Server=erp-sql;Database=Reporting;" },
            new ReportDataSourceDto { TenantId = "northwind", DataSourceId = "crm-rest", Name = "CRM REST", Kind = "REST JSON", Connection = "https://api.example.test/crm" },
            new ReportDataSourceDto { TenantId = "contoso", DataSourceId = "contoso-warehouse", Name = "Warehouse Lakehouse", Kind = "SQL", Connection = "Server=lakehouse;Database=Warehouse;" },
        ];

        _acls =
        [
            new ReportFolderAclEntryDto { TenantId = "northwind", FolderId = "folder-finance", SubjectKind = ReportAclSubjectKindDto.Role, SubjectId = "finance-admins", Effect = ReportAclEffectDto.Allow, Permissions = ReportPermissionsDto.All },
            new ReportFolderAclEntryDto { TenantId = "northwind", FolderId = "folder-finance", SubjectKind = ReportAclSubjectKindDto.Role, SubjectId = "sales-authors", Effect = ReportAclEffectDto.Allow, Permissions = ReportPermissionsDto.View | ReportPermissionsDto.Render },
            new ReportFolderAclEntryDto { TenantId = "northwind", FolderId = "folder-executive", SubjectKind = ReportAclSubjectKindDto.Role, SubjectId = "contractors", Effect = ReportAclEffectDto.Deny, Permissions = ReportPermissionsDto.View },
            new ReportFolderAclEntryDto { TenantId = "contoso", FolderId = "folder-contoso-operations", SubjectKind = ReportAclSubjectKindDto.Role, SubjectId = "contoso-ops", Effect = ReportAclEffectDto.Allow, Permissions = ReportPermissionsDto.View | ReportPermissionsDto.Render },
        ];

        // A deterministic, active embedding key so the API-keys page has a stable row to rotate/revoke.
        _apiKeys =
        [
            new ReportApiKeyDto
            {
                KeyId = "rk_demo_embed",
                TenantId = "northwind",
                ApplicationId = "embedded-app",
                Permissions = ReportPermissionsDto.View | ReportPermissionsDto.Render,
                CreatedAt = DateTimeOffset.Parse("2026-06-01T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                IsActive = true,
            },
        ];
    }

    // ---- Folders --------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportFolderDto>> GetFoldersAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ReportFolderDto>>(
                [.. _folders.Where(folder => folder.TenantId == tenantId).OrderBy(folder => folder.Path, StringComparer.Ordinal)]);
        }
    }

    /// <inheritdoc />
    public Task<ReportFolderDto> CreateFolderAsync(CreateReportFolderRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var parent = _folders.FirstOrDefault(folder =>
                folder.TenantId == request.TenantId && folder.FolderId == request.ParentFolderId);
            var path = parent is null
                ? "/" + request.Name.Trim('/')
                : parent.Path.TrimEnd('/') + "/" + request.Name.Trim('/');
            var folder = new ReportFolderDto
            {
                TenantId = request.TenantId,
                FolderId = $"folder-{Slug(request.Name)}-{++_idCounter}",
                ParentFolderId = string.IsNullOrWhiteSpace(request.ParentFolderId) ? null : request.ParentFolderId,
                Name = request.Name.Trim(),
                Path = path,
            };
            _folders.Add(folder);
            return Task.FromResult(folder);
        }
    }

    /// <inheritdoc />
    public Task<ReportFolderDto> UpdateFolderAsync(string folderId, string tenantId, UpdateReportFolderRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var index = _folders.FindIndex(folder => folder.TenantId == tenantId && folder.FolderId == folderId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Unknown folder '{folderId}'.");
            }

            var folder = _folders[index];
            var oldPath = folder.Path;
            var parent = _folders.FirstOrDefault(candidate => candidate.FolderId == folder.ParentFolderId);
            var newPath = parent is null
                ? "/" + request.Name.Trim('/')
                : parent.Path.TrimEnd('/') + "/" + request.Name.Trim('/');
            _folders[index] = folder with { Name = request.Name.Trim(), Path = newPath };

            // Descendant paths carry the renamed prefix — rewrite them so /resolve stays consistent.
            for (var i = 0; i < _folders.Count; i++)
            {
                if (_folders[i].TenantId == tenantId
                    && _folders[i].Path.StartsWith(oldPath + "/", StringComparison.Ordinal))
                {
                    _folders[i] = _folders[i] with { Path = newPath + _folders[i].Path[oldPath.Length..] };
                }
            }

            return Task.FromResult(_folders[index]);
        }
    }

    /// <inheritdoc />
    public Task<ReportFolderDto> MoveFolderAsync(string folderId, string tenantId, MoveReportFolderRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var index = _folders.FindIndex(folder => folder.TenantId == tenantId && folder.FolderId == folderId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Unknown folder '{folderId}'.");
            }

            var folder = _folders[index];
            var oldPath = folder.Path;
            var parent = _folders.FirstOrDefault(candidate =>
                candidate.TenantId == tenantId && candidate.FolderId == request.ParentFolderId);
            var newPath = parent is null
                ? "/" + folder.Name
                : parent.Path.TrimEnd('/') + "/" + folder.Name;
            _folders[index] = folder with { ParentFolderId = request.ParentFolderId, Path = newPath };
            for (var i = 0; i < _folders.Count; i++)
            {
                if (_folders[i].TenantId == tenantId
                    && _folders[i].FolderId != folderId
                    && _folders[i].Path.StartsWith(oldPath + "/", StringComparison.Ordinal))
                {
                    _folders[i] = _folders[i] with { Path = newPath + _folders[i].Path[oldPath.Length..] };
                }
            }

            return Task.FromResult(_folders[index]);
        }
    }

    /// <inheritdoc />
    public Task DeleteFolderAsync(string folderId, string tenantId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var folder = _folders.FirstOrDefault(item => item.TenantId == tenantId && item.FolderId == folderId);
            if (folder is null)
            {
                return Task.CompletedTask;
            }

            _folders.RemoveAll(item => item.TenantId == tenantId
                && (item.FolderId == folderId || item.Path.StartsWith(folder.Path + "/", StringComparison.Ordinal)));
            return Task.CompletedTask;
        }
    }

    // ---- Reports --------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportSummaryDto>> SearchReportsAsync(ReportSearchRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var reports = _reports.Where(report => report.TenantId == request.TenantId);
            if (!string.IsNullOrWhiteSpace(request.FolderId))
            {
                reports = reports.Where(report => report.FolderId == request.FolderId);
            }

            if (!string.IsNullOrWhiteSpace(request.Query))
            {
                var query = request.Query.Trim();
                reports = reports.Where(report =>
                    report.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (report.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            return Task.FromResult<IReadOnlyList<ReportSummaryDto>>(
                [.. reports.OrderBy(report => report.Name, StringComparer.Ordinal)]);
        }
    }

    /// <inheritdoc />
    public Task<ReportDetailDto> GetReportAsync(string reportId, string tenantId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var report = _reports.FirstOrDefault(item => item.TenantId == tenantId && item.ReportId == reportId)
                ?? throw new KeyNotFoundException($"Unknown report '{reportId}'.");
            return Task.FromResult(DetailOf(report));
        }
    }

    /// <inheritdoc />
    public Task<ReportDetailDto> CreateReportAsync(CreateReportRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var reportId = Slug(request.Name);
            if (_reports.Any(report => report.TenantId == request.TenantId && report.ReportId == reportId))
            {
                reportId = $"{reportId}-{++_idCounter}";
            }

            var revisionId = $"{reportId}-r1";
            _definitionByReportId[reportId] = request.DefinitionJson;
            _revisions.Add(new ReportRevisionDto
            {
                TenantId = request.TenantId,
                RevisionId = revisionId,
                ReportId = reportId,
                RevisionNumber = 1,
                DefinitionJson = request.DefinitionJson,
                CreatedByUserId = "demo.user",
                CreatedAt = _clock.UtcNow,
                Comment = "Initial revision.",
                IsPublished = true,
            });
            var report = new ReportSummaryDto
            {
                TenantId = request.TenantId,
                ReportId = reportId,
                FolderId = request.FolderId,
                Name = request.Name.Trim(),
                Description = request.Description,
                LatestRevisionId = revisionId,
                CreatedAt = _clock.UtcNow,
                UpdatedAt = _clock.UtcNow,
            };
            _reports.Add(report);
            return Task.FromResult(DetailOf(report));
        }
    }

    /// <inheritdoc />
    public Task<ReportDetailDto> MoveReportAsync(string reportId, string tenantId, MoveReportRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var index = _reports.FindIndex(report => report.TenantId == tenantId && report.ReportId == reportId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Unknown report '{reportId}'.");
            }

            _reports[index] = _reports[index] with { FolderId = request.FolderId, UpdatedAt = _clock.UtcNow };
            return Task.FromResult(DetailOf(_reports[index]));
        }
    }

    /// <inheritdoc />
    public Task DeleteReportAsync(string reportId, string tenantId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _reports.RemoveAll(report => report.TenantId == tenantId && report.ReportId == reportId);
            _revisions.RemoveAll(revision => revision.TenantId == tenantId && revision.ReportId == reportId);
            _definitionByReportId.Remove(reportId);
            _favorites.RemoveAll(favorite => favorite.TenantId == tenantId && favorite.ReportId == reportId);
            return Task.CompletedTask;
        }
    }

    // ---- Revisions ------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task<ReportRevisionDto> UpdateReportDefinitionAsync(UpdateReportDefinitionRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var index = _reports.FindIndex(report => report.TenantId == request.TenantId && report.ReportId == request.ReportId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Unknown report '{request.ReportId}'.");
            }

            var nextNumber = _revisions
                .Where(revision => revision.TenantId == request.TenantId && revision.ReportId == request.ReportId)
                .Select(revision => revision.RevisionNumber)
                .DefaultIfEmpty(0)
                .Max() + 1;
            var revision = new ReportRevisionDto
            {
                TenantId = request.TenantId,
                RevisionId = $"{request.ReportId}-r{nextNumber}",
                ReportId = request.ReportId,
                RevisionNumber = nextNumber,
                DefinitionJson = request.DefinitionJson,
                CreatedByUserId = "demo.user",
                CreatedAt = _clock.UtcNow,
                Comment = request.Comment,
                IsPublished = true,
            };
            _revisions.Add(revision);
            _definitionByReportId[request.ReportId] = request.DefinitionJson;
            _reports[index] = _reports[index] with { LatestRevisionId = revision.RevisionId, UpdatedAt = _clock.UtcNow };
            return Task.FromResult(revision);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportRevisionDto>> GetRevisionsAsync(string reportId, string tenantId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ReportRevisionDto>>(
                [.. _revisions
                    .Where(revision => revision.TenantId == tenantId && revision.ReportId == reportId)
                    .OrderByDescending(revision => revision.RevisionNumber)]);
        }
    }

    /// <inheritdoc />
    public Task<ReportRevisionDto> PublishRevisionAsync(string reportId, string tenantId, PublishReportRevisionRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var index = _revisions.FindIndex(revision =>
                revision.TenantId == tenantId && revision.ReportId == reportId && revision.RevisionId == request.RevisionId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Unknown revision '{request.RevisionId}'.");
            }

            _revisions[index] = _revisions[index] with { IsPublished = true };
            return Task.FromResult(_revisions[index]);
        }
    }

    /// <inheritdoc />
    public Task<ReportRevisionDto> RollbackRevisionAsync(string reportId, string tenantId, RollbackReportRevisionRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var source = _revisions.FirstOrDefault(revision =>
                revision.TenantId == tenantId && revision.ReportId == reportId && revision.RevisionId == request.RevisionId)
                ?? throw new KeyNotFoundException($"Unknown revision '{request.RevisionId}'.");
            var nextNumber = _revisions
                .Where(revision => revision.TenantId == tenantId && revision.ReportId == reportId)
                .Max(revision => revision.RevisionNumber) + 1;
            var revision = new ReportRevisionDto
            {
                TenantId = tenantId,
                RevisionId = $"{reportId}-r{nextNumber}",
                ReportId = reportId,
                RevisionNumber = nextNumber,
                DefinitionJson = source.DefinitionJson,
                CreatedByUserId = "demo.user",
                CreatedAt = _clock.UtcNow,
                Comment = request.Comment ?? $"Rollback to revision {source.RevisionNumber}",
                IsPublished = true,
            };
            _revisions.Add(revision);
            _definitionByReportId[reportId] = source.DefinitionJson;

            var reportIndex = _reports.FindIndex(report => report.TenantId == tenantId && report.ReportId == reportId);
            if (reportIndex >= 0)
            {
                _reports[reportIndex] = _reports[reportIndex] with { LatestRevisionId = revision.RevisionId, UpdatedAt = _clock.UtcNow };
            }

            return Task.FromResult(revision);
        }
    }

    // ---- Parameters / render --------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReportParameterMetadataDto>> GetParametersAsync(string reportId, string tenantId, CancellationToken cancellationToken = default)
    {
        var source = CreateSourceFor(reportId, tenantId);
        if (source is not null)
        {
            // The real metadata path resolves available values (e.g. the Region list) — reuse it so the
            // demo parameter form matches what the API would return for the same definition.
            var metadata = await source.GetMetadataAsync(
                new ReportViewerMetadataRequest { TenantId = tenantId },
                cancellationToken).ConfigureAwait(false);
            return [.. metadata.Parameters.Select(ToParameterMetadata)];
        }

        var definition = GetDefinition(reportId, tenantId);
        return definition is null
            ? []
            : [.. definition.Parameters.Select(parameter => ToParameterMetadata(parameter, []))];
    }

    /// <inheritdoc />
    public async Task<RenderReportResultDto> RenderAsync(RenderReportRequestDto request, CancellationToken cancellationToken = default)
    {
        var started = _clock.UtcNow;
        var source = CreateSourceFor(request.ReportId, request.TenantId)
            ?? throw new KeyNotFoundException($"Unknown report '{request.ReportId}'.");

        var renderRequest = new ReportViewerRenderRequest
        {
            TenantId = request.TenantId,
            CultureName = request.CultureName,
            Parameters = request.Parameters.ToDictionary(
                parameter => parameter.Name,
                parameter => parameter.Values.Count > 1
                    ? ReportParameterValue.Multiple(parameter.Values)
                    : ReportParameterValue.Scalar(parameter.Values.FirstOrDefault()),
                StringComparer.Ordinal),
        };

        RenderReportResultDto result;
        switch (request.Format)
        {
            case ReportRenderFormat.Pdf:
                result = ExportResult(request, await source.ExportPdfAsync(renderRequest, cancellationToken).ConfigureAwait(false));
                break;
            case ReportRenderFormat.Csv:
                result = ExportResult(request, await source.ExportCsvAsync(renderRequest, cancellationToken).ConfigureAwait(false));
                break;
            case ReportRenderFormat.Xlsx:
                result = ExportResult(request, await source.ExportXlsxAsync(renderRequest, cancellationToken).ConfigureAwait(false));
                break;
            default:
                var rendered = await source.RenderAsync(renderRequest, cancellationToken).ConfigureAwait(false);
                var snapshotJson = ReportViewerJson.SerializeSnapshot(rendered.Snapshot);
                result = new RenderReportResultDto
                {
                    TenantId = request.TenantId,
                    ReportId = request.ReportId,
                    Format = request.Format,
                    ContentType = "application/json",
                    FileName = $"{request.ReportId}.snapshot.json",
                    Bytes = System.Text.Encoding.UTF8.GetBytes(snapshotJson),
                    SnapshotJson = snapshotJson,
                    PageCount = rendered.Snapshot.Pages.Count,
                };
                break;
        }

        lock (_gate)
        {
            _renderRuns.Insert(0, new RenderRunDto
            {
                TenantId = request.TenantId,
                ActorId = "demo.user",
                ReportId = request.ReportId,
                Format = request.Format.ToString(),
                Outcome = "Succeeded",
                PageCount = result.PageCount,
                ByteSize = result.Bytes.LongLength,
                DurationMs = (int)(_clock.UtcNow - started).TotalMilliseconds,
                CreatedAt = _clock.UtcNow,
                ParametersJson = "{}",
            });
        }

        return result;
    }

    /// <inheritdoc />
    public Task<RenderJobDto> QueueRenderAsync(RenderReportRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var job = new RenderJobDto
            {
                TenantId = request.TenantId,
                JobId = $"job-{Guid.NewGuid():N}",
                ReportId = request.ReportId,
                Format = request.Format,
                Status = RenderJobStatus.Queued,
                QueuedAt = _clock.UtcNow,
            };
            _renderJobs[job.JobId] = job;
            return Task.FromResult(job);
        }
    }

    /// <inheritdoc />
    public Task<RenderJobDto> GetRenderJobAsync(string jobId, string tenantId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_renderJobs.TryGetValue(jobId, out var job))
            {
                // The demo host has no background renderer — a queued job completes on first poll.
                job = job with { Status = RenderJobStatus.Completed, StartedAt = job.QueuedAt, CompletedAt = _clock.UtcNow };
                _renderJobs[jobId] = job;
                return Task.FromResult(job);
            }
        }

        throw new KeyNotFoundException($"Unknown render job '{jobId}'.");
    }

    // ---- Data sources ---------------------------------------------------------------------------

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportDataSourceDto>> GetDataSourcesAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ReportDataSourceDto>>(
                [.. _dataSources.Where(source => source.TenantId == tenantId).OrderBy(source => source.Name, StringComparer.Ordinal)]);
        }
    }

    /// <inheritdoc />
    public Task<ReportDataSourceDto> UpsertDataSourceAsync(UpsertReportDataSourceRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var index = _dataSources.FindIndex(source => source.TenantId == request.TenantId && source.Name == request.Name);
            var source = new ReportDataSourceDto
            {
                TenantId = request.TenantId,
                DataSourceId = index >= 0 ? _dataSources[index].DataSourceId : $"ds-{Slug(request.Name)}-{++_idCounter}",
                Name = request.Name.Trim(),
                Kind = request.Kind.Trim(),
                Connection = request.Connection,
            };
            if (index >= 0)
            {
                _dataSources[index] = source;
            }
            else
            {
                _dataSources.Add(source);
            }

            return Task.FromResult(source);
        }
    }

    /// <inheritdoc />
    public Task DeleteDataSourceAsync(string dataSourceId, string tenantId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _dataSources.RemoveAll(source => source.TenantId == tenantId && source.DataSourceId == dataSourceId);
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<ReportDataSourceConnectionTestResultDto> TestDataSourceConnectionAsync(string dataSourceId, string tenantId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var source = _dataSources.FirstOrDefault(item => item.TenantId == tenantId && item.DataSourceId == dataSourceId)
                ?? throw new KeyNotFoundException($"Unknown data source '{dataSourceId}'.");
            var hasConnection = !string.IsNullOrWhiteSpace(source.Connection);
            return Task.FromResult(new ReportDataSourceConnectionTestResultDto
            {
                Success = hasConnection,
                Message = hasConnection
                    ? $"Connected at {DateTimeOffset.Now:HH:mm:ss}"
                    : "Connection is empty.",
            });
        }
    }

    /// <inheritdoc />
    public Task<ReportDataSourceSchemaDto> GetDataSourceSchemaAsync(string dataSourceId, string tenantId, CancellationToken cancellationToken = default)
        => Task.FromResult(new ReportDataSourceSchemaDto());

    /// <inheritdoc />
    public Task<ReportDataSourcePreviewDto> PreviewDataSourceAsync(string dataSourceId, string tenantId, int top = 5, CancellationToken cancellationToken = default)
        => Task.FromResult(new ReportDataSourcePreviewDto());

    // ---- Schedules (delegate to the shared store so run-now/delivery sees the same rows) ----------

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportScheduleDto>> GetSchedulesAsync(string tenantId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ReportScheduleDto>>(
            [.. _schedules.ListSchedules(tenantId).Select(ToScheduleDto)]);

    /// <inheritdoc />
    public Task<ReportScheduleDto?> GetScheduleAsync(string tenantId, string scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = _schedules.GetSchedule(tenantId, scheduleId);
        return Task.FromResult(schedule is null ? null : ToScheduleDto(schedule));
    }

    /// <inheritdoc />
    public Task<ReportScheduleDto> UpsertScheduleAsync(UpsertReportScheduleRequestDto request, CancellationToken cancellationToken = default)
    {
        var scheduleId = string.IsNullOrWhiteSpace(request.ScheduleId) ? Slug(request.Name) : request.ScheduleId;
        var schedule = _schedules.UpsertSchedule(new ReportScheduleDefinition
        {
            Id = scheduleId,
            TenantId = request.TenantId,
            OwnerUserId = request.OwnerUserId,
            Name = request.Name,
            ReportId = request.ReportId,
            CronExpression = request.CronExpression,
            Format = Enum.TryParse<ReportScheduleOutputFormat>(request.Format.ToString(), ignoreCase: true, out var format)
                ? format
                : ReportScheduleOutputFormat.Pdf,
            CultureName = request.CultureName,
            Parameters = request.Parameters.ToDictionary(
                pair => pair.Key,
                pair => ReportParameterValue.Scalar(pair.Value),
                StringComparer.Ordinal),
            Recipients = string.IsNullOrWhiteSpace(request.DeliveryTarget)
                ? []
                : [new ReportScheduleRecipient(request.DeliveryTarget.Trim())],
            IsEnabled = request.IsEnabled,
        });
        return Task.FromResult(ToScheduleDto(schedule));
    }

    /// <inheritdoc />
    public Task SetScheduleEnabledAsync(string scheduleId, SetReportScheduleEnabledRequestDto request, CancellationToken cancellationToken = default)
    {
        _schedules.ToggleSchedule(request.TenantId, scheduleId, request.IsEnabled);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteScheduleAsync(string scheduleId, string tenantId, CancellationToken cancellationToken = default)
    {
        _schedules.DeleteSchedule(tenantId, scheduleId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportScheduleRunDto>> GetScheduleRunsAsync(string tenantId, string scheduleId, int max = 20, CancellationToken cancellationToken = default)
    {
        // Schedule runs are derived from the smtp4dev outbox: the demo worker records each delivered
        // email there, so run history and the outbox panel stay consistent by construction.
        var runs = _outbox.Messages
            .Where(message => message.TenantId == tenantId && message.ScheduleId == scheduleId)
            .Select(message => new ReportScheduleRunDto
            {
                RunId = message.JobId,
                TenantId = message.TenantId,
                ScheduleId = message.ScheduleId,
                OccurrenceUtc = message.SentAtUtc,
                StartedUtc = message.SentAtUtc,
                CompletedUtc = message.SentAtUtc,
                Status = DtoScheduleRunStatus.Delivered,
                Attempt = 1,
                DeliveryKind = ReportScheduleDeliveryKind.Email,
                DeliveryTarget = string.Join(", ", message.Message.To),
                ArtifactFileName = message.Attachments.FirstOrDefault()?.FileName,
                ArtifactContentType = message.Attachments.FirstOrDefault()?.ContentType,
                ArtifactByteCount = message.Attachments.FirstOrDefault()?.Bytes.Length ?? 0,
            })
            .OrderByDescending(run => run.StartedUtc)
            .Take(max);
        return Task.FromResult<IReadOnlyList<ReportScheduleRunDto>>([.. runs]);
    }

    // ---- API keys --------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task<CreateReportApiKeyResultDto> CreateApiKeyAsync(CreateReportApiKeyRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var keyId = $"rk_{++_idCounter}";
            var key = new ReportApiKeyDto
            {
                KeyId = keyId,
                TenantId = request.TenantId,
                ApplicationId = request.ApplicationId,
                Permissions = request.Permissions,
                CreatedAt = _clock.UtcNow,
                ExpiresAt = request.ExpiresAt,
                IsActive = true,
            };
            _apiKeys.Add(key);
            RecordAudit(request.TenantId, keyId, "create-api-key");
            return Task.FromResult(new CreateReportApiKeyResultDto
            {
                KeyId = keyId,
                PlainTextKey = $"tmr_{Guid.NewGuid():N}",
                Key = key,
            });
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportApiKeyDto>> GetApiKeysAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ReportApiKeyDto>>(
                [.. _apiKeys.Where(key => key.TenantId == tenantId).OrderBy(key => key.CreatedAt)]);
        }
    }

    /// <inheritdoc />
    public Task<CreateReportApiKeyResultDto> RotateApiKeyAsync(string keyId, RotateReportApiKeyRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var index = _apiKeys.FindIndex(key => key.KeyId == keyId && key.TenantId == request.TenantId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Unknown key '{keyId}'.");
            }

            var previous = _apiKeys[index];
            _apiKeys[index] = previous with { RevokedAt = _clock.UtcNow, RevokedByUserId = "demo.user", IsActive = false };
            var newKeyId = $"rk_{++_idCounter}";
            var replacement = new ReportApiKeyDto
            {
                KeyId = newKeyId,
                TenantId = previous.TenantId,
                ApplicationId = previous.ApplicationId,
                Permissions = previous.Permissions,
                CreatedAt = _clock.UtcNow,
                ExpiresAt = request.ExpiresAt,
                IsActive = true,
            };
            _apiKeys.Add(replacement);
            RecordAudit(request.TenantId, newKeyId, "rotate-api-key");
            return Task.FromResult(new CreateReportApiKeyResultDto
            {
                KeyId = newKeyId,
                PlainTextKey = $"tmr_{Guid.NewGuid():N}",
                Key = replacement,
            });
        }
    }

    /// <inheritdoc />
    public Task RevokeApiKeyAsync(string keyId, RevokeReportApiKeyRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var index = _apiKeys.FindIndex(key => key.KeyId == keyId && key.TenantId == request.TenantId);
            if (index >= 0)
            {
                _apiKeys[index] = _apiKeys[index] with { RevokedAt = _clock.UtcNow, RevokedByUserId = "demo.user", IsActive = false };
                RecordAudit(request.TenantId, keyId, "revoke-api-key");
            }

            return Task.CompletedTask;
        }
    }

    // ---- Audit -----------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportAuditEventDto>> QueryAuditAsync(
        string tenantId,
        ReportAuditActionDto? action = null,
        ReportAuditOutcomeDto? outcome = null,
        string? actorId = null,
        string? resourceId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var events = _audit.Where(item => item.TenantId == tenantId);
            if (action is { } a)
            {
                events = events.Where(item => item.Action == a);
            }

            if (outcome is { } o)
            {
                events = events.Where(item => item.Outcome == o);
            }

            if (!string.IsNullOrWhiteSpace(actorId))
            {
                events = events.Where(item => item.ActorId == actorId);
            }

            if (!string.IsNullOrWhiteSpace(resourceId))
            {
                events = events.Where(item => item.ResourceId == resourceId);
            }

            var ordered = events.OrderByDescending(item => item.Timestamp).AsEnumerable();
            if (take is { } limit)
            {
                ordered = ordered.Take(limit);
            }

            return Task.FromResult<IReadOnlyList<ReportAuditEventDto>>([.. ordered]);
        }
    }

    // ---- Permissions -----------------------------------------------------------------------------

    /// <inheritdoc />
    public Task<ReportFolderAclEntryDto> GrantPermissionAsync(GrantReportPermissionRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var entry = new ReportFolderAclEntryDto
            {
                TenantId = request.TenantId,
                FolderId = request.FolderId,
                SubjectKind = request.SubjectKind,
                SubjectId = request.SubjectId,
                Effect = request.Effect,
                Permissions = request.Permissions,
            };
            _acls.RemoveAll(item => item.TenantId == request.TenantId && item.FolderId == request.FolderId
                && item.SubjectKind == request.SubjectKind && item.SubjectId == request.SubjectId);
            _acls.Add(entry);
            return Task.FromResult(entry);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportFolderAclEntryDto>> GetFolderPermissionsAsync(string tenantId, string folderId, string? subjectId = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var entries = _acls.Where(item => item.TenantId == tenantId && item.FolderId == folderId);
            if (!string.IsNullOrWhiteSpace(subjectId))
            {
                entries = entries.Where(item => item.SubjectId == subjectId);
            }

            return Task.FromResult<IReadOnlyList<ReportFolderAclEntryDto>>([.. entries.OrderBy(item => item.SubjectId, StringComparer.Ordinal)]);
        }
    }

    /// <inheritdoc />
    public Task RevokePermissionAsync(RevokeReportPermissionRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _acls.RemoveAll(item => item.TenantId == request.TenantId && item.FolderId == request.FolderId
                && item.SubjectKind == request.SubjectKind && item.SubjectId == request.SubjectId);
            return Task.CompletedTask;
        }
    }

    // ---- Resolve ---------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task<ReportResolveResultDto> ResolveReportAsync(string tenantId, string? reportId = null, string? path = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // Same contract as the API's /resolve: by id it matches ReportId; by path it is
            // folder-qualified — the last segment matches the report's id-or-name inside the folder
            // whose Path equals "/" + the leading segments.
            ReportSummaryDto? report;
            if (!string.IsNullOrWhiteSpace(reportId))
            {
                report = _reports.FirstOrDefault(item => item.TenantId == tenantId && item.ReportId == reportId);
            }
            else if (!string.IsNullOrWhiteSpace(path))
            {
                report = ResolveByPath(tenantId, path);
            }
            else
            {
                throw new KeyNotFoundException("Either reportId or path must be supplied.");
            }

            if (report is null)
            {
                throw new KeyNotFoundException($"No report resolved for reportId='{reportId}' path='{path}'.");
            }

            var latestNumber = _revisions
                .Where(revision => revision.TenantId == tenantId && revision.ReportId == report.ReportId)
                .Select(revision => revision.RevisionNumber)
                .DefaultIfEmpty(0)
                .Max();
            return Task.FromResult(new ReportResolveResultDto
            {
                TenantId = tenantId,
                ReportId = report.ReportId,
                FolderId = report.FolderId,
                Name = report.Name,
                Description = report.Description,
                LatestRevisionId = report.LatestRevisionId,
                PublishedRevisionId = report.LatestRevisionId,
                RevisionNumber = latestNumber,
                DefinitionJson = DefinitionJsonOf(report),
                RenderPath = "api/render",
            });
        }
    }

    // ---- Favorites / render runs -----------------------------------------------------------------

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportFavoriteDto>> ListFavoritesAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ReportFavoriteDto>>(
                [.. _favorites.Where(favorite => favorite.TenantId == tenantId).OrderByDescending(favorite => favorite.CreatedAt)]);
        }
    }

    /// <inheritdoc />
    public Task<ReportFavoriteDto> AddFavoriteAsync(AddReportFavoriteRequestDto request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var report = _reports.FirstOrDefault(item => item.TenantId == request.TenantId && item.ReportId == request.ReportId);
            var favorite = new ReportFavoriteDto
            {
                TenantId = request.TenantId,
                ReportId = request.ReportId,
                ReportName = report?.Name,
                FolderId = report?.FolderId,
                CreatedAt = _clock.UtcNow,
            };
            _favorites.RemoveAll(item => item.TenantId == request.TenantId && item.ReportId == request.ReportId);
            _favorites.Add(favorite);
            return Task.FromResult(favorite);
        }
    }

    /// <inheritdoc />
    public Task RemoveFavoriteAsync(string tenantId, string reportId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _favorites.RemoveAll(item => item.TenantId == tenantId && item.ReportId == reportId);
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RenderRunDto>> ListRenderRunsAsync(string tenantId, string? reportId = null, int? max = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var runs = _renderRuns.Where(run => run.TenantId == tenantId);
            if (!string.IsNullOrWhiteSpace(reportId))
            {
                runs = runs.Where(run => run.ReportId == reportId);
            }

            return Task.FromResult<IReadOnlyList<RenderRunDto>>([.. runs.Take(max ?? int.MaxValue)]);
        }
    }

    // ---- Internals -------------------------------------------------------------------------------

    private ReportSummaryDto? ResolveByPath(string tenantId, string path)
    {
        var trimmed = path.Trim().Trim('/');
        if (trimmed.Length == 0)
        {
            return null;
        }

        // Tolerate a leading "reports/" segment (the legacy explorer path shape).
        if (trimmed.StartsWith("reports/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["reports/".Length..];
        }

        var separator = trimmed.LastIndexOf('/');
        var reportKey = separator < 0 ? trimmed : trimmed[(separator + 1)..];
        var folderPath = separator < 0 ? "/" : "/" + trimmed[..separator];
        var folder = _folders.FirstOrDefault(candidate =>
            candidate.TenantId == tenantId
            && string.Equals(candidate.Path, folderPath, StringComparison.OrdinalIgnoreCase));

        ReportSummaryDto? summary = null;
        if (folder is not null)
        {
            summary = _reports.FirstOrDefault(candidate =>
                candidate.TenantId == tenantId
                && candidate.FolderId == folder.FolderId
                && (string.Equals(candidate.ReportId, reportKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.Name, reportKey, StringComparison.OrdinalIgnoreCase)));
        }

        // Root-folder fallback: a single-segment link resolves tenant-wide by id-or-name.
        return summary ?? (separator < 0
            ? _reports.FirstOrDefault(candidate =>
                candidate.TenantId == tenantId
                && (string.Equals(candidate.ReportId, reportKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.Name, reportKey, StringComparison.OrdinalIgnoreCase)))
            : null);
    }

    private ReportDetailDto DetailOf(ReportSummaryDto report)
        => new()
        {
            TenantId = report.TenantId,
            ReportId = report.ReportId,
            FolderId = report.FolderId,
            Name = report.Name,
            Description = report.Description,
            LatestRevisionId = report.LatestRevisionId,
            DefinitionJson = DefinitionJsonOf(report),
        };

    private string DefinitionJsonOf(ReportSummaryDto report)
    {
        if (_definitionByReportId.TryGetValue(report.ReportId, out var json))
        {
            return json;
        }

        if (_sourceFactory.IsKnownDemoReport(report.ReportId))
        {
            return _definitionByReportId[report.ReportId] =
                ReportDefinitionJsonSerializer.Serialize(_sourceFactory.CreateReportDefinition(report.ReportId));
        }

        return "{}";
    }

    private ReportDefinition? GetDefinition(string reportId, string tenantId)
    {
        var json = _definitionByReportId.TryGetValue(reportId, out var stored)
            ? stored
            : _sourceFactory.IsKnownDemoReport(reportId)
                ? DefinitionJsonOf(new ReportSummaryDto { TenantId = tenantId, ReportId = reportId })
                : null;
        return DemoReportSourceFactory.TryParseDefinition(json);
    }

    private IReportSource? CreateSourceFor(string reportId, string tenantId)
    {
        if (_sourceFactory.IsKnownDemoReport(reportId))
        {
            return _sourceFactory.CreateReportSource(reportId);
        }

        var definition = GetDefinition(reportId, tenantId);
        return definition is null ? null : _sourceFactory.CreateReportSourceFromDefinition(definition);
    }

    private static RenderReportResultDto ExportResult(RenderReportRequestDto request, ReportViewerExportResult export)
        => new()
        {
            TenantId = request.TenantId,
            ReportId = request.ReportId,
            Format = request.Format,
            ContentType = export.ContentType,
            FileName = export.FileName,
            Bytes = export.Bytes,
        };

    private ReportScheduleDto ToScheduleDto(ReportScheduleDefinition schedule)
        => new()
        {
            TenantId = schedule.TenantId,
            ScheduleId = schedule.Id,
            OwnerUserId = schedule.OwnerUserId,
            Name = schedule.Name,
            ReportId = schedule.ReportId,
            CronExpression = schedule.CronExpression,
            Format = Enum.TryParse<ReportScheduleFormat>(schedule.Format.ToString(), ignoreCase: true, out var format)
                ? format
                : ReportScheduleFormat.Pdf,
            CultureName = schedule.CultureName,
            Parameters = schedule.Parameters.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ScalarValue?.ToString() ?? string.Empty,
                StringComparer.Ordinal),
            DeliveryKind = ReportScheduleDeliveryKind.Email,
            DeliveryTarget = string.Join(", ", schedule.Recipients.Select(recipient => recipient.Email)),
            IsEnabled = schedule.IsEnabled,
            NextRunUtc = schedule.NextRunUtc,
            LastRunUtc = schedule.LastRunUtc,
            LastDeliveredUtc = schedule.LastDeliveredUtc,
            RetryAfterUtc = schedule.RetryAfterUtc,
            FailureCount = schedule.FailureCount,
            LastStatus = Enum.TryParse<DtoScheduleRunStatus>(schedule.LastStatus.ToString(), ignoreCase: true, out var status)
                ? status
                : DtoScheduleRunStatus.NeverRun,
            LastStatusMessage = schedule.LastStatusMessage,
        };

    private static ReportParameterMetadataDto ToParameterMetadata(ReportViewerParameterMetadata metadata)
        => ToParameterMetadata(
            metadata.Definition,
            metadata.Options.Select(option => new ReportParameterOptionDto { Value = option.Value, Label = option.Label }));

    private static ReportParameterMetadataDto ToParameterMetadata(
        ReportParameterDefinition parameter,
        IEnumerable<ReportParameterOptionDto> options)
        => new()
        {
            Name = parameter.Name,
            Label = parameter.Label ?? parameter.Name,
            Kind = parameter.DataType switch
            {
                ReportParameterType.Number => ReportParameterMetadataKind.Number,
                ReportParameterType.Date => ReportParameterMetadataKind.Date,
                ReportParameterType.Boolean => ReportParameterMetadataKind.Boolean,
                ReportParameterType.List => parameter.AllowMultipleValues
                    ? ReportParameterMetadataKind.MultiSelect
                    : ReportParameterMetadataKind.Select,
                _ => ReportParameterMetadataKind.String,
            },
            IsRequired = parameter.Required,
            AllowMultiple = parameter.AllowMultipleValues,
            DefaultValues = EvaluateDefault(parameter.DefaultExpression),
            Options = [.. options],
        };

    // Demo defaults are constant literals ("=\"EU\"", "=0", "=true") — evaluate the literal form.
    private static List<string> EvaluateDefault(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return [];
        }

        var text = expression.Trim();
        if (text.StartsWith("=\"", StringComparison.Ordinal) && text.EndsWith('"'))
        {
            return [text[2..^1]];
        }

        return text.StartsWith("=", StringComparison.Ordinal) ? [text[1..]] : [text];
    }

    private void RecordAudit(string tenantId, string resourceId, string operation)
        => _audit.Add(new ReportAuditEventDto
        {
            TenantId = tenantId,
            ActorId = "demo.user",
            Action = ReportAuditActionDto.ChangeAcl,
            ResourceKind = ReportResourceKindDto.Acl,
            ResourceId = resourceId,
            Outcome = ReportAuditOutcomeDto.Allowed,
            Timestamp = _clock.UtcNow,
            Details = new Dictionary<string, string>(StringComparer.Ordinal) { ["operation"] = operation },
        });

    private static ReportFolderDto Folder(string tenantId, string folderId, string? parentFolderId, string name, string path)
        => new()
        {
            TenantId = tenantId,
            FolderId = folderId,
            ParentFolderId = parentFolderId,
            Name = name,
            Path = path,
        };

    private static ReportSummaryDto Summary(
        string tenantId,
        string reportId,
        string folderId,
        string name,
        string description,
        int revision,
        DateTimeOffset modified)
        => new()
        {
            TenantId = tenantId,
            ReportId = reportId,
            FolderId = folderId,
            Name = name,
            Description = description,
            LatestRevisionId = $"{reportId}-r{revision}",
            CreatedAt = modified.AddDays(-7),
            UpdatedAt = modified,
        };

    private static ReportRevisionDto Revision(
        string revisionId,
        string tenantId,
        string reportId,
        int number,
        string author,
        DateTimeOffset createdAt,
        string comment,
        bool isCurrent = false)
        => new()
        {
            TenantId = tenantId,
            RevisionId = revisionId,
            ReportId = reportId,
            RevisionNumber = number,
            CreatedByUserId = author,
            CreatedAt = createdAt,
            Comment = comment,
            IsPublished = isCurrent,
        };

    private static string Slug(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim().ToLowerInvariant();
        var chars = text.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}

#pragma warning restore MA0048
