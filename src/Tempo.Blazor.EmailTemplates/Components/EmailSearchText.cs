using System.Globalization;
using System.Text;

namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>
/// The one diacritics- and case-insensitive text fold the editor's search boxes share (the block palette and
/// the variable picker). A Czech user types <c>tlacitko</c> or <c>krestni</c> without diacritics; folding on
/// BOTH sides is what makes that find „Tlačítko“ and „Křestní jméno“.
/// </summary>
internal static class EmailSearchText
{
    /// <summary>Folds <paramref name="text"/> to lower case without diacritics (canonical composed form).</summary>
    /// <param name="text">The text to fold (maybe empty).</param>
    /// <returns>The folded text; an empty input returns an empty string.</returns>
    internal static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Whether the folded <paramref name="haystack"/> contains the folded <paramref name="needle"/>; an empty
    /// needle matches everything.
    /// </summary>
    /// <param name="haystack">The localized text to search in.</param>
    /// <param name="needle">What the user typed.</param>
    internal static bool Contains(string? haystack, string? needle)
        => string.IsNullOrWhiteSpace(needle)
            || Fold(haystack).Contains(Fold(needle), StringComparison.Ordinal);
}
