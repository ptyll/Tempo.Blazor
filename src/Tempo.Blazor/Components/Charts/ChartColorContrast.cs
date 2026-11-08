using System.Globalization;

namespace Tempo.Blazor.Components.Charts;

/// <summary>
/// The colour arithmetic a chart's ink guard needs: parse a fill in the shapes a palette or a host's
/// <c>BackgroundColors</c> actually hand over, and measure it with the WCAG relative-luminance formula.
/// </summary>
/// <remarks>
/// <para>
/// <b>Accepted shapes.</b> <c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>;
/// <c>rgb()</c>/<c>rgba()</c> with numeric channels (0–255) or percentage channels (multiplied by 2.55,
/// which is what makes <c>rgb(100% 100% 100%)</c> white); and <c>hsl()</c>/<c>hsla()</c> with a degree
/// hue and percentage saturation/lightness. Alpha is ignored on purpose: a chart label is painted ON
/// the band, so the opaque colour is the surface the ink has to read against.
/// </para>
/// <para>
/// <b>Documented fallback.</b> Anything else — a <c>var(--tm-…)</c> token, a named colour, a
/// <c>color-mix()</c> — is UNRESOLVED and the caller keeps the pre-2.11.0 assumption of a dark brand
/// fill. That is a deliberate policy, not an omission: guessing a light fill would flip the ink on a
/// host's brand colour it never measured.
/// </para>
/// </remarks>
internal static class ChartColorContrast
{
    /// <summary>
    /// Luminance at which black and white contrast equally with a fill (WCAG 1.4.3 crossover). Below it
    /// the light ink measures better, at or above it the dark ink does.
    /// </summary>
    internal const double InkCrossoverLuminance = 0.179;

    /// <summary>Whether the resolved fill takes the DARK inside ink (i.e. is light enough).</summary>
    internal static bool IsLight(string fill) =>
        TryParseColour(fill, out var r, out var g, out var b)
        && RelativeLuminance(r, g, b) >= InkCrossoverLuminance;

    /// <summary>
    /// Parses a CSS colour into 8-bit RGB channels. Returns <c>false</c> for every shape this component
    /// cannot resolve (see the documented fallback on the type).
    /// </summary>
    internal static bool TryParseColour(string value, out double r, out double g, out double b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text[0] == '#')
        {
            return TryParseHex(text, out r, out g, out b);
        }

        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRgb(text, out r, out g, out b);
        }

        return text.StartsWith("hsl", StringComparison.OrdinalIgnoreCase)
            && TryParseHsl(text, out r, out g, out b);
    }

    /// <summary>WCAG 2.1 relative luminance of an 8-bit sRGB colour (0 = black, 1 = white).</summary>
    internal static double RelativeLuminance(double r, double g, double b)
    {
        static double Channel(double value)
        {
            var scaled = Math.Clamp(value, 0, 255) / 255.0;
            return scaled <= 0.03928 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static bool TryParseHex(string text, out double r, out double g, out double b)
    {
        r = g = b = 0;
        var hex = text[1..];
        if (hex.Length is not (3 or 4 or 6 or 8))
        {
            return false;
        }

        // uint, not int: an 8-digit #rrggbbaa with a bright alpha ("#ffffffff") is 4 294 967 295, which
        // no int reads — and rejecting a valid white-with-alpha would silently flip the ink.
        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        var digits = hex.Length is 3 or 4
            // #rgb / #rgba — expand the shorthand, ignore alpha.
            ? string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2])
            : hex[..6];

        var packed = int.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        r = (packed >> 16) & 0xFF;
        g = (packed >> 8) & 0xFF;
        b = packed & 0xFF;
        return true;
    }

    private static bool TryParseRgb(string text, out double r, out double g, out double b)
    {
        var channels = Channels(text);
        r = g = b = 0;
        if (channels.Length < 3)
        {
            return false;
        }

        r = Channel(channels[0]);
        g = Channel(channels[1]);
        b = Channel(channels[2]);
        return true;
    }

    private static bool TryParseHsl(string text, out double r, out double g, out double b)
    {
        var channels = Channels(text);
        r = g = b = 0;
        if (channels.Length < 3)
        {
            return false;
        }

        var hue = channels[0].Value % 360;
        if (hue < 0)
        {
            hue += 360;
        }

        // Saturation and lightness are percentages in hsl() whether or not the '%' is written (a bare
        // number there is still a percentage); a `deg` suffix on the hue is stripped by Channels.
        var saturation = Math.Clamp(channels[1].Value / 100.0, 0, 1);
        var lightness = Math.Clamp(channels[2].Value / 100.0, 0, 1);

        var chroma = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var sector = hue / 60;
        var second = chroma * (1 - Math.Abs((sector % 2) - 1));
        var (r1, g1, b1) = (int)sector switch
        {
            0 => (chroma, second, 0d),
            1 => (second, chroma, 0d),
            2 => (0d, chroma, second),
            3 => (0d, second, chroma),
            4 => (second, 0d, chroma),
            _ => (chroma, 0d, second)
        };

        var match = lightness - (chroma / 2);
        r = Math.Round((r1 + match) * 255);
        g = Math.Round((g1 + match) * 255);
        b = Math.Round((b1 + match) * 255);
        return true;
    }

    /// <summary>
    /// The numeric arguments of a colour function: the text between the first '(' and the last ')',
    /// split on commas, spaces and the alpha slash. A <c>%</c> suffix is recorded per channel (and
    /// stripped) and a <c>deg</c> suffix on a hue is stripped, because rgb() channels, hsl() hues and an
    /// alpha argument all arrive through this one reader.
    /// </summary>
    private static (double Value, bool Percentage)[] Channels(string text)
    {
        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            return [];
        }

        var parts = text[(open + 1)..close]
            .Split([',', ' ', '/', '\t'], StringSplitOptions.RemoveEmptyEntries);

        var channels = new List<(double, bool)>(parts.Length);
        foreach (var part in parts)
        {
            var channel = part;
            var isPercentage = channel.EndsWith('%');
            if (isPercentage)
            {
                channel = channel[..^1];
            }
            else if (channel.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
            {
                channel = channel[..^3];
            }

            if (!double.TryParse(channel, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                // An alpha keyword or an unsupported unit: the readable part of the value ends here.
                continue;
            }

            channels.Add((parsed, isPercentage));
        }

        return [.. channels];
    }

    /// <summary>
    /// One rgb() channel: a percentage scales 0–100 % onto 0–255 (×2.55), a number is already 8-bit.
    /// The scale is written as <c>value × 255 / 100</c> and not <c>value × 2.55</c>, so 100 % is exactly
    /// 255 and 50 % exactly 127.5 instead of a float that lands next to it.
    /// </summary>
    private static double Channel((double Value, bool Percentage) channel) =>
        Math.Clamp(channel.Percentage ? channel.Value * 255 / 100 : channel.Value, 0, 255);
}
