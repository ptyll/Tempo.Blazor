namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// Where the minimap renders (DI3, phase 6). <see cref="Auto"/> keeps the panel card on a desktop
/// layout and floats the minimap over the canvas corner from tablet down;
/// <see cref="Panel"/> and <see cref="Overlay"/> force the choice for every layout.
/// </summary>
public enum DiagramMinimapPlacement
{
    /// <summary>Card in the right column on desktop, overlay over the canvas on tablet and mobile.</summary>
    Auto = 0,

    /// <summary>Always the card in the right column (the pre-phase-6 desktop presentation).</summary>
    Panel = 1,

    /// <summary>Always the overlay floating over the canvas corner.</summary>
    Overlay = 2,
}
