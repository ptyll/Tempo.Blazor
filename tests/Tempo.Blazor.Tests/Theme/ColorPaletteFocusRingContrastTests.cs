using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// WCAG 2.2 (1.4.11 non-text contrast / 2.4.13 focus appearance) guard for the
/// <c>TmColorPalette</c> swatch focus indicator. The indicator is two-tone and INWARD: a 2px
/// <c>var(--tm-color-primary)</c> outline at <c>outline-offset: -2px</c> runs inside the swatch
/// edge, and an inset <c>var(--tm-surface)</c> band separates it from the swatch fill — the ring
/// never paints across the 2px grid gap onto a neighbouring swatch (the old outward offset ring
/// measured ~1:1 next to a blue swatch). The ring's adjacent reference is therefore the inset
/// surface band, and that pair must clear 3:1 in BOTH themes (5.17:1 light / 5.75:1 dark at the
/// time of writing). Mouse and keyboard focus must paint the same indicator, a focused+selected
/// swatch must stay distinguishable from a merely selected one, and only <c>--tm-*</c> tokens
/// may paint it (the forced-colors block is the recorded exception: system colors only).
/// </summary>
public sealed class ColorPaletteFocusRingContrastTests
{
    private const string Stylesheet = "_color-palette.css";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SwatchFocusRing_ClearsThreeToOne_AgainstItsInsetSurfaceBand(bool dark)
    {
        var outline = ThemeCss.Property(Stylesheet, ".tm-color-palette-swatch:focus-visible", "outline");
        var keyboardOutline = ThemeCss.Property(Stylesheet, ".tm-color-palette-swatch--keyboard-focus", "outline");
        keyboardOutline.Should().Be(outline, "mouse and keyboard focus must paint the same indicator");
        ThemeCss.Property(Stylesheet, ".tm-color-palette-swatch--keyboard-focus", "box-shadow")
            .Should().Be(
                ThemeCss.Property(Stylesheet, ".tm-color-palette-swatch:focus-visible", "box-shadow"),
                "mouse and keyboard focus must paint the same indicator");

        // The ring lives INSIDE the swatch (negative offset): independent of the neighbours,
        // whatever color they are.
        ThemeCss.Property(Stylesheet, ".tm-color-palette-swatch:focus-visible", "outline-offset")
            .Should().StartWith("-", "an outward ring paints across the 2px grid gap onto the neighbouring swatch");

        var ringToken = ColourTokenOf(outline, "outline");
        var bandToken = ColourTokenOf(
            ThemeCss.Property(Stylesheet, ".tm-color-palette-swatch:focus-visible", "box-shadow"),
            "box-shadow");

        var ratio = ThemeCss.Ratio(ringToken, bandToken, dark);
        ratio.Should().BeGreaterThanOrEqualTo(
            3,
            "{0} ring next to its {1} band ({2} theme) measures {3:0.00}:1 — the swatch focus indicator needs 3:1",
            ringToken, bandToken, dark ? "dark" : "light", ratio);
    }

    [Fact]
    public void FocusedAndSelected_KeepsTheSelectionBorder_AndDropsTheOuterHalo()
    {
        // Selected-only: text-colored border + an OUTER primary halo (3px spread).
        var selectedShadow = ThemeCss.Property(Stylesheet, ".tm-color-palette-swatch--selected", "box-shadow");
        selectedShadow.Should().Contain("0 0 0 3px", "the selected-only state paints an outer halo");
        ThemeCss.TryProperty(Stylesheet, ".tm-color-palette-swatch--selected", "border-color")
            .Should().NotBeNull("the selection border is part of the selected look");

        // Focused+selected: the inward two-tone ring replaces the halo (the border stays), so
        // the combined state is distinguishable from selected-only.
        var combinedShadow = ThemeCss.TryProperty(
            Stylesheet, ".tm-color-palette-swatch--selected.tm-color-palette-swatch--keyboard-focus", "box-shadow");
        combinedShadow.Should().NotBeNull("focused+selected must override the outer halo");
        combinedShadow.Should().Contain("inset", "the inward surface band must remain while focused");
        combinedShadow.Should().NotContain("3px", "the outer selection halo must be gone while focused");

        // The :focus-visible twin of the combined state must agree.
        var combinedVisibleShadow = ThemeCss.TryProperty(
            Stylesheet, ".tm-color-palette-swatch--selected:focus-visible", "box-shadow");
        combinedVisibleShadow.Should().Be(combinedShadow, "mouse and keyboard focus must agree");
    }

    [Fact]
    public void ForcedColors_KeepsTheSwatchColors_AndMarksTheSelectionWithSystemHighlight()
    {
        ThemeCss.Property(Stylesheet, ".tm-color-palette-swatch[aria-checked=\"true\"]", "outline")
            .Should().Be(
                "3px solid Highlight",
                "under forced colors the selection is marked with the system highlight color — "
                + "the one recorded exception to the tokens-only rule");
    }

    private static string ColourTokenOf(string declarationValue, string property)
    {
        var match = Regex.Match(declarationValue, @"var\(--tm-[\w-]+\)", RegexOptions.None, TimeSpan.FromSeconds(5));
        return match.Success
            ? match.Value
            : throw new InvalidOperationException(
                $"{Stylesheet} declares '{property}' without a --tm-* token: '{declarationValue}' — " +
                "focus indicators must be painted from design tokens so both themes stay guarded");
    }
}
