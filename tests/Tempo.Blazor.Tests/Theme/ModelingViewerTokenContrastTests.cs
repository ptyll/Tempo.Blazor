using FluentAssertions;
using Tempo.Blazor.Components.Modeling;
using Xunit;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Phase 6 (Modeling viewer): the overlay tokens the viewer writes into a presentation style are
/// measured, not eyeballed. The diagram's paper is white in every theme, so the diff outlines and the
/// heat scale have to keep their contrast on WHITE in both themes — a brightened dark-theme accent
/// would be invisible on the page it paints on.
/// </summary>
public sealed class ModelingViewerTokenContrastTests
{
    private const string Paper = "var(--tm-color-white)";

    public static TheoryData<string, bool> DiffStrokes => new()
    {
        { "var(--tm-color-diff-added-stroke)", false },
        { "var(--tm-color-diff-changed-stroke)", false },
        { "var(--tm-color-diff-layout-stroke)", false },
        { "var(--tm-color-diff-added-stroke)", true },
        { "var(--tm-color-diff-changed-stroke)", true },
        { "var(--tm-color-diff-layout-stroke)", true }
    };

    public static TheoryData<string, bool> HeatInk => new()
    {
        { "var(--tm-color-heat-0)", false },
        { "var(--tm-color-heat-1)", false },
        { "var(--tm-color-heat-2)", false },
        { "var(--tm-color-heat-0)", true },
        { "var(--tm-color-heat-1)", true },
        { "var(--tm-color-heat-2)", true }
    };

    [Theory]
    [MemberData(nameof(DiffStrokes))]
    public void A_diff_outline_keeps_a_three_to_one_ratio_on_the_white_paper(string stroke, bool dark)
    {
        // 1.4.11 non-text contrast: an outline that carries state is a graphical object boundary.
        ThemeCss.Ratio(stroke, Paper, dark).Should().BeGreaterThanOrEqualTo(3.0,
            $"{stroke} (dark={dark}) is a state cue on the page, not a decoration");
    }

    [Theory]
    [MemberData(nameof(HeatInk))]
    public void The_pale_heat_bands_carry_readable_dark_ink(string fill, bool dark)
    {
        ThemeCss.Ratio("var(--tm-color-heat-ink)", fill, dark).Should().BeGreaterThanOrEqualTo(4.5,
            $"{fill} (dark={dark}) carries a node label");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_high_heat_band_carries_white_ink(bool dark)
    {
        ThemeCss.Ratio("var(--tm-color-heat-3-text)", "var(--tm-color-heat-3)", dark)
            .Should().BeGreaterThanOrEqualTo(4.5, $"the fourth band is the only dark fill (dark={dark})");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_heat_scale_spreads_its_lightness(bool dark)
    {
        var luminance = new[]
        {
            ThemeCss.LuminanceOf("var(--tm-color-heat-0)", dark),
            ThemeCss.LuminanceOf("var(--tm-color-heat-1)", dark),
            ThemeCss.LuminanceOf("var(--tm-color-heat-2)", dark),
            ThemeCss.LuminanceOf("var(--tm-color-heat-3)", dark)
        };

        luminance.Should().BeInDescendingOrder(
            "the four steps must separate on shade as well as hue — a same-lightness scale is colour-only");
        (luminance[0] - luminance[3]).Should().BeGreaterThan(0.5,
            "the highest band has to be unmistakably darker than the lowest");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_overlay_tokens_keep_the_same_values_in_both_themes(bool dark)
    {
        // The viewer paints on the diagram's white paper, so a dark override would put dark ink on a
        // light page. The tokens must resolve identically in both themes.
        foreach (var token in ModelingViewerProjection.ViewerTokens.Where(name => name.Contains("heat", StringComparison.Ordinal)
                     || name.Contains("diff", StringComparison.Ordinal)))
        {
            var light = ThemeCss.TokenGraph(false)[token];
            var themed = ThemeCss.TokenGraph(dark)[token];
            themed.Should().Be(light, $"{token} paints on the theme-invariant paper");
        }
    }

    [Fact]
    public void Every_token_the_viewer_can_write_into_a_style_is_declared()
    {
        foreach (var dark in new[] { false, true })
        {
            var graph = ThemeCss.TokenGraph(dark);
            foreach (var token in ModelingViewerProjection.ViewerTokens)
            {
                graph.Should().ContainKey(token,
                    $"{token} is written into a presentation style; an undeclared token paints nothing");
            }
        }
    }

    [Fact]
    public void The_export_resolves_exactly_the_tokens_the_projection_can_use()
    {
        ModelingViewerProjection.ViewerTokens.Should().Contain(
        [
            "--tm-color-heat-0",
            "--tm-color-heat-1",
            "--tm-color-heat-2",
            "--tm-color-heat-3",
            "--tm-color-heat-3-text",
            "--tm-color-heat-ink",
            "--tm-color-diff-added-stroke",
            "--tm-color-diff-changed-stroke",
            "--tm-color-diff-layout-stroke"
        ]);
    }
}
