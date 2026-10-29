using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// TmGantt redesign, phase 8 (mobile read-mostly, decision G2): at 390x844 and 320x640 the Gantt container resolves the
/// mobile layout — one pinned title column with two-line cells, the full-width view segment and chips, the bottom
/// <c>TmMobileActionBar</c> with the "More" sheet, the read-only task sheet with editable Status / Percent, the minimal
/// add-task sheet, the filter bottom sheet, the read-only history, the run-only reports, the portfolio mini-gantt cards
/// and the workload person list. Import / export are not offered anywhere on the mobile chrome, the page never scrolls
/// horizontally and the controls meet the 44 px target. Screenshots land in <c>TestResults/gantt/phase8</c> (committed
/// shots are rewritten only with <c>TM_E2E_UPDATE_BASELINE=1</c>).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class GanttPhase8MobileE2ETests : WasmTestBase
{
    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string ShotPath(string name)
    {
        var directory = Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E",
            UpdateBaseline ? Path.Combine("__screenshots__", "gantt", "phase8") : Path.Combine("TestResults", "gantt", "phase8"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{name}.png");
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx"))) return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("TempoBlazor.slnx not found above the test output.");
    }

    private async Task<IPage> OpenAsync(int width, int height, bool dark = false, string path = "/gantt")
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = true,
            IgnoreHTTPSErrors = true,
            Locale = "en-US",
        });
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{path}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90_000 });
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-gantt, .tm-gantt-portfolio").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        // Full-bleed: the demo page keeps a sidebar and gutters; the layout is container-owned, so give the Gantt the width the
        // test names. The showcase card's overflow:hidden would also pin the sticky action bar to a non-scrolling box (a
        // documented TmMobileActionBar constraint: sticky needs an overflow-visible ancestor chain), so the shots neutralize it.
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = "aside, nav.tm-sidebar, .tm-sidebar { display: none !important; } main, .tm-main { margin: 0 !important; padding: 0 !important; max-width: none !important; width: 100% !important; } .showcase-card { overflow: visible !important; }" });
        if (dark)
        {
            await page.EvaluateAsync("() => { document.documentElement.setAttribute('data-theme','dark'); document.documentElement.classList.add('dark','tm-dark'); }");
        }

        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private static ILocator Gantt(IPage page) => page.Locator(".tm-gantt").First;

    /// <summary>
    /// Taps the two-line TITLE CELL of a tree row. The row's geometric center at a phone width sits on the
    /// expand button (which stops the propagation by design), so the tests tap the readable cell instead.
    /// </summary>
    private static ILocator Cell(ILocator row) => row.Locator(".tm-gantt__mobile-cell").First;

    private static async Task WaitLayoutAsync(IPage page, string layout) =>
        await page.WaitForFunctionAsync("(l) => document.querySelector('.tm-gantt')?.getAttribute('data-layout') === l", layout, new PageWaitForFunctionOptions { Timeout = 20_000 });

    private static async Task ShootAsync(IPage page, string name)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        await page.WaitForTimeoutAsync(250);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = ShotPath(name), Timeout = 60_000 });
    }

    private static async Task<ICDPSession> CdpAsync(IPage page) => await page.Context.NewCDPSessionAsync(page);

    private static Task TouchAsync(ICDPSession cdp, string type, params (double X, double Y)[] points) =>
        cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = points.Select((p, i) => new Dictionary<string, object> { ["x"] = p.X, ["y"] = p.Y, ["id"] = i }).ToArray(),
        });

    private static async Task<(double X, double Y)> CenterAsync(ILocator locator)
    {
        var box = await locator.BoundingBoxAsync();
        Assert.IsNotNull(box, "the element has no box");
        return (box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    private static async Task LongPressAsync(ICDPSession cdp, (double X, double Y) at, int holdMs = 700)
    {
        await TouchAsync(cdp, "touchStart", at);
        await Task.Delay(holdMs);
        await TouchAsync(cdp, "touchEnd");
    }

    /// <summary>The page must not scroll horizontally; the timeline is the only horizontally scrolling surface.</summary>
    private static async Task AssertNoPageHorizontalScrollAsync(IPage page)
    {
        var sizes = JsonSerializer.Deserialize<double[]>(await page.EvaluateAsync<string>(
            "() => JSON.stringify([document.documentElement.scrollWidth, document.documentElement.clientWidth])"))!;
        Assert.IsTrue(sizes[0] <= sizes[1] + 1, $"the page scrolls horizontally ({sizes[0]} > {sizes[1]})");
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("390x844: mobile layout, title-only two-line tree, full-width segment, chips, action bar, no page overflow; light and dark shots")]
    public async Task Mobile_390_Layout_Segment_Chips_Bar_NoPageOverflow()
    {
        var page = await OpenAsync(390, 844);
        await WaitLayoutAsync(page, "mobile");

        var gantt = Gantt(page);
        Assert.IsTrue((await gantt.GetAttributeAsync("class"))!.Contains("tm-gantt--mobile"));

        // One identifying column; the cells are the two-line title + range.
        var headers = await gantt.Locator(".tm-gantt__tree-header [role='columnheader']").AllTextContentsAsync();
        Assert.IsTrue(headers.Where(t => !string.IsNullOrWhiteSpace(t)).SequenceEqual(new[] { "Task" }),
            $"the mobile tree keeps only the title column, saw: {string.Join("|", headers)}");
        Assert.IsTrue(await gantt.Locator(".tm-gantt__mobile-cell").First.IsVisibleAsync(), "the two-line cell renders");
        Assert.IsTrue(await gantt.Locator(".tm-gantt__mobile-cell-range").First.IsVisibleAsync(), "the range line renders");

        // The full-width segment and the chips; the data commands are gone.
        Assert.AreEqual(3, await gantt.Locator(".tm-gantt__segment--full button").CountAsync(), "Day | Week | Month");
        Assert.IsTrue(await gantt.Locator("[data-testid='gantt-cascade-sort']").IsVisibleAsync(), "sort chip");
        Assert.IsTrue(await gantt.Locator("[data-testid='gantt-fit-to-screen']").IsVisibleAsync(), "fit chip");
        Assert.IsTrue(await gantt.Locator("[data-testid='gantt-filter-btn']").IsVisibleAsync(), "filter chip");
        Assert.AreEqual(0, await page.Locator("[data-testid='gantt-export-btn'], [data-testid='gantt-import-btn']").CountAsync(),
            "export / import are not offered on the mobile chrome");
        Assert.AreEqual(0, await page.Locator("[data-testid='gantt-sidebar']").CountAsync(), "the rail is replaced by the More sheet");

        // The bottom action bar with its four tiles; the percent reads beside the bars.
        Assert.AreEqual(4, await page.Locator(".tm-mobile-action-bar__action").CountAsync(), "Add | Expand | Collapse | More");
        Assert.IsTrue(await page.Locator(".tm-mobile-action-bar__bar").First.IsVisibleAsync(), "the action bar renders");
        Assert.IsTrue(await gantt.Locator("[data-testid='bar-progress-label']").First.IsVisibleAsync(), "the percent shows beside the bars");

        await AssertNoPageHorizontalScrollAsync(page);
        await ShootAsync(page, "mobile-390-main-light");

        var dark = await OpenAsync(390, 844, dark: true);
        await WaitLayoutAsync(dark, "mobile");
        await AssertNoPageHorizontalScrollAsync(dark);
        await ShootAsync(dark, "mobile-390-main-dark");
    }

    [TestMethod]
    [Description("320x640: the narrow phone stays overflow-free")]
    public async Task Mobile_320_Narrow_NoPageOverflow()
    {
        var page = await OpenAsync(320, 640);
        await WaitLayoutAsync(page, "mobile");

        await AssertNoPageHorizontalScrollAsync(page);
        Assert.AreEqual(3, await Gantt(page).Locator(".tm-gantt__segment--full button").CountAsync());
        Assert.AreEqual(4, await page.Locator(".tm-mobile-action-bar__action").CountAsync());

        await ShootAsync(page, "mobile-320-main-light");

        var dark = await OpenAsync(320, 640, dark: true);
        await WaitLayoutAsync(dark, "mobile");
        await AssertNoPageHorizontalScrollAsync(dark);
        await ShootAsync(dark, "mobile-320-main-dark");
    }

    [TestMethod]
    [Description("390: tap a task, the read-only sheet opens as a bottom sheet, Status / Percent save; shots")]
    public async Task Mobile_TaskSheet_ReadOnly_Edit_Percent_Save()
    {
        var page = await OpenAsync(390, 844);
        await WaitLayoutAsync(page, "mobile");

        // A finger tap on the second row's title cell opens the sheet.
        await Cell(page.Locator(".tm-gantt__tree-row").Nth(1)).TapAsync();

        var sheet = page.Locator(".tm-drawer.tm-gantt__sheet").First;
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.IsTrue((await sheet.GetAttributeAsync("class"))!.Contains("tm-drawer--bottom"), "the mobile detail is a bottom sheet");
        Assert.IsTrue((await sheet.TextContentAsync())!.Contains("Full editing is available on a larger screen"),
            "the localized hint points to a larger screen");
        Assert.AreEqual(0, await sheet.Locator("[data-testid='task-title'] input").CountAsync(), "the title is read-only");
        Assert.AreEqual(0, await sheet.Locator("[data-testid='task-section-attachments']").CountAsync(), "attachments are tablet+");
        await ShootAsync(page, "mobile-390-task-sheet-light");

        // Edit the percent, save, and the chart reflects it.
        await sheet.Locator("input[data-testid='task-mobile-percent']").FillAsync("80");
        await sheet.Locator("[data-testid='task-mobile-save']").TapAsync();
        await page.WaitForTimeoutAsync(600);

        var dark = await OpenAsync(390, 844, dark: true);
        await WaitLayoutAsync(dark, "mobile");
        await Cell(dark.Locator(".tm-gantt__tree-row").Nth(1)).TapAsync();
        await dark.Locator(".tm-drawer.tm-gantt__sheet").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ShootAsync(dark, "mobile-390-task-sheet-dark");
    }

    [TestMethod]
    [Description("390: the action bar's Add opens the minimal sheet and creates the task")]
    public async Task Mobile_AddTask_Via_ActionBar()
    {
        var page = await OpenAsync(390, 844);
        await WaitLayoutAsync(page, "mobile");

        var rowsBefore = await page.Locator(".tm-gantt__tree-row").CountAsync();

        await page.Locator("[data-action-id='gantt-mobile-add']").TapAsync();
        var sheet = page.Locator("[data-testid='gantt-add-sheet']");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ShootAsync(page, "mobile-390-add-sheet-light");

        await sheet.Locator("[data-testid='gantt-add-name']").FillAsync("Mobile smoke task");
        await sheet.Locator("[data-testid='gantt-add-submit']").TapAsync();
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });

        await page.WaitForTimeoutAsync(600);
        var rowsAfter = await page.Locator(".tm-gantt__tree-row").CountAsync();
        Assert.IsTrue(rowsAfter > rowsBefore, $"the task was added ({rowsBefore} -> {rowsAfter})");
    }

    [TestMethod]
    [Description("390: the filter chip opens the bottom sheet and Apply filters the rows")]
    public async Task Mobile_Filter_Chip_Opens_Sheet_Apply()
    {
        var page = await OpenAsync(390, 844);
        await WaitLayoutAsync(page, "mobile");

        await page.Locator("[data-testid='gantt-filter-btn']").TapAsync();
        var dialog = page.Locator(".tm-modal-overlay.tm-gantt__filter-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ShootAsync(page, "mobile-390-filter-sheet-light");

        // A title condition that matches nothing: add the row, fill it, and Apply filters every row out.
        await dialog.Locator("[data-testid='filter-add']").TapAsync();
        await dialog.Locator("select.tm-gantt__filter-field").First.SelectOptionAsync("Title");
        var valueInput = dialog.Locator("input.tm-gantt__filter-value").First;
        if (await valueInput.CountAsync() > 0)
        {
            await valueInput.FillAsync("zzz-no-such-task");
        }

        await dialog.Locator("[data-testid='filter-apply']").TapAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await page.WaitForTimeoutAsync(600);

        // The filter chip now carries the active dot.
        Assert.IsTrue(await page.Locator("[data-testid='gantt-filter-btn'] .tm-gantt__mobile-chip-dot").IsVisibleAsync(),
            "the active filter is announced by the dot");
    }

    [TestMethod]
    [Description("390: the More sheet holds zoom / panel / history / reports / settings, never export / import; history read-only, reports run-only")]
    public async Task Mobile_More_Sheet_History_Reports()
    {
        var page = await OpenAsync(390, 844);
        await WaitLayoutAsync(page, "mobile");

        await page.Locator("[data-action-id='gantt-mobile-more']").TapAsync();
        var more = page.Locator("[data-testid='gantt-more-sheet']");
        await more.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        foreach (var id in new[] { "gantt-more-zoom-in", "gantt-more-zoom-out", "gantt-more-task-panel", "gantt-more-history", "gantt-more-reports", "gantt-more-settings" })
        {
            Assert.IsTrue(await more.Locator($"[data-testid='{id}']").IsVisibleAsync(), id);
        }

        await ShootAsync(page, "mobile-390-more-sheet-light");

        // The view-settings view inside the same sheet; the Back entry returns to the menu.
        await more.Locator("[data-testid='gantt-more-settings']").TapAsync();
        await page.Locator("[data-testid='vset-show-today']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.Locator("[data-testid='gantt-more-back']").TapAsync();
        await more.Locator("[data-testid='gantt-more-history']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // History: the read-only sheet (no time-travel, no rollback).
        await more.Locator("[data-testid='gantt-more-history']").TapAsync();
        var history = page.Locator("[data-testid='gantt-history-sheet']");
        await history.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.AreEqual(0, await history.Locator("[data-testid^='history-timetravel-']").CountAsync(), "time travel is tablet+");
        Assert.AreEqual(0, await history.Locator("[data-testid^='history-rollback-']").CountAsync(), "rollback is tablet+");
        await ShootAsync(page, "mobile-390-history-sheet-light");

        // Reports: run-only cards.
        await history.Locator(".tm-drawer__close").TapAsync();
        await page.Locator("[data-action-id='gantt-mobile-more']").TapAsync();
        await more.Locator("[data-testid='gantt-more-reports']").TapAsync();
        var reports = page.Locator("[data-testid='gantt-reports-sheet']");
        await reports.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.AreEqual(0, await reports.Locator("[data-testid^='report-menu-']").CountAsync(), "edit / delete menus are tablet+");
        Assert.AreEqual(0, await page.Locator("[data-testid='reports-new-btn']").CountAsync(), "creating a report is tablet+");
        await ShootAsync(page, "mobile-390-reports-sheet-light");
    }

    [TestMethod]
    [Description("390: the touch controls meet the 44px target")]
    public async Task Mobile_Controls_Meet_44px()
    {
        var page = await OpenAsync(390, 844);
        await WaitLayoutAsync(page, "mobile");

        var boxes = JsonSerializer.Deserialize<double[]>(await page.EvaluateAsync<string>(
            "() => JSON.stringify([...document.querySelectorAll('.tm-gantt__segment--full button, .tm-gantt__mobile-chip, .tm-mobile-action-bar__action')].map(b => b.getBoundingClientRect().height))"))!;
        foreach (var height in boxes)
        {
            Assert.IsTrue(height >= 43.5, $"a mobile control is shorter than 44px ({height})");
        }
    }

    [TestMethod]
    [Description("390: the long-press menu offers Status / Percent / Add task only, and Status opens the sheet")]
    public async Task Mobile_LongPress_Limited_Menu()
    {
        var page = await OpenAsync(390, 844);
        await WaitLayoutAsync(page, "mobile");

        var row = page.Locator(".tm-gantt__tree-row").Nth(2);
        // Center the row: the pinned action bar owns the bottom band of the viewport, and a row scrolled
        // to the bottom edge would be covered by it (the hold would land on the bar, not the cell).
        await row.EvaluateAsync("el => el.scrollIntoView({ block: 'center' })");
        // Let the pointer sessions of gantt.js settle (they attach after the first render) before the hold;
        // a hold that lands while the row is still settling reads as a scroll — retry once.
        await page.WaitForTimeoutAsync(800);
        var cdp = await CdpAsync(page);
        await LongPressAsync(cdp, await CenterAsync(Cell(row)), holdMs: 800);

        var menu = page.Locator(".tm-gantt__mobile-menu");
        if (!await menu.IsVisibleAsync())
        {
            await row.EvaluateAsync("el => el.scrollIntoView({ block: 'center' })");
            await page.WaitForTimeoutAsync(500);
            await LongPressAsync(cdp, await CenterAsync(Cell(row)), holdMs: 800);
        }
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.IsTrue(await menu.Locator("[data-testid='gantt-context-status']").IsVisibleAsync());
        Assert.IsTrue(await menu.Locator("[data-testid='gantt-context-percent']").IsVisibleAsync());
        Assert.IsTrue(await menu.Locator("[data-testid='gantt-context-add']").IsVisibleAsync());
        Assert.AreEqual(0, await menu.Locator("[data-testid='gantt-context-move-up']").CountAsync(), "reorder is tablet+");
        Assert.IsFalse((await menu.TextContentAsync())!.Contains("Delete task"), "delete is tablet+");

        await menu.Locator("[data-testid='gantt-context-status']").TapAsync();
        var sheet = page.Locator(".tm-drawer.tm-gantt__sheet").First;
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.IsTrue(await sheet.Locator("[data-testid='task-mobile-status']").IsVisibleAsync(), "the read-only sheet opens");
    }

    [TestMethod]
    [Description("390: the portfolio cards show the summary, the percent and the mini-gantt; no nested chart")]
    public async Task Mobile_Portfolio_MiniGantt_Cards()
    {
        var page = await OpenAsync(390, 844, path: "/gantt-portfolio");
        await page.WaitForTimeoutAsync(800);

        Assert.AreEqual(0, await page.Locator("[data-testid='gantt-demo-portfolio'] .tm-gantt").CountAsync(),
            "the nested chart is a desktop affordance");
        Assert.IsTrue(await page.Locator("[data-testid^='portfolio-summary-']").First.IsVisibleAsync(), "the summary line renders");
        Assert.IsTrue(await page.Locator(".tm-gantt-portfolio__mini-bar").First.IsVisibleAsync(), "the mini-gantt bars render");

        await AssertNoPageHorizontalScrollAsync(page);
        await ShootAsync(page, "mobile-390-portfolio-light");

        var dark = await OpenAsync(390, 844, dark: true, path: "/gantt-portfolio");
        await dark.WaitForTimeoutAsync(800);
        await ShootAsync(dark, "mobile-390-portfolio-dark");
    }

    [TestMethod]
    [Description("390: the workload view is the person list with a utilization bar and the pinned person column")]
    public async Task Mobile_Workload_PersonList()
    {
        var page = await OpenAsync(390, 844);
        await WaitLayoutAsync(page, "mobile");

        // The demo's own view selector switches to the Workload view.
        await page.Locator("button", new PageLocatorOptions { HasTextString = "Workload" }).First.TapAsync();
        await page.Locator(".tm-gantt__workload-mobile").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.AreEqual(7, await page.Locator(".tm-gantt__workload-mobile-header-cell").CountAsync(), "the week shows 7 day columns");
        Assert.IsTrue(await page.Locator(".tm-gantt__workload-util-bar").First.IsVisibleAsync(), "the utilization bar renders");
        Assert.IsTrue(await page.Locator("[data-testid^='workload-mobile-row-']").First.IsVisibleAsync());

        await AssertNoPageHorizontalScrollAsync(page);
        await ShootAsync(page, "mobile-390-workload-light");
    }
}
