namespace Tempo.Blazor.Components.AI;

/// <summary>
/// How a <c>TmFanOutGrid</c> decides how many tiles sit on one grid row. The default
/// <see cref="Fixed"/> reads the host's <c>Columns</c> parameter (pure CSS plus one inline knob, no
/// measurement); <see cref="Auto"/> measures the component's own container through
/// <c>TmLayoutObserver</c> and derives the count from <c>MinTileWidth</c>, so a phone never scrolls
/// the grid sideways and the C# window math can never diverge from the rendered columns.
/// </summary>
public enum FanOutColumnsMode
{
    /// <summary>The host's <c>Columns</c> parameter decides; the component never measures anything.</summary>
    Fixed = 0,

    /// <summary>The component measures its own container and fits as many tiles of <c>MinTileWidth</c> as the width allows.</summary>
    Auto = 1,
}
