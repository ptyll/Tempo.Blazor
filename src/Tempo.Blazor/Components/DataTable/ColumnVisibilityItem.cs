namespace Tempo.Blazor.Components.DataTable;

/// <summary>
/// Lightweight descriptor used by TmColumnPicker to represent one column's visibility state.
/// Non-generic so TmColumnPicker doesn't need a type parameter.
/// </summary>
public sealed record ColumnVisibilityItem(string Key, string Title, bool IsVisible, bool IsHideable);
