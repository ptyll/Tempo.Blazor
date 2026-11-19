using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Fáze 10a (HeatControl mock findings) — the three <c>MobileLayout=Cards</c> / <c>CardMode</c> defects
/// that no markup assertion can see, because the class is on the element and the rule is in the file:
/// <list type="number">
/// <item>The mobile Sort / Filters controls carry <c>tm-data-table-mobile-only</c> (<c>display: none</c>), but
/// <c>.tm-dropdown-wrapper { display: inline-flex }</c> (imported LATER) and
/// <c>.tm-data-table-cards-toolbar__filter { display: inline-flex }</c> have the same specificity, so the
/// later rule won and both controls showed on a desktop where the header already sorts.</item>
/// <item>Card cells kept the inline <c>width</c> / <c>min-width</c> of their column, so a 72px column
/// squeezed the label of its card row (an "ID" label broke letter by letter).</item>
/// <item>The scroll box kept its border, radius and negative bleed in a card list, framing every card in
/// one more box — although the cards are the surface.</item>
/// </list>
/// The cascade is resolved out of the SHIPPED bundle (the only text where the import order of two
/// component files is visible). The table's own width is a CSS container query, which the resolver cannot
/// evaluate, so the container header is rewritten to the equivalent viewport query for the reading — the
/// rules inside are untouched, which is what is under test.
/// </summary>
public sealed class DataTableCardsLayoutCssTests
{
    private const string ContainerHeader = "@container tm-data-table (width < 640px)";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static readonly CssCascade.MediaContext Wide = new(WidthPx: 1440);
    private static readonly CssCascade.MediaContext Narrow = new(WidthPx: 390);

    /// <summary>The bundle with the table's container query read as the viewport query it stands in for.</summary>
    private static string Bundle()
    {
        var bundle = ThemeCss.BundledCss();
        bundle.Should().Contain(ContainerHeader, "the card rules sit under the table's own container query");
        return bundle.Replace(ContainerHeader, "@media (max-width: 639px)", StringComparison.Ordinal);
    }

    private static readonly CssCascade.Element CardsWrapper = new(
        "div",
        "tm-data-table-wrapper",
        "tm-data-table-wrapper--card",
        "tm-data-table-wrapper--cards",
        "tm-data-table-wrapper--container");

    private static readonly CssCascade.Element CardModeWrapper = new(
        "div",
        "tm-data-table-wrapper",
        "tm-data-table-wrapper--card",
        "tm-data-table-wrapper--container");

    private static readonly CssCascade.Element Toolbar = new("div", "tm-data-table-toolbar");
    private static readonly CssCascade.Element ToolbarRight = new("div", "tm-data-table-toolbar-right");

    /// <summary>The TmDropdown root that renders the Sort control: <c>tm-dropdown-wrapper @Class</c>.</summary>
    private static readonly CssCascade.Element SortControl = new(
        "div", "tm-dropdown-wrapper", "tm-data-table-cards-toolbar__sort", "tm-data-table-mobile-only");

    /// <summary>The Filters button the table renders next to it.</summary>
    private static readonly CssCascade.Element FilterControl = new(
        "button",
        "tm-btn",
        "tm-btn-outline",
        "tm-btn-sm",
        "tm-data-table-cards-toolbar__filter",
        "tm-data-table-mobile-only");

    private static string Display(CssCascade.Element wrapper, CssCascade.Element control, CssCascade.MediaContext media)
    {
        var resolved = CssCascade.Resolve(Bundle(), [wrapper, Toolbar, ToolbarRight, control], "display", media: media);
        resolved.Unmodelled.Should().BeEmpty("a selector the probe cannot read is unmeasurable, not fine");
        resolved.Value.Should().NotBeNull();
        return resolved.Value!;
    }

    [Theory]
    [InlineData("sort")]
    [InlineData("filter")]
    public void MobileOnlyControl_IsHiddenInAWideContainer_EvenAgainstItsOwnDisplayRule(string which)
    {
        var control = which == "sort" ? SortControl : FilterControl;

        Display(CardsWrapper, control, Wide).Should().Be(
            "none",
            "a later `display: inline-flex` of the same specificity (the dropdown wrapper, the filter button's own "
            + "layout rule) must not reveal a control that exists for a narrow container only");
    }

    [Theory]
    [InlineData("sort")]
    [InlineData("filter")]
    public void MobileOnlyControl_IsShownInANarrowContainer(string which)
    {
        var control = which == "sort" ? SortControl : FilterControl;

        Display(CardsWrapper, control, Narrow).Should().Be("inline-flex");
    }

    [Theory]
    [InlineData("tm-data-table-wrapper--cards")]
    [InlineData("tm-data-table-wrapper--card")]
    public void ScrollBox_KeepsItsFrameInAWideContainer(string layoutClass)
    {
        var wrapper = layoutClass == "tm-data-table-wrapper--cards" ? CardsWrapper : CardModeWrapper;
        var scroll = new CssCascade.Element("div", "tm-data-table-scroll");

        Resolve(wrapper, scroll, "border", Wide).Should().Be("1px solid var(--tm-border-color)",
            "a wide container renders the table, which the frame belongs to");
        Resolve(wrapper, scroll, "border-radius", Wide).Should().Be("var(--tm-radius-md)");
    }

    [Theory]
    [InlineData("tm-data-table-wrapper--cards")]
    [InlineData("tm-data-table-wrapper--card")]
    public void ScrollBox_HasNoFrameAroundTheCards_InANarrowContainer(string layoutClass)
    {
        var wrapper = layoutClass == "tm-data-table-wrapper--cards" ? CardsWrapper : CardModeWrapper;
        var scroll = new CssCascade.Element("div", "tm-data-table-scroll");

        Resolve(wrapper, scroll, "border", Narrow).Should().Be("0",
            "the cards are the surface — a frame around all of them is a box inside a box");
        Resolve(wrapper, scroll, "border-radius", Narrow).Should().Be("0");
    }

    [Fact]
    public void ScrollBox_DropsItsBleed_InANarrowContainer_SoTheCardsKeepTheWrapperPadding()
    {
        var scroll = new CssCascade.Element("div", "tm-data-table-scroll", "tm-data-table-virtual-scroll");

        Resolve(CardsWrapper, scroll, "margin", Wide).Should().Contain("-1 * var(--tm-data-table-bleed");
        Resolve(CardsWrapper, scroll, "margin", Narrow).Should().Be("0");
        Resolve(CardsWrapper, scroll, "width", Narrow).Should().Be("100%");
    }

    [Fact]
    public void ScrollBox_OutsideTheCardLayouts_IsUntouched_InANarrowContainer()
    {
        var plain = new CssCascade.Element("div", "tm-data-table-wrapper", "tm-data-table-wrapper--responsive", "tm-data-table-wrapper--container");
        var scroll = new CssCascade.Element("div", "tm-data-table-scroll");

        Resolve(plain, scroll, "border", Narrow).Should().Be("1px solid var(--tm-border-color)",
            "MobileLayout=Scroll keeps its table, and the frame belongs to the table");
    }

    /// <summary>
    /// An inline <c>width</c> beats any selector, so the cell of a 72px column stayed 72px inside a card.
    /// The card rule may only beat it with <c>!important</c> — scoped to the cards in a narrow container,
    /// where a wide container keeps the column widths the table layout needs (a td without a width would
    /// size its column to its content).
    /// </summary>
    [Fact]
    public void CardCells_OverrideTheInlineColumnWidth_OnlyInsideTheNarrowContainerQuery()
    {
        var css = ThemeCss.ComponentCss("_data-table.css");

        var blocks = Blocks(css, Regex.Escape(ContainerHeader) + @"\s*\{").ToList();
        blocks.Should().NotBeEmpty();

        var rule = blocks.SelectMany(Rules)
            .FirstOrDefault(r => r.Selector.Contains(".tm-data-table-wrapper--card .tm-data-table tbody td", StringComparison.Ordinal)
                                 && r.Body.Contains("!important", StringComparison.Ordinal));

        rule.Should().NotBe(default((string, string)), "the card cell must win against the column's inline width");
        rule.Selector.Should().Contain(".tm-data-table-wrapper--cards .tm-data-table tbody td");
        rule.Selector.Should().Contain(":not(.tm-col-check)", "the docked checkbox cell carries no column style and stays shrink-to-fit");
        rule.Body.Should().Contain("width: 100% !important");
        rule.Body.Should().Contain("min-width: 0 !important");

        // Outside the container query (the blocks removed) nothing may force a cell width: a wide
        // container renders the table, where the inline widths ARE the column sizing.
        var outside = css;
        foreach (var block in blocks)
        {
            outside = outside.Replace(block, string.Empty, StringComparison.Ordinal);
        }

        outside.Should().NotContain("width: 100% !important", "outside the container query the column widths must stand");
    }

    [Fact]
    public void MobileOnlyRule_IsDeclaredWithAnOwnershipCompound_NotAsABareClass()
    {
        var css = ThemeCss.ComponentCss("_data-table.css");

        css.Should().Contain(".tm-data-table-wrapper .tm-data-table-mobile-only { display: none; }");
        Blocks(css, Regex.Escape(ContainerHeader) + @"\s*\{")
            .Any(block => block.Contains(".tm-data-table-wrapper .tm-data-table-mobile-only { display: inline-flex; }", StringComparison.Ordinal))
            .Should().BeTrue("the narrow reveal needs the same specificity as the hide, or the hide would win back");
    }

    /// <summary>
    /// The title cell reserves the card's inline-end corner for the docked row checkbox. A table without
    /// <c>Selectable</c> renders no checkbox, so reserving it there only pushed the title's VALUE column
    /// 22px to the left of every other card row's value (found on the Fáze 10a screenshots): the reserve
    /// belongs to a row that actually has the checkbox cell.
    /// </summary>
    [Fact]
    public void TitleCell_ReservesTheCheckboxCorner_OnlyOnARowThatHasTheCheckbox()
    {
        var css = ThemeCss.ComponentCss("_data-table.css");
        var rules = Blocks(css, Regex.Escape(ContainerHeader) + @"\s*\{").SelectMany(Rules).ToList();

        var reserving = rules
            .Where(r => r.Body.Contains("padding-inline-end: calc(var(--tm-touch-target)", StringComparison.Ordinal))
            .ToList();

        reserving.Should().NotBeEmpty("the title still must not run under the docked checkbox when there is one");
        reserving.Should().OnlyContain(
            r => r.Selector.Contains("tbody tr:has(> td.tm-col-check) td[data-card-role=\"Title\"]", StringComparison.Ordinal),
            "a Title cell of a row without a checkbox keeps the full card width, so its value lines up with the other rows' values");
    }

    /// <summary>
    /// Narrowing the title reserve to a row that has the checkbox is a visible layout change, so the changelog
    /// and the card-layout guide both name it (a card of a table without <c>Selectable</c> no longer reserves
    /// the corner).
    /// </summary>
    [Fact]
    public void TitleReserveScoping_IsDocumentedInTheChangelogAndTheGuide()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        const string Sentence = "The card title reserves the checkbox corner only when the row has one.";

        var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
        var guide = File.ReadAllText(Path.Combine(root, "docs", "data-table-redesign.md"));

        changelog.Should().Contain(Sentence).And.Contain("td.tm-col-check");
        guide.Should().Contain(Sentence).And.Contain("td.tm-col-check");
    }

    /// <summary>
    /// The cards toolbar puts "Sort" and "Choose columns" side by side. The coarse-pointer rule gave the Sort /
    /// Filters / dropdown-trigger controls the 44px target but not the column-picker toggle, so on a phone the
    /// two buttons stood next to each other at different heights (found on the Fáze 10a phone-390 shots).
    /// </summary>
    [Fact]
    public void ColumnPickerToggle_GetsTheCoarsePointerTouchTarget_LikeTheOtherToolbarButtons()
    {
        var bundle = ThemeCss.BundledCss();

        var rule = Blocks(bundle, @"@media\s*\(\s*pointer\s*:\s*coarse\s*\)\s*\{")
            .SelectMany(Rules)
            .FirstOrDefault(r => r.Selector.Contains(".tm-data-table-cards-toolbar__sort", StringComparison.Ordinal));

        rule.Should().NotBe(default((string, string)), "the cards-toolbar buttons have a coarse-pointer target rule");
        rule.Selector.Should().Contain(".tm-data-table-wrapper .tm-column-picker-toggle");
        // The bundle is minified: compare the declarations without whitespace.
        var body = rule.Body.Replace(" ", string.Empty, StringComparison.Ordinal);
        body.Should().Contain("min-block-size:var(--tm-touch-target)");
        body.Should().Contain("min-inline-size:var(--tm-touch-target)");

        var source = ThemeCss.ComponentCss("_data-table.css");
        Blocks(source, @"@media\s*\(\s*pointer\s*:\s*coarse\s*\)\s*\{")
            .SelectMany(Rules)
            .Any(r => r.Selector.Contains(".tm-data-table-wrapper .tm-column-picker-toggle", StringComparison.Ordinal))
            .Should().BeTrue("the source file carries the same rule the bundle was built from");
    }

    /// <summary>The demo accent is drawn with the border-width token (times three), never a literal pixel size.</summary>
    [Fact]
    public void DemoCurrentRowAccent_UsesTokensOnly()
    {
        var css = File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor.Demo.SharedUI", "Pages", "DataTablePage.razor.css"));

        css.Should().Contain("box-shadow: inset calc(var(--tm-border-width) * 3) 0 0 var(--tm-color-primary);");
        Regex.IsMatch(css, @"(?<![\w-])\d+px", RegexOptions.None, Timeout).Should().BeFalse("no hardcoded pixel size in the demo CSS");
    }

    private static string Resolve(CssCascade.Element wrapper, CssCascade.Element scroll, string property, CssCascade.MediaContext media)
    {
        var resolved = CssCascade.Resolve(Bundle(), [wrapper, scroll], property, media: media);
        resolved.Unmodelled.Should().BeEmpty("a selector the probe cannot read is unmeasurable, not fine");
        resolved.Value.Should().NotBeNull($"the stylesheet must declare {property} for the scroll box");
        return resolved.Value!;
    }

    /// <summary>The brace-matched body of every at-rule whose header matches <paramref name="headerPattern"/>.</summary>
    private static IEnumerable<string> Blocks(string css, string headerPattern)
    {
        foreach (Match match in Regex.Matches(css, headerPattern, RegexOptions.None, Timeout))
        {
            var depth = 1;
            var index = match.Index + match.Length;
            var start = index;
            while (index < css.Length && depth > 0)
            {
                depth += css[index] == '{' ? 1 : css[index] == '}' ? -1 : 0;
                index++;
            }

            yield return css[start..(index - 1)];
        }
    }

    private static IEnumerable<(string Selector, string Body)> Rules(string block)
    {
        foreach (Match match in Regex.Matches(block, @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.None, Timeout))
        {
            yield return (ThemeCss.Normalise(match.Groups["sel"].Value), ThemeCss.Normalise(match.Groups["body"].Value));
        }
    }
}
