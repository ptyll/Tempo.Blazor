using System.Globalization;

namespace Tempo.Blazor.Components.Files;

/// <summary>
/// One mounted page's overlay box in scroll-content coordinates (the
/// <c>OnContinuousPageGeometry</c> JS report). <see cref="Top"/> and <see cref="Left"/>
/// position the Blazor slot inside the scroll content; <see cref="Width"/> and
/// <see cref="Height"/> keep it exactly over the page's inner (canvas) box.
/// </summary>
/// <param name="PageNumber">One-based page number the box belongs to.</param>
/// <param name="Top">Box top in scroll-content coordinates.</param>
/// <param name="Left">Box left in scroll-content coordinates.</param>
/// <param name="Width">Box width in CSS pixels.</param>
/// <param name="Height">Box height in CSS pixels.</param>
internal sealed record PdfPageOverlayBox(
    int PageNumber,
    double Top,
    double Left,
    double Width,
    double Height)
{
    /// <summary>The invariant-culture CSS position for an overlay slot (the JS report's box).</summary>
    public string ToSlotStyle() => $"top:{Top.ToString("0.##", CultureInfo.InvariantCulture)}px;"
        + $"left:{Left.ToString("0.##", CultureInfo.InvariantCulture)}px;"
        + $"width:{Width.ToString("0.##", CultureInfo.InvariantCulture)}px;"
        + $"height:{Height.ToString("0.##", CultureInfo.InvariantCulture)}px;";
}
