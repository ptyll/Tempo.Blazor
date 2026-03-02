namespace Tempo.Blazor.Models;

/// <summary>
/// Defines a field that can be used for grouping in TmMultiViewList.
/// </summary>
public sealed class GroupFieldDefinition<TItem>
{
    /// <summary>Unique field name (stored in views).</summary>
    public string FieldName { get; init; } = string.Empty;

    /// <summary>Display label shown in the grouping picker.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Accessor function to get the group key value from an item.</summary>
    public Func<TItem, object?> FieldAccessor { get; init; } = _ => null;

    /// <summary>Optional custom display formatter for group header values.</summary>
    public Func<object?, string>? DisplayFormatter { get; init; }

    /// <summary>Aggregation types to compute for this group. Default: Count only.</summary>
    public IReadOnlyList<AggregateType> Aggregations { get; init; } = [AggregateType.Count];
}
