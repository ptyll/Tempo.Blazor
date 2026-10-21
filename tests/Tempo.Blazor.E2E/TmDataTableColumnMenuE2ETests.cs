using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for <c>ColumnMenuMode.Menu</c>: the kebab lives in the data column header, its menu offers
/// only what the column can do, and M / Escape drive it. bUnit covers the render contract; this
/// proves the overlay actually anchors and opens in a browser.
/// </summary>
[TestClass]
public class TmDataTableColumnMenuE2ETests : WasmTestBase
{
    [TestMethod]
    [TestCategory("WASM")]
    public async Task ColumnMenu_OpensFromHeader_Sorts_And_ClosesOnEscape()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}/data-table",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var section = page.Locator("[data-testid='dt-column-menu-section']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        var kebab = section.Locator(".tm-data-table-col-menu[data-column-key='Name']");
        await kebab.WaitForAsync();
        Assert.AreEqual("Column options: Name", await kebab.GetAttributeAsync("aria-label"));
        Assert.AreEqual(0, await section.Locator(".tm-col-pin-btn").CountAsync(),
            "Menu mode replaces the standalone pin button");

        await kebab.ClickAsync();

        var menu = section.Locator("[role='menu']");
        await menu.WaitForAsync();
        var actions = await menu.Locator("[role^='menuitem']").EvaluateAllAsync<string[]>(
            "items => items.map(i => i.getAttribute('data-action'))");
        CollectionAssert.AreEquivalent(
            new[] { "sort-asc", "sort-desc", "pin-left", "pin-right", "group", "hide", "filter" },
            actions);
        Assert.AreEqual("menuitemradio", await menu.Locator("[data-action='sort-asc']").GetAttributeAsync("role"));

        // ArrowDown walks the menu (WAI-ARIA APG): focus leaves the first item for the second.
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.getAttribute('data-action') === 'sort-asc'",
            null, new PageWaitForFunctionOptions { Timeout = 10000 });
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.getAttribute('data-action') === 'sort-desc'",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });

        await menu.Locator("[data-action='sort-asc']").ClickAsync();
        await section.Locator("th[aria-sort='ascending']").WaitForAsync();

        await kebab.ClickAsync();
        await menu.WaitForAsync();
        await page.Keyboard.PressAsync("Escape");
        Assert.AreEqual(0, await section.Locator("[role='menu']").CountAsync());

        // Escape returns focus to the column header (its sort button), not to <body>.
        var header = section.Locator("th:has(.tm-data-table-col-menu[data-column-key='Name']) .tm-th-sort");
        await Assertions.Expect(header).ToBeFocusedAsync(new() { Timeout = 5000 });
    }

    private const string Section = "[data-testid='dt-column-menu-section']";

    private async Task<IPage> OpenAsync(int width, int height, bool touch)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/data-table",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var section = page.Locator(Section);
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await page.EvaluateAsync(
            "() => document.querySelector(\"[data-testid='dt-column-menu-section'] .tm-data-table\").scrollIntoView({ block: 'center', behavior: 'instant' })");
        await page.WaitForTimeoutAsync(300);
        return page;
    }

    /// <summary>Rects of two locators must not overlap (bounding-box intersection test).</summary>
    private static async Task AssertNoOverlapAsync(ILocator a, ILocator b, string because)
    {
        var ab = await a.BoundingBoxAsync() ?? throw new AssertFailedException($"{because}: first rect missing");
        var bb = await b.BoundingBoxAsync() ?? throw new AssertFailedException($"{because}: second rect missing");
        var overlaps = ab.X < bb.X + bb.Width && bb.X < ab.X + ab.Width
                    && ab.Y < bb.Y + bb.Height && bb.Y < ab.Y + ab.Height;
        Assert.IsFalse(overlaps, because);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task ColumnMenu_Kebab_DoesNotCoverTheSortButton_OnARightAlignedColumn()
    {
        var page = await OpenAsync(1440, 1100, touch: false);

        // Sorted, right-aligned column: the worst case for the overlaying kebab (ux finding 1+3).
        var scoreHeader = page.Locator($"{Section} th:has(.tm-data-table-col-menu[data-column-key='Score'])");
        await scoreHeader.Locator(".tm-data-table-col-menu[data-column-key='Score']").ClickAsync();
        await page.Locator("[role='menu']").WaitForAsync();
        await page.Locator("[role='menu'] [data-action='sort-asc']").ClickAsync();
        await scoreHeader.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
        Assert.IsTrue((await scoreHeader.GetAttributeAsync("class"))!.Contains("tm-col-has-menu"));

        await AssertNoOverlapAsync(
            scoreHeader.Locator(".tm-th-sort"),
            scoreHeader.Locator(".tm-data-table-col-menu[data-column-key='Score']"),
            "the kebab must not intersect the sort button of a sorted, right-aligned column");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task ColumnMenu_Kebab_DoesNotCoverSortButtonOrResizeHandle_OnCoarsePointer()
    {
        var page = await OpenAsync(390, 844, touch: true);

        var scoreHeader = page.Locator($"{Section} th:has(.tm-data-table-col-menu[data-column-key='Score'])");
        await scoreHeader.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });

        await AssertNoOverlapAsync(
            scoreHeader.Locator(".tm-th-sort"),
            scoreHeader.Locator(".tm-data-table-col-menu[data-column-key='Score']"),
            "on touch the always-visible 44px kebab must not cover the sort button");

        await AssertNoOverlapAsync(
            scoreHeader.Locator(".tm-data-table-col-menu[data-column-key='Score']"),
            scoreHeader.Locator(".tm-col-resize-handle"),
            "on touch the kebab and the widened resize handle must be separate touch targets");
    }
}
