using System.Text.Json;
using System.Text.Json.Nodes;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Mcp.Notion;

internal sealed class NotionAtomicAuthoringEngine(
    INotionAggregateProvider provider,
    INotionAtomicOperationCompiler compiler,
    InMemoryNotionIdempotencyReceiptStore? receipts,
    TimeSpan? receiptRetention = null)
{
    private const string OperationScope = "notion_apply_block_operations";
    private readonly TimeSpan _receiptRetention = receiptRetention ?? TimeSpan.FromHours(24);

    public async Task<NotionAtomicAuthoringResult> ExecuteAsync(
        NotionAtomicAuthoringRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Failure(
                string.Empty,
                Issue(
                    "idempotency_key_required",
                    "A non-empty idempotencyKey is required.",
                    "$.idempotencyKey"));
        }
        JsonArray? operations;
        try
        {
            operations = JsonNode.Parse(request.OperationsJson) as JsonArray;
        }
        catch (JsonException)
        {
            operations = null;
        }
        catch (ArgumentException)
        {
            operations = null;
        }

        if (operations is null)
        {
            return Failure(
                string.Empty,
                Issue("operations_invalid", "operationsJson must contain a JSON array.", "$.operations"));
        }

        var targets = request.Targets;
        if (targets.Count == 0)
        {
            var discovery = compiler.DiscoverTargets(operations);
            var discoveryErrors = discovery.Issues
                .Where(issue => issue.Severity == NotionIssueSeverity.Error)
                .ToList();
            if (discoveryErrors.Count > 0)
            {
                return Failure(string.Empty, discoveryErrors.ToArray());
            }
            targets = discovery.Targets;
        }
        if (targets.Count == 0)
        {
            return Failure(
                string.Empty,
                Issue("targets_required", "At least one page or block target is required.", "$.targets"));
        }

        var requestHash = NotionCanonicalJson.ComputeRequestHash(request, operations, targets);
        if (provider is INotionIdempotentAggregateProvider idempotentProvider)
        {
            return await ExecuteDurablyAsync(
                idempotentProvider,
                request,
                targets,
                operations,
                requestHash,
                cancellationToken);
        }

        return await ExecuteInMemoryAsync(
            request,
            targets,
            operations,
            requestHash,
            cancellationToken);
    }

    private async Task<NotionAtomicAuthoringResult> ExecuteDurablyAsync(
        INotionIdempotentAggregateProvider idempotentProvider,
        NotionAtomicAuthoringRequest request,
        IReadOnlyList<NotionAggregateTarget> targets,
        JsonArray operations,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var execution = await idempotentProvider.ExecuteIdempotentAsync(
            new NotionIdempotentExecutionRequest
            {
                OperationScope = OperationScope,
                Key = request.IdempotencyKey,
                RequestHash = requestHash,
                Retention = _receiptRetention
            },
            async (transactionProvider, transactionCancellationToken) =>
            {
                var result = await ExecuteCapturedAsync(
                    transactionProvider,
                    request,
                    targets,
                    operations,
                    requestHash,
                    transactionCancellationToken);
                return JsonSerializer.Serialize(result, NotionAggregateJson.Options);
            },
            cancellationToken);

        if (execution.Status == NotionIdempotentExecutionStatus.Collision)
        {
            return IdempotencyCollision(requestHash);
        }
        if (execution.Status is not (
                NotionIdempotentExecutionStatus.Executed or
                NotionIdempotentExecutionStatus.Replayed) ||
            string.IsNullOrWhiteSpace(execution.ResponseJson))
        {
            return Failure(
                requestHash,
                Issue(
                    "idempotency_provider_invalid",
                    "The idempotent aggregate provider returned no committed response.",
                    "$.idempotencyKey"));
        }

        try
        {
            var result = JsonSerializer.Deserialize<NotionAtomicAuthoringResult>(
                execution.ResponseJson,
                NotionAggregateJson.Options);
            return result is null
                ? InvalidReceipt(requestHash)
                : result with
                {
                    Replayed = execution.Status == NotionIdempotentExecutionStatus.Replayed
                };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return InvalidReceipt(requestHash);
        }
    }

    private async Task<NotionAtomicAuthoringResult> ExecuteInMemoryAsync(
        NotionAtomicAuthoringRequest request,
        IReadOnlyList<NotionAggregateTarget> targets,
        JsonArray operations,
        string requestHash,
        CancellationToken cancellationToken)
    {
        if (receipts is null)
        {
            return Failure(
                requestHash,
                Issue(
                    "idempotency_runtime_missing",
                    "The host must register the in-memory idempotency runtime or implement " +
                    "INotionIdempotentAggregateProvider.",
                    "$.idempotencyKey"));
        }

        var acquire = await receipts.AcquireAsync(
            request.IdempotencyKey,
            requestHash,
            cancellationToken);
        if (acquire.Status == NotionReceiptAcquireStatus.Replay)
        {
            return acquire.Result!;
        }
        if (acquire.Status == NotionReceiptAcquireStatus.Collision)
        {
            return IdempotencyCollision(requestHash);
        }

        var lease = acquire.Lease!;
        try
        {
            var result = await ExecuteCapturedAsync(
                provider,
                request,
                targets,
                operations,
                requestHash,
                cancellationToken);

            await receipts.CompleteAsync(lease, result, _receiptRetention);
            return result;
        }
        catch (OperationCanceledException)
        {
            await receipts.AbandonAsync(lease);
            throw;
        }
        catch
        {
            await receipts.AbandonAsync(lease);
            throw;
        }
    }

    private async Task<NotionAtomicAuthoringResult> ExecuteCapturedAsync(
        INotionAggregateProvider transactionProvider,
        NotionAtomicAuthoringRequest request,
        IReadOnlyList<NotionAggregateTarget> targets,
        JsonArray operations,
        string requestHash,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteOwnedAsync(
                transactionProvider,
                request,
                targets,
                operations,
                requestHash,
                cancellationToken);
        }
        catch (Exception ex) when (
            ex is JsonException or InvalidOperationException or ArgumentException)
        {
            return Failure(
                requestHash,
                Issue("authoring_failed", ex.Message, "$.operations"));
        }
    }

    private async Task<NotionAtomicAuthoringResult> ExecuteOwnedAsync(
        INotionAggregateProvider transactionProvider,
        NotionAtomicAuthoringRequest request,
        IReadOnlyList<NotionAggregateTarget> effectiveTargets,
        JsonArray operations,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var loaded = new Dictionary<Guid, NotionPageSnapshot>();
        var loadedTokens = new Dictionary<Guid, string>();
        var loadWarnings = new List<NotionAggregateIssue>();
        var targets = effectiveTargets
            .Select((target, index) => (Target: target, Index: index))
            .DistinctBy(item => item.Target)
            .ToList();

        foreach (var item in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var load = item.Target.Kind == NotionAggregateTargetKind.Page
                ? await transactionProvider.LoadPageAsync(item.Target.Id, cancellationToken)
                : await transactionProvider.LoadBlockAsync(item.Target.Id, cancellationToken);

            if (!load.Found || load.Snapshot is null)
            {
                return Failure(
                    requestHash,
                    Issue(
                        "target_not_found",
                        $"{item.Target.Kind} target '{item.Target.Id}' was not found.",
                        $"$.targets[{item.Index}]"));
            }
            if (load.Issues.Any(issue => issue.Severity == NotionIssueSeverity.Error))
            {
                return Failure(requestHash, load.Issues.ToArray());
            }
            loadWarnings.AddRange(
                load.Issues.Where(issue => issue.Severity != NotionIssueSeverity.Error));
            var snapshot = load.Snapshot;
            if (item.Target.Kind == NotionAggregateTargetKind.Page &&
                snapshot.Page.Id != item.Target.Id)
            {
                return Failure(
                    requestHash,
                    Issue(
                        "page_resolution_mismatch",
                        $"Provider resolved page '{item.Target.Id}' as '{snapshot.Page.Id}'.",
                        $"$.targets[{item.Index}]"));
            }
            if (item.Target.Kind == NotionAggregateTargetKind.Block &&
                (load.MatchedBlockId != item.Target.Id ||
                 !snapshot.Blocks.Any(block => block.Id == item.Target.Id)))
            {
                return Failure(
                    requestHash,
                    Issue(
                        "block_resolution_mismatch",
                        $"Provider resolved block '{item.Target.Id}' as '{load.MatchedBlockId}'.",
                        $"$.targets[{item.Index}]"));
            }
            if (string.IsNullOrWhiteSpace(snapshot.ConcurrencyToken))
            {
                return Failure(
                    requestHash,
                    Issue(
                        "concurrency_token_missing",
                        $"Provider returned no concurrency token for page '{snapshot.Page.Id}'.",
                        $"$.targets[{item.Index}]"));
            }
            if (string.IsNullOrWhiteSpace(snapshot.Digest))
            {
                return Failure(
                    requestHash,
                    Issue(
                        "content_digest_missing",
                        $"Provider returned no content digest for page '{snapshot.Page.Id}'.",
                        $"$.targets[{item.Index}]"));
            }

            if (loaded.TryGetValue(snapshot.Page.Id, out var previous))
            {
                if (!string.Equals(
                        previous.ConcurrencyToken,
                        snapshot.ConcurrencyToken,
                        StringComparison.Ordinal) ||
                    !string.Equals(previous.Digest, snapshot.Digest, StringComparison.Ordinal))
                {
                    return Conflict(
                        requestHash,
                        [
                            new NotionPageConflict
                            {
                                PageId = snapshot.Page.Id,
                                ExpectedConcurrencyToken = previous.ConcurrencyToken,
                                CurrentConcurrencyToken = snapshot.ConcurrencyToken,
                                CurrentDigest = snapshot.Digest
                            }
                        ],
                        "$.targets");
                }

                continue;
            }

            loaded[snapshot.Page.Id] = NotionAggregateWorkingSet.Clone(snapshot);
            loadedTokens[snapshot.Page.Id] = snapshot.ConcurrencyToken;
        }

        var expectedByPage = new Dictionary<Guid, string>();
        for (var index = 0; index < request.ExpectedPageVersions.Count; index++)
        {
            var expected = request.ExpectedPageVersions[index];
            if (!expectedByPage.TryAdd(expected.PageId, expected.ConcurrencyToken))
            {
                if (!string.Equals(
                        expectedByPage[expected.PageId],
                        expected.ConcurrencyToken,
                        StringComparison.Ordinal))
                {
                    return Failure(
                        requestHash,
                        Issue(
                            "duplicate_expected_token",
                            $"Page '{expected.PageId}' has conflicting expected tokens.",
                            $"$.expectedPageVersions[{index}]"));
                }
                continue;
            }
            if (!loadedTokens.TryGetValue(expected.PageId, out var current))
            {
                return Failure(
                    requestHash,
                    Issue(
                        "expected_page_not_loaded",
                        $"Expected token references page '{expected.PageId}', which was not loaded.",
                        $"$.expectedPageVersions[{index}].pageId"));
            }
            if (!string.Equals(expected.ConcurrencyToken, current, StringComparison.Ordinal))
            {
                return Conflict(
                    requestHash,
                    [
                        new NotionPageConflict
                        {
                            PageId = expected.PageId,
                            ExpectedConcurrencyToken = expected.ConcurrencyToken,
                            CurrentConcurrencyToken = current,
                            CurrentDigest = loaded[expected.PageId].Digest
                        }
                    ],
                    $"$.expectedPageVersions[{index}].concurrencyToken");
            }
        }

        var workingSet = new NotionAggregateWorkingSet(loaded);
        var compilation = await compiler.CompileAsync(
            operations,
            workingSet,
            new NotionOperationCompileContext(requestHash, request.IdempotencyKey),
            cancellationToken);
        var compilationErrors = compilation.Issues
            .Where(issue => issue.Severity == NotionIssueSeverity.Error)
            .ToList();
        if (!compilation.Success || compilationErrors.Count > 0)
        {
            return Failure(
                requestHash,
                compilationErrors.Count > 0
                    ? compilationErrors.ToArray()
                    :
                    [
                        Issue(
                            "compilation_failed",
                            "The operation compiler rejected the request.",
                            "$.operations")
                    ]);
        }

        var warnings = loadWarnings
            .Concat(compilation.Issues
                .Where(issue => issue.Severity != NotionIssueSeverity.Error))
            .ToList();
        var created = new List<NotionEntityChange>();
        var updated = new List<NotionEntityChange>();
        var deleted = new List<NotionEntityChange>();

        foreach (var operation in compilation.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var apply = operation.Apply(workingSet);
            var errors = apply.Issues
                .Where(issue => issue.Severity == NotionIssueSeverity.Error)
                .ToList();
            if (!apply.Success || errors.Count > 0)
            {
                return Failure(
                    requestHash,
                    errors.Count > 0
                        ? errors.ToArray()
                        :
                        [
                            Issue(
                                "operation_failed",
                                $"Canonical operation {operation.OperationIndex} failed.",
                                $"$.operations[{operation.OperationIndex}]")
                        ]);
            }

            created.AddRange(apply.Created);
            updated.AddRange(apply.Updated);
            deleted.AddRange(apply.Deleted);
            warnings.AddRange(apply.Issues.Where(issue => issue.Severity != NotionIssueSeverity.Error));
        }

        NotionAggregateNormalizer.Normalize(workingSet);
        var validationErrors = NotionAggregateValidator.Validate(workingSet.Pages.Values);
        if (validationErrors.Count > 0)
        {
            return Failure(requestHash, validationErrors.ToArray());
        }

        if (workingSet.TouchedPageIds.Count == 0)
        {
            return new NotionAtomicAuthoringResult
            {
                Success = true,
                RequestHash = requestHash,
                Applied = compilation.Operations
                    .Select(operation => operation.OperationIndex)
                    .Distinct()
                    .Count(),
                Created = created,
                Updated = updated,
                Deleted = deleted,
                Warnings = warnings
            };
        }

        var saveItems = workingSet.TouchedPageIds
            .OrderBy(pageId => pageId)
            .Select(pageId =>
            {
                var snapshot = workingSet.Pages[pageId];
                snapshot.Digest = NotionCanonicalJson.ComputeContentDigest(snapshot);
                return new NotionPageSave
                {
                    Snapshot = snapshot,
                    BaseConcurrencyToken = loadedTokens[pageId]
                };
            })
            .ToList();

        cancellationToken.ThrowIfCancellationRequested();
        var save = await transactionProvider.SaveAsync(
            new NotionAggregateSaveRequest { Pages = saveItems },
            cancellationToken);
        if (!save.Success)
        {
            var conflicts = save.Conflicts.OrderBy(conflict => conflict.PageId).ToList();
            if (save.Conflict || conflicts.Count > 0)
            {
                return Conflict(requestHash, conflicts, "$.pages", save.Issues);
            }

            var errors = save.Issues
                .Where(issue => issue.Severity == NotionIssueSeverity.Error)
                .ToList();
            if (errors.Count == 0)
            {
                errors.Add(Issue("save_failed", "The aggregate provider rejected the atomic save.", "$.pages"));
            }
            return Failure(requestHash, errors.ToArray());
        }

        warnings.AddRange(save.Issues.Where(issue => issue.Severity != NotionIssueSeverity.Error));
        return new NotionAtomicAuthoringResult
        {
            Success = true,
            RequestHash = requestHash,
            Applied = compilation.Operations
                .Select(operation => operation.OperationIndex)
                .Distinct()
                .Count(),
            Created = created,
            Updated = updated,
            Deleted = deleted,
            Pages = save.Pages.OrderBy(page => page.PageId).ToList(),
            Warnings = warnings
        };
    }

    private static NotionAtomicAuthoringResult Failure(
        string requestHash,
        params NotionAggregateIssue[] errors)
        => new()
        {
            RequestHash = requestHash,
            Errors = errors
        };

    private static NotionAtomicAuthoringResult IdempotencyCollision(string requestHash)
        => Failure(
            requestHash,
            Issue(
                "idempotency_key_reused",
                "The idempotency key was already used with a different canonical request.",
                "$.idempotencyKey"));

    private static NotionAtomicAuthoringResult InvalidReceipt(string requestHash)
        => Failure(
            requestHash,
            Issue(
                "idempotency_receipt_invalid",
                "The idempotent aggregate provider returned an invalid committed response.",
                "$.idempotencyKey"));

    private static NotionAtomicAuthoringResult Conflict(
        string requestHash,
        IReadOnlyList<NotionPageConflict> conflicts,
        string path,
        IReadOnlyList<NotionAggregateIssue>? providerIssues = null)
    {
        var errors = providerIssues?
            .Where(issue => issue.Severity == NotionIssueSeverity.Error)
            .ToList() ?? [];
        if (errors.Count == 0)
        {
            errors.Add(Issue(
                "concurrency_conflict",
                "One or more pages changed after they were loaded; nothing was saved.",
                path));
        }

        return new NotionAtomicAuthoringResult
        {
            Conflict = true,
            RequestHash = requestHash,
            Conflicts = conflicts.OrderBy(conflict => conflict.PageId).ToList(),
            Errors = errors
        };
    }

    private static NotionAggregateIssue Issue(
        string code,
        string message,
        string path)
        => new()
        {
            Code = code,
            Severity = NotionIssueSeverity.Error,
            Message = message,
            Path = path
        };
}
