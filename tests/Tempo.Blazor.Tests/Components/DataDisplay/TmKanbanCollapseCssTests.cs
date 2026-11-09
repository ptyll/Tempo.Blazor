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
            .And.Contain("padding: var(--tm-space-2, 0.5rem)",
                "the slots line up with the lane body they replace");

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
        // --tm-color-primary; the surfaces it can sit on are the lane's muted panel and the page surface.
        // The dark target wash (primary-500 at 15 % over --tm-bg-muted) lifts the background just enough
        // to read 3.49:1, so the pair clears the minimum without a dark-only override.
        foreach (var surface in new[] { "var(--tm-bg-muted)", "var(--tm-bg-surface)" })
        {
            ThemeCss.Ratio("var(--tm-color-primary)", surface, dark).Should()
                .BeGreaterThanOrEqualTo(3.0,
                    "the drop-target outline must be perceivable on {0} in the {1} theme",
                    surface, dark ? "dark" : "light");
        }
    }
}
