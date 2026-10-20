using System.Text;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// Encodes the output of a template expression so a value can never open or close markup. Unlike
/// <c>System.Net.WebUtility.HtmlEncode</c> it leaves non-ASCII text (Czech, emoji, surrogate
/// pairs) untouched — the value is escaped, not transliterated into entities.
/// </summary>
internal static class HtmlTextEscaper
{
    /// <summary>Encodes <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>"</c> and <c>'</c>.</summary>
    public static string Encode(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        StringBuilder? builder = null;
        for (var i = 0; i < value.Length; i++)
        {
            var replacement = value[i] switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => null,
            };

            if (replacement is null)
            {
                builder?.Append(value[i]);
                continue;
            }

            builder ??= CreateBuilder(value, i);
            builder.Append(replacement);
        }

        return builder?.ToString() ?? value;
    }

    private static StringBuilder CreateBuilder(string value, int upTo)
    {
        var builder = new StringBuilder(value.Length + 16);
        builder.Append(value, 0, upTo);
        return builder;
    }
}
