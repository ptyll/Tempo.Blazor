using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Models;

/// <summary>
/// Default implementation of ISidebarNavItem.
/// </summary>
public sealed record SidebarNavItem : ISidebarNavItem
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string Href { get; init; } = string.Empty;
    public int? BadgeCount { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<ISidebarNavItem>? Children { get; init; }
}
