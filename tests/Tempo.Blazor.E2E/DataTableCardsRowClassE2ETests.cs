using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DataTableE2EKit;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 10a (HeatControl mock findings) on the <c>/data-table</c> "Row class" section: the card layout of a
/// table with a 72px <c>ID</c> column and the host-styled current row. Every spec asserts STATE (computed
/// style, geometry, DOM) in a real browser at the width that matters; the screenshots land in
/// <c>__screenshots__/data-table/</c> for UX review, light and dark.
/// </summary>
[TestClass]
public sealed class DataTableCardsRowClassE2ETests : WasmTestBase
{
    private const string Wide = "[data-testid='dt-rowclass-wide']";
    private const string Cards = "[data-testid='dt-rowclass-cards']";

    private async Task<IPage> OpenSectionAsync(int width, int height, string theme = "light")
    {
        var page = await OpenAsync(this, "/data-table", new Options(width, height, Theme: theme));
        await PrepareForCaptureAsync(page);
        var section = page.Locator("[data-testid='dt-rowclass-section']");
        await section.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(page.Locator($"{Wide} tbody tr").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(page.Locator($"{Cards} tbody tr").First).ToBeVisibleAsync();
        return page;
    }

    private static async Task ShotAsync(ILocator element, string name)
    {
        await element.ScrollIntoViewIfNeededAsync();
        await element.Page.Mouse.MoveAsync(0, 0);
        await SettleAsync(element.Page);
        var dir = Path.Combine(FindRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "data-table");
        Directory.CreateDirectory(dir);
        await element.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(dir, name + ".png"), Timeout = 30_000 });
    }

    private static Task<string> StyleAsync(ILocator element, string property) =>
        element.EvaluateAsync<string>("(el, p) => getComputedStyle(el)[p]", property);

    [TestMethod, TestCategory("WASM")]
    public async Task MobileOnlySortAndFilters_AreHiddenInAWideContainer_AndShownInANarrowOne()
    {
        var page = await OpenSectionAsync(1440, 1100);

        // 1440 viewport, the wide table fills the page: the header sorts, so the mobile controls stay away.
        var wideSort = page.Locator($"{Wide} .tm-data-table-cards-toolbar__sort");
        Assert.AreEqual(1, await wideSort.CountAsync(), "the control is in the markup (Cards + sortable columns)");
        Assert.AreEqual("none", await StyleAsync(wideSort, "display"),
            "the later-imported .tm-dropdown-wrapper display must not reveal the mobile Sort on a desktop");
        await Assertions.Expect(wideSort).Not.ToBeVisibleAsync();
        await Assertions.Expect(page.Locator($"{Wide} thead")).ToBeVisibleAsync();

        // The 24rem container of the SAME page is narrow, so the mobile Sort appears there - and the header is gone.
        var narrowSort = page.Locator($"{Cards} .tm-data-table-cards-toolbar__sort");
        // A flex item blockifies inline-flex to flex; what matters is that it is not none.
        CollectionAssert.Contains(new[] { "flex", "inline-flex" }, await StyleAsync(narrowSort, "display"));
        await Assertions.Expect(narrowSort).ToBeVisibleAsync();
        Assert.AreEqual("none", await StyleAsync(page.Locator($"{Cards} thead"), "display"));

        // Redesign playground, switched to Cards: it also has a Filters button (filterable columns).
        await SelectAsync(page, "dt-redesign-mobile-layout", "Cards");
        var redesign = page.Locator("[data-testid='dt-redesign-table']");
        await Assertions.Expect(redesign.Locator(".tm-data-table-wrapper--cards")).ToBeVisibleAsync();
        var filters = redesign.Locator(".tm-data-table-cards-toolbar__filter");
        Assert.AreEqual(1, await filters.CountAsync());
        Assert.AreEqual("none", await StyleAsync(filters, "display"), "the Filters button is mobile-only too");
        await Assertions.Expect(filters).Not.ToBeVisibleAsync();
    }

    [TestMethod, TestCategory("WASM")]
    public async Task CardCell_IsAsWideAsItsCard_ButTheWideTableKeepsTheColumnWidth()
    {
        var page = await OpenSectionAsync(1440, 1100);

        // Wide container: the table, and the ID column keeps the 72px the host asked for.
        var idHeader = page.Locator($"{Wide} thead th").First;
        var headerWidth = (await BoxAsync(idHeader)).Width;
        Assert.IsTrue(headerWidth >= 71 && headerWidth <= 74, $"the ID column of the wide table is 72px, was {headerWidth}");
        var wideCell = page.Locator($"{Wide} tbody tr").First.Locator("td[data-label='ID']");
        Assert.AreEqual("table-cell", await StyleAsync(wideCell, "display"));

        // Narrow container: the card cell spans the card, so the label has room.
        var card = page.Locator($"{Cards} tbody tr").First;
        var cell = card.Locator("td[data-label='ID']");
        var cardBox = await BoxAsync(card);
        var cellBox = await BoxAsync(cell);
        Assert.IsTrue(cellBox.Width > cardBox.Width * 0.8,
            $"the ID cell ({cellBox.Width}px) must span its card ({cardBox.Width}px), not keep the 72px column width");
        Assert.IsTrue(cellBox.Width > 150, $"72px cell squeezed the label: {cellBox.Width}px");

        // The label "ID" is one line (a 2-letter word broke letter by letter at 36px).
        var labelLines = await cell.EvaluateAsync<double>(
            "el => { const cs = getComputedStyle(el, '::before'); const lh = parseFloat(cs.lineHeight) || parseFloat(cs.fontSize) * 1.5; return el.getBoundingClientRect().height / lh; }");
        Assert.IsTrue(labelLines < 2.2, $"the ID cell spans {labelLines:0.0} lines - the label wrapped");

        // The inline column style is still on the markup (the wide table needs it) - the card rule beats it.
        StringAssert.Contains(await cell.GetAttributeAsync("style") ?? string.Empty, "width:72px");
    }

    [TestMethod, TestCategory("WASM")]
    public async Task ScrollBox_HasNoFrameAroundTheCards_ButKeepsItAroundTheWideTable()
    {
        var page = await OpenSectionAsync(1440, 1100);

        var wideScroll = page.Locator($"{Wide} .tm-data-table-scroll");
        Assert.AreEqual("1px", await StyleAsync(wideScroll, "borderTopWidth"), "the wide table keeps its frame");
        Assert.AreNotEqual("0px", await StyleAsync(wideScroll, "borderTopLeftRadius"));

        var cardScroll = page.Locator($"{Cards} .tm-data-table-scroll");
        Assert.AreEqual("0px", await StyleAsync(cardScroll, "borderTopWidth"), "the cards are the surface - no extra box around them");
        Assert.AreEqual("0px", await StyleAsync(cardScroll, "borderTopLeftRadius"));
        var wrapper = await BoxAsync(page.Locator($"{Cards} .tm-data-table-wrapper"));
        var scroll = await BoxAsync(cardScroll);
        Assert.IsTrue(scroll.X >= wrapper.X - 0.5 && scroll.X + scroll.Width <= wrapper.X + wrapper.Width + 0.5,
            $"the card list stays inside the wrapper (scroll {scroll.X}..{scroll.X + scroll.Width}, wrapper {wrapper.X}..{wrapper.X + wrapper.Width})");

        // A real card keeps ITS own border: the frame moved to where it belongs, it did not vanish.
        Assert.AreEqual("1px", await StyleAsync(page.Locator($"{Cards} tbody tr").First, "borderTopWidth"));
    }

    [TestMethod, TestCategory("WASM")]
    public async Task RowClass_MarksOnlyTheCurrentRow_FollowsAClickAndASort()
    {
        var page = await OpenSectionAsync(1440, 1100);
        var current = page.Locator($"{Wide} tbody tr.demo-dt-row--current");

        await Assertions.Expect(current).ToHaveCountAsync(1);
        await Assertions.Expect(current).ToContainTextAsync("Grace Hopper");
        await Assertions.Expect(page.Locator("[data-testid='dt-rowclass-current']")).ToContainTextAsync("Grace Hopper");

        // The class is VISIBLE: its background differs from a neighbouring row and it carries the accent.
        var plainRow = page.Locator($"{Wide} tbody tr:not(.demo-dt-row--current)").First;
        await page.Mouse.MoveAsync(0, 0);
        Assert.AreNotEqual(await StyleAsync(plainRow, "backgroundColor"), await StyleAsync(current, "backgroundColor"));
        Assert.AreNotEqual("none", await StyleAsync(current, "boxShadow"));

        // A class is only visual: the same row is also exposed programmatically.
        await Assertions.Expect(current).ToHaveAttributeAsync("aria-current", "true");
        await Assertions.Expect(page.Locator($"{Wide} tbody tr[aria-current]")).ToHaveCountAsync(1);

        // A click moves it (host state), in the table and in the card view of the same state.
        await page.Locator($"{Wide} tbody tr", new() { HasText = "Alan Turing" }).Locator("td").Nth(1).ClickAsync();
        await Assertions.Expect(current).ToHaveCountAsync(1);
        await Assertions.Expect(current).ToContainTextAsync("Alan Turing");
        await Assertions.Expect(page.Locator($"{Cards} tbody tr.demo-dt-row--current")).ToContainTextAsync("Alan Turing");
        await Assertions.Expect(page.Locator("[data-testid='dt-rowclass-current']")).ToContainTextAsync("Alan Turing");
        await Assertions.Expect(page.Locator($"{Wide} tbody tr[aria-current]")).ToHaveCountAsync(1);
        await Assertions.Expect(current).ToHaveAttributeAsync("aria-current", "true");

        // Sorting reorders the rows; the class stays on the SAME item, not on a position.
        await page.Locator($"{Wide} thead th", new() { HasText = "Name" }).Locator("button.tm-th-sort").ClickAsync();
        await Assertions.Expect(page.Locator($"{Wide} tbody tr").First).ToContainTextAsync("Ada Lovelace");
        await Assertions.Expect(current).ToHaveCountAsync(1);
        await Assertions.Expect(current).ToContainTextAsync("Alan Turing");
        await page.Locator($"{Wide} thead th", new() { HasText = "Name" }).Locator("button.tm-th-sort").ClickAsync();
        await Assertions.Expect(page.Locator($"{Wide} tbody tr").First).ToContainTextAsync("Katherine Johnson");
        await Assertions.Expect(current).ToContainTextAsync("Alan Turing");
    }

    [TestMethod, TestCategory("WASM")]
    [DataRow("light")]
    [DataRow("dark")]
    public async Task Screenshots_WideTableAndCardContainer(string theme)
    {
        var page = await OpenSectionAsync(1440, 1100, theme);
        await Assertions.Expect(page.Locator($"{Wide} tbody tr.demo-dt-row--current")).ToHaveCountAsync(1);
        await ShotAsync(page.Locator("[data-testid='dt-rowclass-section'] .showcase-card"), $"data-table-fze10a-rowclass-{theme}");
        await ShotAsync(page.Locator(Cards), $"data-table-fze10a-cards-24rem-{theme}");

        // Redesign playground in Cards at the desktop width: the table, no stray mobile buttons.
        await SelectAsync(page, "dt-redesign-mobile-layout", "Cards");
        await SelectAsync(page, "dt-redesign-appearance", "Card");
        var redesign = page.Locator("[data-testid='dt-redesign-table']");
        await Assertions.Expect(redesign.Locator(".tm-data-table-wrapper--cards")).ToBeVisibleAsync();
        await ShotAsync(redesign, $"data-table-fze10a-desktop-cards-toolbar-{theme}");
    }

    [TestMethod, TestCategory("WASM")]
    [DataRow("light")]
    [DataRow("dark")]
    public async Task Phone390_CardsToolbarShowsAndTheCardsAreUnframed(string theme)
    {
        var page = await OpenAsync(this, "/data-table",
            new Options(390, 844, Touch: true, IsMobile: true, Theme: theme));
        await PrepareForCaptureAsync(page);
        var cards = page.Locator(Cards);
        await cards.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(cards.Locator("tbody tr").First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        CollectionAssert.Contains(new[] { "flex", "inline-flex" }, await StyleAsync(cards.Locator(".tm-data-table-cards-toolbar__sort"), "display"));
        await Assertions.Expect(cards.Locator(".tm-data-table-cards-toolbar__sort")).ToBeVisibleAsync();
        Assert.AreEqual("0px", await StyleAsync(cards.Locator(".tm-data-table-scroll"), "borderTopWidth"));
        var cell = await BoxAsync(cards.Locator("tbody tr").First.Locator("td[data-label='ID']"));
        var card = await BoxAsync(cards.Locator("tbody tr").First);
        Assert.IsTrue(cell.Width > card.Width * 0.8, "the ID cell spans the card on a phone too");
        // Coarse pointer: the Sort button and the column-picker toggle share the 44px target and the same height.
        var sortBox = await BoxAsync(cards.Locator(".tm-data-table-cards-toolbar__sort .tm-dropdown-trigger"));
        var pickerToggle = cards.Locator(".tm-column-picker-toggle");
        Assert.AreEqual(1, await pickerToggle.CountAsync(), "the column picker toggle is in the cards toolbar");
        var pickerBox = await BoxAsync(pickerToggle);
        Assert.IsTrue(sortBox.Height >= 43.5, $"Sort is a 44px target, was {sortBox.Height}");
        Assert.IsTrue(pickerBox.Height >= 43.5, $"the column-picker toggle is a 44px target, was {pickerBox.Height}");
        Assert.IsTrue(Math.Abs(sortBox.Height - pickerBox.Height) < 1, $"Sort ({sortBox.Height}) and Choose columns ({pickerBox.Height}) stand at the same height");
        await ShotAsync(cards, $"data-table-fze10a-phone-390-{theme}");
    }
}
