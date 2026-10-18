using System.Globalization;
using System.Text;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// Case- and diacritic-insensitive "contains" for the diagram pickers (template gallery, connect
/// dialog, toolbox): "sablona" matches "Šablona". Mirrors Tempo.Blazor's internal AccentInsensitiveText.
/// </summary>
internal static class DiagramSearchText
{
    /// <summary>True when any non-empty candidate contains <paramref name="query"/>; an empty query matches.</summary>
    public static bool MatchesAny(string query, params string?[] candidates)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var needle = Fold(query.Trim());
        foreach (var candidate in candidates)
            if (!string.IsNullOrEmpty(candidate) && Fold(candidate).Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>Strips combining marks (FormD) — "Ústí" → "Usti".</summary>
    public static string Fold(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
