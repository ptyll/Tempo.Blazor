namespace Tempo.Blazor.Components.Diagram;

/// <summary>Zoom range shared by every zoom path (buttons, wheel, pinch, zoomTo, fit). Mirrored by
/// MIN_SCALE / MAX_SCALE in wwwroot/js/diagram-editor.js; DiagramZoomLimitsTests keeps them equal.</summary>
internal static class DiagramZoomLimits
{
    /// <summary>Smallest scale (15 %).</summary>
    public const double MinScale = 0.15;

    /// <summary>Largest scale (500 %).</summary>
    public const double MaxScale = 5.0;

    /// <summary>Clamps <paramref name="scale"/> into [MinScale, MaxScale].</summary>
    public static double Clamp(double scale) => Math.Clamp(scale, MinScale, MaxScale);
}
