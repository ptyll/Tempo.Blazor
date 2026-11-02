using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// The one CSS-colour grammar the data-display components accept for a host-supplied colour (the
/// <c>SubValueColor</c> of <see cref="TmStatCard"/> and the <c>Color</c> of a
/// <see cref="BreakdownPart"/>). Both write the value into an inline <c>style</c> attribute, so it
/// must BE a colour and never a declaration list.
/// </summary>
/// <remarks>
/// Before 2.9.0 <c>SubValueColor</c> reached the attribute verbatim: <c>text-green-600</c> produced a
/// dead declaration (silent colour loss) and <c>red; background:url(…)</c> injected extra
/// declarations. The grammar is deliberately narrower than CSS Color 4 — <c>hwb()</c>, <c>lab()</c>,
/// <c>lch()</c> and <c>oklab()</c> are NOT accepted. That is a deliberate, documented exclusion
/// for 2.9.0, not an oversight; extending the whitelist is a future breaking-surface
/// decision, not a bugfix (N144, review 2026-09-22). The grammar moved here from
/// <see cref="TmStatCard"/> in 2.10.0 so a breakdown part's colour is validated by the same rules.
/// </remarks>
internal static class CssColorValue
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    // Accepted functions are var()/hex/rgb()/rgba()/hsl()/hsla()/oklch()/color-mix() plus the named
    // colours below.
    private static readonly Regex FunctionalColor = new(
        @"^(?:var|rgba?|hsla?|oklch|color-mix)\(.*\)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline,
        RegexTimeout);

    private static readonly Regex VarReference = new(
        @"^var\(\s*--[A-Za-z_][\w-]*",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex HexColor = new(
        @"^#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$",
        RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>
    /// CSS Color Module Level 4 named colours plus the two colour keywords that are not names
    /// (<c>currentcolor</c>, <c>transparent</c>). Case-insensitive, as CSS keywords are.
    /// </summary>
    private static readonly FrozenSet<string> NamedColors = new[]
    {
        "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige", "bisque", "black",
        "blanchedalmond", "blue", "blueviolet", "brown", "burlywood", "cadetblue", "chartreuse",
        "chocolate", "coral", "cornflowerblue", "cornsilk", "crimson", "cyan", "darkblue",
        "darkcyan", "darkgoldenrod", "darkgray", "darkgreen", "darkgrey", "darkkhaki",
        "darkmagenta", "darkolivegreen", "darkorange", "darkorchid", "darkred", "darksalmon",
        "darkseagreen", "darkslateblue", "darkslategray", "darkslategrey", "darkturquoise",
        "darkviolet", "deeppink", "deepskyblue", "dimgray", "dimgrey", "dodgerblue", "firebrick",
        "floralwhite", "forestgreen", "fuchsia", "gainsboro", "ghostwhite", "gold", "goldenrod",
        "gray", "green", "greenyellow", "grey", "honeydew", "hotpink", "indianred", "indigo",
        "ivory", "khaki", "lavender", "lavenderblush", "lawngreen", "lemonchiffon", "lightblue",
        "lightcoral", "lightcyan", "lightgoldenrodyellow", "lightgray", "lightgreen", "lightgrey",
        "lightpink", "lightsalmon", "lightseagreen", "lightskyblue", "lightslategray",
        "lightslategrey", "lightsteelblue", "lightyellow", "lime", "limegreen", "linen",
        "magenta", "maroon", "mediumaquamarine", "mediumblue", "mediumorchid", "mediumpurple",
        "mediumseagreen", "mediumslateblue", "mediumspringgreen", "mediumturquoise",
        "mediumvioletred", "midnightblue", "mintcream", "mistyrose", "moccasin", "navajowhite",
        "navy", "oldlace", "olive", "olivedrab", "orange", "orangered", "orchid",
        "palegoldenrod", "palegreen", "paleturquoise", "palevioletred", "papayawhip",
        "peachpuff", "peru", "pink", "plum", "powderblue", "purple", "rebeccapurple", "red",
        "rosybrown", "royalblue", "saddlebrown", "salmon", "sandybrown", "seagreen", "seashell",
        "sienna", "silver", "skyblue", "slateblue", "slategray", "slategrey", "snow",
        "springgreen", "steelblue", "tan", "teal", "thistle", "tomato", "turquoise", "violet",
        "wheat", "white", "whitesmoke", "yellow", "yellowgreen",
        "currentcolor", "transparent"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="value"/> is a CSS <c>&lt;color&gt;</c> the components may write into
    /// the inline style. Hard rejects (<c>;</c>, <c>{</c>, <c>}</c>, <c>url(</c>,
    /// <c>expression(</c>) run first so no function shape can smuggle a declaration splitter, then
    /// the value must be a named colour, a hex literal, or one of the whitelisted colour functions
    /// with balanced parentheses — <c>var(</c> additionally has to reference a custom property
    /// (<c>--name</c>).
    /// </summary>
    /// <param name="value">The candidate colour value.</param>
    internal static bool IsValid(string value)
    {
        var v = value.Trim();
        if (v.Length == 0
            || v.IndexOfAny([';', '{', '}']) >= 0
            || v.Contains("url(", StringComparison.OrdinalIgnoreCase)
            || v.Contains("expression(", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (NamedColors.Contains(v) || HexColor.IsMatch(v))
        {
            return true;
        }

        if (!FunctionalColor.IsMatch(v) || !BalancedParens(v))
        {
            return false;
        }

        // var( is the only whitelisted function that is not intrinsically a colour — it is one
        // only because the token it resolves to is declared as a colour, so it must at least name a
        // custom property.
        return !v.StartsWith("var", StringComparison.OrdinalIgnoreCase) || VarReference.IsMatch(v);
    }

    private static bool BalancedParens(string value)
    {
        var depth = 0;
        foreach (var ch in value)
        {
            depth += ch switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth < 0)
            {
                return false;
            }
        }

        return depth == 0;
    }
}
