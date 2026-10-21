using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DataTableE2EKit;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 13 (13.10) screenshot GENERATOR for the table / list redesign. It replaces the old full-page
/// <c>DataTablePhase4ScreenshotTests</c> / <c>DataTablePhase8ScreenshotTests</c> captures: every shot is a
/// single element (or a clip around an element plus its overlay), taken after the state it claims to show
/// has been ASSERTED, with fonts ready, reduced motion, a fixed en-US culture, the demo's fixed data and
/// the demo's fixed chrome hidden so it cannot overlap the subject.
/// Off unless <c>TM_WRITE_BASELINES=1</c>; output goes to <c>__screenshots__/data-table-redesign/{theme}/</c>.
/// </summary>
[TestClass]
public class DataTableRedesignBaselineScreenshots : BaselineGeneratorTestBase
{
    private static string OutputDir(string theme)
    {
        var dir = Path.Combine(FindRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "data-table-redesign", theme);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static Options ThemeOptions(string theme, int width, int height, bool touch = false, float dpr = 1) => new(
        width, height, Touch: touch, IsMobile: touch && width < 640, DeviceScaleFactor: dpr,
        Theme: theme == "dark" ? "dark" : "light", Indigo: theme == "indigo");

    private async Task<IPage> OpenThemedAsync(string theme, string route, int width, int height, bool touch = false, float dpr = 1)
    {
        var page = await OpenAsync(Browser, BaseUrl, route, ThemeOptions(theme, width, height, touch, dpr), RegisterContext);
        await PrepareForCaptureAsync(page);
        return page;
    }

    private static async Task ShotAsync(IPage page, ILocator element, string theme, string name)
    {
        await element.ScrollIntoViewIfNeededAsync();
        await page.Mouse.MoveAsync(0, 0);
        await SettleAsync(page);
        await element.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(OutputDir(theme), name + ".png"), Timeout = 30_000 });
    }

    /// <summary>An overlay lives in the top layer, outside its anchor's box: clip a viewport region around every given element.</summary>
    private static async Task ClipShotAsync(IPage page, string theme, string name, params ILocator[] elements)
    {
        await page.Mouse.MoveAsync(0, 0);
        await SettleAsync(page);
        double left = double.MaxValue, top = double.MaxValue, right = 0, bottom = 0;
        foreach (var element in elements)
        {
            var box = await BoxAsync(element);
            left = Math.Min(left, box.X);
            top = Math.Min(top, box.Y);
            right = Math.Max(right, box.X + box.Width);
            bottom = Math.Max(bottom, box.Y + box.Height);
        }

        var viewport = page.ViewportSize!;
        var x = Math.Max(0, left - 12);
        var y = Math.Max(0, top - 12);
        var clip = new Clip
        {
            X = (float)x,
            Y = (float)y,
            Width = (float)Math.Min(viewport.Width - x, right - left + 24),
            Height = (float)Math.Min(viewport.Height - y, bottom - top + 24),
        };
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(OutputDir(theme), name + ".png"), Clip = clip, Timeout = 30_000 });
    }

    private static async Task ViewportShotAsync(IPage page, string theme, string name)
    {
        await page.Mouse.MoveAsync(0, 0);
        await SettleAsync(page);
        // A viewport capture keeps touch emulation honest; FullPage would reset it.
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(OutputDir(theme), name + ".png"), FullPage = false, Timeout = 30_000 });
    }

    [TestMethod, TestCategory("WASM")]
    [DataRow("light")]
    [DataRow("dark")]
    [DataRow("indigo")]
    public async Task Desktop1440_TableExportColumnMenuGroupAndViews(string theme)
    {
        var page = await OpenThemedAsync(theme, "/data-table", 1440, 1000);
        await SelectAsync(page, "dt-redesign-appearance", "Card");
        await SelectAsync(page, "dt-redesign-column-menu", "Menu");
        await ToggleAsync(page, "dt-redesign-export-selected");
        await ToggleAsync(page, "dt-redesign-active-view-name");
        var table = page.Locator("[data-testid='dt-redesign-table']");
        await table.ScrollIntoViewIfNeededAsync();
        var wrapper = table.Locator(".tm-data-table-wrapper");
        await Assertions.Expect(wrapper).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("card-surface"));

        await ShotAsync(page, wrapper, theme, "desktop-1440-table-card");

        // Group zone with a chip: the "Group by" bar shows the active grouping.
        await table.Locator(".tm-data-table-add-grouping .tm-dropdown-trigger").ClickAsync();
        await page.Locator("[role='menuitem']", new() { HasText = "Role" }).First.ClickAsync();
        var zone = table.Locator(".tm-data-table-group-zone");
        await Assertions.Expect(zone.Locator(".tm-data-table-group-chip")).ToContainTextAsync("Role");
        await Assertions.Expect(table.Locator("tr.tm-data-table-group-row").First).ToBeVisibleAsync();
        await ShotAsync(page, zone, theme, "desktop-1440-group-zone");
        await zone.Locator(".tm-data-table-group-chip-remove").ClickAsync();

        // Export menu with the scope switch (two rows selected so "Selected (2)" is enabled).
        var checks = table.Locator("tbody td input[type='checkbox']");
        await checks.Nth(0).CheckAsync();
        await checks.Nth(1).CheckAsync();
        await table.Locator(".tm-data-table__export .tm-dropdown-trigger").ClickAsync();
        var menu = page.Locator(".tm-dropdown-menu");
        await Assertions.Expect(menu).ToBeVisibleAsync();
        await Assertions.Expect(menu.Locator("[data-export-scope='selected']")).ToContainTextAsync("(2)");
        await Assertions.Expect(menu.Locator("[data-export-scope='selected']")).Not.ToHaveAttributeAsync("aria-disabled", "true");
        await ClipShotAsync(page, theme, "desktop-1440-export-menu-scope", table.Locator(".tm-data-table-toolbar"), menu);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(menu).ToHaveCountAsync(0);

        // Column menu open on the Name header.
        await table.Locator(".tm-data-table-col-menu[data-column-key='Name']").ClickAsync();
        var columnMenu = page.Locator("[role='menu'].tm-data-table-col-menu-panel, .tm-data-table-col-menu-panel").First;
        await Assertions.Expect(columnMenu).ToBeVisibleAsync();
        await Assertions.Expect(columnMenu.Locator("[data-action='sort-asc']")).ToBeVisibleAsync();
        await ClipShotAsync(page, theme, "desktop-1440-column-menu-open", table.Locator("thead"), columnMenu);
        await page.Keyboard.PressAsync("Escape");

        // Views panel, anchored under its trigger, with the active view named on the toggle.
        var toggle = table.Locator(".tm-view-manager-toggle");
        await toggle.ClickAsync();
        var panel = page.Locator(".tm-view-manager-panel");
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await Assertions.Expect(panel).ToContainTextAsync("Engineering");
        await ClipShotAsync(page, theme, "desktop-1440-views-panel", table.Locator(".tm-data-table-toolbar"), panel);
    }

    [TestMethod, TestCategory("WASM")]
    [DataRow("light")]
    [DataRow("dark")]
    [DataRow("indigo")]
    public async Task Desktop1440_MultiViewListCardsAndGroups(string theme)
    {
        var page = await OpenThemedAsync(theme, "/multi-view-list", 1440, 1000);
        var list = page.Locator("[data-testid='mvl-redesign-list']");
        await list.ScrollIntoViewIfNeededAsync();
        await SelectAsync(page, "mvl-redesign-avatar-fallback", "Initials");
        await ToggleAsync(page, "mvl-redesign-item-count");
        await ToggleAsync(page, "mvl-redesign-max-columns");
        await list.Locator(".tm-mvl-switch-card").ClickAsync();
        await Assertions.Expect(list.Locator(".tm-mvl-card-grid")).ToBeVisibleAsync();
        await Assertions.Expect(list.Locator(".tm-mvl-card .tm-avatar-fallback").First).ToBeVisibleAsync();
        await Assertions.Expect(list.Locator(".tm-mvl-item-count")).ToContainTextAsync("3");
        await ShotAsync(page, list, theme, "mvl-cards-initials-count-cap2");

        // Focus ring on the radiogroup: arrow to a segment with the keyboard so :focus-visible paints.
        await list.Locator(".tm-mvl-switch-card").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(list.Locator(".tm-mvl-switch-list")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(list.Locator(".tm-mvl-switch-card")).ToBeFocusedAsync();
        await ShotAsync(page, list.Locator(".tm-mvl-switcher"), theme, "focus-ring-radiogroup");

        // Grouped cards (second demo list): group by Role and assert the groups exist.
        var cards = page.Locator("[data-testid='mvl-redesign-cards']");
        await cards.ScrollIntoViewIfNeededAsync();
        await cards.Locator(".tm-mvl-group-btn").ClickAsync();
        await page.Locator(".tm-mvl-group-dropdown .tm-mvl-group-option").First.ClickAsync();
        await Assertions.Expect(cards.Locator(".tm-mvl-group-header").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".tm-mvl-group-dropdown")).ToHaveCountAsync(0);
        await ShotAsync(page, cards, theme, "mvl-grouped-cards");
    }

    [TestMethod, TestCategory("WASM")]
    [DataRow("light")]
    [DataRow("dark")]
    [DataRow("indigo")]
    public async Task Tablet1024_And_GridFocusRing(string theme)
    {
        var page = await OpenThemedAsync(theme, "/data-table", 1024, 800);
        var section = page.Locator("[data-testid='dt-ergonomics-section'] .tm-data-table-wrapper");
        await section.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(section.Locator("thead th.tm-col-pinned-left").First).ToBeVisibleAsync();
        await ShotAsync(page, section, theme, "tablet-1024-ergonomics");

        var grid = page.Locator("[data-testid='dt-grid-keyboard-section']");
        await grid.ScrollIntoViewIfNeededAsync();
        await grid.Locator("td[tabindex='0']").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        var cell = grid.Locator("td[tabindex='0']");
        await Assertions.Expect(cell).ToBeFocusedAsync();
        await ShotAsync(page, grid.Locator(".tm-data-table-scroll").First, theme, "focus-ring-grid-cell");
    }

    [TestMethod, TestCategory("WASM")]
    [DataRow("light")]
    [DataRow("dark")]
    [DataRow("indigo")]
    public async Task Mobile390_CardsScrollSheetsAndSaveDialog(string theme)
    {
        var page = await OpenThemedAsync(theme, "/data-table", 390, 844, touch: true);
        await SelectAsync(page, "dt-redesign-mobile-layout", "Cards");
        await SelectAsync(page, "dt-redesign-appearance", "Card");
        var table = page.Locator("[data-testid='dt-redesign-table']");
        await table.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(table.Locator("thead")).Not.ToBeVisibleAsync();
        await Assertions.Expect(table.Locator("tbody tr").First).ToContainTextAsync("Ada Lovelace");
        await ShotAsync(page, table.Locator(".tm-data-table-wrapper"), theme, "mobile-390-cards");

        // Filter sheet.
        await table.Locator(".tm-data-table-cards-toolbar__filter").ClickAsync();
        var sheet = page.Locator(".tm-drawer--bottom");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        Assert.AreEqual(390, (await BoxAsync(sheet)).Width, 1);
        await ViewportShotAsync(page, theme, "mobile-390-filter-sheet");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(sheet).ToHaveCountAsync(0);

        // Views sheet, then the Save view dialog on top of the phone viewport.
        await table.Locator(".tm-view-manager-toggle").ClickAsync();
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        Assert.AreEqual(390, (await BoxAsync(sheet)).Width, 1);
        await ViewportShotAsync(page, theme, "mobile-390-views-sheet");
        await sheet.Locator(".tm-view-manager-footer .tm-btn-secondary").ClickAsync();
        var dialog = page.Locator(".tm-modal").First;
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await ViewportShotAsync(page, theme, "mobile-390-save-view-dialog");

        // Scroll layout on a fresh page so the toolbar state of the Cards scene cannot leak in.
        var scroll = await OpenThemedAsync(theme, "/data-table", 390, 844, touch: true);
        await SelectAsync(scroll, "dt-redesign-mobile-layout", "Scroll");
        await SelectAsync(scroll, "dt-redesign-appearance", "Card");
        var t2 = scroll.Locator("[data-testid='dt-redesign-table']");
        await t2.ScrollIntoViewIfNeededAsync();
        var scroller = t2.Locator(".tm-data-table-scroll");
        Assert.IsTrue(await scroller.EvaluateAsync<int>("el => el.scrollWidth - el.clientWidth") >= 0);
        await ShotAsync(scroll, t2.Locator(".tm-data-table-wrapper"), theme, "mobile-390-scroll");
    }

    [TestMethod, TestCategory("WASM")]
    [DataRow("light")]
    [DataRow("dark")]
    [DataRow("indigo")]
    public async Task EdgeCases_EmptyLoadingLongWideRtlZoom(string theme)
    {
        var page = await OpenThemedAsync(theme, "/data-table", 1440, 1000);
        var empty = page.Locator("[data-testid='dt-edge-empty']");
        await Assertions.Expect(empty.Locator(".tm-empty-state")).ToBeVisibleAsync();
        await ShotAsync(page, empty, theme, "edge-empty-table");
        var emptyCards = page.Locator("[data-testid='dt-edge-empty-cards']");
        await Assertions.Expect(emptyCards.Locator(".tm-empty-state")).ToBeVisibleAsync();
        await ShotAsync(page, emptyCards, theme, "edge-empty-cards");

        var search = page.Locator("[data-testid='dt-edge-search']");
        await search.ScrollIntoViewIfNeededAsync();
        await search.Locator("input[type='search']").FillAsync("zzz-no-such-person");
        await Assertions.Expect(search.Locator(".tm-empty-state")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await ShotAsync(page, search, theme, "edge-no-results");

        var loading = page.Locator("[data-testid='dt-edge-loading']");
        // The loading state renders FOUR skeleton rows (one cell per column, so widths do not jump);
        // assert the first of them — a multi-element locator would trip Playwright's strict mode.
        await Assertions.Expect(loading.Locator(".tm-data-table-loading-row").First).ToBeVisibleAsync();
        await ShotAsync(page, loading, theme, "edge-loading");

        var host = page.Locator("[data-testid='dt-edge-long']");
        await host.ScrollIntoViewIfNeededAsync();
        await host.Locator(".tm-view-manager-toggle").ClickAsync();
        await page.Locator(".tm-view-manager-panel .tm-view-item").First.ClickAsync();
        await Assertions.Expect(host.Locator(".tm-view-manager-toggle-name")).ToBeVisibleAsync();
        await ShotAsync(page, host, theme, "edge-long-text-and-view-name");

        var wide = page.Locator("[data-testid='dt-edge-wide']");
        await wide.ScrollIntoViewIfNeededAsync();
        Assert.IsTrue(await wide.Locator(".tm-data-table-scroll").EvaluateAsync<int>("el => el.scrollWidth - el.clientWidth") > 200);
        await ShotAsync(page, wide, theme, "edge-fifteen-columns");

        // 15 columns do not fit 1440px: the start shot above shows the first ~10, so scroll the
        // scroller to its end and capture the remaining columns — between the two shots the
        // baseline demonstrates the whole column set (UX review: "shows only 10 of 15").
        await wide.Locator(".tm-data-table-scroll").EvaluateAsync("el => { el.scrollLeft = el.scrollWidth - el.clientWidth; }");
        await page.WaitForTimeoutAsync(300);
        await ShotAsync(page, wide, theme, "edge-fifteen-columns-end");

        await page.EvaluateAsync("() => document.documentElement.setAttribute('dir', 'rtl')");
        await page.WaitForTimeoutAsync(600);
        // The 15-column table overflows its scroller, so the shot is cut where the scroller ends - in RTL that
        // is the LEFT edge, mid-column. That is ordinary scroll overflow, not a clipped header: scroll to the
        // scroll end so both edges of the baseline fall on whole columns, with the pinned column on the right.
        await wide.Locator(".tm-data-table-scroll").EvaluateAsync("el => { el.scrollLeft = -(el.scrollWidth - el.clientWidth); }");
        await page.WaitForTimeoutAsync(300);
        await ShotAsync(page, wide, theme, "edge-rtl-pinned-column");

        var zoom = await OpenThemedAsync(theme, "/data-table", 720, 450, dpr: 2);
        await SelectAsync(zoom, "dt-redesign-appearance", "Card");
        var zt = zoom.Locator("[data-testid='dt-redesign-table']");
        await zt.ScrollIntoViewIfNeededAsync();
        await ShotAsync(zoom, zt.Locator(".tm-data-table-wrapper"), theme, "edge-zoom-200");
    }
}
