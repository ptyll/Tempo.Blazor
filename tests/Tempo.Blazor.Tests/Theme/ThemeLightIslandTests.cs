using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// A <c>data-theme="light"</c> island inside a dark page (TmGantt <c>Theme = Light</c> on a dark ancestor).
/// The core used to define <c>:root</c> and <c>[data-theme="dark"]</c> only, so a light region nested in a
/// dark tree inherited the dark tokens: the attribute was dead. <c>tokens-dark.css</c> now carries the
/// mirror block, declaring EXACTLY the properties the dark block declares (otherwise a token the dark
/// block flips would leak into the island), and <c>theme-indigo.css</c> repeats the indigo light scale for
/// an island nested in an indigo root.
/// </summary>
public sealed class ThemeLightIslandTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex RuleBlock =
        new(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex Property =
        new(@"(?<name>(?:--[\w-]+|color-scheme))\s*:\s*(?<value>[^;]+);", RegexOptions.Compiled, RegexTimeout);

    private const string LightSwitch = "[data-theme=\"light\"]";
    private const string DarkSwitch = "[data-theme=\"dark\"]";

    /// <summary>The declarations (custom properties plus <c>color-scheme</c>) of the first rule listing the selector part.</summary>
    private static Dictionary<string, string> Block(string file, string selectorPart)
    {
        var css = ThemeCss.StripComments(File.ReadAllText(ThemeCss.CssPath(file)));
        foreach (Match rule in RuleBlock.Matches(css))
        {
            if (!ThemeCss.SelectorParts(rule.Groups["selector"].Value).Contains(selectorPart, StringComparer.Ordinal))
            {
                continue;
            }

            var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match property in Property.Matches(rule.Groups["body"].Value))
            {
                declarations[property.Groups["name"].Value] = ThemeCss.Normalise(property.Groups["value"].Value);
            }

            return declarations;
        }

        throw new InvalidOperationException($"{file} has no rule listing '{selectorPart}'.");
    }

    [Fact]
    public void LightBlock_DeclaresExactlyTheDarkBlocksPropertyNames()
    {
        var dark = Block("tokens-dark.css", DarkSwitch);
        var light = Block("tokens-dark.css", LightSwitch);

        dark.Should().HaveCountGreaterThan(100, "the sweep has to have read the dark block");
        light.Keys.Should().BeEquivalentTo(dark.Keys,
            "a light island must reset every property the dark block flips - a missing one leaks the dark value into it");
    }

    [Fact]
    public void LightBlock_FollowsTheDarkBlock_AndTheRootSelectorIsUntouched()
    {
        var css = ThemeCss.StripComments(File.ReadAllText(ThemeCss.CssPath("tokens-dark.css")));

        css.IndexOf(LightSwitch, StringComparison.Ordinal).Should().BeGreaterThan(
            css.IndexOf(DarkSwitch, StringComparison.Ordinal),
            "the light block comes right after the dark one");

        var tokens = ThemeCss.StripComments(File.ReadAllText(ThemeCss.CssPath("tokens.css")));
        tokens.Should().MatchRegex(@"(?m)^:root\s*\{", "the light defaults stay on :root");
        tokens.Should().NotContain(LightSwitch, "the :root selector is not widened - the island lives in tokens-dark.css");
    }

    [Fact]
    public void LightBlock_ValuesAreTheLightRootValues()
    {
        var light = Block("tokens-dark.css", LightSwitch);
        var root = Block("tokens.css", ":root");

        foreach (var (name, value) in light)
        {
            root.Should().ContainKey(name, "the island mirrors a :root declaration");
            value.Should().Be(root[name], "{0} must equal the light :root value", name);
        }

        light["color-scheme"].Should().Be("light");
    }

    [Fact]
    public void IndigoNestedLightSelector_JoinsTheIndigoLightScale()
    {
        // The nested light island sits in the same rule as the indigo light scale (one declaration list, two
        // selectors), the mirror of the nested dark selectors that share the dark scale.
        const string sameElement = ":root[data-tm-theme=\"indigo\"]";
        const string nestedLight = ":root[data-tm-theme=\"indigo\"] [data-theme=\"light\"]";
        const string nestedDark = ":root[data-tm-theme=\"indigo\"] [data-theme=\"dark\"]";

        var indigoLight = Block("theme-indigo.css", nestedLight);
        var rootLight = Block("theme-indigo.css", sameElement);

        indigoLight.Should().ContainKey("--tm-color-primary-500", "an island inside the indigo root re-declares the whole indigo light scale");
        indigoLight["--tm-color-primary-500-rgb"].Should().Be("99, 102, 241");
        indigoLight.Keys.Should().BeEquivalentTo(rootLight.Keys);

        var css = ThemeCss.StripComments(File.ReadAllText(ThemeCss.CssPath("theme-indigo.css")));
        css.Should().Contain(nestedDark, "the nested dark selector stays");
        ThemeCss.SelectorParts(css[..css.IndexOf('{', StringComparison.Ordinal)])
            .Should().Contain(nestedLight);
    }
    [Fact]
    public void FlatReaders_IgnoreTheLightIsland()
    {
        // ThemeCss.Declarations is "later declaration wins" over the whole file: the island, which comes
        // after the dark block, must not turn the dark graph light again.
        var dark = ThemeCss.Declarations(ThemeCss.CssPath("tokens-dark.css"));

        dark["--tm-bg-surface"].Should().Be("#1e293b");
        dark["--tm-text-primary"].Should().Be("#f1f5f9");
        ThemeCss.Declarations(ThemeCss.CssPath("theme-indigo.css"))["--tm-color-primary-500-rgb"]
            .Should().Be("129, 140, 248", "the dark indigo channels are the last word of the flat reader");
    }
}
