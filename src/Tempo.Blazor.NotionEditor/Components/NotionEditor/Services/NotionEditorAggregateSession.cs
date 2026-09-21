using System.Text.Json;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Services;

/// <summary>
/// Stateful load/clone/validate/save-once boundary used by one interactive Notion editor.
/// A conflicted local candidate remains current until the user reloads or reapplies it.
/// </summary>
public sealed class NotionEditorAggregateSession(
    INotionAggregateProvider provider,
    Func<string?>? currentUserId = null)
{
    private Func<NotionPageSnapshot, NotionPageSnapshot>? _pendingMutation;

    /// <summary>The currently displayed canonical snapshot, including an unsaved conflict candidate.</summary>
    public NotionPageSnapshot? CurrentSnapshot { get; private set; }

    /// <summary>Whether the current local candidate conflicted with a newer provider snapshot.</summary>
    public bool HasPendingConflict => _pendingMutation is not null;

    /// <summary>Loads a complete canonical page aggregate and clears pending conflict state.</summary>
    public async Task<NotionEditorAggregateSaveResult> LoadAsync(
        Guid pageId,
        CancellationToken cancellationToken = default)
    {
        var load = await provider.LoadPageAsync(pageId, cancellationToken);
        if (!load.Found || load.Snapshot is null)
        {
            return Failure("page_not_found", $"Page '{pageId}' was not found.", "$.pageId");
        }

        var issues = load.Issues
            .Concat(NotionAggregateValidator.Validate([load.Snapshot]))
            .ToList();
        if (issues.Any(issue => issue.Severity == NotionIssueSeverity.Error))
        {
            return new NotionEditorAggregateSaveResult { Issues = issues };
        }

        CurrentSnapshot = Clone(load.Snapshot);
        _pendingMutation = null;
        return new NotionEditorAggregateSaveResult
        {
            Success = true,
            Snapshot = CurrentSnapshot,
            Issues = issues
        };
    }

    /// <summary>
    /// Applies one logical editor mutation in memory, validates the complete candidate and invokes
    /// exactly one aggregate save.
    /// </summary>
    public async Task<NotionEditorAggregateSaveResult> ApplyAsync(
        Func<NotionPageSnapshot, NotionPageSnapshot> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (CurrentSnapshot is null)
        {
            return Failure(
                "editor_snapshot_not_loaded",
                "Load a page snapshot before applying editor mutations.",
                "$.snapshot");
        }
        if (_pendingMutation is not null)
        {
            return Failure(
                "editor_conflict_pending",
                "Resolve the pending conflict before applying another editor mutation.",
                "$.conflict");
        }

        var baseline = Clone(CurrentSnapshot);
        var candidate = mutation(Clone(baseline)) ??
            throw new InvalidOperationException("The editor mutation returned null.");
        return await SaveCandidateAsync(
            baseline,
            candidate,
            mutation,
            cancellationToken);
    }

    /// <summary>Discards the conflicted local candidate and reloads the provider snapshot.</summary>
    public Task<NotionEditorAggregateSaveResult> ReloadAsync(
        CancellationToken cancellationToken = default)
        => CurrentSnapshot is null
            ? Task.FromResult(Failure(
                "editor_snapshot_not_loaded",
                "No editor page is loaded.",
                "$.snapshot"))
            : LoadAsync(CurrentSnapshot.Page.Id, cancellationToken);

    /// <summary>
    /// Reloads the latest provider snapshot, reapplies the retained logical mutation and performs
    /// one optimistic save against the fresh token.
    /// </summary>
    public async Task<NotionEditorAggregateSaveResult> ReapplyAsync(
        CancellationToken cancellationToken = default)
    {
        if (CurrentSnapshot is null || _pendingMutation is null)
        {
            return Failure(
                "editor_conflict_not_pending",
                "There is no conflicted editor mutation to reapply.",
                "$.conflict");
        }

        var mutation = _pendingMutation;
        var pageId = CurrentSnapshot.Page.Id;
        var load = await provider.LoadPageAsync(pageId, cancellationToken);
        if (!load.Found || load.Snapshot is null)
        {
            return Failure("page_not_found", $"Page '{pageId}' was not found.", "$.pageId");
        }

        var baseline = Clone(load.Snapshot);
        var issues = load.Issues
            .Concat(NotionAggregateValidator.Validate([baseline]))
            .ToList();
        if (issues.Any(issue => issue.Severity == NotionIssueSeverity.Error))
        {
            return new NotionEditorAggregateSaveResult
            {
                Snapshot = CurrentSnapshot,
                Issues = issues
            };
        }

        var candidate = mutation(Clone(baseline)) ??
            throw new InvalidOperationException("The editor mutation returned null.");
        return await SaveCandidateAsync(
            baseline,
            candidate,
            mutation,
            cancellationToken);
    }

    private async Task<NotionEditorAggregateSaveResult> SaveCandidateAsync(
        NotionPageSnapshot baseline,
        NotionPageSnapshot candidate,
        Func<NotionPageSnapshot, NotionPageSnapshot> mutation,
        CancellationToken cancellationToken)
    {
        var issues = NotionAggregateValidator.Validate([candidate]);
        if (issues.Any(issue => issue.Severity == NotionIssueSeverity.Error))
        {
            return new NotionEditorAggregateSaveResult
            {
                Snapshot = CurrentSnapshot,
                Issues = issues
            };
        }

        // Stamp the actor on the page snapshot so the provider/API can attribute the edit —
        // the demo API uses it to notify page watchers (watch → page-edit notification).
        var actor = currentUserId?.Invoke();
        if (!string.IsNullOrWhiteSpace(actor))
        {
            candidate.Page.LastEditedByUserId = actor.Trim();
            candidate.Page.LastEditedAt = DateTime.UtcNow;
        }

        var save = await provider.SaveAsync(
            new NotionAggregateSaveRequest
            {
                Pages =
                [
                    new NotionPageSave
                    {
                        Snapshot = candidate,
                        BaseConcurrencyToken = baseline.ConcurrencyToken
                    }
                ]
            },
            cancellationToken);
        if (save.Conflict)
        {
            CurrentSnapshot = candidate;
            _pendingMutation = mutation;
            return new NotionEditorAggregateSaveResult
            {
                Conflict = true,
                Snapshot = CurrentSnapshot,
                Issues = save.Issues,
                Conflicts = save.Conflicts
            };
        }
        if (!save.Success)
        {
            return new NotionEditorAggregateSaveResult
            {
                Snapshot = CurrentSnapshot,
                Issues = save.Issues
            };
        }

        var savedPage = save.Pages.SingleOrDefault(page => page.PageId == candidate.Page.Id);
        if (savedPage is null)
        {
            return Failure(
                "saved_page_metadata_missing",
                "The provider did not return saved page metadata.",
                "$.save.pages");
        }

        candidate.ConcurrencyToken = savedPage.ConcurrencyToken;
        candidate.Digest = savedPage.Digest;
        CurrentSnapshot = candidate;
        _pendingMutation = null;
        return new NotionEditorAggregateSaveResult
        {
            Success = true,
            Snapshot = CurrentSnapshot,
            Issues = save.Issues
        };
    }

    private static NotionEditorAggregateSaveResult Failure(
        string code,
        string message,
        string path)
        => new()
        {
            Issues =
            [
                new NotionAggregateIssue
                {
                    Code = code,
                    Severity = NotionIssueSeverity.Error,
                    Message = message,
                    Path = path
                }
            ]
        };

    private static NotionPageSnapshot Clone(NotionPageSnapshot snapshot)
        => JsonSerializer.Deserialize<NotionPageSnapshot>(
            JsonSerializer.Serialize(snapshot, NotionAggregateJson.Options),
            NotionAggregateJson.Options)
           ?? throw new InvalidDataException("Could not clone the Notion page snapshot.");
}

/// <summary>Result of an interactive aggregate load or save.</summary>
public sealed class NotionEditorAggregateSaveResult
{
    /// <summary>Whether the load or save completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Whether an optimistic concurrency conflict preserved a local candidate.</summary>
    public bool Conflict { get; init; }

    /// <summary>Current canonical snapshot after the operation.</summary>
    public NotionPageSnapshot? Snapshot { get; init; }

    /// <summary>Structured validation or provider issues.</summary>
    public IReadOnlyList<NotionAggregateIssue> Issues { get; init; } = [];

    /// <summary>Structured optimistic-concurrency conflicts.</summary>
    public IReadOnlyList<NotionPageConflict> Conflicts { get; init; } = [];
}
