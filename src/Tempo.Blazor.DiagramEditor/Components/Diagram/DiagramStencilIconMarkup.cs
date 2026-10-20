using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// Icon markup for small stencil previews (toolbox, connect dialog). Text elements longer than two
/// characters (stereotypes, captions) are illegible at 32–40px and overflow the tile, so they are
/// removed; single-glyph markers ("T", "?", "$", "sd") are the icon itself and stay.
/// </summary>
internal static class DiagramStencilIconMarkup
{
    private static readonly Regex TextElement = new(
        @"<text\b[^>]*>(?<content>.*?)</text>",
        RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>The preview markup for <paramref name="iconSvg"/> (cached per immutable markup string).</summary>
    public static string ForPreview(string iconSvg) => Cache.GetOrAdd(iconSvg, static svg =>
    {
        try
        {
            return TextElement.Replace(svg, m =>
                System.Net.WebUtility.HtmlDecode(m.Groups["content"].Value).Trim().Length > 2 ? string.Empty : m.Value);
        }
        catch (RegexMatchTimeoutException)
        {
            return svg;
        }
    });
}
