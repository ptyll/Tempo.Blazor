namespace Tempo.Blazor.Components.Inputs;

/// <summary>
/// Visual mode for displaying selected items in TmMultiSelect.
/// </summary>
public enum MultiSelectMode
{
    /// <summary>Selected items shown as removable chips (default).</summary>
    Chip,

    /// <summary>Selected items shown as delimiter-separated text.</summary>
    Delimiter,

    /// <summary>Items shown with checkboxes in dropdown; trigger shows count.</summary>
    CheckBox
}
