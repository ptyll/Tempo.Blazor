using System.Globalization;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>How a host-supplied paint value may reach an SVG element.</summary>
internal enum QrCodePaintKind
{
    /// <summary>The value is not a usable colour; the caller falls back to its default.</summary>
    None = 0,

    /// <summary>
    /// The value is a valid SVG <c>fill</c> presentation attribute (hex, <c>rgb()</c>/<c>hsl()</c>,
    /// a named colour or <c>currentColor</c>). It reaches the DOM as an attribute, so the element
    /// carries no inline style and a strict <c>style-src</c> is satisfied.
    /// </summary>
    Attribute = 1,

    /// <summary>
    /// The value is a CSS custom-property reference (<c>var(--…)</c>) or another CSS colour function
    /// that is NOT valid as a presentation attribute. It reaches the DOM as a <c>fill</c> declaration
    /// inside a <c>style</c> attribute — CSS resolves <c>var()</c>, an attribute cannot.
    /// </summary>
    CssDeclaration = 2,
}

/// <summary>One resolved paint: the value and how it has to be written.</summary>
/// <param name="Value">The trimmed CSS value, safe to put in an attribute or a declaration.</param>
/// <param name="Kind">Where <paramref name="Value"/> may be written.</param>
internal readonly record struct QrCodePaint(string Value, QrCodePaintKind Kind)
{
    /// <summary>Whether the value is usable at all.</summary>
    public bool IsUsable => Kind != QrCodePaintKind.None;
}

/// <summary>
/// Reads the <c>ForegroundColor</c> / <c>BackgroundColor</c> parameters of <see cref="TmQRCode"/>.
/// </summary>
/// <remarks>
/// A colour has TWO possible homes in an SVG. A plain colour (<c>#0f172a</c>, <c>rgb(15, 23, 42)</c>,
/// <c>black</c>, <c>currentColor</c>) is a valid <c>fill</c> ATTRIBUTE value — the component prefers
/// it, because an attribute needs no inline style and therefore survives a strict <c>style-src</c>.
/// A <c>var(--token)</c> reference is NOT: presentation attributes are parsed as the property's
/// grammar and no browser substitutes custom properties there, so a token can only work as a CSS
/// declaration. The reader below picks the right home per value and rejects everything that is not a
/// colour at all (<c>red; background:url(…)</c> would otherwise inject a declaration).
///
/// The grammar is deliberately narrow: the component renders a scannable graphic, so accepting an
/// arbitrary CSS value would buy nothing and risk markup injection. <see cref="QrCodeContrast"/>
/// reuses the same reader for its WCAG check.
/// </remarks>
internal static class QrCodePaintReader
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Characters and function shapes that never belong to a single colour value. <c>&amp;</c> is here
    /// because the value is written into markup: an entity such as <c>&amp;quot;</c> must be rejected at
    /// the door rather than encoded (or decoded) later.
    /// </summary>
    private static readonly string[] Forbidden =
    [
        ";", "{", "}", "<", ">", "\"", "'", "&", "\\", "url(", "expression(", "@import",
    ];

    private static readonly Regex HexColor = new(
        @"^#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$",
        RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>
    /// The CSS Color Module named colours plus <c>currentColor</c> and <c>transparent</c>. A bare
    /// identifier is NOT enough: <c>fill="not-a-colour"</c> is an invalid presentation attribute and the
    /// element silently drops to the initial fill, which for the quiet zone would mean a transparent
    /// surface.
    /// </summary>
    private static readonly HashSet<string> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige", "bisque", "black",
        "blanchedalmond", "blue", "blueviolet", "brown", "burlywood", "cadetblue", "chartreuse",
        "chocolate", "coral", "cornflowerblue", "cornsilk", "crimson", "cyan", "darkblue", "darkcyan",
        "darkgoldenrod", "darkgray", "darkgreen", "darkgrey", "darkkhaki", "darkmagenta",
        "darkolivegreen", "darkorange", "darkorchid", "darkred", "darksalmon", "darkseagreen",
        "darkslateblue", "darkslategray", "darkslategrey", "darkturquoise", "darkviolet", "deeppink",
        "deepskyblue", "dimgray", "dimgrey", "dodgerblue", "firebrick", "floralwhite", "forestgreen",
        "fuchsia", "gainsboro", "ghostwhite", "gold", "goldenrod", "gray", "green", "greenyellow",
        "grey", "honeydew", "hotpink", "indianred", "indigo", "ivory", "khaki", "lavender",
        "lavenderblush", "lawngreen", "lemonchiffon", "lightblue", "lightcoral", "lightcyan",
        "lightgoldenrodyellow", "lightgray", "lightgreen", "lightgrey", "lightpink", "lightsalmon",
        "lightseagreen", "lightskyblue", "lightslategray", "lightslategrey", "lightsteelblue",
        "lightyellow", "lime", "limegreen", "linen", "magenta", "maroon", "mediumaquamarine",
        "mediumblue", "mediumorchid", "mediumpurple", "mediumseagreen", "mediumslateblue",
        "mediumspringgreen", "mediumturquoise", "mediumvioletred", "midnightblue", "mintcream",
        "mistyrose", "moccasin", "navajowhite", "navy", "oldlace", "olive", "olivedrab", "orange",
        "orangered", "orchid", "palegoldenrod", "palegreen", "paleturquoise", "palevioletred",
        "papayawhip", "peachpuff", "peru", "pink", "plum", "powderblue", "purple", "rebeccapurple",
        "red", "rosybrown", "royalblue", "saddlebrown", "salmon", "sandybrown", "seagreen", "seashell",
        "sienna", "silver", "skyblue", "slateblue", "slategray", "slategrey", "snow", "springgreen",
        "steelblue", "tan", "teal", "thistle", "tomato", "turquoise", "violet", "wheat", "white",
        "whitesmoke", "yellow", "yellowgreen", "currentcolor", "transparent"
    };

    private static readonly Regex PaintFunction = new(
        @"^(?:rgba?|hsla?)\((?<body>[^()]*)\)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    /// <summary>Functions that are only meaningful as CSS declarations (<c>var()</c> first).</summary>
    private static readonly Regex CssOnlyFunction = new(
        @"^(?:var|color-mix|light-dark|oklch|oklab|lab|lch|color)\(.*\)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline,
        RegexTimeout);

    /// <summary>A reference to a custom property — the only <c>var()</c> shape that names a token.</summary>
    private static readonly Regex VarReference = new(
        @"^var\(\s*--",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    /// <summary>Reads one paint value.</summary>
    /// <param name="value">The host's value, possibly blank.</param>
    /// <param name="paint">The resolved paint when the method returns <c>true</c>.</param>
    /// <returns><c>true</c> when the value is a usable colour.</returns>
    public static bool TryRead(string? value, out QrCodePaint paint)
    {
        paint = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        foreach (var forbidden in Forbidden)
        {
            if (text.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (CssOnlyFunction.IsMatch(text))
        {
            // A var() reference must name a custom property; anything else in the family is a colour
            // function whose own validity is the browser's business.
            if (text.StartsWith("var(", StringComparison.OrdinalIgnoreCase) && !VarReference.IsMatch(text))
            {
                return false;
            }

            paint = new QrCodePaint(text, QrCodePaintKind.CssDeclaration);
            return true;
        }

        if (HexColor.IsMatch(text))
        {
            paint = new QrCodePaint(text, QrCodePaintKind.Attribute);
            return true;
        }

        var function = PaintFunction.Match(text);
        if (function.Success)
        {
            if (!HasBalancedArguments(function.Groups["body"].Value))
            {
                return false;
            }

            paint = new QrCodePaint(text, QrCodePaintKind.Attribute);
            return true;
        }

        if (NamedColors.Contains(text))
        {
            paint = new QrCodePaint(text, QrCodePaintKind.Attribute);
            return true;
        }

        return false;
    }

    private static bool HasBalancedArguments(string body)
    {
        // The regex already rejected nested parentheses; what is left is the channel list. Every
        // channel must be numeric (or a percentage) — `rgb(foo)` and `rgb()` are not colours.
        var channels = body.Split([' ', ',', '/'], StringSplitOptions.RemoveEmptyEntries);
        return channels.Length >= 3
               && channels.Take(3).All(channel =>
                   channel.EndsWith('%')
                       ? double.TryParse(channel[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                       : double.TryParse(channel, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
    }
}

/// <summary>
/// The WCAG 2.1 contrast of a QR code's ink against its quiet zone — the "will a scanner still read
/// this?" check. Reports only when BOTH values are absolute colours it can resolve; a token,
/// <c>currentColor</c> or named colour returns <see langword="null"/> (the component cannot know what
/// it will resolve to, and a guessed warning on every render is noise).
/// </summary>
/// <remarks>
/// This is a deliberately narrow copy of the luminance maths in the core package's
/// <c>ChartColorContrast</c>, which is <c>internal</c> to <c>Tempo.Blazor</c> and therefore not
/// reachable from this assembly. It resolves only hex and <c>rgb()</c>/<c>rgba()</c> — the shapes the
/// component documents for a dev-time warning. Extending it beyond a warning is a decision to promote
/// one shared reader, not to grow this copy, with ONE exception: <see cref="LegibleInk"/> also picks the
/// ink of the exported file, because a downloaded SVG cannot resolve a page token and would otherwise
/// paint black on a dark surface.
/// </remarks>
internal static class QrCodeContrast
{
    /// <summary>WCAG 1.4.11: a graphical object needs 3:1 against its adjacent colours.</summary>
    public const double MinimumGraphicRatio = 3.0;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex HexColor = new(
        @"^#(?<hex>[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex RgbColor = new(
        @"^rgba?\((?<body>[^()]*)\)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    /// <summary>Ratio of the relative luminances of <paramref name="foreground"/> and <paramref name="background"/>.</summary>
    /// <param name="foreground">The module colour.</param>
    /// <param name="background">The quiet-zone colour.</param>
    /// <returns>The ratio (1…21), or <see langword="null"/> when either value is not resolvable here.</returns>
    public static double? Ratio(string foreground, string background)
    {
        if (!TryResolve(foreground, out var foregroundLuminance)
            || !TryResolve(background, out var backgroundLuminance))
        {
            return null;
        }

        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// The ink an exported file should carry on <paramref name="background"/>: plain black or plain
    /// white, whichever has the higher contrast. Returns <see langword="null"/> when the background is
    /// not resolvable here (a named colour, a token), which the caller answers by forcing the default
    /// pair rather than guessing.
    /// </summary>
    /// <param name="background">The surface of the exported SVG.</param>
    /// <returns><c>#000000</c>, <c>#ffffff</c>, or <see langword="null"/>.</returns>
    public static string? LegibleInk(string background)
    {
        var black = Ratio("#000000", background);
        var white = Ratio("#ffffff", background);
        return black is null || white is null
            ? null
            : white > black
                ? "#ffffff"
                : "#000000";
    }

    private static bool TryResolve(string value, out double luminance)
    {
        luminance = 0;
        var text = value.Trim();

        var hex = HexColor.Match(text);
        if (hex.Success)
        {
            var digits = hex.Groups["hex"].Value;
            if (digits.Length is 3 or 4)
            {
                digits = string.Concat(digits[..3].Select(character => new string(character, 2)));
            }

            luminance = RelativeLuminance(
                Convert.ToInt32(digits[..2], 16),
                Convert.ToInt32(digits.Substring(2, 2), 16),
                Convert.ToInt32(digits.Substring(4, 2), 16));
            return true;
        }

        var rgb = RgbColor.Match(text);
        if (!rgb.Success)
        {
            return false;
        }

        var channels = rgb.Groups["body"].Value.Split([' ', ',', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (channels.Length < 3)
        {
            return false;
        }

        var values = new double[3];
        for (var index = 0; index < 3; index++)
        {
            var channel = channels[index];
            if (!double.TryParse(
                    channel.EndsWith('%') ? channel[..^1] : channel,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out values[index]))
            {
                return false;
            }

            if (channel.EndsWith('%'))
            {
                values[index] = values[index] / 100 * 255;
            }
        }

        luminance = RelativeLuminance(values[0], values[1], values[2]);
        return true;
    }

    /// <summary>WCAG 2.1 relative luminance of an 8-bit sRGB colour (0 = black, 1 = white).</summary>
    private static double RelativeLuminance(double r, double g, double b)
    {
        static double Channel(double value)
        {
            var scaled = Math.Clamp(value, 0, 255) / 255.0;
            return scaled <= 0.03928 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(r)) + (0.7152 * Channel(g)) + (0.0722 * Channel(b));
    }
}
