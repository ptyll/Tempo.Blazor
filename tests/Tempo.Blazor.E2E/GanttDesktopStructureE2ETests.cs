using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// TmGantt redesign, phase 4 (desktop structure): the grouped labelled toolbar, the split "Add task", the panel
/// tab strip, the Duration / Priority columns with the column menu, and the treegrid keyboard contract, at the
/// desktop 1440 viewport in light and dark. Screenshots land in <c>TestResults/gantt/phase4</c> (committed
/// shots are rewritten only with <c>TM_E2E_UPDATE_BASELINE=1</c>). The demo page opens with panel tabs, labels
/// and quick search ON, so the "without labels / rail" contrasts toggle the demo chips first.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class GanttDesktopStructureE2ETests : WasmTestBase
{
    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string ShotPath(string name)
    {
        var directory = Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E",
            UpdateBaseline ? Path.Combine("__screenshots__", "gantt", "phase4") : Path.Combine("TestResults", "gantt", "phase4"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{name}.png");
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("TempoBlazor.slnx not found above the test output.");
    }

    private async Task<IPage> OpenAsync(bool dark = false, int width = 1440, int height = 900)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
            Locale = "en-US",
        });
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/gantt", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90_000 });
        await WaitForAppReadyAsync(page);
        await Gantt(page).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await Gantt(page).Locator(".tm-gantt__tree-row").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        if (dark)
        {
            await page.EvaluateAsync(
                """
                () => {
                    document.documentElement.setAttribute('data-theme', 'dark');
                    document.documentElement.classList.add('dark', 'tm-dark');
                    document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', 'dark'));
                }
                """);
        }

        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private static ILocator Gantt(IPage page) => page.Locator(".tm-gantt").First;

    private static async Task ToggleChipAsync(IPage page, string name) =>
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = name, Exact = true }).First.ClickAsync();

    private static async Task ShootAsync(IPage page, string name)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        await Gantt(page).ScrollIntoViewIfNeededAsync();
        await Gantt(page).ScreenshotAsync(new LocatorScreenshotOptions { Path = ShotPath(name), Timeout = 60_000 });
    }

    [TestMethod]
    [Description("Gantt phase 4: the toolbar is a role=toolbar of six labelled groups with visible labels (demo default)")]
    public async Task Gantt_Toolbar_Is_Grouped_With_Visible_Labels()
    {
        var page = await OpenAsync();
        var gantt = Gantt(page);

        var toolbar = gantt.Locator(".tm-gantt__toolbar [role='toolbar']");
        Assert.AreEqual(1, await toolbar.CountAsync());
        var groups = await toolbar.Locator("[role='group'][data-tm-group]:not(.tm-gantt__segment)").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('data-tm-group'))");
        CollectionAssert.AreEqual(new[] { "create", "structure", "time", "panels", "data", "settings" }, groups);

        var labelled = await gantt.Locator("button[data-testid='gantt-export-btn'] .tm-toolbar-btn-text").InnerTextAsync();
        Assert.AreEqual("Export", labelled.Trim());
        var addTask = await gantt.Locator("button[data-testid='gantt-add-task']").InnerTextAsync();
        StringAssert.Contains(addTask, "Add task");

        await ShootAsync(page, "gantt-desktop-labels-light");
    }

    [TestMethod]
    [Description("Gantt phase 4: without the label toggle the toolbar is today's icon row, every button still named")]
    public async Task Gantt_Toolbar_Without_Labels_Is_Icon_Only_And_Named()
    {
        var page = await OpenAsync();
        await ToggleChipAsync(page, "Toolbar labels");
        var gantt = Gantt(page);

        Assert.AreEqual(0, await gantt.Locator("button[data-testid='gantt-export-btn'] .tm-toolbar-btn-text").CountAsync());
        var unnamed = await gantt.Locator(".tm-gantt__toolbar button").EvaluateAllAsync<int>(
            "els => els.filter(b => !(b.getAttribute('aria-label') || (b.textContent || '').trim())).length");
        Assert.AreEqual(0, unnamed, "every toolbar button has an accessible name");
        Assert.AreEqual(0, await gantt.Locator(".tm-gantt__toolbar input[type='search']").CountAsync(), "quick search follows the demo chip");

        await ShootAsync(page, "gantt-desktop-icons-light");
    }

    [TestMethod]
    [Description("Gantt phase 4: dark desktop with labels")]
    public async Task Gantt_Desktop_Dark_With_Labels()
    {
        var page = await OpenAsync(dark: true);
        var gantt = Gantt(page);

        var toolbarBg = await gantt.Locator(".tm-gantt__toolbar").EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        var treeBg = await gantt.Locator(".tm-gantt__tree").EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.AreEqual(treeBg, toolbarBg, "toolbar and grid paint the same (dark) surface token");
        Assert.AreNotEqual("rgb(255, 255, 255)", toolbarBg);

        await ShootAsync(page, "gantt-desktop-labels-dark");
    }

    [TestMethod]
    [Description("Gantt phase 4: the panel tab strip is an ARIA tablist and arrow keys move selection and focus")]
    public async Task Gantt_Panel_Tabs_Are_Aria_Tabs_With_Arrow_Navigation()
    {
        var page = await OpenAsync();
        var gantt = Gantt(page);

        var tablist = gantt.Locator("[role='tablist']");
        await tablist.WaitForAsync();
        Assert.AreEqual(5, await tablist.Locator("[role='tab']").CountAsync());
        Assert.AreEqual(0, await gantt.Locator("nav.tm-gantt__sidebar").CountAsync(), "the icon rail is replaced");
        Assert.AreEqual("true", await tablist.Locator("[role='tab']").First.GetAttributeAsync("aria-selected"));

        await tablist.Locator("[role='tab']").First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await gantt.Locator("[data-testid='sidebar-panel-workload']").WaitForAsync();
        var focusedTab = await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('data-testid')");
        Assert.AreEqual("panel-tab-workload", focusedTab, "focus follows the selection");
        Assert.AreEqual("true", await gantt.Locator("[data-testid='panel-tab-workload']").GetAttributeAsync("aria-selected"));

        await ShootAsync(page, "gantt-desktop-tabs-workload-light");
        // ShootAsync blurs the active element (a clean frame), so put the focus back on the selected tab first.
        await gantt.Locator("[data-testid='panel-tab-workload']").FocusAsync();
        await page.Keyboard.PressAsync("Home");
        await gantt.Locator("[data-testid='sidebar-panel-workload']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        Assert.AreEqual("true", await gantt.Locator("[data-testid='panel-tab-tasks']").GetAttributeAsync("aria-selected"), "Home selects the first tab");
    }

    [TestMethod]
    [Description("Gantt phase 4: the rail is still there when the demo chip switches the tab strip off")]
    public async Task Gantt_Rail_Mode_Is_Unchanged()
    {
        var page = await OpenAsync();
        await ToggleChipAsync(page, "Panel tabs");
        var gantt = Gantt(page);

        Assert.AreEqual(0, await gantt.Locator("[role='tablist']").CountAsync());
        Assert.AreEqual(1, await gantt.Locator("nav.tm-gantt__sidebar").CountAsync());
        Assert.AreEqual(5, await gantt.Locator("nav.tm-gantt__sidebar button").CountAsync());
        await ShootAsync(page, "gantt-desktop-rail-light");
    }

    [TestMethod]
    [Description("Gantt phase 4: the Add task split offers above / below / child for the selected task and inserts relative to it")]
    public async Task Gantt_AddTask_Split_Menu_Inserts_Relative_To_Selection()
    {
        var page = await OpenAsync();
        var gantt = Gantt(page);
        var rows = gantt.Locator(".tm-gantt__tree-row");
        var before = await rows.CountAsync();

        await gantt.Locator("[data-testid='gantt-add-task-split'] .tm-split-button__toggle").ClickAsync();
        var disabled = await page.Locator(".tm-split-button__dropdown .tm-context-menu__item[disabled]").CountAsync();
        Assert.AreEqual(3, disabled, "no selection, nothing to insert relative to");
        await page.Keyboard.PressAsync("Escape");

        await rows.Nth(1).ClickAsync();
        await gantt.Locator("[data-testid='gantt-add-task-split'] .tm-split-button__toggle").ClickAsync();
        var items = page.Locator(".tm-split-button__dropdown .tm-context-menu__item");
        Assert.AreEqual(3, await items.CountAsync());
        await page.Mouse.MoveAsync(0, 0);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = ShotPath("gantt-desktop-add-split-open-light"), Timeout = 60_000 });
        await items.Nth(1).ClickAsync();

        await page.WaitForFunctionAsync("(n) => document.querySelectorAll('.tm-gantt__tree-row').length === n", before + 1);
        Assert.AreEqual(before + 1, await rows.CountAsync());
    }

    [TestMethod]
    [Description("Gantt phase 4: Duration and Priority columns render; the column menu hides and shows a column")]
    public async Task Gantt_Duration_Priority_Columns_And_Column_Menu()
    {
        var page = await OpenAsync();
        var gantt = Gantt(page);

        // TextContent, not InnerText: the header cells are text-transform: uppercase.
        var header = await gantt.Locator(".tm-gantt__tree-header").TextContentAsync() ?? string.Empty;
        StringAssert.Contains(header, "Duration");
        StringAssert.Contains(header, "Priority");
        var duration = await gantt.Locator("[data-testid^='tree-cell-duration-']").First.InnerTextAsync();
        StringAssert.Matches(duration.Trim(), new System.Text.RegularExpressions.Regex(@"^\d+ days?$"));
        Assert.IsTrue(await gantt.Locator("[data-testid^='tree-cell-priority-'] .tm-gantt__priority-glyph svg").CountAsync() > 0, "priority shows a glyph");

        await gantt.Locator("[data-testid='gantt-column-menu'] .tm-column-picker-toggle").ClickAsync();
        await page.Locator(".tm-column-picker-panel").WaitForAsync();
        await ShootAsync(page, "gantt-desktop-column-menu-light");
        await page.Locator(".tm-column-picker-item input[data-key='Duration']").UncheckAsync();
        await page.Keyboard.PressAsync("Escape");

        Assert.AreEqual(0, await gantt.Locator("[data-testid^='tree-cell-duration-']").CountAsync(), "the hidden column leaves the grid");
        Assert.AreEqual(0, await gantt.Locator("[data-testid='col-header-duration']").CountAsync());
    }

    [TestMethod]
    [Description("Gantt phase 4: the treegrid is keyboard operable - Tab into it, arrows move, Left/Right collapse/expand, Enter opens the panel once")]
    public async Task Gantt_Treegrid_Keyboard_Contract()
    {
        var page = await OpenAsync();
        var gantt = Gantt(page);
        var grid = gantt.Locator("[role='treegrid']");
        Assert.AreEqual("Task list", await grid.GetAttributeAsync("aria-label"));

        var rows = gantt.Locator(".tm-gantt__tree-row");
        Assert.AreEqual(1, await gantt.Locator(".tm-gantt__tree-row[tabindex='0']").CountAsync(), "exactly one roving tab stop");
        await rows.First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        var second = await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('data-testid')");
        Assert.AreEqual(await rows.Nth(1).GetAttributeAsync("data-testid"), second, "ArrowDown moved DOM focus to the next row");

        await page.Keyboard.PressAsync("ArrowUp");
        var first = await rows.First.GetAttributeAsync("aria-expanded");
        if (first is not null)
        {
            await page.Keyboard.PressAsync("ArrowLeft");
            Assert.AreEqual("false", await rows.First.GetAttributeAsync("aria-expanded"), "ArrowLeft collapses a parent");
            await page.Keyboard.PressAsync("ArrowRight");
            Assert.AreEqual("true", await rows.First.GetAttributeAsync("aria-expanded"), "ArrowRight expands it again");
        }

        await page.Keyboard.PressAsync("Enter");
        await page.Locator(".tm-gantt-task-panel").First.WaitForAsync();
        Assert.AreEqual(1, await page.Locator(".tm-gantt-task-panel").CountAsync(), "Enter opens exactly one task panel");
        Assert.AreEqual("true", await rows.First.GetAttributeAsync("aria-selected"));
        await ShootAsync(page, "gantt-desktop-treegrid-focus-light");
    }

    [TestMethod]
    [Description("Gantt phase 4 (UX review): every header cell sits over its column - header and cells share their left edge")]
    public async Task Gantt_Tree_Header_Cells_Align_With_Their_Columns()
    {
        var page = await OpenAsync();
        var gantt = Gantt(page);
        var firstRow = gantt.Locator(".tm-gantt__tree-row").First;
        var headers = gantt.Locator(".tm-gantt__tree-header [role='columnheader']");
        var cells = firstRow.Locator("[role='gridcell']");
        var count = Math.Min(await headers.CountAsync(), await cells.CountAsync());
        Assert.IsTrue(count >= 5, "the demo grid has Title, Status, Start, End, Duration, Priority and custom fields");
        for (var i = 0; i < count; i++)
        {
            var h = (await headers.Nth(i).BoundingBoxAsync())!;
            var c = (await cells.Nth(i).BoundingBoxAsync())!;
            Assert.AreEqual(c.X, h.X, 2, $"column {i}: the header starts where its cells start");
        }
    }

    [TestMethod]
    [Description("Gantt phase 4: Tab walks the toolbar as ONE roving stop; arrows move inside it")]
    public async Task Gantt_Toolbar_Is_One_Tab_Stop_With_Arrow_Roving()
    {
        var page = await OpenAsync();
        var gantt = Gantt(page);
        var stops = await gantt.Locator(".tm-gantt__toolbar [role='toolbar'] [tabindex='0']").CountAsync();
        Assert.IsTrue(stops >= 1 && stops <= 3, $"the toolbar exposes a roving tab stop, not one per button (found {stops})");

        await gantt.Locator("button[data-testid='gantt-add-task']").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        var active = await page.EvaluateAsync<string>("() => document.activeElement?.className || ''");
        StringAssert.Contains(active, "tm-split-button__toggle", "the split's toggle is the next arrow stop");
    }

    [TestMethod]
    [Description("Gantt phase 4: bars are focusable from the row; Enter on a bar opens the panel, Escape returns to the row")]
    public async Task Gantt_Bar_Is_Reachable_From_Its_Row()
    {
        var page = await OpenAsync();
        var gantt = Gantt(page);
        // A leaf row: ArrowRight focuses its bar.
        var leaf = gantt.Locator(".tm-gantt__tree-row:not([aria-expanded])").First;
        await leaf.FocusAsync();
        var rowId = await leaf.GetAttributeAsync("data-testid");
        await page.Keyboard.PressAsync("ArrowRight");
        var barFocused = await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('data-testid') || document.activeElement?.className || ''");
        StringAssert.Contains(barFocused, "task-bar-", "the bar of the leaf took the focus");
        Assert.AreEqual("button", await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('role')"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('aria-label')")));

        await page.Keyboard.PressAsync("Escape");
        var back = await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('data-testid')");
        Assert.AreEqual(rowId, back, "Escape returns to the row");
    }

    [TestMethod]
    [Description("Gantt phase 4 edge: a narrow container (900px) keeps every toolbar control reachable (the bar wraps, nothing is clipped)")]
    public async Task Gantt_Toolbar_Wraps_Instead_Of_Clipping_When_Narrow()
    {
        var page = await OpenAsync(width: 900, height: 900);
        var gantt = Gantt(page);
        var toolbar = gantt.Locator(".tm-gantt__toolbar");
        var box = (await toolbar.BoundingBoxAsync())!;
        var ganttBox = (await gantt.BoundingBoxAsync())!;
        var overflow = await toolbar.Locator("[role='toolbar']").EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 1");
        Assert.IsFalse(overflow, "the toolbar wraps its groups instead of scrolling or clipping");
        Assert.IsTrue(box.Width <= ganttBox.Width + 1);
        await ShootAsync(page, "gantt-desktop-narrow-900-light");
    }

    [TestMethod]
    [Description("Gantt phase 4 edge: the quick search narrows the tree through Filters and the filter button shows the badge")]
    public async Task Gantt_QuickSearch_Filters_Tree_And_Shows_Badge()
    {
        var page = await OpenAsync();
        var gantt = Gantt(page);
        var rows = gantt.Locator(".tm-gantt__tree-row");
        var before = await rows.CountAsync();

        var search = gantt.Locator(".tm-gantt__toolbar input[type='search']");
        await search.FillAsync("zzz-no-such-task");
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-gantt__tree-row').length === 0");
        var badge = await gantt.Locator("button[data-testid='gantt-filter-btn'] .tm-toolbar-btn-badge").InnerTextAsync();
        Assert.AreEqual("1", badge.Trim());

        await search.FillAsync("");
        await page.WaitForFunctionAsync("(n) => document.querySelectorAll('.tm-gantt__tree-row').length === n", before);
        Assert.AreEqual(0, await gantt.Locator("button[data-testid='gantt-filter-btn'] .tm-toolbar-btn-badge").CountAsync());
    }
}