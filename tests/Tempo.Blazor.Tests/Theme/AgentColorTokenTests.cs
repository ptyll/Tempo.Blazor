using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan d67b33af, Fáze 1 — the agent colour palette: <c>--tm-agent-color-0</c> … <c>--tm-agent-color-7</c> plus
/// the ink painted on them, <c>--tm-agent-color-ink</c>.
/// <para>
/// Every agent visualization (lanes headers, graph nodes, fan-out tiles, the presence cursors) paints an agent
/// with this one family, and the palette carries an accessibility obligation that a component test cannot see:
/// the initials or the icon on an agent's coloured avatar must reach WCAG AA (4.5:1) against the fill, in BOTH
/// themes. A light-only token family would keep the light 700-tones under the dark ink and put dark on dark — the
/// exact defect <c>TokenContrastTests</c> exists for. The numbers below are measured through the real token graph
/// (<see cref="ThemeCss.TokenGraph"/>), never copied.
/// </para>
/// </summary>
public sealed class AgentColorTokenTests
{
    private const string Ink = "--tm-agent-color-ink";

    /// <summary>The eight slots the plan fixes. A ninth (or a hole) would break `ColorIndex % 8`.</summary>
    private static readonly string[] Slots = Enumerable.Range(0, 8).Select(index => $"--tm-agent-color-{index}").ToArray();

    [Fact]
    public void TheLightTheme_DeclaresEveryPaletteSlot()
    {
        var light = ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"));

        foreach (var slot in Slots)
        {
            light.Should().ContainKey(slot, "the badge reads {0} for the agent whose ColorIndex points at it", slot);
        }

        light.Should().ContainKey(Ink, "the initials need a declared ink — a currentColor would follow the page");
        light.Keys.Should().NotContain("--tm-agent-color-8", "the palette is exactly eight slots");
    }

    [Fact]
    public void TheDarkTheme_RepeatsTheFamily_DeclaredByItsOwnBlock()
    {
        var dark = ThemeCss.Declarations(ThemeCss.CssPath("tokens-dark.css"));

        foreach (var slot in Slots)
        {
            dark.Should().ContainKey(slot, "{0} must be re-declared by the dark theme, not inherited", slot);
        }

        dark.Should().ContainKey(Ink);
    }

    /// <summary>
    /// The dark block restates the ink as the same <c>var()</c> expression the light root declares, which is the
    /// convention the file documents for <c>--tm-text-inverse</c> / <c>--tm-sort-indicator-active</c>: a custom
    /// property substitutes its <c>var()</c> where it is DECLARED, so a family declared only on <c>:root</c> hands
    /// every dark element the light value.
    /// </summary>
    [Fact]
    public void TheInk_ResolvesToTheThemeInverse_NotToTheLightValue()
    {
        ThemeCss.ResolveColour($"var({Ink})", ThemeCss.TokenGraph(dark: false))
            .Should().Be("#ffffff", "light initials are white on the 700-tones");

        ThemeCss.ResolveColour($"var({Ink})", ThemeCss.TokenGraph(dark: true))
            .Should().Be("#0f172a", "dark initials are the dark ink on the lifted 300-tones");
    }

    [Fact]
    public void TheDarkPalette_ReallyDiffersFromTheLightOne()
    {
        var light = ThemeCss.TokenGraph(dark: false);
        var dark = ThemeCss.TokenGraph(dark: true);

        foreach (var slot in Slots)
        {
            ThemeCss.ResolveColour($"var({slot})", dark).Should().NotBe(
                ThemeCss.ResolveColour($"var({slot})", light),
                "{0} must be lifted in the dark theme — an unchanged value is a token that only looks themed", slot);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EverySlot_KeepsAaTextContrast_OnItsInk(bool dark)
    {
        foreach (var slot in Slots)
        {
            var ratio = ThemeCss.Ratio($"var({slot})", $"var({Ink})", dark);
            ratio.Should().BeGreaterThanOrEqualTo(4.5,
                "{0} carries white initials in light and dark ink in dark: measured {1:0.00}:1 in the {2} theme",
                slot, ratio, dark ? "dark" : "light");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheEightSlots_AreEightDistinguishableColours(bool dark)
    {
        var graph = ThemeCss.TokenGraph(dark);
        var resolved = Slots.Select(slot => ThemeCss.ResolveColour($"var({slot})", graph)).ToList();

        resolved.Should().OnlyHaveUniqueItems(
            "two agents in a lane or a graph must not paint the same colour in the {0} theme — a duplicated "
            + "value is a copy-paste in the palette, not a design", dark ? "dark" : "light");
        resolved.Should().NotContain(value => value.Contains("--tm-", StringComparison.Ordinal),
            "every slot has to resolve to a real colour, not to an unresolved alias");
    }

    /// <summary>
    /// The palette must be usable as a FILL, so no slot may be a raw palette STEP alias that the dark theme never
    /// revisits (<c>var(--tm-color-primary-700)</c> keeps its light value under the dark ink). The declaration is
    /// therefore a literal colour with the step it came from named in the comment.
    /// </summary>
    [Fact]
    public void NoSlot_IsDeclaredAsARawPaletteStepAlias()
    {
        var light = ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"));

        foreach (var slot in Slots)
        {
            light[slot].Should().NotContain("--tm-color-",
                "{0} is a semantic agent colour with its own dark counterpart — an alias to a palette step "
                + "would pin one theme forever", slot);
        }
    }
}
