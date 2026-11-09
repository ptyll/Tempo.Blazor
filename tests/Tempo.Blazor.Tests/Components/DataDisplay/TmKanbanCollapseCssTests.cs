using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// CSS contract of the collapsible-column surface of <c>TmKanbanBoard</c> (plan a98e4a71, review fze-2).
/// <para>
/// The swimlane layout's sticky header cell used to be a reduced strip: its vertical title, colour dot and
/// WIP badge were <c>display: none</c>, so a collapsed column there was an unexplained narrow gap. It now
/// renders the SAME strip as the classic column (vertical title, count and WIP stacked, the sticky header
/// row stretching to it) and keeps a WIP breach visible. Both collapse toggles are 24×24 controls that grow
/// to the shared touch target on a coarse pointer and hover through tokens.
/// </para>
/// <para>
/// These assertions read the SOURCE partial and the committed bundle: a source-only edit the bundler never
/// shipped is the failure this guards (a consumer links <c>tempo-blazor.bundled.css</c>).
/// </para>
/// </summary>
public sealed class TmKanbanCollapseCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex RuleBlock =
        new(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Compiled, RegexTimeout);

    private static string Read(params string[] path)
        => File.ReadAllText(Path.Combine(new[] { ThemeCss.RepositoryRoot().FullName }.Concat(path).ToArray()));

    private static string Sheet()
        => ThemeCss.StripComments(Read("src", "Tempo.Blazor", "wwwroot", "css", "components", "_kanban.css"));

    private static string Bundle()
        => ThemeCss.StripComments(Read("src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.bundled.css"));

    private static IEnumerable<(string Selector, string Body)> Rules(string css)
        => RuleBlock.Matches(css).Select(match =>
            (ThemeCss.Normalise(match.Groups["selector"].Value), ThemeCss.Normalise(match.Groups["body"].Value)));

    /// <summary>Every declaration made by a selector part, in source order.</summary>
    private static string Body(string css, string selector)
        => string.Join(" ",
            Rules(css)
                .Where(rule => rule.Selector.Split(',').Select(ThemeCss.Normalise).Contains(selector))
                .Select(rule => rule.Body));

    /// <summary>The text inside a top-level at-rule, brace-counted so nested rules survive.</summary>
    private static string MediaBlock(string css, string atRule)
    {
        var start = css.IndexOf(atRule, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{atRule} must exist in the shipped stylesheet");

        var open = css.IndexOf('{', start);
        var depth = 1;
        var index = open + 1;
        for (; index < css.Length && depth > 0; index++)
        {
            if (css[index] == '{') depth++;
            else if (css[index] == '}') depth--;
        }

        depth.Should().Be(0, $"{atRule} must be brace-balanced");
        return css[(open + 1)..(index - 1)];
    }

    [Fact]
    public void CollapsedHeaderOnlyColumn_RendersTheClassicStrip_AndStretchesTheStickyHeaderRow()
    {
        var css = Sheet();

        Body(css, ".tm-kanban__column--collapsed .tm-kanban__header-title")
            .Should().Contain("writing-mode: vertical-rl",
                "the classic collapsed strip renders its title vertically");

        var hidesTheStripFurniture = Rules(css)
            .Where(rule => rule.Selector.Contains("tm-kanban__column--header-only", StringComparison.Ordinal)
                           && rule.Selector.Contains("tm-kanban__column--collapsed", StringComparison.Ordinal)
                           && (rule.Selector.Contains("__header-title", StringComparison.Ordinal)
                               || rule.Selector.Contains("__header-color", StringComparison.Ordinal)
                               || rule.Selector.Contains("__wip-limit", StringComparison.Ordinal)))
            .Any(rule => rule.Body.Contains("display: none", StringComparison.Ordinal));

        hidesTheStripFurniture.Should().BeFalse(
            "the header-only strip renders the title, colour and WIP badge like the classic strip");

        Body(css, ".tm-kanban__column--header-only.tm-kanban__column--collapsed .tm-kanban__header")
            .Should().Contain("min-height: var(--tm-kanban-collapsed-header-height)",
                "the vertical title needs a height in the sticky header row");

        Body(css, ".tm-kanban__column-headers")
            .Should().Contain("align-items: stretch",
                "every header cell stretches to the collapsed column's strip");

        Body(css, ".tm-kanban__column--collapsed .tm-kanban__header")
            .Should().Contain("cursor: pointer",
                "the whole collapsed header is the pointer expand target");

        // Phase 5: the sticky row's strip carries the classic column's PANEL. Without it the vertical
        // title, dot and count float on the page background inside a 160px-tall header row, so the
        // column does not read as collapsed at all (the phase-5 visual review caught exactly that).
        Body(css, ".tm-kanban__column--header-only.tm-kanban__column--collapsed").Should()
            .Contain("background-color: var(--tm-bg-muted)",
                "the sticky strip paints the same panel as the classic column")
            .And.Contain("border: var(--tm-border-width) solid var(--tm-border-color)")
            .And.Contain("border-radius: var(--tm-radius-lg, 0.75rem)",
                "the strip is one continuous column with the narrowed lane cells below it");
    }

    [Fact]
    public void HeaderOnlyColumn_KeepsTheOverLimitTintVisible()
    {
        var css = Sheet();

        var overLimitHeader =
            Body(css, ".tm-kanban__column--header-only.tm-kanban__column--over-limit .tm-kanban__header");

        overLimitHeader.Should().Contain("background-color: var(--tm-color-danger-subtle)",
            "a header-only column is transparent, so the tint has to land on its header");
        overLimitHeader.Should().Contain("var(--tm-color-danger-soft-border)");
    }

    [Fact]
    public void BothToggles_AreAtLeast24px_GrowOnCoarsePointers_AndHoverThroughTokens()
    {
        var css = Sheet();
        var toggle = Body(css, ".tm-kanban__column-toggle");
        var swimlaneToggle = Body(css, ".tm-kanban__swimlane-toggle");

        foreach (var (name, body) in new[]
                 {
                     ("column toggle", toggle),
                     ("swimlane toggle", swimlaneToggle),
                 })
        {
            body.Should().Contain("width: var(--tm-space-6)", "{0} is at least 24×24 (WCAG 2.5.8)", name);
            body.Should().Contain("height: var(--tm-space-6)", "{0} is at least 24×24 (WCAG 2.5.8)", name);
        }

        var coarse = MediaBlock(css, "@media (pointer: coarse)");
        foreach (var selector in new[] { ".tm-kanban__column-toggle", ".tm-kanban__swimlane-toggle" })
        {
            var body = Body(coarse, selector);
            body.Should().Contain("min-width: var(--tm-touch-target)",
                "{0} grows to the 44px touch target on a coarse pointer", selector);
            body.Should().Contain("min-height: var(--tm-touch-target)",
                "{0} grows to the 44px touch target on a coarse pointer", selector);
        }

        foreach (var hover in new[]
                 {
                     ".tm-kanban__column-toggle:hover",
                     ".tm-kanban__swimlane-toggle:hover",
                 })
        {
            Body(css, hover).Should()
                .Contain("background-color: var(--tm-color-primary-subtle)",
                    "{0} paints its hover through a token", hover)
                .And.Contain("color: var(--tm-text-primary)",
                    "{0} raises the glyph ink through a token", hover);
        }

        toggle.Should().Contain("transition: background-color var(--tm-transition-fast), color var(--tm-transition-fast)");
    }

    [Fact]
    public void TheCommittedBundle_ShipsTheCorrectedRules()
    {
        var bundle = Bundle();

        bundle.Should().Contain(".tm-kanban__column--header-only.tm-kanban__column--collapsed .tm-kanban__header",
            "a consumer links the bundle, not the partial");
        bundle.Should().Contain("--tm-kanban-collapsed-header-height");
        bundle.Should().Contain("writing-mode:vertical-rl");
        bundle.Should().Contain("@media (pointer:coarse)");
        bundle.Should().Contain("--tm-touch-target");
        bundle.Should().NotContain(
            ".tm-kanban__column--header-only.tm-kanban__column--collapsed .tm-kanban__header-title{display:none",
            "the header-only strip must not hide its title in the shipped bundle");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Phase 3 (plan a98e4a71, D3/D4) — collapsed-lane slots and the target
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CollapsedLaneSlots_AreQuietUntilTheGesture_CopyTheGridWidths_AndShipInTheBundle()
    {
        var css = Sheet();

        Body(css, ".tm-kanban__swimlane-slots").Should()
            .Contain("display: flex")
            .And.Contain("gap: var(--tm-space-4, 1rem)")
            .And.Contain("padding: var(--tm-kanban-lane-padding)",
                "the slots line up with the lane body they replace — one knob drives both");

        var slot = Body(css, ".tm-kanban__lane-slot");
        slot.Should().Contain("flex: 0 0 var(--tm-kanban-column-width)")
            .And.Contain("min-width: var(--tm-kanban-column-width)",
                "a slot copies the column width, so the grid stays aligned")
            .And.Contain("min-height: var(--tm-kanban-lane-slot-height)",
                "outside a drag the strip stays low and quiet")
            .And.Contain("border: 1px dashed transparent",
                "an idle slot paints no affordance at all");

        Body(css, ".tm-kanban__lane-slot--collapsed").Should()
            .Contain("flex: 0 0 var(--tm-kanban-collapsed-width)")
            .And.Contain("max-width: var(--tm-kanban-collapsed-width)",
                "a slot under a collapsed column is the narrow strip");

        Body(css, ".tm-kanban--dragging .tm-kanban__lane-slot").Should()
            .Contain("border-color: var(--tm-color-primary-soft-border)")
            .And.Contain("background-color: var(--tm-color-primary-subtle)",
                "the root's gesture class is what turns the quiet slots into visible targets");

        Body(css, ".tm-kanban__lane-slot--drop-target").Should()
            .Contain("outline: 2px solid var(--tm-color-primary)")
            .And.Contain("background-color: var(--tm-color-primary-subtle)");

        var reducedMotion = MediaBlock(css, "@media (prefers-reduced-motion: reduce)");
        Body(reducedMotion, ".tm-kanban__lane-slot").Should().Contain("transition: none");

        var bundle = Bundle();
        bundle.Should().Contain(".tm-kanban__swimlane-slots", "a consumer links the bundle, not the partial");
        bundle.Should().Contain(".tm-kanban__lane-slot--drop-target");
        bundle.Should().Contain("--tm-kanban-lane-slot-height");
        bundle.Should().Contain("--tm-kanban-column-width");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SlotDropTarget_OutlineMeetsTheNonTextContrastMinimum_OnEverySurface(bool dark)
    {
        // WCAG 1.4.11 — a non-text indicator needs 3:1 against its neighbour. The outline is
        // --tm-color-primary and the drop target paints --tm-color-primary-subtle UNDER it, so the
        // surfaces it can sit on are the lane's muted panel, the page surface AND that wash. The dark
        // wash is `rgb(from --tm-color-primary-500 / 0.15)`, i.e. measured as painted, composited over
        // the muted panel — the lightest backdrop it can land on and therefore the worst case.
        //
        // Measured (24px outline on its own background, `ThemeCss` resolver): light 4.70 on muted,
        // 5.17 on surface, 4.75 on the wash; dark 4.07 on muted, 5.75 on surface, 3.48 on the wash.
        foreach (var surface in new[] { "var(--tm-bg-muted)", "var(--tm-bg-surface)" })
        {
            ThemeCss.Ratio("var(--tm-color-primary)", surface, dark).Should()
                .BeGreaterThanOrEqualTo(3.0,
                    "the drop-target outline must be perceivable on {0} in the {1} theme",
                    surface, dark ? "dark" : "light");
        }

        ThemeCss.PaintedRatio("var(--tm-color-primary)", "var(--tm-color-primary-subtle)", dark,
                backdrop: "var(--tm-bg-muted)").Should()
            .BeGreaterThanOrEqualTo(3.0,
                "the outline sits on the target's own wash in the {0} theme (measured 4.75 light / 3.48 dark)",
                dark ? "dark" : "light");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Phase 3 review (fze-3) — the whole-gesture wash and the drop claim
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CollapsedStrips_AdvertiseThemselvesForTheWholeGesture_LikeTheLaneSlots()
    {
        var css = Sheet();

        // A collapsed strip is a drop zone for the WHOLE gesture, so it joins the lane slots' in-flight
        // wash instead of staying invisible until the pointer happens to be over it. Its own
        // --drop-target rule repeats the dragging prefix and outweighs this wash.
        Body(css, ".tm-kanban--dragging .tm-kanban__column--collapsed").Should()
            .Contain("border-color: var(--tm-color-primary-soft-border)",
                "the root's gesture class turns every strip into a visible target");

        Body(css, ".tm-kanban__column--collapsed.tm-kanban__column--drop-target").Should()
            .Contain("border-color: var(--tm-color-primary)")
            .And.Contain("background-color: var(--tm-color-primary-subtle)");

        Bundle().Should().Contain(".tm-kanban--dragging .tm-kanban__column--collapsed");
    }

    /// <summary>
    /// The render-time <c>preventDefault</c> flag never reaches the DOM (it lives on the event handler
    /// descriptor), so no bUnit assertion can observe it and a real drag only shows its effect when the
    /// drop lands. It is nevertheless the contract: a DISABLED board must not claim a drop it will
    /// refuse, or the browser paints a drop cursor and the drop event dies on the floor. Hence the one
    /// source guard in this suite — every drop zone binds <c>@(!Disabled)</c>, never a constant.
    /// </summary>
    [Fact]
    public void EveryDropZone_ClaimsTheDropOnlyWhileTheBoardIsEnabled()
    {
        var razor = Read("src", "Tempo.Blazor", "Components", "DataDisplay", "TmKanbanBoard.razor");

        Regex.Matches(razor, @"@ondragover:preventDefault=""@\(!Disabled\)""").Count.Should()
            .Be(3, "the column, the swimlane cell and the collapsed lane's slot each bind the live flag");
        razor.Should().NotContain("@ondragover:preventDefault=\"true\"",
            "a constant true would let a disabled board claim the drop");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Phase 4 (plan a98e4a71) — the card's non-drag move trigger
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TheCardMoveTrigger_IsATokenOnlyTouchTarget_AndNeverRestylesTheMenusOwnTrigger()
    {
        var css = Sheet();

        Body(css, ".tm-kanban__card").Should().Contain("position: relative",
            "the trigger is positioned in the card's own corner");
        Body(css, ".tm-kanban__card--movable").Should()
            .Contain("padding-inline-end: calc(var(--tm-space-3, 0.75rem) + var(--tm-space-6))",
                "the reserved corner keeps the host's content out from under the trigger");
        Body(css, ".tm-kanban__card-content").Should().NotBeNullOrEmpty();
        Body(css, ".tm-kanban__card-actions").Should().Contain("position: absolute");

        var trigger = Body(css, ".tm-kanban__card-move-trigger");
        trigger.Should().Contain("inline-size: var(--tm-space-6)",
                "the glyph is a 24×24 target (WCAG 2.5.8)")
            .And.Contain("block-size: var(--tm-space-6)")
            .And.Contain("color: var(--tm-text-secondary)");

        var coarse = MediaBlock(css, "@media (pointer: coarse)");
        Body(coarse, ".tm-kanban__card-move-trigger").Should()
            .Contain("inline-size: var(--tm-touch-target)")
            .And.Contain("block-size: var(--tm-touch-target)");

        Body(css, ".tm-kanban__card-move:hover .tm-kanban__card-move-trigger").Should()
            .Contain("background-color: var(--tm-bg-muted)")
            .And.Contain("color: var(--tm-text-primary)");

        // Ownership: the menu's own trigger is a foreign element, so this sheet styles ITS OWN glyph
        // through the wrapper's state and never the wrapper itself.
        css.Should().NotContain(".tm-context-menu__trigger",
            "the kanban stylesheet must not restyle the context menu's own trigger");

        Body(css, ".tm-kanban__card:focus-visible").Should().Contain("box-shadow: var(--tm-focus-ring)",
            "the focusable card paints the shared focus ring");

        var bundle = Bundle();
        bundle.Should().Contain(".tm-kanban__card-move-trigger");
        bundle.Should().Contain(".tm-kanban__card--movable");
        bundle.Should().Contain("--tm-touch-target");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Phase 5 (plan a98e4a71, fze-5) — the sticky header row shares the lane inset
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A swimlane's cells start after the lane's own border and its body's padding, while the sticky header
    /// row is a sibling of the lanes with no such border — so without the same inset on the header row a
    /// header cell sat 9px to the LEFT of the lane cells it heads. The phase-5 geometry spec
    /// (<c>KanbanCollapsedLayoutE2ETests</c>) measures the real boxes; this guard keeps the declarations
    /// that produce that geometry tied to ONE knob, so a future padding/border edit cannot split them again
    /// without failing here (the E2E would only see it as a 9px drift in a browser).
    /// </summary>
    [Fact]
    public void TheStickyHeaderRow_SharesTheLaneRowsInset_ThroughOneKnob()
    {
        var css = Sheet();

        var root = Body(css, ".tm-kanban");
        root.Should().Contain("--tm-kanban-lane-padding: var(--tm-space-2, 0.5rem)",
            "the lane body's padding is a knob the header row can read");
        root.Should().Contain("--tm-kanban-lane-inset: calc(var(--tm-kanban-lane-padding) + var(--tm-border-width))",
            "the lane's border is read from the shared width token, never repeated as a literal");

        Body(css, ".tm-kanban__column-headers").Should()
            .Contain("padding-inline: var(--tm-kanban-lane-inset)",
                "the header row starts where every lane row starts");
        Body(css, ".tm-kanban__swimlane-body").Should()
            .Contain("padding: var(--tm-kanban-lane-padding)");
        Body(css, ".tm-kanban__swimlane-slots").Should()
            .Contain("padding: var(--tm-kanban-lane-padding)");
        Body(css, ".tm-kanban__swimlane").Should()
            .Contain("border: var(--tm-border-width) solid var(--tm-border-color)",
                "the border the inset accounts for is the same token");

        var bundle = Bundle();
        bundle.Should().Contain("--tm-kanban-lane-inset", "a consumer links the bundle, not the partial");
        bundle.Should().Contain("padding-inline:var(--tm-kanban-lane-inset)");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Phase 5 (plan a98e4a71, fze-5) — the strip's WIP reading and breach cue
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A collapsed strip keeps exactly three signals: the vertical title, the count and the WIP badge.
    /// The phase-5 visual review found two contrast problems the eye alone would have negotiated with:
    /// the WIP badge read in <c>--tm-text-tertiary</c> (4.39:1 light / 4.04:1 dark on the muted panel —
    /// under AA for its 11.2px), and the over-limit state leaned on the decorative danger pair, which is
    /// ~1.3:1 and therefore invisible on the strip that has no card body left to show the breach.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheCollapsedStrip_KeepsItsBadgesReadable_AndStatesTheWipBreachAtTheNonTextMinimum(bool dark)
    {
        var css = Sheet();

        Body(css, ".tm-kanban__wip-limit").Should()
            .Contain("color: var(--tm-text-secondary)",
                "the 11.2px badge must clear AA (tertiary is 4.39:1 light / 4.04:1 dark on the muted panel)");
        Body(css, ".tm-kanban__count").Should().Contain("color: var(--tm-text-secondary)");

        ThemeCss.Ratio("var(--tm-text-secondary)", "var(--tm-bg-muted)", dark).Should()
            .BeGreaterThanOrEqualTo(4.5,
                "the strip's numbers are small text on the muted panel in the {0} theme",
                dark ? "dark" : "light");

        Body(css, ".tm-kanban__column--collapsed.tm-kanban__column--over-limit").Should()
            .Contain("border-color: var(--tm-color-danger)")
            .And.Contain("outline: 2px solid var(--tm-color-danger)")
            .And.Contain("background-color: var(--tm-color-danger-subtle)");

        // Measured against the muted panel, the WORST case for both inks: in dark mode the wash is
        // rgba(239,68,68,0.15) over that panel (#4f4152), i.e. a touch lighter than the panel itself, so
        // the real pair is a hair stronger (danger 3.43:1, danger-text 5.01:1) than these floors.
        ThemeCss.Ratio("var(--tm-color-danger)", "var(--tm-bg-muted)", dark).Should()
            .BeGreaterThanOrEqualTo(3.0,
                "the strip's over-limit edge is the only non-text cue and must be perceivable (WCAG 1.4.11)");

        foreach (var number in new[] { "__count", "__wip-limit" })
        {
            Body(css, $".tm-kanban__column--collapsed.tm-kanban__column--over-limit .tm-kanban{number}").Should()
                .Contain("color: var(--tm-color-danger-text)");
        }

        ThemeCss.Ratio("var(--tm-color-danger-text)", "var(--tm-bg-muted)", dark).Should()
            .BeGreaterThanOrEqualTo(4.5,
                "the numbers stay readable over the breach wash in the {0} theme", dark ? "dark" : "light");

        Bundle().Should().Contain(".tm-kanban__column--collapsed.tm-kanban__column--over-limit",
            "a consumer links the bundle, not the partial");
    }
}
