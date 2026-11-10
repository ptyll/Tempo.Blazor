using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan d67b33af, Fáze 8 — the stylesheet contract of <see cref="Tempo.Blazor.Components.AI.TmConflictRadar"/>.
/// <para>
/// The package-level token gate (<see cref="AiMarkdownViewCssTests"/>) proves the AI package paints with tokens only;
/// this guard proves the radar's own share of it: the partial is imported by the package entry stylesheet, every class
/// the markup emits has a rule, the conflict and change tones are knobs a rule SETS and a base rule READS, the merge
/// action, the directory buttons and the side radios carry real focus rings, a coarse pointer reaches the 44 px target,
/// the forced-colors block hands the palette back to the system while keeping the words, and the partial animates
/// nothing at all.
/// </para>
/// </summary>
public sealed class TmConflictRadarCssTests
{
    /// <summary>Every <c>tm-ai-radar…</c> class the markup emits.</summary>
    private static readonly string[] Emitted =
    [
        "tm-ai-radar",
        "tm-ai-radar--conflicts-only",
        "tm-ai-radar__header",
        "tm-ai-radar__title",
        "tm-ai-radar__summary",
        "tm-ai-radar__filter",
        "tm-ai-radar__merge",
        "tm-ai-radar__merge-icon",
        "tm-ai-radar__merge-text",
        "tm-ai-radar__instructions",
        "tm-ai-radar__merge-hint",
        "tm-ai-radar__empty",
        "tm-ai-radar__measure",
        "tm-ai-radar__grid-wrap",
        "tm-ai-radar__grid",
        "tm-ai-radar__head-row",
        "tm-ai-radar__cell",
        "tm-ai-radar__cell--file",
        "tm-ai-radar__cell--agent",
        "tm-ai-radar__cell--change",
        "tm-ai-radar__branch",
        "tm-ai-radar__group",
        "tm-ai-radar__directory-row",
        "tm-ai-radar__directory-cell",
        "tm-ai-radar__directory-toggle",
        "tm-ai-radar__directory-icon",
        "tm-ai-radar__directory-name",
        "tm-ai-radar__directory-meta",
        "tm-ai-radar__row",
        "tm-ai-radar__row--conflict",
        "tm-ai-radar__file-name",
        "tm-ai-radar__file-path",
        "tm-ai-radar__conflict",
        "tm-ai-radar__conflict-icon",
        "tm-ai-radar__conflict-text",
        "tm-ai-radar__none",
        "tm-ai-radar__none-mark",
        "tm-ai-radar__change",
        "tm-ai-radar__change--added",
        "tm-ai-radar__change--modified",
        "tm-ai-radar__change--deleted",
        "tm-ai-radar__change--renamed",
        "tm-ai-radar__change-icon",
        "tm-ai-radar__change-kind",
        "tm-ai-radar__change-from",
        "tm-ai-radar__row--resolution",
        "tm-ai-radar__resolution-cell",
        "tm-ai-radar__sides",
        "tm-ai-radar__side",
        "tm-ai-radar__side-input",
        "tm-ai-radar__side-text",
        "tm-ai-radar__cards",
        "tm-ai-radar__card-group",
        "tm-ai-radar__card-list",
        "tm-ai-radar__card",
        "tm-ai-radar__card--conflict",
        "tm-ai-radar__card-file",
        "tm-ai-radar__card-changes",
        "tm-ai-radar__card-change",
    ];

    private static string RadarCss()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-conflict-radar.css");
        File.Exists(path).Should().BeTrue(
            "the radar ships its own partial next to the component that reads it: " + path);
        return ThemeCss.StripComments(File.ReadAllText(path));
    }

    [Fact]
    public void ThePackageEntrySheet_ImportsTheRadarPartial()
    {
        var entry = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css")));

        entry.Should().Contain("@import \"components/_ai-conflict-radar.css\"",
            "a host loads exactly one stylesheet for the package — an unimported partial never ships");
    }

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var css = RadarCss();

        foreach (var name in Emitted)
        {
            css.Should().Contain("." + name, "{0} is emitted by the component and must be styled", name);
        }
    }

    [Fact]
    public void TheTonesAreKnobsARuleSets_AndTheBaseRuleReads()
    {
        var css = RadarCss();

        css.Should().Contain("--tm-radar-conflict-ink:",
            "the conflicting row's ink is one knob, so a modifier cannot lose to a base rule declared later");
        css.Should().Contain("--tm-radar-conflict-fill:");
        css.Should().Contain("color: var(--tm-radar-conflict-ink)");
        css.Should().Contain("background-color: var(--tm-radar-conflict-fill)");
        css.Should().Contain("--tm-radar-none-ink:");
        css.Should().Contain("color: var(--tm-radar-none-ink)");
    }

    [Fact]
    public void AConflictIsAFrameAndAWord_NotATintAlone()
    {
        var css = RadarCss();

        css.Should().Contain(".tm-ai-radar__row--conflict");
        css.Should().Contain(".tm-ai-radar__conflict-text",
            "the conflict is a word plus an icon; the tint only agrees with it");
        css.Should().Contain("box-shadow: inset 3px 0 0 0 var(--tm-color-danger)");
    }

    [Fact]
    public void EveryChangeKindPaintsItsOwnTone()
    {
        var css = RadarCss();

        foreach (var kind in new[] { "added", "deleted", "renamed" })
        {
            css.Should().Contain($".tm-ai-radar__change--{kind}",
                "the {0} change must read differently from a plain modification", kind);
        }
    }

    [Fact]
    public void TheMergeActionTheDirectoryButtonsAndTheRadios_CarryRealFocusRings()
    {
        var css = RadarCss();

        css.Should().Contain(".tm-ai-radar__merge:focus-visible");
        css.Should().Contain(".tm-ai-radar__directory-toggle:focus-visible");
        css.Should().Contain(".tm-ai-radar__side-input:focus-visible");
        css.Should().Contain("outline: var(--tm-border-width) solid var(--tm-border-color-focus)");
    }

    [Fact]
    public void AnUnavailableMerge_LooksUnavailable()
    {
        var css = RadarCss();

        css.Should().Contain(".tm-ai-radar__merge[disabled]");
        css.Should().Contain("color: var(--tm-text-disabled)");
        css.Should().Contain("cursor: not-allowed");
    }

    [Fact]
    public void ACoarsePointer_GrowsTheSideLabelsAndTheActionsToTheTouchTarget()
    {
        var css = RadarCss();

        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("min-block-size: var(--tm-touch-target)");
        css.Should().Contain(".tm-ai-radar__side-input",
            "the radio glyph stays a radio; the LABEL around it is the target");
    }

    [Fact]
    public void ForcedColors_KeepTheSystemPaletteInCharge()
    {
        var css = RadarCss();

        css.Should().Contain("@media (forced-colors: active)");
        css.Should().Contain("outline: 2px solid Highlight",
            "every focus ring is handed back to the system Highlight colour");
        css.Should().Contain("CanvasText");
        css.Should().Contain("GrayText", "a disabled merge must still read as unavailable");
    }

    [Fact]
    public void TheForcedColorsBlock_NeverPaintsATokenColour()
    {
        var block = Block(RadarCss(), "@media (forced-colors: active)");

        block.Should().NotBeEmpty("the forced-colors block must exist to hand the palette back");
        block.Should().NotContain("var(--tm-",
            "inside forced-colors a --tm-* token paints the design system's colour, not the reader's system palette");
        block.Should().Contain("color: CanvasText");
    }

    [Fact]
    public void TheSideLabelsAreSpacedForTheTargetSizeExemption()
    {
        var css = RadarCss();

        ThemeCss.RuleDeclarations(css, ".tm-ai-radar__sides").Should().Contain(
            "gap: var(--tm-space-2) var(--tm-space-3)",
            "the 24 px labels stack with a row gap, so a wrapped row keeps the WCAG 2.5.8 spacing exemption");
    }

    /// <summary>Extracts one at-rule block (the marker plus its balanced braces).</summary>
    private static string Block(string css, string marker)
    {
        var start = css.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        var open = css.IndexOf('{', start);
        if (open < 0)
        {
            return string.Empty;
        }

        var depth = 0;
        for (var index = open; index < css.Length; index++)
        {
            if (css[index] == '{')
            {
                depth++;
            }
            else if (css[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return css[start..(index + 1)];
                }
            }
        }

        return string.Empty;
    }

    [Fact]
    public void ThePartialAnimatesNothing_BecauseEveryStateIsAlsoText()
    {
        var css = RadarCss();

        css.Should().NotContain("@keyframes");
        css.Should().NotContain("animation:");
        css.Should().NotContain("transition:",
            "a row that slid in would be motion without information");
    }

    [Fact]
    public void TheGridScrollsInsideItsOwnWrapper_NotThePage()
    {
        var css = RadarCss();

        css.Should().Contain("min-inline-size: 0");
        css.Should().Contain("overflow-x: auto",
            "a grid with many agents scrolls inside its wrapper, never the page");
        css.Should().Contain("position: sticky",
            "the file column stays readable while the agents scroll past it");
        css.Should().Contain("position: relative",
            "the wrapper is the containing block of the sr-only spans, so a wide table cannot park them on the page");
    }

    [Fact]
    public void TheScrolledGridHintsAtTheHiddenColumn_WithAPureCssShadow()
    {
        var css = RadarCss();

        css.Should().Contain("--tm-radar-shadow-ink:",
            "the shadow's ink is a declared knob, and it fades a token rather than a colour literal");
        css.Should().Contain("no-repeat local",
            "the cover gradients ride WITH the content, so the shadow disappears once that edge is reached");
        css.Should().Contain("no-repeat scroll",
            "the edge shadows stay pinned to the viewport of the scroller");
        css.Should().Contain("radial-gradient(farthest-side at 100% 50%",
            "a plain overflow clip gives a phone reader no hint that another agent column exists");
    }

    [Fact]
    public void TheNarrowPresentationIsAListOfCards_NotATableWithHiddenColumns()
    {
        var css = RadarCss();

        css.Should().Contain(".tm-ai-radar__cards");
        css.Should().Contain(".tm-ai-radar__card-list");
        css.Should().Contain(".tm-ai-radar__card-changes",
            "the agents' changes hang UNDER the file, which is the whole point of the narrow presentation");
        ThemeCss.RuleDeclarations(css, ".tm-ai-radar__card-list").Should().Contain("list-style: none",
            "the list semantics stay in the markup; the bullets do not");
        ThemeCss.RuleDeclarations(css, ".tm-ai-radar__card--conflict").Should().Contain(
            "border-inline-start: var(--tm-space-0-5) solid var(--tm-color-danger)",
            "a conflicting card keeps the table's solid edge, so the conflict is not a tint alone — and the edge is "
            + "LOGICAL, so a mirrored writing mode puts it on the inline start instead of a physical left side");
    }

    [Fact]
    public void TheForcedColorsBlock_DropsThePaintOnlyShadow()
    {
        var block = Block(RadarCss(), "@media (forced-colors: active)");

        block.Should().Contain("background: none",
            "a translucent fade cannot be expressed in the system palette, so it is dropped instead of faked");
    }
}
