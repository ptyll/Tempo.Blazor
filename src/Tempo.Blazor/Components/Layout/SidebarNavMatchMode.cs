namespace Tempo.Blazor.Components.Layout;

/// <summary>
/// How <see cref="TmSidebar"/> compares an item's <c>Href</c> with the current location when
/// <see cref="TmSidebar.AutoActive"/> is on.
/// </summary>
public enum SidebarNavMatchMode
{
    /// <summary>
    /// The item is active only when its path equals the current path — <c>/settings</c> is NOT
    /// active on <c>/settings/profile</c>. The default, because it never lights up two entries for
    /// one page.
    /// </summary>
    Exact = 0,

    /// <summary>
    /// The item is active when its path equals the current path OR the current path continues it
    /// (<c>/settings</c> is active on <c>/settings/profile</c>). Use it for a section entry that
    /// owns its sub-pages; a segment boundary is respected, so <c>/settings</c> never matches
    /// <c>/settings-old</c>.
    /// </summary>
    Prefix = 1,
}
