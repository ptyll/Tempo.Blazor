namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents an item in a filterable/searchable dropdown with additional display metadata.
/// Extends ISelectOption with description and avatar support.
/// Used by TmFilterableDropdown.
/// </summary>
/// <typeparam name="TValue">The value type of the item.</typeparam>
public interface IDropdownItem<TValue> : ISelectOption<TValue>
{
    /// <summary>Gets an optional secondary description displayed below the label.</summary>
    string? Description { get; }

    /// <summary>Gets an optional avatar image URL for this item.</summary>
    string? AvatarSrc { get; }

    /// <summary>Gets optional initials to display when AvatarSrc is null.</summary>
    string? AvatarInitials { get; }
}
