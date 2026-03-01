using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Models;

/// <summary>
/// Default implementation of ISelectOption&lt;TValue&gt;.
/// Use with TmSelect, TmRadioGroup, and other selection components.
/// </summary>
/// <typeparam name="TValue">The value type.</typeparam>
public sealed record SelectOption<TValue> : ISelectOption<TValue>
{
    public TValue Value { get; init; } = default!;
    public string Label { get; init; } = string.Empty;
    public bool IsDisabled { get; init; }
    public string? Icon { get; init; }

    public SelectOption() { }

    public SelectOption(TValue value, string label, bool isDisabled = false, string? icon = null)
    {
        Value = value;
        Label = label;
        IsDisabled = isDisabled;
        Icon = icon;
    }

    /// <summary>Creates a SelectOption from a value and label.</summary>
    public static SelectOption<TValue> From(TValue value, string label) => new(value, label);
}
