namespace Tempo.Blazor.Models;

/// <summary>Describes a filterable field for use in TmFilterBuilder.</summary>
public sealed class FilterDefinition
{
    /// <summary>Programmatic field name (e.g. "status", "createdAt").</summary>
    public string FieldName { get; init; } = string.Empty;

    /// <summary>Human-readable label shown in the UI.</summary>
    public string FieldLabel { get; init; } = string.Empty;

    /// <summary>Determines which input type and operators are shown.</summary>
    public FilterFieldType FieldType { get; init; }

    /// <summary>Options for Select / MultiSelect fields.</summary>
    public IReadOnlyList<SelectOption<string>>? Options { get; init; }
}

/// <summary>An active (applied) filter produced by TmFilterBuilder.</summary>
public sealed record ActiveFilter(
    string FieldName,
    string FieldLabel,
    FilterOperator Operator,
    object? Value,
    string DisplayValue);

/// <summary>Determines which input/operator set TmFilterBuilder renders for a field.</summary>
public enum FilterFieldType
{
    Text,
    Number,
    Date,
    DateTime,
    Boolean,
    Select,
    MultiSelect,
}

/// <summary>Filter comparison operators used in TmFilterBuilder.</summary>
public enum FilterOperator
{
    Contains,
    NotContains,
    Equals,
    NotEquals,
    GreaterThan,
    LessThan,
    GreaterOrEqual,
    LessOrEqual,
    Between,
    IsEmpty,
    IsNotEmpty,
    In,
    NotIn,
}
