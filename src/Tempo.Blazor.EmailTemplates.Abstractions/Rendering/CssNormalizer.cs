using System.Globalization;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// CSS canonicalisation used before a URL inside CSS is judged (§3.7a). A blocked scheme can be hidden
/// behind a comment (<c>java/**/script:</c>) or a CSS escape (<c>u\72l(javascript:…)</c>), so the guard
/// looks at a decoded copy of the text — never at the raw value alone.
/// </summary>
internal static partial class CssNormalizer
{
    /// <summary>
    /// Returns the text with comments removed and CSS escapes decoded: <c>\72</c> (1–6 hex digits plus an
    /// optional whitespace terminator) and <c>\.</c> (a backslash-escaped literal character).
    /// </summary>
    public static string Normalize(string css)
    {
        if (string.IsNullOrEmpty(css) || css.IndexOf('\\') < 0 && css.IndexOf("/*", StringComparison.Ordinal) < 0)
        {
            return css;
        }

        var withoutComments = CssCommentPattern().Replace(css, " ");
        return CssEscapePattern().Replace(withoutComments, DecodeEscape);
    }

    private static string DecodeEscape(Match match)
    {
        var hex = match.Groups["hex"].Value;
        if (hex.Length == 0)
        {
            var literal = match.Groups["char"].Value;
            return literal.Length == 0 ? string.Empty : literal;
        }

        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint) ||
            codePoint is 0 or > 0x10FFFF)
        {
            return "\uFFFD";
        }

        return char.ConvertFromUtf32(codePoint);
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex CssCommentPattern();

    /// <summary>A CSS escape: hex form with an optional terminating space, or a backslash-escaped char.</summary>
    [GeneratedRegex(@"\\(?:(?<hex>[0-9a-fA-F]{1,6})[ \t\r\n]?|(?<char>[^0-9a-fA-F]))", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex CssEscapePattern();
}
