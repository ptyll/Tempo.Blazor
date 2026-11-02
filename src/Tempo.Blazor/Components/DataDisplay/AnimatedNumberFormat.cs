using System.Globalization;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// The <c>Intl.NumberFormat</c> shape of one .NET numeric format string — what
/// <c>tm-animated-number.js</c> needs to paint the intermediate frames of an animated number
/// (plan ff043b2e, phase 1). <see cref="Describe"/> is a pure function: it never touches the DOM,
/// the current culture or the JavaScript runtime, so every branch is unit-testable.
/// </summary>
/// <remarks>
/// <para>
/// The animation paints frames in JavaScript, which cannot evaluate a .NET format string. The
/// descriptor therefore mirrors the formats a ticker is realistically given — the standard
/// <c>N</c>/<c>F</c>/<c>P</c>/<c>C</c> families and the custom <c>0</c>/<c>#</c> shapes — and
/// falls back to whole numbers for anything else (<c>E</c>, <c>G</c>, <c>X</c>, a malformed
/// string). That fallback is deliberate and harmless: the FINAL frame is written by Blazor through
/// <c>double.ToString(format, culture)</c>, so the number that stays on screen is always the exact
/// .NET result, and only the frames in flight may show a plainer shape.
/// </para>
/// <para>
/// <c>P</c> maps to <c>style: "percent"</c> because .NET and <c>Intl</c> agree on the ×100 scaling
/// and on the culture's percent pattern, so a percentage ticker lands on the same string Blazor
/// writes in the last frame. <c>C</c> keeps the culture's currency decimal digits but the decimal
/// style: <c>Intl</c> needs an ISO 4217 code while <see cref="NumberFormatInfo"/> only carries the
/// symbol, and guessing the code would be worse than a plain tick.
/// </para>
/// </remarks>
internal readonly record struct AnimatedNumberFormat(
    string Locale,
    int MinimumFractionDigits,
    int MaximumFractionDigits,
    string Style)
{
    /// <summary>The style used for every format the descriptor can mirror as a plain number.</summary>
    internal const string DecimalStyle = "decimal";

    /// <summary>The style used for <c>P</c>/<c>p</c> formats (×100 on both sides).</summary>
    internal const string PercentStyle = "percent";

    /// <summary>
    /// Translates a .NET numeric format string into the descriptor for <paramref name="culture"/>.
    /// A null, empty or whitespace format means the component's default (<c>N0</c>), which is what
    /// makes an unconfigured ticker show whole, group-separated numbers.
    /// </summary>
    /// <param name="format">The .NET numeric format string (standard or custom).</param>
    /// <param name="culture">The culture whose separators and digits the final text uses.</param>
    internal static AnimatedNumberFormat Describe(string? format, CultureInfo culture)
    {
        var locale = string.IsNullOrEmpty(culture.Name) ? "en-US" : culture.Name;
        var shape = string.IsNullOrWhiteSpace(format) ? "N0" : format!.Trim();
        var digits = FractionDigits(shape, culture);

        var style = shape.Length >= 1 && (shape[0] is 'P' or 'p')
            ? PercentStyle
            : DecimalStyle;

        return new AnimatedNumberFormat(locale, digits, digits, style);
    }

    /// <summary>
    /// The fraction digits a format string asks for. Standard formats carry them as a trailing
    /// number (<c>N2</c>, <c>P1</c>); a standard format without one keeps .NET's default of two,
    /// except for the integer families (<c>D</c>/<c>X</c>) which have none. A custom format is
    /// counted by its zero/hash placeholders after the decimal separator.
    /// </summary>
    private static int FractionDigits(string format, CultureInfo culture)
    {
        if (format.Length >= 1 && format[0] is 'N' or 'n' or 'F' or 'f' or 'P' or 'p' or 'C' or 'c')
        {
            if (format.Length > 1 && int.TryParse(format[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var explicitDigits))
            {
                return Math.Clamp(explicitDigits, 0, 15);
            }

            return format[0] is 'C' or 'c'
                ? Math.Clamp(culture.NumberFormat.CurrencyDecimalDigits, 0, 15)
                : 2;
        }

        if (format.Length >= 1 && format[0] is 'D' or 'd' or 'X' or 'x' or 'E' or 'e' or 'G' or 'g' or 'R' or 'r')
        {
            return 0;
        }

        var separator = format.IndexOf('.');
        if (separator < 0)
        {
            return 0;
        }

        var digits = 0;
        for (var i = separator + 1; i < format.Length; i++)
        {
            if (format[i] is '0' or '#')
            {
                digits++;
                continue;
            }

            // The fraction section ends at the first character that is not a digit placeholder
            // (a literal, a group separator or an exponent marker).
            break;
        }

        return Math.Clamp(digits, 0, 15);
    }
}
