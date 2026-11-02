using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Models;

/// <summary>
/// A named group of <see cref="ISidebarNavItem"/>s, rendered by <c>TmSidebar</c> under one visible
/// heading. The heading names the group's list (<c>aria-labelledby</c>), so a screen-reader user
/// hears the group name before the items instead of one long unlabelled list.
/// </summary>
public sealed record SidebarNavSection
{
    /// <summary>
    /// Heading text of the group. Rendered as the section heading and used as the accessible name
    /// of the group's list — keep it short ("Main", "Administration").
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Items of the group, in display order.</summary>
    public IReadOnlyList<ISidebarNavItem> Items { get; init; } = [];

    /// <summary>Creates an empty section.</summary>
    public SidebarNavSection() { }

    /// <summary>Creates a section with a heading and its items.</summary>
    /// <param name="title">Heading text of the group.</param>
    /// <param name="items">Items of the group, in display order; null is treated as an empty group.</param>
    public SidebarNavSection(string title, IReadOnlyList<ISidebarNavItem>? items)
    {
        Title = title;
        Items = items ?? [];
    }
}
