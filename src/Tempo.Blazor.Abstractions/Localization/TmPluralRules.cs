using System.Globalization;

namespace Tempo.Blazor.Localization;

/// <summary>Selects the plural category of a number for a culture (a subset of CLDR).</summary>
public static class TmPluralRules
{
    /// <summary>The category of <paramref name="count"/> in <paramref name="culture"/>.</summary>
    /// <param name="culture">Culture whose language rules apply; a region (<c>cs-CZ</c>) uses its language.</param>
    /// <param name="count">The number being counted; fractions are supported.</param>
    public static TmPluralCategory Select(CultureInfo culture, double count)
        => throw new NotImplementedException();
}