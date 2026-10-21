using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DataTableE2EKit;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 13 spec bodies for the table, view manager and filter builder. They are static so the WASM and
/// the Server host run exactly the same assertions; every spec asserts STATE (DOM, geometry, focus,
/// downloads) and never a picture. Screenshots are produced by <c>DataTableRedesignBaselineScreenshots</c>.
/// </summary>
internal static class DataTableSpecs
{
    private static readonly PageWaitForFunctionOptions Wait = new() { Timeout = 10_000 };

    private static ILocator Redesign(IPage page) => page.Locator("[data-testid='dt-redesign-table']");

    // ───────────────────────── 13.2 grid keyboard ─────────────────────────

    internal static async Task GridKeyboardAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/data-table", new Options(), register);
        await SelectAsync(page, "dt-redesign-keyboard", "Grid");
        var table = Redesign(page);
        await table.ScrollIntoViewIfNeededAsync();
        await InsertProbeAsync(page, "[data-testid='dt-redesign-table']");

        // Tab enters the body on exactly ONE cell, and the next Tab leaves the body instead of walking to a second cell.
        Assert.AreEqual(1, await table.Locator("tbody td[tabindex='0']").CountAsync(), "exactly one roving cell");
        await page.Locator("#tm-probe").FocusAsync();
        var landed = false;
        for (var i = 0; i < 12 && !landed; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            landed = (await ActiveAsync(page)).Split('|')[1].Length > 0;
        }

        Assert.IsTrue(landed, "Tab reaches a grid cell");
        await page.Keyboard.PressAsync("Tab");
        Assert.AreEqual(string.Empty, (await ActiveAsync(page)).Split('|')[1], "the second Tab leaves the grid body");

        // Arrows move the real focus and never scroll the page.
        var first = table.Locator("tbody td[tabindex='0']");
        await first.FocusAsync();
        await SettleScrollAsync(page);
        var y0 = await page.EvaluateAsync<double>("() => window.scrollY");
        var start = (await ActiveAsync(page)).Split('|')[1];
        await page.Keyboard.PressAsync("ArrowRight");
        var right = (await ActiveAsync(page)).Split('|')[1];
        Assert.AreNotEqual(start, right, "ArrowRight moves document.activeElement");
        await page.Keyboard.PressAsync("ArrowDown");
        var down = (await ActiveAsync(page)).Split('|')[1];
        Assert.AreNotEqual(right, down, "ArrowDown moves document.activeElement");
        await SettleScrollAsync(page);
        Assert.AreEqual(y0, await page.EvaluateAsync<double>("() => window.scrollY"), 0.5, "arrows must not scroll the page");

        // Home with Selectable lands on the checkbox cell (column index 0).
        await page.Keyboard.PressAsync("Home");
        Assert.IsTrue((await ActiveAsync(page)).Split('|')[1].EndsWith(":0", StringComparison.Ordinal), "Home → checkbox cell");

        // Enter activates once; Space toggles selection and leaves the counter alone.
        var counter = page.Locator("[data-testid='dt-row-activations']");
        await Assertions.Expect(counter).ToContainTextAsync("0");
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(counter).ToContainTextAsync("1");
        await page.WaitForTimeoutAsync(300);
        await Assertions.Expect(counter).ToContainTextAsync("1");
        await page.Keyboard.PressAsync(" ");
        await Assertions.Expect(table.Locator("tbody tr.tm-row-selected")).ToHaveCountAsync(1);
        await Assertions.Expect(counter).ToContainTextAsync("1");

        // Filtered down to one row the table is still reachable: one roving cell, still a Tab stop.
        await table.Locator("input[type='search']").FillAsync("Ada");
        await Assertions.Expect(table.Locator("tbody tr")).ToHaveCountAsync(1);
        Assert.AreEqual(1, await table.Locator("tbody td[tabindex='0']").CountAsync());
        await page.Locator("#tm-probe").FocusAsync();
        landed = false;
        for (var i = 0; i < 12 && !landed; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            landed = (await ActiveAsync(page)).Split('|')[1].Length > 0;
        }

        Assert.IsTrue(landed, "a one-row table is still reachable with Tab");

        // Grouping: Enter on a group header collapses it.
        var grouped = page.Locator("[data-testid='dt-grid-grouped']");
        await grouped.ScrollIntoViewIfNeededAsync();
        var header = grouped.Locator("tr.tm-data-table-group-row td[tabindex='0']").First;
        await header.FocusAsync();
        var toggle = grouped.Locator(".tm-data-table-group-toggle").First;
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
    }

    internal static async Task GridEditAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/data-table", new Options(), register);
        var section = page.Locator("[data-testid='dt-grid-edit']");
        await section.ScrollIntoViewIfNeededAsync();
        await section.Locator("td[tabindex='0']").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");

        // F2 opens the editor AND moves focus into it; Escape then cancels and returns to the cell.
        await page.Keyboard.PressAsync("F2");
        var input = section.Locator("[data-testid='grid-edit-note-input']");
        await Assertions.Expect(input).ToBeFocusedAsync(new() { Timeout = 10_000 });
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(section.Locator("tr.tm-row-editing")).ToHaveCountAsync(0);
        await page.WaitForFunctionAsync("() => (document.activeElement?.getAttribute('data-grid-cell') || '') !== ''", null, Wait);

        // Edit again and save with Enter: the new value is committed and the cell has focus again.
        await page.Keyboard.PressAsync("F2");
        await Assertions.Expect(input).ToBeFocusedAsync(new() { Timeout = 10_000 });
        await input.FillAsync("Edited by keyboard");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("[data-testid='dt-grid-edit-saved']")).ToContainTextAsync("Edited by keyboard");
        await page.WaitForFunctionAsync("() => (document.activeElement?.getAttribute('data-grid-cell') || '') !== ''", null, Wait);
    }

    // ───────────────────────── 13.6 column menu ─────────────────────────

    internal static async Task ColumnMenuAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/data-table", new Options(), register);
        var section = page.Locator("[data-testid='dt-column-menu-section']");
        await section.ScrollIntoViewIfNeededAsync();
        var kebab = section.Locator(".tm-data-table-col-menu[data-column-key='Name']");
        var firstCell = section.Locator("tbody tr").First.Locator("td").First;

        await kebab.ClickAsync();
        await section.Locator("[data-action='sort-desc']").ClickAsync();
        await Assertions.Expect(firstCell).ToContainTextAsync("Employee 12");
        await kebab.ClickAsync();
        await section.Locator("[data-action='sort-asc']").ClickAsync();
        await Assertions.Expect(firstCell).ToContainTextAsync("Employee 01");

        await section.Locator(".tm-data-table-col-menu[data-column-key='Role']").ClickAsync();
        await section.Locator("[data-action='hide']").ClickAsync();
        await Assertions.Expect(section.Locator("thead th", new() { HasText = "Role" })).ToHaveCountAsync(0);

        await kebab.ClickAsync();
        await section.Locator("[data-action='group']").ClickAsync();
        await Assertions.Expect(section.Locator(".tm-data-table-group-chip")).ToContainTextAsync("Name");

        // Keyboard: the sort button is the header's Tab stop; Alt+ArrowDown opens the menu and it STAYS open.
        var page2 = await OpenAsync(browser, baseUrl, "/data-table", new Options(), register);
        var s2 = page2.Locator("[data-testid='dt-column-menu-section']");
        await s2.ScrollIntoViewIfNeededAsync();
        await InsertProbeAsync(page2, "[data-testid='dt-column-menu-section']");
        await page2.Locator("#tm-probe").FocusAsync();
        var sortButton = s2.Locator("th:has(.tm-data-table-col-menu[data-column-key='Name']) .tm-th-sort");
        for (var i = 0; i < 6; i++)
        {
            await page2.Keyboard.PressAsync("Tab");
            if (await sortButton.EvaluateAsync<bool>("el => el === document.activeElement"))
            {
                break;
            }
        }

        await Assertions.Expect(sortButton).ToBeFocusedAsync();
        await SettleScrollAsync(page2);
        var y0 = await page2.EvaluateAsync<double>("() => window.scrollY");
        await page2.Keyboard.PressAsync("Alt+ArrowDown");
        var menu = s2.Locator("[role='menu']");
        await Assertions.Expect(menu).ToBeVisibleAsync();
        await page2.WaitForTimeoutAsync(600); // the old defect closed the menu ~150 ms after it opened
        await Assertions.Expect(menu).ToBeVisibleAsync();
        Assert.AreEqual(y0, await page2.EvaluateAsync<double>("() => window.scrollY"), 1.0, "Alt+ArrowDown must not scroll the page");
        await page2.WaitForFunctionAsync("() => document.activeElement?.getAttribute('data-action') === 'sort-asc'", null, Wait);
        await page2.Keyboard.PressAsync("ArrowDown");
        await page2.WaitForFunctionAsync("() => document.activeElement?.getAttribute('data-action') === 'sort-desc'", null, Wait);
        await page2.Keyboard.PressAsync("Escape");
        await Assertions.Expect(menu).ToHaveCountAsync(0);
        await Assertions.Expect(sortButton).ToBeFocusedAsync();

        // The kebab itself is reachable by script/pointer and opens with Enter as well.
        await s2.Locator(".tm-data-table-col-menu[data-column-key='Name']").FocusAsync();
        await page2.Keyboard.PressAsync("Enter");
        await Assertions.Expect(menu).ToBeVisibleAsync();
    }

    // ───────────────────────── 13.7 result preview ─────────────────────────

    internal static async Task ResultPreviewAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/data-table", new Options(), register);
        await ToggleAsync(page, "dt-redesign-result-preview");
        var table = Redesign(page);
        await table.ScrollIntoViewIfNeededAsync();
        await table.Locator(".tm-filter-builder-add").ClickAsync();
        await page.Locator(".tm-filter-field-option", new() { HasText = "Department" }).ClickAsync();
        await page.Locator(".tm-filter-editor select.tm-filter-value-input").SelectOptionAsync("Engineering");
        await page.Locator(".tm-filter-apply").ClickAsync();

        var preview = table.Locator(".tm-filter-result-preview");
        await Assertions.Expect(table.Locator("tbody tr")).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        var rows = await table.Locator("tbody tr").CountAsync();
        // The count is debounced (300 ms): wait for the preview to CATCH UP with the table, never read it earlier.
        await Assertions.Expect(preview).ToHaveTextAsync(new Regex($@"^\s*{rows} matching records\s*$"), new() { Timeout = 15_000 });
    }

    // ───────────────────────── 13.4 overlays ─────────────────────────

    internal static async Task ViewManagerDesktopAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/data-table", new Options(), register);
        var table = Redesign(page);
        await table.ScrollIntoViewIfNeededAsync();
        var trigger = table.Locator(".tm-view-manager-toggle");
        await trigger.ClickAsync();
        var panel = page.Locator(".tm-view-manager-panel");
        await Assertions.Expect(panel).ToBeVisibleAsync();
        var t = await BoxAsync(trigger);
        var p = await BoxAsync(panel);
        var gap = p.Y - (t.Y + t.Height);
        Assert.IsTrue(gap >= -1 && gap <= 8, $"the panel is anchored right below its trigger (gap {gap}px)");

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(panel).ToHaveCountAsync(0);
        await Assertions.Expect(trigger).ToBeFocusedAsync();

        await trigger.ClickAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await page.Mouse.ClickAsync(5, 300);
        await Assertions.Expect(panel).ToHaveCountAsync(0);
        Assert.IsFalse(await trigger.EvaluateAsync<bool>("el => el === document.activeElement"),
            "an outside click must not pull focus back to the trigger");
    }

    internal static async Task ViewManagerMobileSheetAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/data-table", new Options(390, 844, Touch: true, IsMobile: true), register);
        await SelectAsync(page, "dt-redesign-mobile-layout", "Cards");
        var table = Redesign(page);
        await table.ScrollIntoViewIfNeededAsync();
        await table.Locator(".tm-view-manager-toggle").ClickAsync();
        var sheet = page.Locator(".tm-drawer--bottom");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        var box = await BoxAsync(sheet);
        Assert.AreEqual(0, box.X, 1, "the sheet starts at the left edge");
        Assert.AreEqual(390, box.Width, 1, "the sheet is full width");
        await Assertions.Expect(sheet).ToContainTextAsync("Engineering");
    }

    internal static async Task ExportScopeAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/data-table", new Options(), register);
        var section = page.Locator("[data-testid='dt-export-section']");
        await section.ScrollIntoViewIfNeededAsync();
        var trigger = section.Locator(".tm-data-table__export .tm-dropdown-trigger");
        await trigger.ClickAsync();
        var selected = section.Locator("[data-export-scope='selected']");
        await Assertions.Expect(selected).ToHaveAttributeAsync("aria-disabled", "true");
        await Assertions.Expect(selected).ToContainTextAsync("Select rows to export them");
        await page.Keyboard.PressAsync("Escape");

        var checks = section.Locator("tbody tr td input[type='checkbox']");
        await checks.Nth(0).CheckAsync();
        await checks.Nth(2).CheckAsync();
        await trigger.ClickAsync();
        await Assertions.Expect(selected).Not.ToHaveAttributeAsync("aria-disabled", "true");
        await Assertions.Expect(selected).ToContainTextAsync("(2)");
        await selected.ClickAsync();
        var download = await page.RunAndWaitForDownloadAsync(() => section.Locator("[data-export-format='csv']").ClickAsync());
        var path = await download.PathAsync();
        Assert.IsNotNull(path);
        var lines = File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
        Assert.AreEqual(3, lines.Length, "header + the two selected rows: " + string.Join(" | ", lines));
        StringAssert.Contains(lines[0], "Amount");
    }

    internal static async Task FilterBuilderPresentationAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var desktop = await OpenAsync(browser, baseUrl, "/data-table", new Options(), register);
        var popover = desktop.Locator("[data-testid='dt-filter-popover']");
        await popover.ScrollIntoViewIfNeededAsync();
        var add = popover.Locator(".tm-filter-builder-add");
        await add.ClickAsync();
        var panel = desktop.Locator(".tm-filter-builder-panel");
        await Assertions.Expect(panel).ToBeVisibleAsync();
        var a = await BoxAsync(add);
        var p = await BoxAsync(panel);
        var gap = p.Y - (a.Y + a.Height);
        Assert.IsTrue(gap >= -1 && gap <= 8, $"Popover is anchored under Add filter (gap {gap}px)");
        await desktop.Keyboard.PressAsync("Escape");
        await Assertions.Expect(add).ToBeFocusedAsync();

        var mobile = await OpenAsync(browser, baseUrl, "/data-table", new Options(390, 844, Touch: true, IsMobile: true), register);
        var auto = mobile.Locator("[data-testid='dt-filter-auto']");
        await auto.ScrollIntoViewIfNeededAsync();
        await auto.Locator(".tm-filter-builder-add").ClickAsync();
        var sheet = mobile.Locator(".tm-drawer--bottom");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        Assert.AreEqual(390, (await BoxAsync(sheet)).Width, 1, "Auto becomes a full-width sheet on mobile");
    }

    // ───────────────────────── 13.3 mobile ─────────────────────────

    /// <summary>
    /// Deferred 13.3 carry-forward: at tablet 1024 the pinned columns must not overlap their
    /// neighbours (measured by bounding boxes) and the first Scroll column must stay pinned
    /// while the table scrolls horizontally.
    /// </summary>
    internal static async Task PinnedColumns_Tablet1024Async(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/data-table", new Options(1024, 800), register);
        var host = page.Locator("[data-testid='dt-edge-wide']");
        await host.ScrollIntoViewIfNeededAsync();
        var scroller = host.Locator(".tm-data-table-scroll").First;
        var firstRow = host.Locator("tbody tr").First;
        var pinned = firstRow.Locator("td.tm-col-pinned-left, td.data-mobile-pinned").First;
        await pinned.WaitForAsync();

        async Task AssertNoOverlapAsync(string when)
        {
            var boxes = await firstRow.Locator("td").EvaluateAllAsync<double[][]>(
                "els => els.map(e => { const r = e.getBoundingClientRect(); return [r.left, r.right]; })");
            for (var i = 1; i < boxes.Length; i++)
            {
                Assert.IsTrue(boxes[i - 1][1] <= boxes[i][0] + 0.5, $"{when}: cells {i - 1} and {i} overlap ({boxes[i - 1][1]} > {boxes[i][0]})");
            }
        }

        await AssertNoOverlapAsync("at rest");

        var pinnedX = (await BoxAsync(pinned)).X;
        await scroller.EvaluateAsync("el => { el.scrollLeft = el.scrollWidth / 2; }");
        await page.WaitForTimeoutAsync(250);
        Assert.IsTrue(await scroller.EvaluateAsync<double>("el => el.scrollLeft") > 0, "the fifteen-column table scrolls horizontally");
        Assert.AreEqual(pinnedX, (await BoxAsync(pinned)).X, 1.0, "the first column keeps its pinned edge while the table scrolls");
        // While scrolled the neighbouring cells legitimately slide UNDER the opaque sticky pinned
        // column, so pairwise bounding boxes would always "overlap"; the contract here is that
        // the pinned edge holds (asserted above) and the pinned column stays fully visible.
        var scrolledPinned = await BoxAsync(pinned);
        var scrolledScroller = await BoxAsync(scroller);
        Assert.IsTrue(scrolledPinned.X >= scrolledScroller.X - 1, "the pinned column stays visible while scrolled");
        await scroller.EvaluateAsync("el => { el.scrollLeft = 0; }");
        await page.WaitForTimeoutAsync(150);
        await AssertNoOverlapAsync("back at rest");
    }

    internal static async Task MobileCardsAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/data-table", new Options(390, 844, Touch: true, IsMobile: true), register);
        await SelectAsync(page, "dt-redesign-mobile-layout", "Cards");
        await SelectAsync(page, "dt-redesign-appearance", "Card");
        var table = Redesign(page);
        await table.ScrollIntoViewIfNeededAsync();
        await SettleAsync(page);

        Assert.AreEqual("none", await table.Locator("thead").EvaluateAsync<string>("el => getComputedStyle(el).display"), "the header is gone in Cards");
        Assert.AreEqual("block", await table.Locator("tbody tr").First.EvaluateAsync<string>("el => getComputedStyle(el).display"), "a row is a card");
        await InsertProbeAsync(page, "[data-testid='dt-redesign-table']");
        await page.Locator("#tm-probe").FocusAsync();
        for (var i = 0; i < 8; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            Assert.IsFalse(await page.EvaluateAsync<bool>("() => !!document.activeElement.closest('thead')"), "Tab never enters the hidden header");
        }

        // The toolbar stays inside the card: nothing is clipped on the right.
        var wrapper = await BoxAsync(table.Locator(".tm-data-table-wrapper"));
        foreach (var box in await table.Locator(".tm-data-table-toolbar-right > *").EvaluateAllAsync<double[][]>(
                     "els => els.map(e => { const r = e.getBoundingClientRect(); return [r.left, r.right]; })"))
        {
            Assert.IsTrue(box[1] <= wrapper.X + wrapper.Width + 1, $"toolbar control ends at {box[1]}, wrapper ends at {wrapper.X + wrapper.Width}");
        }

        // Sort dropdown.
        await table.Locator(".tm-data-table-cards-toolbar__sort .tm-dropdown-trigger").ClickAsync();
        await page.Locator("[role='menuitemradio'], [role='menuitem']", new() { HasText = "Sort by Name — Sort descending" }).First.ClickAsync();
        await Assertions.Expect(table.Locator("tbody tr").First).ToContainTextAsync("Katherine Johnson");

        // Filters (N) opens a full-width sheet; filtering changes the data and the count in the button.
        var filters = table.Locator(".tm-data-table-cards-toolbar__filter");
        await Assertions.Expect(filters).ToContainTextAsync("Filters (0)");
        await filters.ClickAsync();
        var sheet = page.Locator(".tm-drawer--bottom");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        var sheetBox = await BoxAsync(sheet);
        Assert.AreEqual(390, sheetBox.Width, 1, "the filter sheet is full width");
        await sheet.Locator("input[type='text'], input:not([type])").First.FillAsync("Ada");
        await Assertions.Expect(table.Locator("tbody tr")).ToHaveCountAsync(1, new() { Timeout = 15_000 });
        await Assertions.Expect(filters).ToContainTextAsync("Filters (1)");
    }

    internal static async Task ScrollPinnedAndPagerAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/data-table", new Options(1024, 900), register);
        var host = page.Locator("[data-testid='dt-export-responsive']");
        await host.ScrollIntoViewIfNeededAsync();
        await host.EvaluateAsync("el => { el.style.maxWidth = '360px'; }");
        await page.WaitForTimeoutAsync(400);
        var scroller = host.Locator(".tm-data-table-scroll").First;
        var pinned = host.Locator("tbody tr").First.Locator("td.data-mobile-pinned").First;
        await pinned.WaitForAsync();
        Assert.IsTrue(await scroller.EvaluateAsync<int>("el => el.scrollWidth - el.clientWidth") > 0, "the Scroll layout overflows");
        var before = (await BoxAsync(pinned)).X;
        await scroller.EvaluateAsync("el => { el.scrollLeft = el.scrollWidth; }");
        await page.WaitForTimeoutAsync(250);
        Assert.IsTrue(await scroller.EvaluateAsync<double>("el => el.scrollLeft") > 0);
        Assert.AreEqual(before, (await BoxAsync(pinned)).X, 1.0, "the first Scroll column keeps its left edge");

        // Pager: page numbers on a wide container, "Page X of Y" only on a narrow one.
        var wideViewport = await OpenAsync(browser, baseUrl, "/data-table", new Options(1440, 900), register);
        var wide = wideViewport.Locator("[data-testid='dt-edge-pager-wide']");
        await wide.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(wide.Locator(".tm-page-btn").First).ToBeVisibleAsync();
        Assert.IsTrue((await BoxAsync(wide)).Width >= 640, "the wide container is past the 640px breakpoint");
        var narrow = page.Locator("[data-testid='dt-edge-pager-narrow']");
        await narrow.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(narrow.Locator(".tm-page-btn").First).Not.ToBeVisibleAsync();
        await Assertions.Expect(narrow.Locator(".tm-data-table-page-label")).ToBeVisibleAsync();

        // Tablet: the first row's cells never overlap each other and Score is visible.
        await SetViewportAsync(page, 1024, 800);
        var ergonomics = page.Locator("[data-testid='dt-ergonomics-section']");
        await ergonomics.ScrollIntoViewIfNeededAsync();
        var score = ergonomics.Locator("tbody tr").First.Locator("td", new() { HasTextRegex = new Regex(@"^\s*\d+\s*$") }).First;
        await Assertions.Expect(score).ToBeVisibleAsync();
        var cells = await ergonomics.Locator("tbody tr").First.Locator("td:not(.tm-col-pinned-left):not(.tm-col-pinned-right)").EvaluateAllAsync<double[][]>(
            "els => els.map(e => { const r = e.getBoundingClientRect(); return [r.left, r.right]; })");
        for (var i = 1; i < cells.Length; i++)
        {
            Assert.IsTrue(cells[i - 1][1] <= cells[i][0] + 0.5, $"cells {i - 1} and {i} overlap: {cells[i - 1][1]} > {cells[i][0]}");
        }
    }

    // ───────────────────────── 13.8 localization ─────────────────────────

    internal static async Task CzechAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/data-table", new Options(Culture: "cs"), register);
        await SelectAsync(page, "dt-redesign-appearance", "Card");
        await ToggleAsync(page, "dt-redesign-result-preview");
        var table = Redesign(page);
        await table.ScrollIntoViewIfNeededAsync();

        var toolbar = await table.Locator(".tm-data-table-toolbar").InnerTextAsync();
        foreach (var expected in new[] { "Pohledy", "Exportovat" })
        {
            StringAssert.Contains(toolbar, expected);
        }

        Assert.IsFalse(Regex.IsMatch(toolbar, @"Choose columns|\bViews\b|\bExport\b|Search|Group by|Add filter"), "no English fallback in the toolbar: " + toolbar);
        StringAssert.Contains(await table.Locator(".tm-data-table-group-zone").InnerTextAsync(), "Seskupit podle");
        StringAssert.Contains(await table.Locator(".tm-filter-builder-add").InnerTextAsync(), "Přidat filtr");

        // Result preview plural forms (cs: 1 / 2–4 / 5+) through the standalone builder over the fixed four employees.
        var standalone = page.Locator("[data-testid='dt-filter-popover']");
        await standalone.ScrollIntoViewIfNeededAsync();
        async Task<string> PreviewFor(string value)
        {
            await standalone.Locator(".tm-filter-builder-add").ClickAsync();
            await page.Locator(".tm-filter-field-option").First.ClickAsync();
            await page.Locator(".tm-filter-editor input.tm-filter-value-input").FillAsync(value);
            await page.Locator(".tm-filter-apply").ClickAsync();
            var preview = standalone.Locator(".tm-filter-result-preview");
            await Assertions.Expect(preview).ToContainTextAsync("odpovídající", new() { Timeout = 15_000 });
            var text = await preview.InnerTextAsync();
            await standalone.Locator(".tm-filter-chip-remove").First.ClickAsync();
            await page.WaitForTimeoutAsync(500);
            return text;
        }

        StringAssert.Matches(await PreviewFor("Ada"), new Regex(@"1 odpovídající záznam$"));
        StringAssert.Matches(await PreviewFor("r"), new Regex(@"3 odpovídající záznamy$"));
        StringAssert.Matches(await PreviewFor("a"), new Regex(@"4 odpovídající záznamy$"));

        // Selection summary (cs: 1 vybrán / 3 vybrány / 5 vybráno / 12 vybráno).
        var multi = page.Locator(".demo-section", new() { Has = page.Locator("h2:text-is('Multi-Select Table')") });
        await multi.ScrollIntoViewIfNeededAsync();
        var checks = multi.Locator("tbody td input[type='checkbox']");
        var bar = multi.Locator(".tm-data-table-selection-bar");
        var expectations = new[] { (1, "1 vybrán"), (3, "3 vybrány"), (5, "5 vybráno") };
        var checkedSoFar = 0;
        foreach (var (count, text) in expectations)
        {
            while (checkedSoFar < count)
            {
                await checks.Nth(checkedSoFar++).CheckAsync();
            }

            await Assertions.Expect(bar).ToContainTextAsync(text);
        }
    }

    // ───────────────────────── 13.9 edge cases ─────────────────────────

    internal static async Task EdgeStatesAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/data-table", new Options(), register);
        var empty = page.Locator("[data-testid='dt-edge-empty']");
        await empty.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(empty.Locator(".tm-empty-state")).ToContainTextAsync("No employees yet");
        var emptyCards = page.Locator("[data-testid='dt-edge-empty-cards']");
        await Assertions.Expect(emptyCards.Locator(".tm-empty-state")).ToContainTextAsync("No employees yet");

        var search = page.Locator("[data-testid='dt-edge-search']");
        await search.ScrollIntoViewIfNeededAsync();
        await search.Locator("input[type='search']").FillAsync("zzz-no-such-person");
        await Assertions.Expect(search.Locator(".tm-empty-state")).ToContainTextAsync("No employees match your search", new() { Timeout = 15_000 });

        var loading = page.Locator("[data-testid='dt-edge-loading']");
        await loading.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(loading.Locator(".tm-data-table-loading-row").First).ToBeVisibleAsync();
        Assert.AreEqual(0, await loading.Locator("tbody tr td", new() { HasText = "Ada" }).CountAsync(), "rows are not painted while loading");
        // The skeleton rows carry one cell per column so the widths do not shift when data arrives.
        var headerCells = await loading.Locator("thead th").CountAsync();
        var skeletonCellCount = await loading.Locator(".tm-data-table-loading-row").First.Locator("td").CountAsync();
        Assert.AreEqual(headerCells, skeletonCellCount, "skeleton rows measure the same columns as data rows");

        // Very long cell text and view name: ellipsis, no horizontal page overflow.
        var host = page.Locator("[data-testid='dt-edge-long']");
        await host.ScrollIntoViewIfNeededAsync();
        await host.Locator(".tm-view-manager-toggle").ClickAsync();
        await page.Locator(".tm-view-manager-panel .tm-view-item").First.ClickAsync();
        var name = host.Locator(".tm-view-manager-toggle-name");
        await Assertions.Expect(name).ToBeVisibleAsync();
        Assert.IsTrue(await name.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth"), "the long view name is truncated with an ellipsis");
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"), "no horizontal page overflow");
        var hostBox = await BoxAsync(host);
        Assert.IsTrue((await BoxAsync(host.Locator(".tm-data-table-wrapper"))).Width <= hostBox.Width + 1, "the table stays inside its 24rem container");

        // Fifteen columns scroll horizontally and the pinned column stays put.
        var wide = page.Locator("[data-testid='dt-edge-wide']");
        await wide.ScrollIntoViewIfNeededAsync();
        var scroller = wide.Locator(".tm-data-table-scroll");
        Assert.IsTrue(await scroller.EvaluateAsync<int>("el => el.scrollWidth - el.clientWidth") > 200, "fifteen columns overflow");
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
        var pinned = wide.Locator("thead th.tm-col-pinned-left").First;
        await page.WaitForTimeoutAsync(400);
        await scroller.EvaluateAsync("el => { el.scrollLeft = 80; }");
        await page.WaitForTimeoutAsync(200);
        var x1 = (await BoxAsync(pinned)).X;
        await scroller.EvaluateAsync("el => { el.scrollLeft = el.scrollWidth; }");
        await page.WaitForTimeoutAsync(200);
        Assert.AreEqual(x1, (await BoxAsync(pinned)).X, 1.0, "the pinned column keeps its edge while 15 columns scroll");
    }

    internal static async Task ZoomRtlAndTouchAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        // 200% zoom: DPR 2 at 720×450 — the toolbar still fits its card and the page does not overflow.
        var zoom = await OpenAsync(browser, baseUrl, "/data-table", new Options(720, 450, DeviceScaleFactor: 2), register);
        await SelectAsync(zoom, "dt-redesign-appearance", "Card");
        var table = Redesign(zoom);
        await table.ScrollIntoViewIfNeededAsync();
        await SettleAsync(zoom);
        var wrapper = await BoxAsync(table.Locator(".tm-data-table-wrapper"));
        foreach (var edge in await table.Locator(".tm-data-table-toolbar-right > *").EvaluateAllAsync<double[]>(
                     "els => els.map(e => e.getBoundingClientRect().right)"))
        {
            Assert.IsTrue(edge <= wrapper.X + wrapper.Width + 1, $"toolbar control at {edge} is clipped by the card ending at {wrapper.X + wrapper.Width}");
        }

        Assert.IsTrue(await zoom.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"), "no horizontal page overflow at 200%");

        // RTL: a column pinned to the start edge moves to the right side.
        var rtl = await OpenAsync(browser, baseUrl, "/data-table", new Options(), register);
        var wide = rtl.Locator("[data-testid='dt-edge-wide']");
        await wide.ScrollIntoViewIfNeededAsync();
        await rtl.EvaluateAsync("() => { document.documentElement.setAttribute('dir', 'rtl'); }");
        await SettleAsync(rtl);
        await rtl.WaitForTimeoutAsync(600);
        var scroller = await BoxAsync(wide.Locator(".tm-data-table-scroll"));
        var pin = await BoxAsync(wide.Locator("thead th.tm-col-pinned-left").First);
        Assert.IsTrue(pin.X + pin.Width / 2 > scroller.X + scroller.Width / 2,
            $"under dir=rtl the start-pinned column sits on the right (pin centre {pin.X + pin.Width / 2}, scroller centre {scroller.X + scroller.Width / 2})");

        // Coarse pointer on a tablet: every control the table owns is at least 44px.
        var touch = await OpenAsync(browser, baseUrl, "/data-table", new Options(1024, 768, Touch: true), register);
        Assert.IsTrue(await touch.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"), "precondition: coarse pointer");
        await SelectAsync(touch, "dt-redesign-column-menu", "Menu");
        var t = Redesign(touch);
        await t.ScrollIntoViewIfNeededAsync();
        foreach (var selector in new[] { ".tm-data-table-col-menu-btn", ".tm-filter-builder-add", ".tm-pagination-btn" })
        {
            var target = selector == ".tm-pagination-btn" ? touch.Locator("[data-testid='dt-edge-pager-wide']").Locator(selector).First : t.Locator(selector).First;
            await target.ScrollIntoViewIfNeededAsync();
            var box = await BoxAsync(target);
            Assert.IsTrue(box.Width >= 43.5 && box.Height >= 43.5, $"{selector} is {box.Width}×{box.Height}, expected ≥ 44px on a coarse pointer");
        }
    }

    /// <summary>
    /// Review fix: the default (Plain) surface keeps the pre-redesign group zone and export
    /// look — solid-primary chip, outlined Expand/Collapse buttons and single-line export
    /// items; the Card surface keeps the redesign styling.
    /// </summary>
    internal static async Task DefaultAppearanceGroupZoneAndExportAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/data-table", new Options(), register);
        var table = Redesign(page);
        await table.ScrollIntoViewIfNeededAsync();

        // The default export items are single-line (nowrap), like before the redesign.
        await table.Locator(".tm-data-table__export .tm-dropdown-trigger").ClickAsync();
        var csv = table.Locator("[data-export-format='csv']");
        await Assertions.Expect(csv).ToBeVisibleAsync();
        Assert.AreEqual("nowrap", await csv.EvaluateAsync<string>("el => getComputedStyle(el).whiteSpace"),
            "a default export item does not wrap onto two lines");
        await page.Keyboard.PressAsync("Escape");

        // Group via the Card-only "Add grouping" dropdown, then flip back to the default look.
        await SelectAsync(page, "dt-redesign-appearance", "Card");
        await table.Locator(".tm-data-table-add-grouping .tm-dropdown-trigger").ClickAsync();
        await table.Locator(".tm-dropdown-item").First.ClickAsync();
        var chip = table.Locator(".tm-data-table-group-chip").First;
        await Assertions.Expect(chip).ToBeVisibleAsync();
        await SelectAsync(page, "dt-redesign-appearance", "Plain");
        await Assertions.Expect(chip).ToBeVisibleAsync();

        const string colorProbe =
            "() => { const probe = document.createElement('div'); probe.style.display = 'none'; document.body.appendChild(probe);" +
            "const read = t => { probe.style.color = 'var(' + t + ')'; return getComputedStyle(probe).color; };" +
            "const zone = document.querySelector(\"[data-testid='dt-redesign-table'] .tm-data-table-group-zone\");" +
            "const chip = zone.querySelector('.tm-data-table-group-chip');" +
            "const expand = zone.querySelector('.tm-data-table-group-expand-all');" +
            "const out = [getComputedStyle(chip).backgroundColor, read('--tm-color-primary'), read('--tm-color-primary-subtle')," +
            "getComputedStyle(expand).borderStyle, expand.className]; probe.remove(); return out; }";

        // Default: solid primary chip and the original outlined mini-buttons.
        var plain = await page.EvaluateAsync<string[]>(colorProbe);
        Assert.AreEqual(plain[1], plain[0], "the default chip background is the primary colour");
        Assert.AreNotEqual(plain[2], plain[0], "the default chip is not the subtle pill");
        Assert.AreNotEqual("none", plain[3], "the default Expand all button keeps its border");
        Assert.IsFalse(plain[4].Contains("tm-link-button"), "the default buttons are not link-buttons");

        await PrepareForCaptureAsync(page);
        var dir = Path.Combine(FindRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "data-table-redesign", "light");
        Directory.CreateDirectory(dir);
        await table.Locator(".tm-data-table-group-zone").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(dir, "dt-default-group-zone.png"),
            Timeout = 30_000
        });

        // Card (opt-in): the subtle pill comes back and tm-link-button owns the buttons.
        await SelectAsync(page, "dt-redesign-appearance", "Card");
        var card = await page.EvaluateAsync<string[]>(colorProbe);
        Assert.AreEqual(card[2], card[0], "the Card chip uses the subtle primary");
        Assert.IsTrue(card[4].Contains("tm-link-button"), "the Card buttons are link-buttons");
    }
}
