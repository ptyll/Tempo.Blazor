using Tempo.Blazor.Models;

namespace Tempo.Blazor.Helpers;

internal static class FilterOperatorParser
{
    public static FilterOperator Parse(string? op) => op?.ToLowerInvariant() switch
    {
        "contains" => FilterOperator.Contains,
        "notcontains" => FilterOperator.NotContains,
        "equals" => FilterOperator.Equals,
        "notequals" => FilterOperator.NotEquals,
        "greaterthan" => FilterOperator.GreaterThan,
        "lessthan" => FilterOperator.LessThan,
        "greaterorequal" => FilterOperator.GreaterOrEqual,
        "lessorequal" => FilterOperator.LessOrEqual,
        "between" => FilterOperator.Between,
        "isempty" => FilterOperator.IsEmpty,
        "isnotempty" => FilterOperator.IsNotEmpty,
        "in" => FilterOperator.In,
        "notin" => FilterOperator.NotIn,
        _ => FilterOperator.Contains
    };
}
