using System.Globalization;

namespace Tempo.Blazor.Localization;

/// <summary>
/// Selects the plural category of a number for a culture — the part of the Unicode CLDR plural rules
/// Tempo.Blazor ships strings for. A region (<c>cs-CZ</c>) uses its language's rules; a language
/// without an entry uses the English rules (one / other), which is also what the neutral resources
/// are written for.
/// <list type="table">
///   <listheader><term>Language</term><description>Rule</description></listheader>
///   <item><term>cs, sk</term><description>1 → One; 2–4 → Few; any fraction → Many; else Other</description></item>
///   <item><term>pl</term><description>1 → One; 2–4 (not 12–14) → Few; other integers → Many; fractions → Other</description></item>
///   <item><term>fr</term><description>0 ≤ n &lt; 2 → One; whole millions → Many; else Other</description></item>
///   <item><term>en and everything else</term><description>1 → One; else Other</description></item>
/// </list>
/// </summary>
public static class TmPluralRules
{
    private const double MaxExactInteger = 9_007_199_254_740_992d;   // 2^53: beyond this a double has no fraction digits

    /// <summary>The category of <paramref name="count"/> in <paramref name="culture"/>.</summary>
    /// <param name="culture">Culture whose language rules apply; a region (<c>cs-CZ</c>) uses its language.</param>
    /// <param name="count">The number being counted; fractions are supported, the sign is ignored.</param>
    /// <returns>The category; <see cref="TmPluralCategory.Other"/> for NaN and infinity.</returns>
    public static TmPluralCategory Select(CultureInfo culture, double count)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (double.IsNaN(count) || double.IsInfinity(count))
        {
            return TmPluralCategory.Other;
        }

        var n = Math.Abs(count);
        var isInteger = n >= MaxExactInteger || n == Math.Floor(n);
        var integer = n >= MaxExactInteger ? long.MaxValue : (long)Math.Floor(n);

        return culture.TwoLetterISOLanguageName switch
        {
            "cs" or "sk" => Slavic(isInteger, integer),
            "pl" => Polish(isInteger, integer),
            "fr" => French(isInteger, integer, n),
            _ => isInteger && integer == 1 ? TmPluralCategory.One : TmPluralCategory.Other,
        };
    }

    /// <summary>Czech and Slovak: one (1), few (2–4), many (fractions), other.</summary>
    private static TmPluralCategory Slavic(bool isInteger, long integer)
    {
        if (!isInteger)
        {
            return TmPluralCategory.Many;
        }

        return integer switch
        {
            1 => TmPluralCategory.One,
            >= 2 and <= 4 => TmPluralCategory.Few,
            _ => TmPluralCategory.Other,
        };
    }

    /// <summary>Polish: one (1), few (ends 2–4 but not 12–14), many (other integers), other (fractions).</summary>
    private static TmPluralCategory Polish(bool isInteger, long integer)
    {
        if (!isInteger)
        {
            return TmPluralCategory.Other;
        }

        if (integer == 1)
        {
            return TmPluralCategory.One;
        }

        var lastDigit = integer % 10;
        var lastTwo = integer % 100;
        return lastDigit is >= 2 and <= 4 && lastTwo is < 12 or > 14
            ? TmPluralCategory.Few
            : TmPluralCategory.Many;
    }

    /// <summary>French: one (0 up to, not including, 2), many (whole millions), other.</summary>
    private static TmPluralCategory French(bool isInteger, long integer, double n)
    {
        if (n < 2)
        {
            return TmPluralCategory.One;
        }

        return isInteger && integer != 0 && integer % 1_000_000 == 0
            ? TmPluralCategory.Many
            : TmPluralCategory.Other;
    }
}