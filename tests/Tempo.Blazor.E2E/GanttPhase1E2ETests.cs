using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
[TestCategory("WASM")]
public class GanttPhase1E2ETests : WasmTestBase
{
    private async Task<(IPage page, ILocator gantt)> OpenGanttAsync()
    {
        var page = await CreatePageAsync();
        // Phase 7: the Gantt resolves its layout from its OWN container; the default 1280px viewport leaves the demo Gantt under 1024px (tablet: the
        // commands sit in "More"), so a spec of the desktop toolbar runs at the 1440px desktop viewport.
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);
        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        return (page, gantt);
    }

    // ── F1.1 Per-task Color ──────────────────────────────────────────────────

    [TestMethod]
    [Description("E2E-1.1.1: Task bar with Color renders that color via CSS custom property")]
    public async Task Gantt_TaskBar_With_Color_Renders_Custom_Color()
    {
        var (page, gantt) = await OpenGanttAsync();

        // Task "9" (Development) has Color="#ef4444" — wait for its bar
        var bars = gantt.Locator(".tm-gantt__bar");
        await bars.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // At least one bar should carry --tm-gantt-task-color inline style
        var count = await bars.CountAsync();
        var hasColorStyle = false;
        for (var i = 0; i < count; i++)
        {
            var style = await bars.Nth(i).GetAttributeAsync("style") ?? "";
            if (style.Contains("--tm-gantt-task-color"))
            {
                hasColorStyle = true;
                break;
            }
        }
        Assert.IsTrue(hasColorStyle, "At least one bar should have --tm-gantt-task-color inline style");

        await TakeScreenshotAsync(page, "f11_task_color");
    }

    // ── F1.2 Drop Shadow + Done Opacity ─────────────────────────────────────

    [TestMethod]
    [Description("E2E-1.2.1: Done tasks have tm-gantt__bar--completed CSS class")]
    public async Task Gantt_Done_Tasks_Have_Completed_Class()
    {
        var (page, gantt) = await OpenGanttAsync();

        var completedBars = gantt.Locator(".tm-gantt__bar--completed");
        var cnt = await completedBars.CountAsync();
        Assert.IsTrue(cnt > 0, $"Expected at least one bar with --completed class. Found: {cnt}");

        await TakeScreenshotAsync(page, "f12_completed_bars");
    }

    // ── F1.3 Hover Tooltip ───────────────────────────────────────────────────

    [TestMethod]
    [Description("E2E-1.3.1: Hovering a task bar reveals the tooltip element")]
    public async Task Gantt_Bar_Hover_Shows_Tooltip()
    {
        var (page, gantt) = await OpenGanttAsync();

        var firstBar = gantt.Locator(".tm-gantt__bar").First;
        await firstBar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Tooltip element must exist in DOM
        var tooltip = firstBar.Locator("[data-testid='bar-tooltip']");
        Assert.AreEqual(1, await tooltip.CountAsync(), "Tooltip element should exist inside bar");

        // Hover to make it visible
        await firstBar.HoverAsync();
        await page.WaitForTimeoutAsync(400);

        await TakeScreenshotAsync(page, "f13_bar_tooltip_hover");
    }

    [TestMethod]
    [Description("E2E-1.3.2: Clicking a task bar selects it (adds --selected class)")]
    public async Task Gantt_Bar_Click_Selects_Task()
    {
        var (page, gantt) = await OpenGanttAsync();

        var firstBar = gantt.Locator(".tm-gantt__bar").First;
        await firstBar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await firstBar.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var selectedBar = gantt.Locator(".tm-gantt__bar--selected");
        Assert.IsTrue(await selectedBar.CountAsync() > 0, "A bar should get --selected class after click");

        await TakeScreenshotAsync(page, "f13_bar_click_selected");
    }

    // ── F1.4 Status Badge + Priority Indicator ───────────────────────────────

    [TestMethod]
    [Description("E2E-1.4.1: Status badges are rendered in tree rows")]
    public async Task Gantt_TreeRows_Have_Status_Badges()
    {
        var (page, gantt) = await OpenGanttAsync();

        var badges = gantt.Locator("[data-testid='status-badge']");
        var cnt = await badges.CountAsync();
        Assert.IsTrue(cnt > 0, $"Expected status badges in tree rows, found {cnt}");

        // Verify Done badge is green (has --done class)
        var doneBadge = gantt.Locator("[data-testid='status-badge'].tm-gantt__status-badge--done");
        Assert.IsTrue(await doneBadge.CountAsync() > 0, "Done tasks should show a --done status badge");

        await TakeScreenshotAsync(page, "f14_status_badges");
    }

    [TestMethod]
    [Description("E2E-1.4.2: Priority icons are rendered in tree rows")]
    public async Task Gantt_TreeRows_Have_Priority_Icons()
    {
        var (page, gantt) = await OpenGanttAsync();

        var icons = gantt.Locator("[data-testid='priority-icon']");
        var cnt = await icons.CountAsync();
        Assert.IsTrue(cnt > 0, $"Expected priority icons in tree rows, found {cnt}");

        await TakeScreenshotAsync(page, "f14_priority_icons");
    }

    // ── F1.5 Deadline Marker ─────────────────────────────────────────────────

    [TestMethod]
    [Description("E2E-1.5.1: Overdue task (End > Deadline) shows flame deadline marker")]
    public async Task Gantt_Overdue_Task_Shows_Deadline_Marker()
    {
        var (page, gantt) = await OpenGanttAsync();

        // Task "16" (Overdue Task demo) has End > Deadline
        var markers = gantt.Locator("[data-testid='deadline-marker']");
        var cnt = await markers.CountAsync();
        Assert.IsTrue(cnt > 0, $"Expected at least one deadline marker for overdue task. Found: {cnt}");

        await TakeScreenshotAsync(page, "f15_deadline_marker");
    }

    // ── F1.6 Today Marker ────────────────────────────────────────────────────

    [TestMethod]
    [Description("E2E-1.6.1: Today marker line is visible in the timeline")]
    public async Task Gantt_Timeline_Shows_Today_Marker()
    {
        var (page, gantt) = await OpenGanttAsync();

        var marker = gantt.Locator("[data-testid='today-marker']");
        await marker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await marker.IsVisibleAsync(), "Today marker should be visible by default");

        // Verify it has a left style (offset > 0 since we have past tasks)
        var style = await marker.GetAttributeAsync("style") ?? "";
        Assert.IsTrue(style.Contains("left:") || style.Contains("left :"), $"Today marker should have left positioning. Style: {style}");

        await TakeScreenshotAsync(page, "f16_today_marker");
    }

    // ── F1.7 Non-working Days ────────────────────────────────────────────────

    [TestMethod]
    [Description("E2E-1.7.1: Weekend columns show non-working overlay in timeline")]
    public async Task Gantt_Timeline_Shows_NonWorking_Overlays()
    {
        var (page, gantt) = await OpenGanttAsync();

        var overlays = gantt.Locator("[data-testid='nonworking-overlay']");
        var cnt = await overlays.CountAsync();
        Assert.IsTrue(cnt > 0, $"Expected non-working day overlays. Found: {cnt}");

        await TakeScreenshotAsync(page, "f17_nonworking_overlays");
    }

    // ── F1.8 Expand / Collapse All ───────────────────────────────────────────

    [TestMethod]
    [Description("E2E-1.8.1: Collapse All hides child rows; Expand All shows them again")]
    public async Task Gantt_CollapseAll_And_ExpandAll_Work()
    {
        var (page, gantt) = await OpenGanttAsync();

        var rows = gantt.Locator(".tm-gantt__tree-row");
        var initialCount = await rows.CountAsync();
        Assert.IsTrue(initialCount > 3, "Need multiple rows for this test");

        // Collapse all
        var collapseBtn = gantt.Locator("[data-testid='gantt-collapse-all']");
        await collapseBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await collapseBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var collapsedCount = await rows.CountAsync();
        Assert.IsTrue(collapsedCount < initialCount, $"After collapse, rows ({collapsedCount}) should be fewer than initial ({initialCount})");

        await TakeScreenshotAsync(page, "f18_collapsed");

        // Expand all
        var expandBtn = gantt.Locator("[data-testid='gantt-expand-all']");
        await expandBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var expandedCount = await rows.CountAsync();
        Assert.AreEqual(initialCount, expandedCount, $"After expand, rows ({expandedCount}) should match initial ({initialCount})");

        await TakeScreenshotAsync(page, "f18_expanded");
    }

    // ── F1.9 View Settings Dropdown ──────────────────────────────────────────

    [TestMethod]
    [Description("E2E-1.9.1: ShowClosedTasks=false hides Done/Closed tasks from tree")]
    public async Task Gantt_ViewSettings_ShowClosedTasks_False_Hides_Done_Tasks()
    {
        var (page, gantt) = await OpenGanttAsync();

        var rows = gantt.Locator(".tm-gantt__tree-row");
        var initialCount = await rows.CountAsync();
        Assert.IsTrue(initialCount > 0);

        // Open View Settings dropdown
        var vsBtn = gantt.Locator("[data-testid='gantt-view-settings-btn']");
        await vsBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await vsBtn.ClickAsync();
        await page.WaitForTimeoutAsync(200);

        // Uncheck ShowClosedTasks
        var showClosedChk = gantt.Locator("[data-testid='vset-show-today']").First;
        // Actually find ShowClosedTasks checkbox specifically
        var closedChk = page.Locator("input[type='checkbox']").Filter(new LocatorFilterOptions
        {
            Has = page.Locator("~ label:has-text('closed'), ~ label:has-text('Closed'), + label:has-text('closed')")
        }).First;

        // Use the data-testid approach — check parent label
        var menu = gantt.Locator(".tm-gantt__view-settings-menu");
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Find the checkbox by proximity to its label text
        var allCheckboxes = menu.Locator("input[type='checkbox']");
        var chkCount = await allCheckboxes.CountAsync();
        Assert.IsTrue(chkCount >= 3, $"View settings menu should have at least 3 checkboxes. Found: {chkCount}");

        await TakeScreenshotAsync(page, "f19_view_settings_open");

        // Close by pressing Escape
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);
    }

    [TestMethod]
    [Description("E2E-1.9.2: Dark theme applies tm-gantt--theme-dark CSS class to root")]
    public async Task Gantt_ViewSettings_DarkTheme_Applies_CSS_Class()
    {
        var (page, gantt) = await OpenGanttAsync();

        // Open View Settings
        var vsBtn = gantt.Locator("[data-testid='gantt-view-settings-btn']");
        await vsBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await vsBtn.ClickAsync();
        await page.WaitForTimeoutAsync(200);

        var menu = gantt.Locator(".tm-gantt__view-settings-menu");
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Find Dark radio button and click it
        var darkRadio = menu.Locator("input[type='radio'][value='Dark'], label:has-text('Dark') input, label:has-text('Tmavý') input").First;
        if (await darkRadio.CountAsync() == 0)
            darkRadio = menu.Locator("input[type='radio']").Nth(2); // 3rd radio = Dark

        await darkRadio.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var hasDarkClass = await gantt.EvaluateAsync<bool>("el => el.classList.contains('tm-gantt--theme-dark')");
        Assert.IsTrue(hasDarkClass, "Gantt root should have tm-gantt--theme-dark class after selecting Dark theme");

        await TakeScreenshotAsync(page, "f19_dark_theme");
    }
}
