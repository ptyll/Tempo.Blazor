namespace Tempo.Blazor.Abstractions.Layout;

/// <summary>
/// How a component decides its responsive layout. <see cref="Auto"/> measures the component's own
/// container; the other values force a layout so a host — and a test — can render a mode without a DOM.
/// </summary>
public enum TmLayoutMode
{
    /// <summary>Measure the component root and follow its width. The first frame, before a measurement, is <see cref="Desktop"/>.</summary>
    Auto = 0,

    /// <summary>Force the wide layout (container at least <see cref="TmBreakpoints.Lg"/>).</summary>
    Desktop = 1,

    /// <summary>Force the compact layout (container from <see cref="TmBreakpoints.Sm"/> up to, but not including, <see cref="TmBreakpoints.Lg"/>).</summary>
    Tablet = 2,

    /// <summary>Force the narrow layout (container below <see cref="TmBreakpoints.Sm"/>).</summary>
    Mobile = 3,
}
