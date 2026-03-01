namespace Tempo.Blazor.Models;

/// <summary>
/// Request parameters passed to IDropdownDataProvider&lt;T&gt;.GetItemsAsync().
/// </summary>
public sealed class DropdownSearchRequest
{
    /// <summary>The user's current search/filter text. Empty string means no filter.</summary>
    public string SearchText { get; init; } = string.Empty;

    /// <summary>1-based page number for pagination.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Maximum items to return per page.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Item IDs to exclude from results (e.g., already-selected items in multi-select).</summary>
    public IReadOnlyCollection<string> ExcludedIds { get; init; } = Array.Empty<string>();

    /// <summary>Cancellation token for async operations.</summary>
    public CancellationToken CancellationToken { get; init; } = default;
}
