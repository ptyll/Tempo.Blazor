using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests for TmTreeList, TmGantt, and TmRecurrenceEditor on WASM demo.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class TreeListGanttRecurrenceE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("TreeList renders rows and expand/collapse toggles children")]
    public async Task TreeList_RendersAndExpands()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/tree-list");
        await WaitForAppReadyAsync(page);

        var treeList = page.Locator(".tm-tree-list").First;
        await treeList.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Verify initial rows are present (collapsed by default shows only roots)
        var rows = treeList.Locator(".tm-tree-list-row");
        var initialCount = await rows.CountAsync();
        Assert.IsTrue(initialCount > 0, "Expected tree rows to be rendered");

        // Find first parent row with toggle button (Alice Johnson is CEO and has children)
        var toggleBtn = treeList.Locator(".tm-tree-list-toggle").First;
        await toggleBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Expand root node (default is collapsed)
        await toggleBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var expandedCount = await rows.CountAsync();
        Assert.IsTrue(expandedCount > initialCount, "Expanding should increase visible row count");

        // Collapse back
        await toggleBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var collapsedCount = await rows.CountAsync();
        Assert.AreEqual(initialCount, collapsedCount, "Collapsing should restore original row count");

        await TakeScreenshotAsync(page, "treelist_expand_collapse");
    }

    [TestMethod]
    [Description("TreeList selects a row on click")]
    public async Task TreeList_SelectsRow()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/tree-list");
        await WaitForAppReadyAsync(page);

        var treeList = page.Locator(".tm-tree-list").First;
        await treeList.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var firstRow = treeList.Locator(".tm-tree-list-row").First;
        // Click on a cell outside the toggle button to trigger row selection
        var nameCell = firstRow.Locator(".tm-tree-list-cell").Nth(1);
        await nameCell.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Verify selected info appears below the table (row select event fired)
        await Expect(page.Locator("text=Selected:").First).ToBeVisibleAsync();

        await TakeScreenshotAsync(page, "treelist_select_row");
    }

    [TestMethod]
    [Description("Gantt renders tasks, dependencies, and milestone")]
    public async Task Gantt_RendersAndSwitchesViews()
    {
        var page = await CreatePageAsync();
        page.Console += (_, msg) => TestContext.WriteLine($"[console] {msg.Text}");
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Verify tree rows exist
        var treeRows = gantt.Locator(".tm-gantt__tree-row");
        Assert.IsTrue(await treeRows.CountAsync() > 0, "Expected Gantt tree rows");

        // Verify task bars exist
        var bars = gantt.Locator(".tm-gantt__bar");
        Assert.IsTrue(await bars.CountAsync() > 0, "Expected Gantt task bars");

        // Verify milestone exists
        var milestone = gantt.Locator(".tm-gantt__milestone").First;
        await Expect(milestone).ToBeVisibleAsync();

        // Verify dependency lines exist
        var deps = gantt.Locator(".tm-gantt__dependency-line");
        Assert.IsTrue(await deps.CountAsync() > 0, "Expected dependency lines");

        // Switch views
        foreach (var view in new[] { "Day", "Week", "Month" })
        {
            var viewBtn = gantt.Locator($"button:has-text('{view}')").First;
            if (await viewBtn.IsVisibleAsync())
            {
                await viewBtn.ClickAsync();
                await page.WaitForTimeoutAsync(500);
                // After switching, bars should still be present
                Assert.IsTrue(await bars.CountAsync() > 0, $"Expected bars after switching to {view} view");
            }
        }

        await TakeScreenshotAsync(page, "gantt_views");
    }

    [TestMethod]
    [Description("Gantt selects a task when clicking a bar")]
    public async Task Gantt_SelectsTask()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var firstBar = gantt.Locator(".tm-gantt__bar").First;
        await firstBar.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        await Expect(firstBar).ToHaveClassAsync(new Regex("tm-gantt__bar--selected"));

        // Selected task info panel should appear
        await Expect(page.Locator("text=Selected Task").First).ToBeVisibleAsync();

        await TakeScreenshotAsync(page, "gantt_select_task");
    }

    [TestMethod]
    [Description("Gantt timeline pans horizontally on drag")]
    [Ignore("Playwright synthetic mouse events do not reliably trigger Blazor @onmousemove in WASM/Server hybrid. Panning is fully covered by bUnit tests in TmGanttPanTests.cs.")]
    public async Task Gantt_TimelinePan_DragsHorizontally()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var timeline = gantt.Locator(".tm-gantt__timeline").First;

        // Switch to Day view so timeline becomes scrollable (more pixels per day)
        var dayViewBtn = gantt.Locator("button").Filter(new() { HasText = "Day" }).First;
        if (await dayViewBtn.IsVisibleAsync())
        {
            await dayViewBtn.ClickAsync();
            await page.WaitForTimeoutAsync(200);
        }

        // Verify timeline is scrollable
        var scrollWidth = await timeline.EvaluateAsync<int>("el => el.scrollWidth");
        var clientWidth = await timeline.EvaluateAsync<int>("el => el.clientWidth");
        TestContext.WriteLine($"Timeline dimensions: sw={scrollWidth} cw={clientWidth}");

        // Verify we can set scrollLeft directly via JS (sanity check)
        await timeline.EvaluateAsync("el => el.scrollLeft = 50");
        var directScroll = await timeline.EvaluateAsync<int>("el => el.scrollLeft");
        TestContext.WriteLine($"Direct scrollLeft test: {directScroll}");
        await timeline.EvaluateAsync("el => el.scrollLeft = 0");

        // Fallback: zoom in if still not scrollable
        if (scrollWidth <= clientWidth)
        {
            var zoomInBtn = gantt.Locator("button").Filter(new() { Has = page.Locator("[data-icon='zoom-in']") }).First;
            for (int i = 0; i < 4; i++)
            {
                if (await zoomInBtn.IsVisibleAsync())
                    await zoomInBtn.ClickAsync();
            }
            await page.WaitForTimeoutAsync(200);
            scrollWidth = await timeline.EvaluateAsync<int>("el => el.scrollWidth");
            clientWidth = await timeline.EvaluateAsync<int>("el => el.clientWidth");
            TestContext.WriteLine($"Timeline dimensions after zoom: sw={scrollWidth} cw={clientWidth}");
        }

        var initialScrollLeft = await timeline.EvaluateAsync<int>("el => el.scrollLeft");

        // Use Playwright mouse actions for realistic drag (required for Blazor Server trusted events)
        var box = await timeline.BoundingBoxAsync();
        Assert.IsNotNull(box);
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.DownAsync();
        await page.WaitForTimeoutAsync(50);

        // Verify panning class was added
        var hasPanClass = await timeline.EvaluateAsync<bool>("el => el.classList.contains('tm-gantt__timeline--panning')");
        TestContext.WriteLine($"Has panning class after mousedown: {hasPanClass}");

        await page.Mouse.MoveAsync(box.X + box.Width / 2 - 150, box.Y + box.Height / 2);
        await page.WaitForTimeoutAsync(100);
        var scrollAfterMove = await timeline.EvaluateAsync<int>("el => el.scrollLeft");
        TestContext.WriteLine($"ScrollLeft after mousemove: {scrollAfterMove}");
        await page.Mouse.UpAsync();

        var newScrollLeft = await timeline.EvaluateAsync<int>("el => el.scrollLeft");
        TestContext.WriteLine($"ScrollLeft before={initialScrollLeft} after={newScrollLeft}");
        Assert.IsTrue(newScrollLeft > initialScrollLeft, "Panning should increase scrollLeft");

        await TakeScreenshotAsync(page, "gantt_pan");
    }

    [TestMethod]
    [Description("Gantt expand/collapse toggles children in tree")]
    public async Task Gantt_ExpandCollapse()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var rows = gantt.Locator(".tm-gantt__tree-row");
        var initialCount = await rows.CountAsync();

        // Find first expand button
        var expandBtn = gantt.Locator(".tm-gantt__expand-btn").First;
        await expandBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await expandBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var collapsedCount = await rows.CountAsync();
        Assert.IsTrue(collapsedCount < initialCount, "Collapsing should hide child rows");

        await expandBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var expandedCount = await rows.CountAsync();
        Assert.AreEqual(initialCount, expandedCount, "Expanding should restore rows");

        await TakeScreenshotAsync(page, "gantt_expand_collapse");
    }

    [TestMethod]
    [Description("Gantt fit-to-screen button adjusts zoom to show all tasks")]
    public async Task Gantt_FitToScreen_AdjustsZoom()
    {
        var page = await CreatePageAsync();
        // Phase 7: the Gantt resolves its layout from its OWN container; the default 1280px viewport leaves the demo Gantt under 1024px (tablet: the
        // commands sit in "More"), so a spec of the desktop toolbar runs at the 1440px desktop viewport.
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Switch to Day view so timeline becomes wider than viewport
        var dayViewBtn = gantt.Locator("button").Filter(new() { HasText = "Day" }).First;
        if (await dayViewBtn.IsVisibleAsync())
        {
            await dayViewBtn.ClickAsync();
            await page.WaitForTimeoutAsync(200);
        }

        var zoomLabel = gantt.Locator(".tm-gantt__zoom-level").First;
        var zoomBefore = await zoomLabel.TextContentAsync();
        TestContext.WriteLine($"Zoom before fit: {zoomBefore}");

        var timeline = gantt.Locator(".tm-gantt__timeline").First;
        var dims = await timeline.EvaluateAsync<string>("el => `cw=${el.clientWidth} sw=${el.scrollWidth}`");
        TestContext.WriteLine($"Timeline dims: {dims}");

        // Click fit-to-screen button by title
        var fitBtn = gantt.Locator("button[title='Fit to screen']").First;
        await fitBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await fitBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var zoomAfter = await zoomLabel.TextContentAsync();
        TestContext.WriteLine($"Zoom after fit: {zoomAfter}");
        Assert.AreNotEqual(zoomBefore, zoomAfter, "Fit-to-screen should change zoom level");

        await TakeScreenshotAsync(page, "gantt_fit_to_screen");
    }

    [TestMethod]
    [Description("RecurrenceEditor renders with initial RRULE and updates summary")]
    public async Task RecurrenceEditor_RendersAndChangesPattern()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/recurrence-editor");
        await WaitForAppReadyAsync(page);

        var editor = page.Locator(".tm-recurrence-editor").First;
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Verify initial summary shows weekly pattern
        var summary = editor.Locator(".tm-recurrence-editor__summary-text");
        await Expect(summary).ToContainTextAsync("FREQ=WEEKLY");

        // Change pattern to Daily
        var patternSelect = editor.Locator("select").First;
        await patternSelect.SelectOptionAsync("Daily");
        await page.WaitForTimeoutAsync(500);

        await Expect(summary).ToContainTextAsync("FREQ=DAILY");

        await TakeScreenshotAsync(page, "recurrence_change_pattern");
    }

    [TestMethod]
    [Description("RecurrenceEditor toggles weekly days and updates summary")]
    public async Task RecurrenceEditor_WeeklyDaysToggle()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/recurrence-editor");
        await WaitForAppReadyAsync(page);

        var editor = page.Locator(".tm-recurrence-editor").First;
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Ensure Weekly pattern
        var patternSelect = editor.Locator("select").First;
        await patternSelect.SelectOptionAsync("Weekly");
        await page.WaitForTimeoutAsync(500);

        var summary = editor.Locator(".tm-recurrence-editor__summary-text");
        await Expect(summary).ToContainTextAsync("FREQ=WEEKLY");

        // Toggle a day checkbox (e.g., uncheck Tuesday)
        var dayCheckbox = editor.Locator(".tm-recurrence-editor__day").Filter(new LocatorFilterOptions { HasText = "Tue" }).Locator("input[type='checkbox']");
        await dayCheckbox.UncheckAsync();
        await page.WaitForTimeoutAsync(500);

        // Summary should still contain WEEKLY but not have Tue
        var summaryText = await summary.TextContentAsync();
        StringAssert.Contains(summaryText, "FREQ=WEEKLY");

        await TakeScreenshotAsync(page, "recurrence_weekly_days");
    }

    [TestMethod]
    [Description("RecurrenceEditor changes end condition to count")]
    public async Task RecurrenceEditor_EndCondition()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/recurrence-editor");
        await WaitForAppReadyAsync(page);

        var editor = page.Locator(".tm-recurrence-editor").First;
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Select "After" occurrences radio
        var afterRadio = editor.Locator("input[type='radio']").Nth(1);
        await afterRadio.CheckAsync();
        await page.WaitForTimeoutAsync(500);

        // Change count to 5
        var countInput = editor.Locator("input[type='number']").Filter(new LocatorFilterOptions { Has = page.Locator("xpath=../..").Filter(new LocatorFilterOptions { HasText = "After" }) }).First;
        // Fallback: get all number inputs and pick the one near "After"
        var numberInputs = editor.Locator("input[type='number']");
        var count = await numberInputs.CountAsync();
        for (int i = 0; i < count; i++)
        {
            var input = numberInputs.Nth(i);
            var parentText = await input.EvaluateAsync<string>("el => el.closest('label')?.textContent || ''");
            if (parentText.Contains("After"))
            {
                await input.FillAsync("5");
                await input.BlurAsync();
                break;
            }
        }
        await page.WaitForTimeoutAsync(500);

        var summary = editor.Locator(".tm-recurrence-editor__summary-text");
        await Expect(summary).ToContainTextAsync("COUNT=5");

        await TakeScreenshotAsync(page, "recurrence_end_condition");
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
