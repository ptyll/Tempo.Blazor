namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents a navigation item in TmSidebar.
/// </summary>
public interface ISidebarNavItem
{
    /// <summary>Unique identifier for the nav item.</summary>
    string Id { get; }

    /// <summary>Display label shown in the sidebar.</summary>
    string Label { get; }

    /// <summary>Icon name from IconNames constants.</summary>
    string Icon { get; }

    /// <summary>Navigation URL for the item.</summary>
    string Href { get; }

    /// <summary>Optional badge count (e.g., unread notifications). Null = no badge.</summary>
    int? BadgeCount { get; }

    /// <summary>Whether this item is currently active/selected.</summary>
    bool IsActive { get; }

    /// <summary>Optional child items for nested navigation.</summary>
    IReadOnlyList<ISidebarNavItem>? Children { get; }
}
