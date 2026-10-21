using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Phase 13 fix round 1 (UX review of the redesign screenshots): the open defects of 13.12 are
/// guarded in the shipped stylesheet - RTL pin overlap, clipped long text, sub-44px sheet targets
/// and the cards hint that points at a header that is not there.
/// </summary>
public sealed class DataTableUxDefectsCssTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static string Css() => ThemeCss.ComponentCss("_data-table.css");

    private static string Rule(string css, string selectorFragment)
    {
        foreach (Match m in Regex.Matches(css, @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.None, Timeout))
        {
            if (m.Groups["sel"].Value.Contains(selectorFragment, StringComparison.Ordinal))
                return m.Groups["body"].Value;
        }

        throw new Xunit.Sdk.XunitException($"No rule for '{selectorFragment}'");
    }

    [Fact]
    public void RtlPinnedHeader_PaddingUsesTheSharedPinGlyphTokens_NoHardcodedSizes()
    {
        var css = Css();
        var body = Rule(css, "[dir=\"rtl\"] .tm-data-table th.tm-col-pinned");
        body.Should().Contain("--tm-data-table-pin-glyph").And.Contain("--tm-data-table-pin-inset");
        body.Should().NotContain("6px").And.NotContain("1.1rem");

        var pin = Rule(css, "\n.tm-col-pin-btn");
        pin.Should().Contain("inset-inline-end: var(--tm-data-table-pin-inset)");
        pin.Should().Contain("width: var(--tm-data-table-pin-glyph)");
    }

    [Fact]
    public void PinnedColumns_StickToLogicalEdges()
    {
        var css = Css();
        css.Should().Contain(".tm-col-pinned-left { inset-inline-start: var(--tm-pin-left, 0); }");
        css.Should().Contain(".tm-col-pinned-right { inset-inline-end: var(--tm-pin-right, 0); }");
    }

    [Fact]
    public void RtlPinnedHeader_ReservesTheGlyphWithLogicalPadding()
    {
        var body = Rule(Css(), "[dir=\"rtl\"] .tm-data-table th.tm-col-pinned");
        body.Should().Contain("padding-inline-start");
        body.Should().Contain("padding-inline-end");
    }

    [Fact]
    public void Rtl_PlainCellsFollowTheWritingDirection()
    {
        Rule(Css(), "[dir=\"rtl\"] .tm-data-table th:not(.tm-text-right)").Should().Contain("text-align: start");
    }

    [Fact]
    public void ColumnMenuHeader_ReservesTheKebabWidth_OnBothSides()
    {
        var css = Css();

        // Desktop: the th reserves the 28px kebab so it never paints over label / sort arrow / index.
        Rule(css, ".tm-data-table th.tm-col-has-menu")
            .Should().Contain("padding-inline-end: calc(var(--tm-data-table-cell-px) + var(--tm-space-7) + var(--tm-space-2))");

        // Coarse pointer: the 44px target clears the widened resize handle (the pin button's inset).
        css.Should().Contain("padding-inline-end: calc(var(--tm-data-table-cell-px) + var(--tm-touch-target) + var(--tm-space-3))");
        css.Should().Contain(".tm-data-table th .tm-data-table-col-menu { inset-inline-end: var(--tm-space-3); }");

        // RTL pinned header keeps BOTH reservations (pin glyph + kebab).
        Rule(css, "[dir=\"rtl\"] .tm-data-table th.tm-col-pinned.tm-col-has-menu")
            .Should().Contain("--tm-data-table-pin-glyph")
            .And.Contain("--tm-space-7");
    }

    [Fact]
    public void ScrollLayoutCells_EndInAnEllipsisAndTheCapIsContainerRelative()
    {
        var css = Css();
        var at = css.IndexOf(".tm-data-table-wrapper--responsive .tm-data-table td:not(", StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0);
        var open = css.IndexOf('{', at);
        var body = css[open..css.IndexOf('}', open)];
        body.Should().Contain("overflow: hidden");
        body.Should().Contain("text-overflow: ellipsis");
        body.Should().Contain("white-space: nowrap");
        body.Should().Contain("max-inline-size");
    }

    [Fact]
    public void FilterInputAndPlaceholder_EndInAnEllipsis()
    {
        var body = Rule(Css(), ".tm-col-filter-input::placeholder");
        body.Should().Contain("overflow: hidden");
        body.Should().Contain("text-overflow: ellipsis");
        body.Should().Contain("white-space: nowrap");
    }

    [Theory]
    [InlineData(".tm-overlay-panel-sheet .tm-col-filter-input")]
    [InlineData(".tm-overlay-panel-sheet .tm-col-filter-select")]
    [InlineData(".tm-overlay-panel-sheet .tm-view-item")]
    [InlineData(".tm-overlay-panel-sheet .tm-view-edit-btn")]
    [InlineData(".tm-overlay-panel-sheet .tm-view-delete-btn")]
    [InlineData(".tm-overlay-panel-sheet .tm-view-manager-footer .tm-btn")]
    [InlineData(".tm-overlay-panel-sheet .tm-data-table-cards-sheet__footer .tm-btn")]
    public void SheetControls_AreAtLeastATouchTarget(string selector)
    {
        var css = Css();
        var rule = Regex.Matches(css, @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.None, Timeout)
            .Where(m => m.Groups["sel"].Value.Split(',').Any(s => s.Trim() == selector))
            .Select(m => m.Groups["body"].Value)
            .FirstOrDefault(b => b.Contains("min-block-size: var(--tm-touch-target)", StringComparison.Ordinal));
        rule.Should().NotBeNull($"{selector} must be at least var(--tm-touch-target) tall inside a sheet");
    }

    [Fact]
    public void SheetFooterDone_IsHiddenBecauseTheSheetHeaderCarriesIt()
    {
        Rule(Css(), ".tm-overlay-panel-sheet .tm-btn.tm-data-table-cards-sheet__done").Should().Contain("display: none");
    }

    [Fact]
    public void CardsGroupHint_IsHiddenWhereTheHeaderIsGone()
    {
        var css = Css();
        var at = css.IndexOf(".tm-data-table-wrapper--cards .tm-data-table-group-placeholder", StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0);
        Regex.IsMatch(css[..at], @"@container\s+tm-data-table\s*\(width\s*<\s*640px\)\s*\{\s*$", RegexOptions.None, Timeout)
            .Should().BeTrue("the hint disappears only where the cards layout is active (narrow container)");
        css[at..].Should().Contain("display: none");
    }

    // ── Cleanup round (post-phase 14): the deferred polish items ──

    [Fact]
    public void Rtl_PinGlyph_IsMirrored()
    {
        var body = Rule(Css(), "[dir=\"rtl\"] .tm-col-pin-btn > span");
        body.Should().Contain("transform: scaleX(-1)",
            "the pin glyph points at the inline-end edge, which RTL mirrors");
    }

    [Fact]
    public void Rtl_PlainCells_AreStartAligned()
    {
        Rule(Css(), "[dir=\"rtl\"] .tm-data-table th:not(.tm-text-right):not(.tm-text-center)")
            .Should().Contain("text-align: start");
    }

    [Fact]
    public void CardsMode_EmptyStateCell_IsCentred()
    {
        var css = Css();
        var at = css.IndexOf(".tm-data-table-wrapper--cards .tm-data-table td.tm-data-table-empty-cell", StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0);
        var body = css[css.IndexOf('{', at)..css.IndexOf('}', at)];
        body.Should().Contain("display: block").And.Contain("text-align: center");
    }

    [Fact]
    public void LoadingSkeletonRows_CarryASizingRule()
    {
        Rule(Css(), ".tm-data-table-loading-row td").Should().Contain("block-size");
    }

    // ── Fix round 3: the deferred "Open" polish items ──

    private static string MvlCss() => ThemeCss.ComponentCss("_multi-view-list.css");

    [Fact]
    public void CardsCheckbox_DocksIntoTheCardCorner_InsteadOfItsOwnRow()
    {
        var css = Css();
        var at = css.IndexOf(".tm-data-table-wrapper--cards .tm-data-table tbody td.tm-col-check", StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0);
        var body = css[css.IndexOf('{', at)..css.IndexOf('}', at)];
        body.Should().Contain("position: absolute", "a full card row spent on an empty checkbox was the defect");
        body.Should().Contain("inset-inline-end");
        body.Should().Contain("var(--tm-touch-target)");
    }

    [Fact]
    public void CardsTitle_ReservesTheCheckboxCorner()
    {
        var css = Css();
        // The Title cell has an earlier grid-column rule; the padding reservation is the LAST one.
        var at = css.LastIndexOf(".tm-data-table-wrapper--cards .tm-data-table tbody td[data-card-role=\"Title\"]", StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0);
        css[css.IndexOf('{', at)..css.IndexOf('}', at)].Should().Contain("padding-inline-end");
    }

    [Fact]
    public void PortaledCheckboxes_FollowTheThemeAccent()
    {
        var match = Regex.Matches(Css(), @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.None, Timeout)
            .Where(m => m.Groups["sel"].Value.Contains(".tm-column-picker-item input", StringComparison.Ordinal))
            .Select(m => m.Groups["body"].Value)
            .FirstOrDefault(b => b.Contains("accent-color: var(--tm-color-primary)", StringComparison.Ordinal));
        match.Should().NotBeNull("native checkboxes rendered unthemed (white) in dark mode");
    }

    [Fact]
    public void MvlToolbar_ViewsToggleMatchesItsNeighbours()
    {
        var toggle = MvlCss();
        toggle.Should().Contain(".tm-mvl-toolbar-right .tm-view-manager-toggle");
        Rule(toggle, ".tm-mvl-toolbar-right .tm-view-manager-toggle")
            .Should().Contain("var(--tm-input-height-sm)");
        Rule(toggle, ".tm-mvl-switch-table")
            .Should().Contain("min-block-size: var(--tm-input-height-sm)",
                "the switcher buttons were content-height next to a full-height .tm-btn");
    }

    [Fact]
    public void BareDropdownTrigger_FollowsTheThemeTextToken()
    {
        Rule(ThemeCss.ComponentCss("_dropdown.css"), ".tm-dropdown-trigger")
            .Should().Contain("color: var(--tm-text-primary)",
                "a bare trigger inherits the host page's ink, which was light-theme colour in dark");
    }

    [Fact]
    public void DarkThemeRoot_DeclaresColorSchemeDark()
    {
        // accent-color only tints the CHECKED state; the unchecked box, native scrollbars and form
        // control chrome are drawn by the browser and follow `color-scheme`. Without this the
        // unchecked card checkbox rendered as a bright white square on the dark surface.
        var dark = File.ReadAllText(ThemeCss.CssPath("tokens-dark.css"));
        var light = File.ReadAllText(ThemeCss.CssPath("tokens.css"));

        var darkRoot = Rule(dark, "[data-theme=\"dark\"]");
        darkRoot.Should().Contain("color-scheme: dark",
            "the browser-drawn unchecked checkbox and scrollbars follow color-scheme, not accent-color");

        Rule(light, ":root").Should().Contain("color-scheme: light",
            "the light default must be explicit so the dark override is a real switch");
    }
}
