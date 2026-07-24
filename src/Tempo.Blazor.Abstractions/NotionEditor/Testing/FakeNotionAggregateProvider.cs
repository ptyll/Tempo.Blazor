using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.NotionEditor.Testing;

/// <summary>
/// In-memory reference implementation of the atomic Notion aggregate persistence contract.
/// </summary>
/// <remarks>
/// The fake is intended for consumer tests and executable examples. It validates every complete
/// snapshot, checks all optimistic-concurrency tokens before changing state, and commits all page
/// replacements under one lock. It intentionally implements only
/// <see cref="INotionAggregateProvider"/>; durable MCP replay tests should wrap it in a host
/// implementation of <see cref="INotionIdempotentAggregateProvider"/>.
/// </remarks>
public sealed class FakeNotionAggregateProvider : INotionAggregateProvider
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, NotionPageSnapshot> _snapshots = [];
    private NotionAggregateSaveRequest? _lastSaveRequest;
    private int _loadCallCount;
    private int _saveCallCount;
    private long _version;

    /// <summary>Creates an empty provider.</summary>
    public FakeNotionAggregateProvider()
    {
    }

    /// <summary>Creates a provider seeded with complete page snapshots.</summary>
    /// <param name="initialPages">Initial snapshots keyed by their page identifiers.</param>
    public FakeNotionAggregateProvider(IEnumerable<NotionPageSnapshot> initialPages)
    {
        ArgumentNullException.ThrowIfNull(initialPages);
        foreach (var page in initialPages)
        {
            Seed(page);
        }
    }

    /// <summary>Gets the number of aggregate load calls observed by this instance.</summary>
    public int LoadCallCount
    {
        get
        {
            lock (_gate)
            {
                return _loadCallCount;
            }
        }
    }

    /// <summary>Gets the number of atomic save calls observed by this instance.</summary>
    public int SaveCallCount
    {
        get
        {
            lock (_gate)
            {
                return _saveCallCount;
            }
        }
    }

    /// <summary>Gets a defensive copy of the most recent save request.</summary>
    public NotionAggregateSaveRequest? LastSaveRequest
    {
        get
        {
            lock (_gate)
            {
                return _lastSaveRequest is null
                    ? null
                    : Clone(_lastSaveRequest);
            }
        }
    }

    /// <summary>Adds or replaces one seeded page without incrementing save counters.</summary>
    /// <param name="snapshot">Complete snapshot to store.</param>
    public void Seed(NotionPageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            var clone = Clone(snapshot);
            if (string.IsNullOrWhiteSpace(clone.ConcurrencyToken))
            {
                clone.ConcurrencyToken = NextToken(clone.Page.Id);
            }
            if (string.IsNullOrWhiteSpace(clone.Digest))
            {
                clone.Digest = ComputeDigest(clone);
            }
            _snapshots[clone.Page.Id] = clone;
        }
    }

    /// <summary>Returns a defensive copy of a currently stored page snapshot.</summary>
    /// <param name="pageId">Page identifier to inspect.</param>
    /// <returns>The stored snapshot.</returns>
    /// <exception cref="KeyNotFoundException">The page does not exist.</exception>
    public NotionPageSnapshot GetSnapshot(Guid pageId)
    {
        lock (_gate)
        {
            return Clone(_snapshots[pageId]);
        }
    }

    /// <inheritdoc />
    public Task<NotionAggregateLoadResult> LoadPageAsync(
        Guid pageId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _loadCallCount++;
            return Task.FromResult(_snapshots.TryGetValue(pageId, out var snapshot)
                ? new NotionAggregateLoadResult
                {
                    Found = true,
                    Snapshot = Clone(snapshot)
                }
                : new NotionAggregateLoadResult { Found = false });
        }
    }

    /// <inheritdoc />
    public Task<NotionAggregateLoadResult> LoadBlockAsync(
        Guid blockId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _loadCallCount++;
            var owner = _snapshots.Values
                .OrderBy(snapshot => snapshot.Page.Id)
                .FirstOrDefault(snapshot => snapshot.Blocks.Any(block => block.Id == blockId));
            return Task.FromResult(owner is null
                ? new NotionAggregateLoadResult { Found = false }
                : new NotionAggregateLoadResult
                {
                    Found = true,
                    Snapshot = Clone(owner),
                    MatchedBlockId = blockId
                });
        }
    }

    /// <inheritdoc />
    public Task<NotionAggregateSaveResult> SaveAsync(
        NotionAggregateSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var requestCopy = Clone(request);
        var issues = NotionAggregateValidator.Validate(
                requestCopy.Pages.Select(page => page.Snapshot))
            .ToList();
        var duplicatePage = requestCopy.Pages
            .GroupBy(page => page.Snapshot.Page.Id)
            .OrderBy(group => group.Key)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePage is not null)
        {
            issues.Add(new NotionAggregateIssue
            {
                Code = "duplicate_page_replacement",
                Severity = NotionIssueSeverity.Error,
                Message = $"Page '{duplicatePage.Key}' appears more than once in the save request.",
                Path = "$.pages",
                SuggestedFix = "Supply exactly one complete replacement for each page."
            });
        }

        lock (_gate)
        {
            _saveCallCount++;
            _lastSaveRequest = Clone(requestCopy);

            if (issues.Any(issue => issue.Severity == NotionIssueSeverity.Error))
            {
                return Task.FromResult(new NotionAggregateSaveResult { Issues = issues });
            }

            var conflicts = requestCopy.Pages
                .Where(page =>
                    !_snapshots.TryGetValue(page.Snapshot.Page.Id, out var current) ||
                    !string.Equals(
                        current.ConcurrencyToken,
                        page.BaseConcurrencyToken,
                        StringComparison.Ordinal))
                .Select(page =>
                {
                    _snapshots.TryGetValue(page.Snapshot.Page.Id, out var current);
                    return new NotionPageConflict
                    {
                        PageId = page.Snapshot.Page.Id,
                        ExpectedConcurrencyToken = page.BaseConcurrencyToken,
                        CurrentConcurrencyToken = current?.ConcurrencyToken,
                        CurrentDigest = current?.Digest
                    };
                })
                .OrderBy(conflict => conflict.PageId)
                .ToList();
            if (conflicts.Count > 0)
            {
                return Task.FromResult(new NotionAggregateSaveResult
                {
                    Conflict = true,
                    Conflicts = conflicts
                });
            }

            cancellationToken.ThrowIfCancellationRequested();
            var replacements = requestCopy.Pages.Select(page =>
            {
                var snapshot = Clone(page.Snapshot);
                snapshot.ConcurrencyToken = NextToken(snapshot.Page.Id);
                snapshot.Digest = ComputeDigest(snapshot);
                return snapshot;
            }).ToList();

            foreach (var snapshot in replacements)
            {
                _snapshots[snapshot.Page.Id] = snapshot;
            }

            return Task.FromResult(new NotionAggregateSaveResult
            {
                Success = true,
                Pages = replacements.Select(snapshot => new NotionSavedPage
                {
                    PageId = snapshot.Page.Id,
                    ConcurrencyToken = snapshot.ConcurrencyToken,
                    Digest = snapshot.Digest,
                    SchemaVersion = snapshot.SchemaVersion
                }).ToList(),
                Issues = issues
            });
        }
    }

    private string NextToken(Guid pageId)
        => $"fake:{++_version}:{pageId:N}";

    private static string ComputeDigest(NotionPageSnapshot snapshot)
    {
        var clone = Clone(snapshot);
        clone.ConcurrencyToken = string.Empty;
        clone.Digest = string.Empty;
        var bytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(clone, NotionAggregateJson.Options));
        return $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
    }

    private static T Clone<T>(T value)
        => JsonSerializer.Deserialize<T>(
               JsonSerializer.Serialize(value, NotionAggregateJson.Options),
               NotionAggregateJson.Options)
           ?? throw new InvalidDataException(
               $"Could not clone {typeof(T).Name} through the canonical Notion JSON contract.");
}
