using System.Globalization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.Files;

/// <summary>
/// Package-internal byte-size formatter for the <see cref="TmPdfViewer"/> header subtitle
/// (FZE-11). Formats a byte count in the B/KB/MB/GB ladder using the ambient (or an
/// explicitly given) culture for the digits and the localized unit labels
/// (<c>TmPdfViewer_FileSize_B/_KB/_MB/_GB</c>). The core library keeps no public
/// formatter (the per-component <c>FormatSize</c> helpers are private), so the package
/// owns this primitive instead of copying markup into the component.
/// </summary>
internal static class PdfFileSizeFormatter
{
    private const double Kb = 1024;
    private const double Mb = Kb * 1024;
    private const double Gb = Mb * 1024;

    /// <summary>
    /// Formats a byte count as a culture-correct "1.4 MB"-style string.
    /// </summary>
    /// <param name="bytes">The document byte length.</param>
    /// <param name="localizer">Localizer supplying the unit labels.</param>
    /// <param name="culture">
    /// Culture for the number formatting; defaults to
    /// <see cref="CultureInfo.CurrentCulture"/> (the header renders in the request culture).
    /// </param>
    /// <returns>The formatted size, e.g. <c>500 B</c>, <c>1.0 KB</c>, <c>1.4 MB</c>, <c>3.3 GB</c>.</returns>
    public static string Format(long bytes, ITmLocalizer localizer, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        var value = (double)bytes;
        return bytes switch
        {
            >= (long)Gb => string.Create(culture, $"{value / Gb:0.0} {localizer["TmPdfViewer_FileSize_GB"]}"),
            >= (long)Mb => string.Create(culture, $"{value / Mb:0.0} {localizer["TmPdfViewer_FileSize_MB"]}"),
            >= (long)Kb => string.Create(culture, $"{value / Kb:0.0} {localizer["TmPdfViewer_FileSize_KB"]}"),
            _ => string.Create(culture, $"{bytes} {localizer["TmPdfViewer_FileSize_B"]}"),
        };
    }
}
