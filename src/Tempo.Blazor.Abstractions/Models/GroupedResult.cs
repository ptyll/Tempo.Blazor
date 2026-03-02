namespace Tempo.Blazor.Models;

/// <summary>
/// Represents a group of items with aggregations, returned by server-side grouping.
/// </summary>
public sealed class DataGroup<TItem>
{
    /// <summary>Field name this group is based on.</summary>
    public string FieldName { get; init; } = string.Empty;

    /// <summary>The group key value.</summary>
    public object? Key { get; init; }

    /// <summary>Display text for the group header.</summary>
    public string DisplayValue { get; init; } = string.Empty;

    /// <summary>Number of items in this group.</summary>
    public int Count { get; init; }

    /// <summary>Items in this group (may be empty if collapsed / lazy-loaded).</summary>
    public IReadOnlyList<TItem> Items { get; init; } = [];

    /// <summary>Nested sub-groups (for multi-level grouping).</summary>
    public IReadOnlyList<DataGroup<TItem>> SubGroups { get; init; } = [];

    /// <summary>Aggregation values keyed by column key.</summary>
    public IReadOnlyDictionary<string, AggregateValue> Aggregations { get; init; }
        = new Dictionary<string, AggregateValue>();
}

/// <summary>Aggregation result for a single column in a group.</summary>
public sealed record AggregateValue(
    decimal? Sum,
    decimal? Average,
    decimal? Min,
    decimal? Max,
    int Count
);

/// <summary>Type of aggregation to compute.</summary>
public enum AggregateType
{
    Count,
    Sum,
    Average,
    Min,
    Max
}
