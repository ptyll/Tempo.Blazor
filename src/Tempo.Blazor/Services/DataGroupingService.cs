using Tempo.Blazor.Models;

namespace Tempo.Blazor.Services;

/// <summary>Defines one level of grouping with accessor and display info.</summary>
public sealed record GroupingLevel<TItem>(
    string FieldName,
    Func<TItem, object?> KeyAccessor,
    Func<object?, string>? DisplayFormatter = null,
    Dictionary<string, Func<TItem, object?>>? AggregateAccessors = null,
    IReadOnlyList<AggregateType>? AggregateTypes = null
);

/// <summary>
/// Static helper for grouping collections and computing aggregations.
/// </summary>
public static class DataGroupingService
{
    /// <summary>
    /// Groups items by specified field accessors in order (multi-level grouping).
    /// </summary>
    public static IReadOnlyList<DataGroup<TItem>> GroupItems<TItem>(
        IEnumerable<TItem> items,
        IReadOnlyList<GroupingLevel<TItem>> levels)
    {
        if (levels.Count == 0)
            return [];

        return GroupItemsRecursive(items, levels, 0);
    }

    /// <summary>
    /// Computes aggregate values for a set of items on a given column.
    /// </summary>
    public static AggregateValue ComputeAggregate<TItem>(
        IEnumerable<TItem> items,
        Func<TItem, object?> accessor,
        IReadOnlyList<AggregateType> types)
    {
        var list = items as IReadOnlyList<TItem> ?? items.ToList();
        var count = list.Count;

        if (count == 0)
        {
            return new AggregateValue(
                Sum: types.Contains(AggregateType.Sum) ? 0m : null,
                Average: null,
                Min: null,
                Max: null,
                Count: 0
            );
        }

        var numerics = ExtractDecimals(list, accessor);

        decimal? sum = null;
        decimal? average = null;
        decimal? min = null;
        decimal? max = null;

        if (numerics is not null && numerics.Count > 0)
        {
            if (types.Contains(AggregateType.Sum))
                sum = numerics.Sum();
            if (types.Contains(AggregateType.Average))
                average = numerics.Sum() / numerics.Count;
            if (types.Contains(AggregateType.Min))
                min = numerics.Min();
            if (types.Contains(AggregateType.Max))
                max = numerics.Max();
        }

        return new AggregateValue(Sum: sum, Average: average, Min: min, Max: max, Count: count);
    }

    private static IReadOnlyList<DataGroup<TItem>> GroupItemsRecursive<TItem>(
        IEnumerable<TItem> items,
        IReadOnlyList<GroupingLevel<TItem>> levels,
        int levelIndex)
    {
        var level = levels[levelIndex];
        var grouped = items.GroupBy(item => level.KeyAccessor(item));
        var isLeaf = levelIndex == levels.Count - 1;

        var result = new List<DataGroup<TItem>>();

        foreach (var g in grouped)
        {
            var groupItems = g.ToList();
            var displayValue = level.DisplayFormatter is not null
                ? level.DisplayFormatter(g.Key)
                : g.Key?.ToString() ?? string.Empty;

            var aggregations = ComputeGroupAggregations(groupItems, level);

            if (isLeaf)
            {
                result.Add(new DataGroup<TItem>
                {
                    FieldName = level.FieldName,
                    Key = g.Key,
                    DisplayValue = displayValue,
                    Count = groupItems.Count,
                    Items = groupItems,
                    SubGroups = [],
                    Aggregations = aggregations
                });
            }
            else
            {
                var subGroups = GroupItemsRecursive(groupItems, levels, levelIndex + 1);
                result.Add(new DataGroup<TItem>
                {
                    FieldName = level.FieldName,
                    Key = g.Key,
                    DisplayValue = displayValue,
                    Count = groupItems.Count,
                    Items = [],
                    SubGroups = subGroups,
                    Aggregations = aggregations
                });
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, AggregateValue> ComputeGroupAggregations<TItem>(
        List<TItem> items,
        GroupingLevel<TItem> level)
    {
        if (level.AggregateAccessors is null || level.AggregateAccessors.Count == 0)
            return new Dictionary<string, AggregateValue>();

        var types = level.AggregateTypes ?? [AggregateType.Count];
        var result = new Dictionary<string, AggregateValue>();

        foreach (var (key, accessor) in level.AggregateAccessors)
        {
            result[key] = ComputeAggregate(items, accessor, types);
        }

        return result;
    }

    private static List<decimal>? ExtractDecimals<TItem>(
        IReadOnlyList<TItem> items,
        Func<TItem, object?> accessor)
    {
        var result = new List<decimal>(items.Count);

        foreach (var item in items)
        {
            var value = accessor(item);
            if (value is null) continue;

            if (TryConvertToDecimal(value, out var d))
            {
                result.Add(d);
            }
            else
            {
                // Non-numeric field - return null to signal no numeric aggregation
                return null;
            }
        }

        return result;
    }

    private static bool TryConvertToDecimal(object value, out decimal result)
    {
        switch (value)
        {
            case decimal d: result = d; return true;
            case int i: result = i; return true;
            case long l: result = l; return true;
            case double dbl: result = (decimal)dbl; return true;
            case float f: result = (decimal)f; return true;
            case short s: result = s; return true;
            case byte b: result = b; return true;
            default:
                result = 0;
                return false;
        }
    }
}
