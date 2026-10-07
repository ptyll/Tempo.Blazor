namespace Tempo.Blazor.Components.Layout;

/// <summary>
/// Side from which the drawer slides in.
/// </summary>
public enum DrawerPosition
{
    /// <summary>Slide in from the right edge.</summary>
    Right,

    /// <summary>Slide in from the left edge.</summary>
    Left,

    /// <summary>
    /// Slide up from the bottom edge as a sheet: a drag handle, snap points, a sticky footer and
    /// safe-area insets. The sheet is positioned against the viewport, not the container it sits in.
    /// </summary>
    Bottom
}
