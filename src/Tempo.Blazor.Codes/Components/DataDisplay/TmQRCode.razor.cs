using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using QRCoder;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// Renders a QR code as an SVG element you can scan and a screen reader can name.
/// </summary>
/// <remarks>
/// <para>
/// The SVG is built directly from QRCoder's module matrix instead of post-processing the library's
/// SVG string: the accessible name, the colour homes and the quiet zone are attributes of ONE
/// generated element, so no regular expression has to find them afterwards.
/// </para>
/// <para>
/// <b>Accessible name.</b> The root <c>&lt;svg&gt;</c> is <c>role="img"</c> and always carries a name:
/// <see cref="AriaLabel"/> when the host supplies one, otherwise the localized
/// <c>TmQRCode_DefaultLabel</c>. A <c>&lt;title&gt;</c> with the same text gives the native tooltip.
/// </para>
/// <para>
/// <b>Colours.</b> <see cref="ForegroundColor"/> and <see cref="BackgroundColor"/> take a hex,
/// <c>rgb()</c>/<c>hsl()</c>, named colour or <c>currentColor</c> value and are written as SVG
/// <c>fill</c> attributes — the component writes NO inline style on that path, so a strict
/// <c>style-src</c> is satisfied. A <c>var(--token)</c> reference is not a valid presentation
/// attribute (browsers do not substitute custom properties there), so that value — and only that
/// value — reaches the DOM as a <c>fill</c> declaration in a <c>style</c> attribute;
/// <see cref="UseThemeColors"/> is the CSP-clean way to follow the theme.
/// </para>
/// <para>
/// <b>Quiet zone.</b> The four-module quiet zone is part of the matrix and is painted by the surface
/// rectangle. In theme mode it reads <c>--tm-qr-code-background</c>, whose default is
/// <c>var(--tm-paper)</c> — the paper tokens are deliberately theme-invariant, so the quiet zone stays
/// light on a dark page and the code keeps scanning.
/// </para>
/// </remarks>
public partial class TmQRCode : ComponentBase
{
    /// <summary>Pixels per module in the SVG's own coordinate system (one viewBox unit).</summary>
    /// <remarks>Fixed at 10 because the pre-2.11.0 markup called <c>SvgQRCode.GetGraphic(10, …)</c>; the viewBox must stay byte-identical for consumers that sized the graphic from it.</remarks>
    private const int ModulePixels = 10;

    private const string DefaultForeground = "#000000";
    private const string DefaultBackground = "#ffffff";
    private const string DefaultFileName = "qr-code.svg";
    private const string DownloadHrefPrefix = "data:image/svg+xml;base64,";

    /// <summary>Optional logging, resolved through <see cref="IServiceProvider"/>: a host without logging simply gets no warning.</summary>
    [Inject] private IServiceProvider Services { get; set; } = default!;

    /// <summary>Localizer for the fallback accessible name and the download label.</summary>
    [Inject] private ITmLocalizer Loc { get; set; } = default!;

    private ILogger? _logger;
    private bool _loggerResolved;
    private readonly HashSet<string> _warnedSignatures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warnedContrasts = new(StringComparer.Ordinal);
    private string? _svgContent;
    private string? _standaloneSvg;
    private string? _downloadHref;
    private string _label = string.Empty;

    /// <summary>The text or URL to encode.</summary>
    [Parameter] public string Value { get; set; } = string.Empty;

    /// <summary>The size in pixels. Default is 200.</summary>
    [Parameter] public int Size { get; set; } = 200;

    /// <summary>Error correction level. Default is M.</summary>
    [Parameter] public QRErrorCorrectionLevel ErrorCorrectionLevel { get; set; } = QRErrorCorrectionLevel.M;

    /// <summary>
    /// Colour of the dark modules: a hex value (<c>#000000</c>), <c>rgb()</c>/<c>hsl()</c>, a named
    /// colour, <c>currentColor</c>, or a <c>var(--token)</c> reference. <see langword="null"/> (the
    /// default) paints <c>#000000</c>, or <c>currentColor</c> with <see cref="UseThemeColors"/>.
    /// </summary>
    /// <remarks>
    /// A <c>var(--token)</c> can only be a CSS declaration, so it is written into a <c>style</c>
    /// attribute — acceptable under a strict <c>style-src</c> only with <c>'unsafe-inline'</c>. Use
    /// <see cref="UseThemeColors"/> and the <c>--tm-qr-code-foreground</c> token for the CSP-clean path.
    /// </remarks>
    [Parameter] public string? ForegroundColor { get; set; }

    /// <summary>
    /// Colour of the surface and of the four-module quiet zone: any value <see cref="ForegroundColor"/>
    /// accepts. <see langword="null"/> (the default) paints <c>#ffffff</c>, or the
    /// <c>--tm-qr-code-background</c> token with <see cref="UseThemeColors"/>.
    /// </summary>
    [Parameter] public string? BackgroundColor { get; set; }

    /// <summary>
    /// Whether the ink and the quiet zone follow the component tokens
    /// (<c>--tm-qr-code-foreground</c>, default <c>var(--tm-paper-ink)</c>; <c>--tm-qr-code-background</c>,
    /// default <c>var(--tm-paper)</c>) instead of the default hex pair. Default is <see langword="false"/>,
    /// which keeps the pre-2.11.0 markup. An explicit <see cref="ForegroundColor"/>/<see cref="BackgroundColor"/>
    /// still wins over the token. Needs no inline style.
    /// </summary>
    [Parameter] public bool UseThemeColors { get; set; }

    /// <summary>
    /// The accessible name of the graphic. Default is <see langword="null"/>, which uses the localized
    /// <c>TmQRCode_DefaultLabel</c> ("QR code"); a blank value is treated the same way.
    /// </summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>
    /// Whether to render the opt-in "Download SVG" link. Default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The link is a native <c>&lt;a download&gt;</c> whose <c>href</c> is a
    /// <c>data:image/svg+xml;base64,</c> URL of <see cref="GetStandaloneSvg"/>: no JavaScript, no Blob,
    /// no extra round trip. The exported file carries absolute colours, so it renders on its own even
    /// when the page painted the QR code from tokens.
    /// </remarks>
    [Parameter] public bool ShowDownload { get; set; }

    /// <summary>The download file name. Default is <c>qr-code.svg</c>.</summary>
    [Parameter] public string? DownloadFileName { get; set; }

    /// <summary>Optional CSS class applied to the root element.</summary>
    [Parameter] public string? CssClass { get; set; }

    private string EffectiveDownloadFileName =>
        string.IsNullOrWhiteSpace(DownloadFileName) ? DefaultFileName : DownloadFileName.Trim();

    /// <summary>
    /// The SVG markup exactly as it is rendered into the page — possibly token-driven
    /// (<c>currentColor</c>, a <c>--tm-*</c> reference).
    /// </summary>
    /// <returns>The markup, or <see langword="null"/> while <see cref="Value"/> is empty.</returns>
    public string? GetSvg() => _svgContent;

    /// <summary>
    /// The SVG as a self-contained file: the same geometry with every colour resolved to an absolute
    /// value, so it renders outside the page's theme.
    /// </summary>
    /// <returns>The markup, or <see langword="null"/> while <see cref="Value"/> is empty.</returns>
    public string? GetStandaloneSvg() => _standaloneSvg;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        GenerateQrCode();
        base.OnParametersSet();
    }

    private void GenerateQrCode()
    {
        _svgContent = null;
        _standaloneSvg = null;
        _downloadHref = null;
        _label = ResolveLabel();

        if (string.IsNullOrEmpty(Value))
        {
            return;
        }

        var eccLevel = ErrorCorrectionLevel switch
        {
            QRErrorCorrectionLevel.L => QRCodeGenerator.ECCLevel.L,
            QRErrorCorrectionLevel.Q => QRCodeGenerator.ECCLevel.Q,
            QRErrorCorrectionLevel.H => QRCodeGenerator.ECCLevel.H,
            _ => QRCodeGenerator.ECCLevel.M
        };

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(Value, eccLevel);
        var runs = BuildRuns(data);
        var extent = data.ModuleMatrix.Count * ModulePixels;

        // Resolve each pair ONCE: Render only writes what the resolver decided, and the contrast check
        // inspects the same pair the markup carries instead of resolving a second time.
        var ink = ResolveInk();
        var surface = ResolveSurface();
        _svgContent = Render(runs, extent, ink, surface, standalone: false);
        WarnOnLowContrast(ink, surface);

        var (standaloneInk, standaloneSurface) = ResolveStandalonePair();
        _standaloneSvg = Render(runs, extent, standaloneInk, standaloneSurface, standalone: true);
        WarnOnLowContrast(standaloneInk, standaloneSurface);
        if (ShowDownload)
        {
            _downloadHref = DownloadHrefPrefix
                + Convert.ToBase64String(Encoding.UTF8.GetBytes(_standaloneSvg));
        }
    }

    /// <summary>Collapses the matrix into merged dark runs — the same rectangles QRCoder's SVG renderer emits.</summary>
    private static List<(int X, int Y, int Width, int Height)> BuildRuns(QRCodeData data)
    {
        var matrix = data.ModuleMatrix;
        var size = matrix.Count;
        var runs = new int[size, size];

        for (var y = 0; y < size; y++)
        {
            var row = matrix[y];
            var start = -1;
            var length = 0;
            for (var x = 0; x < size; x++)
            {
                if (x < row.Count && row[x])
                {
                    if (start < 0)
                    {
                        start = x;
                    }

                    length++;
                }
                else if (length > 0)
                {
                    runs[y, start] = length;
                    start = -1;
                    length = 0;
                }
            }

            if (length > 0)
            {
                runs[y, start] = length;
            }
        }

        var rectangles = new List<(int X, int Y, int Width, int Height)>();
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var length = runs[y, x];
                if (length <= 0)
                {
                    continue;
                }

                var height = 1;
                while (y + height < size && runs[y + height, x] == length)
                {
                    runs[y + height, x] = 0;
                    height++;
                }

                rectangles.Add((
                    x * ModulePixels,
                    y * ModulePixels,
                    length * ModulePixels,
                    height * ModulePixels));
            }
        }

        return rectangles;
    }

    private string Render(
        List<(int X, int Y, int Width, int Height)> rectangles,
        int extent,
        QrCodePaint ink,
        QrCodeSurface surface,
        bool standalone)
    {
        var themedSvg = !standalone
                        && UseThemeColors
                        && string.Equals(ink.Value, "currentColor", StringComparison.OrdinalIgnoreCase);

        var svg = new StringBuilder();
        svg.Append("<svg version=\"1.1\" baseProfile=\"full\" shape-rendering=\"crispEdges\"");
        if (themedSvg)
        {
            svg.Append(" class=\"tm-qr-code__svg tm-qr-code__svg--themed\"");
        }

        svg.Append(" role=\"img\"");
        svg.Append(" aria-label=\"").Append(Encode(_label)).Append('"');
        svg.Append(" width=\"").Append(Size.ToString(CultureInfo.InvariantCulture)).Append('"');
        svg.Append(" height=\"").Append(Size.ToString(CultureInfo.InvariantCulture)).Append('"');
        svg.Append(" viewBox=\"0 0 ").Append(extent.ToString(CultureInfo.InvariantCulture))
            .Append(' ').Append(extent.ToString(CultureInfo.InvariantCulture)).Append('"');
        svg.Append(" xmlns=\"http://www.w3.org/2000/svg\">");
        svg.Append("<title>").Append(Encode(_label)).Append("</title>");

        // The surface also paints the quiet zone: the matrix carries the four padding modules, and the
        // ink runs never cover them, so the light rectangle underneath is what a scanner sees.
        svg.Append("<rect");
        if (surface.Themed)
        {
            svg.Append(" class=\"tm-qr-code__surface\"");
        }
        else
        {
            AppendPaint(svg, surface.Paint);
        }

        svg.Append(" x=\"0\" y=\"0\" width=\"").Append(extent.ToString(CultureInfo.InvariantCulture))
            .Append("\" height=\"").Append(extent.ToString(CultureInfo.InvariantCulture)).Append("\" />");

        svg.Append("<g class=\"tm-qr-code__modules\"");
        AppendPaint(svg, ink);
        svg.Append('>');
        foreach (var (x, y, width, height) in rectangles)
        {
            svg.Append("<rect x=\"").Append(x.ToString(CultureInfo.InvariantCulture))
                .Append("\" y=\"").Append(y.ToString(CultureInfo.InvariantCulture))
                .Append("\" width=\"").Append(width.ToString(CultureInfo.InvariantCulture))
                .Append("\" height=\"").Append(height.ToString(CultureInfo.InvariantCulture))
                .Append("\" />");
        }

        svg.Append("</g></svg>");
        return svg.ToString();
    }

    private static void AppendPaint(StringBuilder svg, QrCodePaint paint)
    {
        if (paint.Kind == QrCodePaintKind.CssDeclaration)
        {
            // Only a CSS custom property (or a CSS-only colour function) lands here: neither is legal
            // as a presentation attribute, and this is the ONE place the component writes an inline
            // style. `UseThemeColors` is the style-src-safe alternative.
            svg.Append(" style=\"fill:").Append(Encode(paint.Value)).Append('"');
            return;
        }

        if (paint.IsUsable)
        {
            svg.Append(" fill=\"").Append(Encode(paint.Value)).Append('"');
        }
    }

    /// <summary>The ink of the SVG as it is rendered into the page.</summary>
    private QrCodePaint ResolveInk()
    {
        if (!string.IsNullOrWhiteSpace(ForegroundColor))
        {
            if (!QrCodePaintReader.TryRead(ForegroundColor, out var paint))
            {
                WarnOnce(nameof(ForegroundColor), ForegroundColor);
                return new QrCodePaint(DefaultForeground, QrCodePaintKind.Attribute);
            }

            return paint;
        }

        return UseThemeColors
            ? new QrCodePaint("currentColor", QrCodePaintKind.Attribute)
            : new QrCodePaint(DefaultForeground, QrCodePaintKind.Attribute);
    }

    /// <summary>The surface (and quiet zone) of the SVG as it is rendered into the page.</summary>
    private QrCodeSurface ResolveSurface()
    {
        if (!string.IsNullOrWhiteSpace(BackgroundColor))
        {
            if (!QrCodePaintReader.TryRead(BackgroundColor, out var paint))
            {
                WarnOnce(nameof(BackgroundColor), BackgroundColor);
                return new QrCodeSurface(false, new QrCodePaint(DefaultBackground, QrCodePaintKind.Attribute));
            }

            return new QrCodeSurface(false, paint);
        }

        return UseThemeColors
            ? new QrCodeSurface(true, default)
            : new QrCodeSurface(false, new QrCodePaint(DefaultBackground, QrCodePaintKind.Attribute));
    }

    /// <summary>
    /// The pair written into <see cref="GetStandaloneSvg"/>: a downloaded file opens outside the page,
    /// where <c>var()</c> never resolves and <c>currentColor</c> falls back to the viewer's default, so
    /// the export has to carry two absolute colours — ones that still clear the 3:1 graphic minimum.
    /// </summary>
    private (QrCodePaint Ink, QrCodeSurface Surface) ResolveStandalonePair()
    {
        var surface = ResolveStandaloneSurface();

        if (!string.IsNullOrWhiteSpace(ForegroundColor)
            && QrCodePaintReader.TryRead(ForegroundColor, out var hostInk)
            && IsAbsolute(hostInk))
        {
            // The host picked an absolute ink, so the export keeps the host's pair: repainting a brand
            // colour would be a surprise, and the contrast check below still reports a failing pair.
            return (hostInk, new QrCodeSurface(false, surface));
        }

        // The ink fell back (a token, currentColor, no value or an unusable one). Black on a dark
        // exported surface would be unscannable, so pick black or white by the surface's luminance.
        var legible = QrCodeContrast.LegibleInk(surface.Value);
        return legible is null
            ? (new QrCodePaint(DefaultForeground, QrCodePaintKind.Attribute),
               new QrCodeSurface(false, new QrCodePaint(DefaultBackground, QrCodePaintKind.Attribute)))
            : (new QrCodePaint(legible, QrCodePaintKind.Attribute),
               new QrCodeSurface(false, surface));
    }

    /// <summary>The exported surface: the host's absolute colour, otherwise the default white.</summary>
    private QrCodePaint ResolveStandaloneSurface()
    {
        if (!string.IsNullOrWhiteSpace(BackgroundColor)
            && QrCodePaintReader.TryRead(BackgroundColor, out var paint)
            && IsAbsolute(paint))
        {
            return paint;
        }

        return new QrCodePaint(DefaultBackground, QrCodePaintKind.Attribute);
    }

    private static bool IsAbsolute(QrCodePaint paint) =>
        paint.Kind == QrCodePaintKind.Attribute
        && !string.Equals(paint.Value, "currentColor", StringComparison.OrdinalIgnoreCase);

    private string ResolveLabel()
    {
        if (!string.IsNullOrWhiteSpace(AriaLabel))
        {
            return AriaLabel.Trim();
        }

        var localized = Loc["TmQRCode_DefaultLabel"];
        if (!string.IsNullOrWhiteSpace(localized) && !string.Equals(localized, "TmQRCode_DefaultLabel", StringComparison.Ordinal))
        {
            return localized;
        }

        // A localizer that resolves nothing must not leave the graphic unnamed again: the encoded value
        // is still a name, and a component without a value renders no graphic at all.
        return Value;
    }

    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);

    /// <summary>
    /// Reports an unusable colour ONCE per parameter/value pair, so a re-render cannot flood the log
    /// with the same finding — and a second invalid parameter is not hidden by the first one's
    /// signature.
    /// </summary>
    private void WarnOnce(string parameterName, string? invalidValue)
    {
        if (!_warnedSignatures.Add(parameterName + "|" + invalidValue))
        {
            return;
        }

        Logger?.LogWarning(
            "TmQRCode: {Parameter}=\"{Value}\" is not an SVG colour (hex, rgb()/hsl(), a named colour "
            + "or currentColor) — the default was used. For a token use UseThemeColors with "
            + "--tm-qr-code-foreground / --tm-qr-code-background, or pass ForegroundColor=\"var(--token)\" "
            + "accepting an inline style.",
            parameterName,
            invalidValue);
    }

    private void WarnOnLowContrast(QrCodePaint ink, QrCodeSurface surface)
    {
        if (surface.Themed || ink.Kind != QrCodePaintKind.Attribute
            || surface.Paint.Kind != QrCodePaintKind.Attribute)
        {
            return;
        }

        var ratio = QrCodeContrast.Ratio(ink.Value, surface.Paint.Value);
        if (ratio is null || ratio >= QrCodeContrast.MinimumGraphicRatio)
        {
            return;
        }

        var signature = ink.Value + "|" + surface.Paint.Value;
        if (!_warnedContrasts.Add(signature))
        {
            return;
        }

        Logger?.LogWarning(
            "TmQRCode: ForegroundColor=\"{Foreground}\" on BackgroundColor=\"{Background}\" has a contrast of "
            + "{Ratio:0.00}:1 — below the {Minimum:0.#}:1 WCAG minimum for a graphical object. "
            + "A scanner may not read the code.",
            ink.Value,
            surface.Paint.Value,
            ratio.Value,
            QrCodeContrast.MinimumGraphicRatio);
    }

    private ILogger? Logger
    {
        get
        {
            if (!_loggerResolved)
            {
                _loggerResolved = true;
                _logger = (Services.GetService(typeof(ILoggerFactory)) as ILoggerFactory)
                    ?.CreateLogger<TmQRCode>();
            }

            return _logger;
        }
    }

    /// <summary>One resolved surface: either a token-driven class or an explicit paint.</summary>
    private readonly record struct QrCodeSurface(bool Themed, QrCodePaint Paint);
}
