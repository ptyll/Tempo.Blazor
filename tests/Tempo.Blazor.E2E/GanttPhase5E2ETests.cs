using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// TmGantt redesign, phase 5 (desktop panels): the task panel as an inspector (status / priority, grouped
/// sections, Actions, sticky Cancel | Save footer), the docked workload panel, the history drawer and sidebar feed,
/// the report cards with their kebab, and the collapsible portfolio cards - at desktop 1440, light and dark.
/// Computed-style and geometry assertions pin what bUnit cannot see; screenshots land in
/// <c>TestResults/gantt/phase5</c> (committed shots are rewritten only with <c>TM_E2E_UPDATE_BASELINE=1</c>).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class GanttPhase5E2ETests : WasmTestBase
{
    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string ShotPath(string name)
    {
        var directory = Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E",
            UpdateBaseline ? Path.Combine("__screenshots__", "gantt", "phase5") : Path.Combine("TestResults", "gantt", "phase5"));
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

    private async Task<IPage> OpenAsync(string route = "/gantt", bool dark = false, int width = 1440, int height = 900)
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
        await page.GotoAsync($"{BaseUrl}{route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90_000 });
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

    private static async Task<string> StyleAsync(ILocator locator, params string[] properties) =>
        await locator.EvaluateAsync<string>(
            "(el, props) => { const c = getComputedStyle(el); return props.map(p => c.getPropertyValue(p)).join('|'); }",
            properties);

    private static async Task ShootAsync(IPage page, ILocator target, string name)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        await target.ScrollIntoViewIfNeededAsync();
        await target.ScreenshotAsync(new LocatorScreenshotOptions { Path = ShotPath(name), Timeout = 60_000 });
    }

    private static async Task ToggleChipAsync(IPage page, string name) =>
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = name, Exact = true }).First.ClickAsync();

    private static async Task OpenTaskPanelAsync(IPage page)
    {
        await Gantt(page).Locator(".tm-gantt__tree-row").First.ClickAsync();
        await Gantt(page).Locator(".tm-gantt-task-panel").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    }

    // ── Task panel ───────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Gantt phase 5: the task panel has every section, the footer sits outside the scroller and stays put while it scrolls, Cancel | Save")]
    public async Task Gantt_TaskPanel_Sections_And_Sticky_Footer()
    {
        var page = await OpenAsync();
        await OpenTaskPanelAsync(page);
        var panel = Gantt(page).Locator(".tm-gantt-task-panel");

        var sections = await panel.Locator(".tm-gantt-task-panel__scroll > section").EvaluateAllAsync<string[]>("els => els.map(e => e.dataset.testid)");
        CollectionAssert.AreEqual(new[]
        {
            "task-section-plan", "task-section-progress", "task-section-structure", "task-section-appearance", "task-section-description",
            "task-section-attachments", "task-section-comments", "task-section-budget", "task-section-timelog", "task-section-dependencies",
            "task-section-actions",
        }, sections, "every section is present, in the planned order");

        var footer = panel.Locator(".tm-gantt-task-panel__actions");
        var before = await footer.BoundingBoxAsync();
        await panel.Locator(".tm-gantt-task-panel__scroll").EvaluateAsync("el => { el.scrollTop = el.scrollHeight; }");
        await page.WaitForTimeoutAsync(150);
        var after = await footer.BoundingBoxAsync();
        Assert.IsNotNull(before);
        Assert.IsNotNull(after);
        Assert.AreEqual(Math.Round(before!.Y), Math.Round(after!.Y), "the footer does not move while the fields scroll");
        Assert.IsTrue(await footer.IsVisibleAsync(), "the footer is in view at the bottom of the scroll");

        var order = await footer.Locator("button").EvaluateAllAsync<string[]>("els => els.map(e => e.dataset.testid)");
        CollectionAssert.AreEqual(new[] { "task-cancel", "task-save" }, order, "Cancel first, the primary Save last (right)");

        var save = await footer.Locator("[data-testid='task-save']").BoundingBoxAsync();
        var cancel = await footer.Locator("[data-testid='task-cancel']").BoundingBoxAsync();
        Assert.IsTrue(save!.X > cancel!.X, "Save is to the right of Cancel");

        var swatch = panel.Locator(".tm-gantt__panel-color-swatch").First;
        Assert.AreEqual("32px|32px", await StyleAsync(swatch, "width", "height"), "swatches are 32px");
    }

    [TestMethod]
    [Description("Gantt phase 5: the status select changes the task and the bar's status chip; Mark done sets 100 %")]
    public async Task Gantt_TaskPanel_Status_And_MarkDone()
    {
        var page = await OpenAsync();
        await OpenTaskPanelAsync(page);
        var panel = Gantt(page).Locator(".tm-gantt-task-panel");

        var select = panel.Locator("[data-testid='task-status'] select");
        await select.SelectOptionAsync("InProgress");
        Assert.AreEqual("InProgress", await select.InputValueAsync(), "the select keeps the chosen status");

        await panel.Locator("[data-testid='task-action-mark-done']").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual("Done", await panel.Locator("[data-testid='task-status'] select").InputValueAsync(), "Mark done re-syncs the status field");
        Assert.AreEqual("100", await panel.Locator("[data-testid='task-percent'] input").InputValueAsync(), "...and the percent field");
    }

    [TestMethod]
    [Description("Gantt phase 5: the task panel's Delete asks for confirmation and a cancel keeps the task")]
    public async Task Gantt_TaskPanel_Delete_Asks_For_Confirmation()
    {
        var page = await OpenAsync();
        await OpenTaskPanelAsync(page);
        var panel = Gantt(page).Locator(".tm-gantt-task-panel");
        var bars = await Gantt(page).Locator("[data-testid^='task-bar-']").CountAsync();

        await panel.Locator("[data-testid='task-action-delete']").ClickAsync();
        var dialog = page.Locator(".tm-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await dialog.Locator(".tm-dialog-btn-cancel").ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        Assert.AreEqual(bars, await Gantt(page).Locator("[data-testid^='task-bar-']").CountAsync(), "a cancelled delete keeps the task");
    }

    [TestMethod]
    [Description("Gantt phase 5: the task panel in light and dark at 1440")]
    public async Task Gantt_TaskPanel_Screenshots()
    {
        foreach (var dark in new[] { false, true })
        {
            var page = await OpenAsync(dark: dark);
            await OpenTaskPanelAsync(page);
            await ShootAsync(page, Gantt(page), $"gantt-task-panel--{(dark ? "dark" : "light")}");
        }
    }

    // ── Workload ─────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Gantt phase 5: the docked workload sits under the timeline, toggles Hours | Percent, and scrolls with the timeline")]
    public async Task Gantt_Workload_Docked_Toggle_And_Synchronous_Scroll()
    {
        var page = await OpenAsync();
        await ToggleChipAsync(page, "Docked workload");
        var dock = Gantt(page).Locator(".tm-gantt__docked-workload");
        await dock.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var main = await Gantt(page).Locator(".tm-gantt__main").BoundingBoxAsync();
        var dockBox = await dock.BoundingBoxAsync();
        Assert.IsTrue(dockBox!.Y >= main!.Y + main.Height - 2, "the panel sits under the tree + timeline");

        var firstCell = dock.Locator("[data-testid^='workload-cell-']").First;
        var hours = (await firstCell.TextContentAsync())!.Trim();
        await dock.Locator("[data-testid='workload-mode-percent']").ClickAsync();
        await page.WaitForTimeoutAsync(200);
        var percent = (await dock.Locator("[data-testid^='workload-cell-']").First.TextContentAsync())!.Trim();
        Assert.IsTrue(percent.Contains('%') || percent.Contains('–'), $"percent mode shows %, got '{percent}' (hours were '{hours}')");
        Assert.AreEqual("true", await dock.Locator("[data-testid='workload-mode-percent']").GetAttributeAsync("aria-pressed"));

        Assert.IsTrue(await dock.Locator(".tm-gantt__workload-legend-item").CountAsync() == 3, "the legend has three entries");
        Assert.IsTrue(await dock.Locator("[data-testid='workload-range-info']").IsVisibleAsync(), "the range note is shown");

        // the axis is the Gantt's own continuous range: more day columns than the sparse window would hold
        var scroller = dock.Locator(".tm-gantt__workload-scroller");
        var scrollable = await scroller.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth");
        if (scrollable)
        {
            await scroller.EvaluateAsync("el => { el.scrollLeft = el.scrollWidth; }");
            await page.WaitForTimeoutAsync(250);
            var timelineRatio = await Gantt(page).Locator(".tm-gantt__timeline").EvaluateAsync<double>(
                "el => { const m = el.scrollWidth - el.clientWidth; return m > 0 ? el.scrollLeft / m : 1; }");
            Assert.IsTrue(timelineRatio > 0.9, $"the timeline followed the workload scroll, ratio {timelineRatio}");

            await Gantt(page).Locator(".tm-gantt__timeline").EvaluateAsync("el => { el.scrollLeft = 0; }");
            await page.WaitForTimeoutAsync(250);
            var workloadLeft = await scroller.EvaluateAsync<double>("el => el.scrollLeft");
            Assert.IsTrue(workloadLeft < 5, $"the workload followed the timeline back to the start, got {workloadLeft}");
        }
    }

    [TestMethod]
    [Description("Gantt phase 5: the docked workload in light and dark at 1440")]
    public async Task Gantt_Workload_Docked_Screenshots()
    {
        foreach (var dark in new[] { false, true })
        {
            var page = await OpenAsync(dark: dark);
            await ToggleChipAsync(page, "Docked workload");
            await Gantt(page).Locator(".tm-gantt__docked-workload").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await ShootAsync(page, Gantt(page), $"gantt-workload-docked--{(dark ? "dark" : "light")}");
        }
    }

    // ── History ──────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Gantt phase 5: the history drawer lists typed entries, links the task, asks before a rollback and closes on Escape")]
    public async Task Gantt_History_Drawer_Icons_Link_Confirm_And_Escape()
    {
        var page = await OpenAsync();
        await Gantt(page).Locator("button[data-testid='gantt-history-btn']").ClickAsync();
        var drawer = page.Locator(".tm-gantt__history-drawer");
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.IsTrue(await drawer.Locator(".tm-gantt__history-entry-icon svg").CountAsync() > 0, "every entry has an icon in a circle");
        Assert.AreEqual("50%", await StyleAsync(drawer.Locator(".tm-gantt__history-entry-icon").First, "border-radius"), "the icon sits in a circle");
        Assert.IsTrue(await drawer.Locator("button.tm-gantt__history-entry-task").CountAsync() > 0, "a task title is a link");

        await drawer.Locator("[data-testid^='history-rollback-']").First.ClickAsync();
        var dialog = page.Locator(".tm-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await dialog.Locator(".tm-dialog-btn-cancel").ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        await ShootAsync(page, Gantt(page), "gantt-history-drawer--light");

        await drawer.FocusAsync();
        await page.Keyboard.PressAsync("Escape");
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        Assert.AreEqual("gantt-history-btn", await page.EvaluateAsync<string?>("() => document.activeElement && document.activeElement.dataset.testid"),
            "closing the drawer returns focus to the History button that opened it");
    }

    [TestMethod]
    [Description("Gantt phase 5: the sidebar History panel shares the list; dark drawer screenshot")]
    public async Task Gantt_History_Sidebar_And_Dark_Drawer()
    {
        var page = await OpenAsync();
        await Gantt(page).Locator("[data-testid='panel-tab-history']").ClickAsync();
        var feed = Gantt(page).Locator("[data-testid='sidebar-panel-history'] .tm-gantt__history-entry");
        await feed.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.AreEqual(0, await Gantt(page).Locator("[data-testid='sidebar-panel-history'] .tm-gantt__history-entry-actions").CountAsync(), "the sidebar feed is read-only");
        await ShootAsync(page, Gantt(page), "gantt-history-sidebar--light");

        var dark = await OpenAsync(dark: true);
        await Gantt(dark).Locator("button[data-testid='gantt-history-btn']").ClickAsync();
        await dark.Locator(".tm-gantt__history-drawer").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ShootAsync(dark, Gantt(dark), "gantt-history-drawer--dark");
    }

    // ── Reports ──────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Gantt phase 5: report cards have a type icon, a Secondary Run button and a kebab that offers the supplied callbacks; the panel replaces the task panel")]
    public async Task Gantt_Reports_Cards_Kebab_And_Right_Panel()
    {
        var page = await OpenAsync();
        await OpenTaskPanelAsync(page);
        await Gantt(page).Locator("[data-testid='panel-tab-reports']").ClickAsync();
        var panel = Gantt(page).Locator("[data-testid='gantt-reports-panel']");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.AreEqual(0, await Gantt(page).Locator(".tm-gantt-task-panel").CountAsync(), "one right-hand panel at a time");
        Assert.IsTrue(await panel.Locator(".tm-gantt-reports-panel__item-icon svg").CountAsync() > 0, "every card has its type icon");

        var menu = panel.Locator("[data-testid^='report-menu-']").First;
        await menu.Locator(".tm-dropdown-trigger").ClickAsync();
        var items = await page.Locator(".tm-dropdown-item").AllInnerTextsAsync();
        CollectionAssert.AreEqual(new[] { "Edit", "Duplicate", "Delete" }, items.Select(i => i.Trim()).ToArray(), "the demo handles all three callbacks");
        await page.Keyboard.PressAsync("Escape");

        await ShootAsync(page, Gantt(page), "gantt-reports--light");
        var dark = await OpenAsync(dark: true);
        await Gantt(dark).Locator("[data-testid='panel-tab-reports']").ClickAsync();
        await Gantt(dark).Locator("[data-testid='gantt-reports-panel']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ShootAsync(dark, Gantt(dark), "gantt-reports--dark");
    }

    // ── Portfolio ────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Gantt phase 5: a portfolio card shows range, count and progress, collapses and expands, hides the nested toolbar and sidebar")]
    public async Task Gantt_Portfolio_Cards_Collapse_And_Summary()
    {
        var page = await OpenAsync("/gantt-portfolio");
        var card = page.Locator("[data-testid='gantt-demo-portfolio'] [data-testid^='portfolio-project-']").First;
        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.IsTrue(await card.Locator(".tm-gantt-portfolio__project-range").IsVisibleAsync(), "the date range is shown");
        StringAssert.Contains((await card.Locator(".tm-gantt-portfolio__project-count").TextContentAsync())!, "Tasks:");
        Assert.IsTrue(await card.Locator("[role='progressbar']").IsVisibleAsync(), "the weighted progress bar is shown");
        Assert.AreEqual(0, await card.Locator(".tm-gantt__toolbar").CountAsync(), "the nested Gantt has no toolbar");
        Assert.AreEqual(0, await card.Locator("[data-testid$='gantt-sidebar']").CountAsync(), "...and no sidebar");

        var toggle = card.Locator("[data-testid^='portfolio-toggle-']");
        Assert.AreEqual("true", await toggle.GetAttributeAsync("aria-expanded"));
        await ShootAsync(page, page.Locator("[data-testid='gantt-demo-portfolio']"), "gantt-portfolio-expanded--light");
        await toggle.ClickAsync();
        Assert.AreEqual("false", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.AreEqual(0, await card.Locator(".tm-gantt__tree-row").CountAsync(), "a collapsed card hides its Gantt");
        await ShootAsync(page, page.Locator("[data-testid='gantt-demo-portfolio']"), "gantt-portfolio-collapsed--light");

        var dark = await OpenAsync("/gantt-portfolio", dark: true);
        await ShootAsync(dark, dark.Locator("[data-testid='gantt-demo-portfolio']"), "gantt-portfolio-expanded--dark");
    }
}
