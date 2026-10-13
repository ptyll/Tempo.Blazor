using System.Globalization;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.Abstractions.Models;

/// <summary>
/// Helper methods for color conversion between Hex, RGB, and RGBA formats.
/// </summary>
public static class ColorHelper
{
    private static readonly Regex HexRegex = new("^#?(?<r>[0-9A-Fa-f]{2})(?<g>[0-9A-Fa-f]{2})(?<b>[0-9A-Fa-f]{2})(?<a>[0-9A-Fa-f]{2})?$", RegexOptions.Compiled);
    private static readonly Regex RgbRegex = new(@"^rgb\s*\(\s*(?<r>\d{1,3})\s*,\s*(?<g>\d{1,3})\s*,\s*(?<b>\d{1,3})\s*\)$", RegexOptions.Compiled);
    private static readonly Regex RgbaRegex = new(@"^rgba\s*\(\s*(?<r>\d{1,3})\s*,\s*(?<g>\d{1,3})\s*,\s*(?<b>\d{1,3})\s*,\s*(?<a>[0-9]*\.?[0-9]+)\s*\)$", RegexOptions.Compiled);

    /// <summary>Parses a color string and returns RGBA components.</summary>
    public static (byte R, byte G, byte B, double A) Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (0, 0, 0, 1);

        value = value.Trim();

        var hexMatch = HexRegex.Match(value);
        if (hexMatch.Success)
        {
            var r = byte.Parse(hexMatch.Groups["r"].Value, NumberStyles.HexNumber);
            var g = byte.Parse(hexMatch.Groups["g"].Value, NumberStyles.HexNumber);
            var b = byte.Parse(hexMatch.Groups["b"].Value, NumberStyles.HexNumber);
            var a = hexMatch.Groups["a"].Success
                ? byte.Parse(hexMatch.Groups["a"].Value, NumberStyles.HexNumber) / 255.0
                : 1.0;
            return (r, g, b, a);
        }

        var rgbMatch = RgbRegex.Match(value);
        if (rgbMatch.Success)
        {
            var r = ClampByte(int.Parse(rgbMatch.Groups["r"].Value));
            var g = ClampByte(int.Parse(rgbMatch.Groups["g"].Value));
            var b = ClampByte(int.Parse(rgbMatch.Groups["b"].Value));
            return (r, g, b, 1.0);
        }

        var rgbaMatch = RgbaRegex.Match(value);
        if (rgbaMatch.Success)
        {
            var r = ClampByte(int.Parse(rgbaMatch.Groups["r"].Value));
            var g = ClampByte(int.Parse(rgbaMatch.Groups["g"].Value));
            var b = ClampByte(int.Parse(rgbaMatch.Groups["b"].Value));
            var a = ClampDouble(double.Parse(rgbaMatch.Groups["a"].Value, CultureInfo.InvariantCulture));
            return (r, g, b, a);
        }

        return (0, 0, 0, 1);
    }

    /// <summary>Converts RGBA components to a hex string (with optional alpha).</summary>
    public static string ToHex(byte r, byte g, byte b, double a = 1.0)
    {
        if (Math.Abs(a - 1.0) < 0.001)
            return $"#{r:X2}{g:X2}{b:X2}";
        return $"#{r:X2}{g:X2}{b:X2}{(byte)Math.Round(a * 255):X2}";
    }

    /// <summary>Converts RGBA components to an RGB string.</summary>
    public static string ToRgb(byte r, byte g, byte b)
        => $"rgb({r}, {g}, {b})";

    /// <summary>Converts RGBA components to an RGBA string.</summary>
    public static string ToRgba(byte r, byte g, byte b, double a)
        => $"rgba({r}, {g}, {b}, {a.ToString("0.##", CultureInfo.InvariantCulture)})";

    /// <summary>Converts RGB to HSV (Hue 0-360, Saturation 0-1, Value 0-1).</summary>
    public static (double H, double S, double V) RgbToHsv(byte r, byte g, byte b)
    {
        var rd = r / 255.0;
        var gd = g / 255.0;
        var bd = b / 255.0;

        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var delta = max - min;

        double h;
        if (delta == 0) h = 0;
        else if (max == rd) h = (60 * ((gd - bd) / delta) + 360) % 360;
        else if (max == gd) h = (60 * ((bd - rd) / delta) + 120) % 360;
        else h = (60 * ((rd - gd) / delta) + 240) % 360;

        var s = max == 0 ? 0 : delta / max;
        var v = max;

        return (h, s, v);
    }

    /// <summary>Converts HSV to RGB.</summary>
    public static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60.0 % 2) - 1));
        var m = v - c;

        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        // Round, don't truncate: the floor made every converted channel land a unit low, so an
        // RGB → HSV → RGB round trip could not return to its input (a keyboard-stepped colour
        // drifted as the value echoed back through the component).
        return (ClampByte((int)Math.Round((r + m) * 255)), ClampByte((int)Math.Round((g + m) * 255)), ClampByte((int)Math.Round((b + m) * 255)));
    }

    private static byte ClampByte(int value)
        => (byte)Math.Clamp(value, 0, 255);

    private static double ClampDouble(double value)
        => Math.Clamp(value, 0.0, 1.0);
}
