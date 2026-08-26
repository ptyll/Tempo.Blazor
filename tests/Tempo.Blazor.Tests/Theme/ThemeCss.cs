using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Reads the shipped stylesheets and resolves colours through the token graph, so a contrast guard can be
/// computed from what the CSS ACTUALLY declares instead of from numbers copied into a test.
/// <para>
/// It lives on its own because three guards need it (selection controls, ink on the primary fill, the
/// documented token table) and a copy per guard is how the same <c>var()</c> resolver drifts into three
/// slightly different resolvers.
/// </para>
/// <para>
/// Selector matching is EXACT on a comma-separated part. A "contains" match silently binds to whichever
/// rule happens to come first and share a fragment — <c>.tm-multiselect__option-checkbox--checked</c> is a
/// substring of the dark override for the same class, so a fragment matcher was measuring the right value
/// only by ordering luck.
/// </para>
/// </summary>
internal static class ThemeCss
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex Declaration =
        new(@"(?<name>--tm-[\w-]+)\s*:\s*(?<value>[^;{}]+);", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex RuleBlock =
        new(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex Comment =
        new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled, RegexTimeout);

    public static DirectoryInfo RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }

            current = current.Parent!;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }

    public static string CssPath(params string[] parts) =>
        Path.Combine(new[] { RepositoryRoot().FullName, "src", "Tempo.Blazor", "wwwroot", "css" }
            .Concat(parts).ToArray());

    /// <summary>Comments would otherwise leak into a selector or swallow a declaration (they contain colons).</summary>
    public static string StripComments(string css) => Comment.Replace(css, " ");

    public static string ComponentCss(string file) =>
        StripComments(File.ReadAllText(CssPath("components", file)));

    /// <summary>
    /// The stylesheet a consumer actually links, comments stripped. It is the only text in which the
    /// ORDER of two rules coming from two different component files is visible, so every guard about
    /// "which of two equally specific rules wins" has to read this and not the sources.
    /// </summary>
    public static string BundledCss() =>
        StripComments(File.ReadAllText(CssPath("tempo-blazor.bundled.css")));

    public static string Normalise(string text) => Whitespace.Replace(text, " ").Trim();

    /// <summary>All <c>--tm-*</c> declarations of a file, later declarations winning.</summary>
    public static Dictionary<string, string> Declarations(string path)
    {
        var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Declaration.Matches(StripComments(File.ReadAllText(path))))
        {
            declarations[match.Groups["name"].Value] = Normalise(match.Groups["value"].Value);
        }

        return declarations;
    }

    /// <summary>The token graph of a theme: the light tokens with the dark overrides layered on top.</summary>
    public static Dictionary<string, string> TokenGraph(bool dark)
    {
        var tokens = Declarations(CssPath("tokens.css"));
        if (dark)
        {
            foreach (var (name, value) in Declarations(CssPath("tokens-dark.css")))
            {
                tokens[name] = value;
            }
        }

        return tokens;
    }

    /// <summary>The first <c>var(…)</c> in a value, with nested fallbacks kept intact.</summary>
    public static string? FirstVar(string value)
    {
        var start = value.IndexOf("var(", StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        for (var i = start + 3; i < value.Length; i++)
        {
            if (value[i] == '(')
            {
                depth++;
            }
            else if (value[i] == ')' && --depth == 0)
            {
                return value[start..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced var() in '{value}'.");
    }

    /// <summary>Resolves a CSS value (literal or a <c>var()</c> chain, fallbacks included) to #rrggbb.</summary>
    public static string ResolveColour(string value, Dictionary<string, string> tokens, int depth = 0)
    {
        depth.Should().BeLessThan(16, "a var() chain must not be cyclic");
        value = value.Trim();

        var reference = FirstVar(value);
        if (reference is null)
        {
            return value;
        }

        var inner = reference[4..^1];
        var (name, fallback) = SplitOnTopLevelComma(inner);
        name = name.Trim();

        if (tokens.TryGetValue(name, out var referenced))
        {
            return ResolveColour(referenced, tokens, depth + 1);
        }

        fallback.Should().NotBeNull($"token {name} must be declared (or carry a fallback)");
        return ResolveColour(fallback!, tokens, depth + 1);
    }

    public static (string Name, string? Fallback) SplitOnTopLevelComma(string inner)
    {
        var depth = 0;
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '(')
            {
                depth++;
            }
            else if (inner[i] == ')')
            {
                depth--;
            }
            else if (inner[i] == ',' && depth == 0)
            {
                return (inner[..i], inner[(i + 1)..]);
            }
        }

        return (inner, null);
    }

    /// <summary>
    /// Every rule of a component stylesheet, in source order, as its selector list and its declaration
    /// body.
    /// <para>
    /// Exposed next to <see cref="TryProperty"/> because the two answer different questions and only one
    /// of them can answer "is there a rule I did not expect": a lookup is given the selector it is
    /// looking for, so it can report FOUND or NOT FOUND and nothing else — never "a second rule declares
    /// this too". A guard about which rules EXIST has to sweep them.
    /// </para>
    /// </summary>
    public static IEnumerable<(string Selector, string Body)> Rules(string stylesheet) =>
        RuleBlock.Matches(ComponentCss(stylesheet))
            .Select(rule => (Normalise(rule.Groups["selector"].Value), rule.Groups["body"].Value));

    /// <summary>Whether a declaration body sets <paramref name="property"/> itself (not as a shorthand).</summary>
    public static bool Declares(string body, string property) =>
        body.Split(';').Any(declaration =>
        {
            var separator = declaration.IndexOf(':', StringComparison.Ordinal);
            return separator > 0 &&
                   string.Equals(declaration[..separator].Trim(), property, StringComparison.Ordinal);
        });

    /// <summary>The comma-separated parts of a selector list, whitespace-normalised.</summary>
    public static IReadOnlyList<string> SelectorParts(string selector) =>
        Normalise(selector).Split(',').Select(part => part.Trim()).Where(part => part.Length > 0).ToList();

    /// <summary>
    /// The value a property has in the first rule that lists <paramref name="selector"/> as one of its
    /// comma-separated parts, compared EXACTLY.
    /// </summary>
    public static string Property(string stylesheet, string selector, string property) =>
        TryProperty(stylesheet, selector, property)
        ?? throw new InvalidOperationException(
            $"{stylesheet} has no rule with the exact selector '{selector}' declaring '{property}'.");

    public static string? TryProperty(string stylesheet, string selector, string property)
    {
        foreach (Match rule in RuleBlock.Matches(ComponentCss(stylesheet)))
        {
            if (!SelectorParts(rule.Groups["selector"].Value).Contains(selector, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (var declaration in rule.Groups["body"].Value.Split(';'))
            {
                var separator = declaration.IndexOf(':', StringComparison.Ordinal);
                if (separator < 0)
                {
                    continue;
                }

                if (string.Equals(declaration[..separator].Trim(), property, StringComparison.Ordinal))
                {
                    return Normalise(declaration[(separator + 1)..]);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The opaque colour a viewer sees when <paramref name="foreground"/> is painted at
    /// <paramref name="alpha"/> over <paramref name="background"/> — the ONE way this suite composites,
    /// so two probes can no longer disagree by compositing differently.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fáze 14 left two numbers for the sort indicator that looked like a methodological dispute — the
    /// UX review read 1,91:1 light and 2,95:1 dark, the application probe read 2,52:1 and 3,58:1 — and
    /// the register recorded the difference as "probably the reference background". It was not. Feeding
    /// this one function the four recorded pixel values reproduces BOTH pairs against the SAME
    /// background: the review measured the UNSORTED icon, the probe measured the SORTED one. Same
    /// method, different question. <c>SortIndicatorContrastTests</c> pins that arithmetic.
    /// </para>
    /// <para>
    /// Nested <c>opacity</c> multiplies: an <c>::after</c> at <c>opacity: 1</c> inside a box at
    /// <c>opacity: .4</c> paints at 0.4, not at 1. That is why an <c>opacity: 1</c> on the pseudo-element
    /// changed nothing and why alpha, not colour, has to be passed in here explicitly.
    /// </para>
    /// </remarks>
    public static string Composite(string foreground, double alpha, string background)
    {
        alpha.Should().BeInRange(0, 1, "alpha je podíl, ne procento");

        var over = Channels(foreground);
        var under = Channels(background);
        var mixed = Enumerable.Range(0, 3)
            .Select(i => (int)Math.Round((alpha * over[i]) + ((1 - alpha) * under[i]), MidpointRounding.AwayFromZero))
            .Select(value => Math.Clamp(value, 0, 255));

        return "#" + string.Concat(mixed.Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static int[] Channels(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        hex.Length.Should().Be(6, $"'{hex}' must resolve to an opaque #rrggbb colour");
        return Enumerable.Range(0, 3)
            .Select(i => int.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToArray();
    }

    /// <summary>WCAG relative luminance of an opaque <c>#rrggbb</c> colour.</summary>
    public static double Luminance(string hex)
    {
        static double Channel(double value)
        {
            value /= 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        hex = hex.Trim().TrimStart('#');
        hex.Length.Should().Be(6, $"'{hex}' must resolve to an opaque #rrggbb colour");
        var channels = Enumerable.Range(0, 3)
            .Select(i => Channel(int.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture)))
            .ToArray();
        return (0.2126 * channels[0]) + (0.7152 * channels[1]) + (0.0722 * channels[2]);
    }

    public static double Contrast(string foreground, string background)
    {
        var first = Luminance(foreground);
        var second = Luminance(background);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    /// <summary>Relative luminance of a CSS value resolved through the token graph of one theme.</summary>
    public static double LuminanceOf(string value, bool dark) =>
        Luminance(ResolveColour(value, TokenGraph(dark)));

    /// <summary>Contrast between two CSS values, both resolved through the token graph of one theme.</summary>
    public static double Ratio(string foregroundValue, string backgroundValue, bool dark)
    {
        var tokens = TokenGraph(dark);
        return Contrast(ResolveColour(foregroundValue, tokens), ResolveColour(backgroundValue, tokens));
    }
}
