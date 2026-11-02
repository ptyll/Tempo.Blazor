namespace Tempo.Blazor.Components.Layout;

/// <summary>
/// How an <see cref="TmAppShell"/> presents its navigation on a PHONE layout (a container narrower
/// than the shell's mobile threshold). A phone has no room for the docked column or the tablet rail,
/// so the navigation moves into a surface the user opens on demand, or into a bottom bar.
/// </summary>
public enum AppShellMobileNav
{
    /// <summary>
    /// The top bar carries a hamburger button that opens the full navigation in a modal
    /// <c>TmDrawer</c> (the default): focus enters the drawer, Escape / its close button / choosing an
    /// entry close it, and the focus returns to the hamburger. The drawer renders as a sibling of the
    /// frame (never inside its CSS size container).
    /// </summary>
    Drawer = 0,

    /// <summary>
    /// The navigation is a <c>TmBottomNavigation</c> bar at the bottom of the frame, and no drawer
    /// exists: the top bar renders no hamburger. Entries come from <see cref="TmAppShell.BottomNavItems"/>
    /// or are derived from the navigation model. The bar is a layout ROW of the frame, not
    /// viewport-fixed chrome.
    /// </summary>
    BottomNavigation = 1,
}
