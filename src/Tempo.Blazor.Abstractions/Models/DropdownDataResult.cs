namespace Tempo.Blazor.Models;

/// <summary>
/// Result returned by IDropdownDataProvider&lt;T&gt;.GetItemsAsync().
/// </summary>
/// <typeparam name="TItem">The item type.</typeparam>
public sealed class DropdownDataResult<TItem>
{
    /// <summary>The items for the current page.</summary>
    public IReadOnlyList<TItem> Items { get; init; } = Array.Empty<TItem>();

    /// <summary>Total count of matching items across all pages.</summary>
    public int TotalCount { get; init; }

    /// <summary>True if there are more items beyond the current page.</summary>
    public bool HasMore => Items.Count < TotalCount;

    private DropdownDataResult() { }

    /// <summary>Creates an empty result (no items found).</summary>
    public static DropdownDataResult<TItem> Empty() => new() { Items = Array.Empty<TItem>(), TotalCount = 0 };

    /// <summary>Creates a result with items and total count.</summary>
    public static DropdownDataResult<TItem> WithItems(IReadOnlyList<TItem> items, int totalCount) =>
        new() { Items = items, TotalCount = totalCount };

    /// <summary>Creates a result where all items fit in one page (no further pagination).</summary>
    public static DropdownDataResult<TItem> WithAllItems(IReadOnlyList<TItem> items) =>
        new() { Items = items, TotalCount = items.Count };
}
