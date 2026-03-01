namespace Tempo.Blazor.Models;

/// <summary>Definition of a field in a dynamic form.</summary>
public sealed record DynamicFieldDefinition
{
    /// <summary>Unique field name (used as dictionary key).</summary>
    public required string Name { get; init; }

    /// <summary>Type of the field (determines rendered component).</summary>
    public required DynamicFieldType FieldType { get; init; }

    /// <summary>Label displayed above the field.</summary>
    public required string Label { get; init; }

    /// <summary>Placeholder text.</summary>
    public string? Placeholder { get; init; }

    /// <summary>Whether the field is required.</summary>
    public bool Required { get; init; }

    /// <summary>Whether the field is disabled.</summary>
    public bool Disabled { get; init; }

    /// <summary>Select options (only for FieldType.Select).</summary>
    public IReadOnlyList<SelectOption<string>>? Options { get; init; }

    /// <summary>Default value as string.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Help text displayed below the field.</summary>
    public string? HelpText { get; init; }
}
