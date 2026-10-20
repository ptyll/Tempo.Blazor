namespace Tempo.Blazor.Components.Layout;

/// <summary>
/// The edge a <see cref="TmSidePanel"/> belongs to: a right-hand inspector (the default) or a
/// left-hand navigation / task panel. It decides which edge the side sheet slides in from.
/// </summary>
public enum SidePanelSide
{
    /// <summary>The panel sits on the left edge of its host; its side sheet slides in from the left.</summary>
    Left,

    /// <summary>The panel sits on the right edge of its host; its side sheet slides in from the right.</summary>
    Right,
}
