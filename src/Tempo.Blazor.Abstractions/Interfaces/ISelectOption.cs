namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents a selectable option with a typed value and display label.
/// Used by TmSelect, TmRadioGroup, and other selection components.
/// </summary>
/// <typeparam name="TValue">The value type of the option.</typeparam>
public interface ISelectOption<TValue>
{
    /// <summary>Gets the option value used for binding.</summary>
    TValue Value { get; }

    /// <summary>Gets the display label shown to the user.</summary>
    string Label { get; }

    /// <summary>Gets whether this option is disabled and cannot be selected.</summary>
    bool IsDisabled { get; }

    /// <summary>Gets an optional icon name (from IconNames) to display alongside the label.</summary>
    string? Icon { get; }
}
