using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// Validation half of <see cref="TmStatCard"/>: <see cref="TmStatCard.SubValueColor"/> is written
/// verbatim into an inline <c>style</c>, so before 2.9.0 any string reached the attribute —
/// <c>text-green-600</c> produced a dead declaration (the silent colour loss the changelog now
/// owns) and <c>red; background:url(…)</c> injected extra declarations. Since 2.9.0 the value
/// must BE a CSS colour: a <c>var(--…)</c> reference, a hex literal, an
/// <c>rgb/hsl/oklch/color-mix</c> function, or a named colour. Anything else emits no
/// <c>style</c> and logs a warning through the optionally injected <see cref="ILoggerFactory"/>.
/// </summary>
public partial class TmStatCard
{
    /// <summary>
    /// Optional logging: resolved through <see cref="IServiceProvider"/> so a consumer without
    /// logging registered simply gets no warning — never a missing-service exception.
    /// </summary>
    [Inject] private IServiceProvider Services { get; set; } = default!;

    private string? _validatedSubValueColor;
    private ILogger? _logger;
    private bool _loggerResolved;

    /// <summary>
    /// Returns <see cref="SubValueColor"/> when it is a CSS colour, <see langword="null"/> after
    /// logging a warning otherwise. Called from <see cref="OnParametersSet"/> so a value turned
    /// invalid on re-render drops the style on that render, not the next.
    /// </summary>
    private string? ValidateSubValueColor()
    {
        if (string.IsNullOrEmpty(SubValueColor) || IsValidCssColor(SubValueColor))
        {
            return SubValueColor;
        }

        if (!_loggerResolved)
        {
            _loggerResolved = true;
            _logger = (Services.GetService(typeof(ILoggerFactory)) as ILoggerFactory)
                ?.CreateLogger<TmStatCard>();
        }

        _logger?.LogWarning(
            "TmStatCard: SubValueColor=\"{Value}\" is not a CSS color — no style emitted " +
            "(Title=\"{Title}\"). SubValueColor expects a CSS color (2.9.0 breaking change: " +
            "CSS class names are no longer accepted).",
            SubValueColor,
            Title);
        return null;
    }

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

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
    /// Whether <paramref name="value"/> is a CSS <c>&lt;color&gt;</c> the component may write into
    /// the inline style. The grammar is deliberately narrower than the full CSS syntax: hard
    /// rejects (<c>;</c>, <c>{</c>, <c>}</c>, <c>url(</c>, <c>expression(</c>) run first so no
    /// function shape can smuggle a declaration splitter, then the value must be a named colour,
    /// a hex literal, or one of the whitelisted colour functions with balanced parentheses —
    /// <c>var(</c> additionally has to reference a custom property (<c>--name</c>).
    /// </summary>
    internal static bool IsValidCssColor(string value)
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
        // only because the token it resolves to is declared as a colour, so it must at least
        // name a custom property.
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
