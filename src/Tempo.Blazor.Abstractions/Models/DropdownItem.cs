using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Models;

/// <summary>
/// Default implementation of IDropdownItem&lt;TValue&gt;.
/// Use with TmFilterableDropdown for items with description and avatar support.
/// </summary>
/// <typeparam name="TValue">The value type.</typeparam>
public sealed record DropdownItem<TValue> : IDropdownItem<TValue>
{
    public TValue Value { get; init; } = default!;
    public string Label { get; init; } = string.Empty;
    public bool IsDisabled { get; init; }
    public string? Icon { get; init; }
    public string? Description { get; init; }
    public string? AvatarSrc { get; init; }
    public string? AvatarInitials { get; init; }

    public DropdownItem() { }

    public DropdownItem(TValue value, string label, string? description = null)
    {
        Value = value;
        Label = label;
        Description = description;
    }
}
