using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>
/// Fáze 10c (2.12.0): a FullWidth tray of several short segments (seven weekdays Mo–Su in a 360–390 px
/// column) used to clip its labels to "M…" — every segment kept <c>padding-inline: var(--tm-space-3)</c>
/// (or the size's own, up to <c>--tm-space-4</c> on Lg) next to <c>flex: 1 1 0</c>, so the box left for
/// the label shrank to ~20 px while the text itself needs ~22 px. FullWidth segments share the row
/// equally and centre their content, so a smaller inline padding costs nothing when there is room and
/// is the whole difference when there is not. The padding is read from the SHIPPED bundle through the
/// cascade resolver (the only text where the import order exists), for every size, never from one rule.
/// </summary>
public class TmSegmentedControlFullWidthCssTests
{
    private static IReadOnlyList<CssCascade.Element> Option(string size, bool fullWidth)
        =>
        [
            new("div", fullWidth
                ? new[] { "tm-segmented", $"tm-segmented--{size}", "tm-segmented--full-width" }
                : new[] { "tm-segmented", $"tm-segmented--{size}" }),
            new("div", "tm-segmented__track"),
            new("button", "tm-segmented__option"),
        ];

    [Theory]
    [InlineData("sm")]
    [InlineData("md")]
    [InlineData("lg")]
    public void FullWidthOption_DropsToTheTightPadding_ForEverySize(string size)
    {
        var winner = CssCascade.Resolve(ThemeCss.BundledCss(), Option(size, fullWidth: true), "padding-inline");

        winner.Unmodelled.Should().BeEmpty();
        winner.Value.Should().Be("var(--tm-segmented-full-width-padding-inline)",
            "a {0} FullWidth segment must leave the label the room the equal split gives it", size);
    }

    [Fact]
    public void TheKnob_IsDeclaredOnRoot_AsTheThinGutter_SoAHostOverrideOnAnyAncestorReachesIt()
    {
        // Declared on :root (like --tm-tabs-sticky-top), never on .tm-segmented--full-width: a declaration
        // on the component's own class would beat every inherited host value. The strict token audit also
        // rejects an undefined --tm-* alias, even one with a fallback.
        var declarations = ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"));

        declarations.Should().ContainKey("--tm-segmented-full-width-padding-inline");
        declarations["--tm-segmented-full-width-padding-inline"].Should().Be("var(--tm-space-1)");

        var tokenTail = ThemeCss.Normalise(ThemeCss.ComponentCss("_segmented-control.css"));
        tokenTail.Should().NotContain("--tm-segmented-full-width-padding-inline:",
            "the component stylesheet only READS the knob");
    }

    [Theory]
    [InlineData("sm", "var(--tm-space-2)")]
    [InlineData("md", "var(--tm-space-3)")]
    [InlineData("lg", "var(--tm-space-4)")]
    public void ContentSizedOption_KeepsItsSizePadding(string size, string expected)
    {
        CssCascade.Resolve(ThemeCss.BundledCss(), Option(size, fullWidth: false), "padding-inline")
            .Value.Should().Be(expected, "only the FullWidth tray tightens the padding — the content-sized one is unchanged");
    }

    [Fact]
    public void TheFullWidthRule_WinsBySpecificity_NotBySourceOrder()
    {
        var winner = CssCascade.Resolve(ThemeCss.BundledCss(), Option("lg", fullWidth: true), "padding-inline");

        winner.Source.Should().Be(".tm-segmented.tm-segmented--full-width .tm-segmented__option",
            "the size rules are two classes and come later in the file — a bare two-class FullWidth rule would lose to Lg");
    }

    [Fact]
    public void TheTightPadding_IsATokenizedKnob_ADevelopmentHostCanSetToZero()
    {
        var css = ThemeCss.Normalise(ThemeCss.ComponentCss("_segmented-control.css"));

        css.Should().Contain("padding-inline: var(--tm-segmented-full-width-padding-inline)",
            "the padding is a documented component knob (padding-inline: 0 for the narrowest sets), not a magic number");
        css.Should().NotMatchRegex(@"tm-segmented--full-width[^{]*\{[^}]*padding-inline:\s*\d",
            "no hardcoded length — every spacing is a --tm-* token");
    }

    [Fact]
    public void TheLabelStillEllipsises_SoALongLabelNeverBreaksTheTray()
    {
        // The fix is the padding, not the removal of the safety net: a label that really is too long keeps
        // the ellipsis instead of pushing the row wider than its container.
        var css = ThemeCss.Normalise(ThemeCss.ComponentCss("_segmented-control.css"));

        css.Should().Contain(".tm-segmented__option-label { overflow: hidden; text-overflow: ellipsis; }");
    }
}
