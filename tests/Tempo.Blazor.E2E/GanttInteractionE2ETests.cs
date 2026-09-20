using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests for Gantt interactions: add task, context menu, drag & drop.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class GanttInteractionE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("Gantt toolbar add-task button creates a new row")]
    public async Task Gantt_AddTask_CreatesNewRow()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var addBtn = gantt.Locator("button[data-testid='gantt-add-task']");
        await Expect(addBtn).ToBeVisibleAsync();
        await addBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Verify new "New task" row appeared
        var rows = gantt.Locator(".tm-gantt__tree-row");
        var rowTexts = await rows.AllTextContentsAsync();
        Assert.IsTrue(rowTexts.Any(t => t.Contains("New task")), "A new task row should be created");

        await TakeScreenshotAsync(page, "gantt_add_task");
    }

    [TestMethod]
    [Description("Gantt context menu adds a task below the selected row")]
    public async Task Gantt_ContextMenu_AddsTaskBelow()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Right-click first row
        var firstRow = gantt.Locator(".tm-gantt__tree-row").First;
        await firstRow.ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });
        await page.WaitForTimeoutAsync(200);

        // Click "Add task below"
        var belowOption = gantt.Locator(".tm-gantt__context-menu-item").Filter(new LocatorFilterOptions { HasText = "below" });
        await belowOption.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Verify new task appears
        var rows = gantt.Locator(".tm-gantt__tree-row");
        var rowTexts = await rows.AllTextContentsAsync();
        Assert.IsTrue(rowTexts.Any(t => t.Contains("New task")), "Context menu 'Add below' should create a new task");

        await TakeScreenshotAsync(page, "gantt_context_add_below");
    }

    [TestMethod]
    [Description("Gantt drag handle appears on hover and is draggable")]
    public async Task Gantt_DragHandle_AppearsOnHover_And_IsDraggable()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var rows = gantt.Locator(".tm-gantt__tree-row");
        var initialCount = await rows.CountAsync();
        Assert.IsTrue(initialCount >= 2, "Need at least 2 rows for drag handle test");

        // Hover first row to reveal drag handle
        await rows.First.HoverAsync();
        await page.WaitForTimeoutAsync(200);

        // Verify drag handle is now visible (opacity 1 in CSS)
        var handle = gantt.Locator(".tm-gantt__drag-handle").First;
        var isVisible = await handle.IsVisibleAsync();
        Assert.IsTrue(isVisible, "Drag handle should be visible after hovering row");

        // Verify tree row is draggable (Blazor sets draggable on the row, not the handle)
        var rowDraggable = await rows.First.EvaluateAsync<bool>("el => el.draggable === true || el.getAttribute('draggable') === 'true'");
        Assert.IsTrue(rowDraggable, "Tree row should be draggable");

        // Verify there is at least one handle per row (lazy: count handles == count rows)
        var handles = gantt.Locator(".tm-gantt__drag-handle");
        var handleCount = await handles.CountAsync();
        Assert.AreEqual(initialCount, handleCount, "Every tree row should have a drag handle");

        await TakeScreenshotAsync(page, "gantt_drag_handle");
    }

    [TestMethod]
    [Description("Adding a task increases timeline content height to match row count")]
    public async Task Gantt_AddTask_IncreasesTimelineContentHeight()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var timelineContent = gantt.Locator(".tm-gantt__timeline-content").First;
        var addBtn = gantt.Locator("button[data-testid='gantt-add-task']");

        // Read initial height
        var initialHeight = await timelineContent.EvaluateAsync<int>("el => parseInt(el.style.height, 10) || 0");
        Assert.IsTrue(initialHeight > 0, "Timeline content should have explicit initial height");

        // Add a task
        await addBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Verify height increased by one row (40px)
        var newHeight = await timelineContent.EvaluateAsync<int>("el => parseInt(el.style.height, 10) || 0");
        Assert.AreEqual(initialHeight + 40, newHeight, "Timeline content height should increase by 40px per new row");

        await TakeScreenshotAsync(page, "gantt_timeline_height");
    }

    [TestMethod]
    [Description("Wheel scroll on timeline body scrolls vertically")]
    public async Task Gantt_TimelineWheelScrolls_Vertically()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var timelineBody = gantt.Locator(".tm-gantt__timeline-body").First;

        // Reset scroll
        await timelineBody.EvaluateAsync("el => el.scrollTop = 0");
        await page.WaitForTimeoutAsync(100);

        // Move mouse over timeline body and wheel down
        var box = await timelineBody.BoundingBoxAsync();
        Assert.IsNotNull(box);
        await page.Mouse.MoveAsync(box.X + 10, box.Y + 10);
        await page.Mouse.WheelAsync(0, 60);
        await page.WaitForTimeoutAsync(400);

        var scrollTop = await timelineBody.EvaluateAsync<double>("el => el.scrollTop");
        Assert.IsTrue(scrollTop > 0, $"Timeline body should scroll vertically after mouse wheel. scrollTop was {scrollTop}");

        await TakeScreenshotAsync(page, "gantt_timeline_wheel");
    }

    [TestMethod]
    [Description("Zoom in/out buttons change the zoom level display")]
    public async Task Gantt_ZoomButtons_ChangeZoomLevel()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var zoomOutBtn = gantt.Locator("[data-testid='gantt-zoom-out']").First;
        var zoomInBtn = gantt.Locator("[data-testid='gantt-zoom-in']").First;
        var zoomLabel = gantt.Locator(".tm-gantt__zoom-level").First;

        var initialZoomText = await zoomLabel.TextContentAsync();
        Assert.IsNotNull(initialZoomText);
        var initialZoom = int.Parse(initialZoomText.TrimEnd('%'));

        // Click zoom in
        await zoomInBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var zoomInText = await zoomLabel.TextContentAsync();
        var zoomIn = int.Parse(zoomInText!.TrimEnd('%'));
        Assert.IsTrue(zoomIn > initialZoom, $"Zoom should increase after clicking zoom-in. Initial: {initialZoom}, After: {zoomIn}");

        // Click zoom out twice to return below initial (or at least decrease)
        await zoomOutBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var zoomOutText = await zoomLabel.TextContentAsync();
        var zoomOut = int.Parse(zoomOutText!.TrimEnd('%'));
        Assert.IsTrue(zoomOut < zoomIn, $"Zoom should decrease after clicking zoom-out. After zoom in: {zoomIn}, After: {zoomOut}");

        await TakeScreenshotAsync(page, "gantt_zoom_buttons");
    }

    [TestMethod]
    [Description("Gantt import dialog: the file chooser is a real button — Tab focuses it, Enter and Space both open the native file chooser")]
    public async Task Gantt_ImportDialog_FileChooser_KeyboardAccessible()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await gantt.Locator("button[data-testid='gantt-import-btn']").ClickAsync();

        var dialog = page.Locator(".tm-gantt__import-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var chooseBtn = dialog.Locator("[data-testid='import-choose-file']");
        await Expect(chooseBtn).ToBeVisibleAsync();

        // Tab forward until the chooser button owns focus — it must be reachable by keyboard.
        for (var i = 0; i < 16; i++)
        {
            var activeTestId = await page.EvaluateAsync<string>(
                "() => document.activeElement?.getAttribute('data-testid') ?? ''");
            if (activeTestId == "import-choose-file") break;
            await page.Keyboard.PressAsync("Tab");
        }
        await Expect(chooseBtn).ToBeFocusedAsync();
        await TakeScreenshotAsync(page, "gantt_import_choose_file_focus");

        // Enter opens the native file chooser.
        var chooserByEnter = await page.RunAndWaitForFileChooserAsync(
            () => page.Keyboard.PressAsync("Enter"),
            new PageRunAndWaitForFileChooserOptions { Timeout = 10000 });
        Assert.IsNotNull(chooserByEnter, "Enter on the choose-file button must open the file chooser");

        // Space opens it too (button activation on keyup).
        var chooserBySpace = await page.RunAndWaitForFileChooserAsync(
            () => page.Keyboard.PressAsync("Space"),
            new PageRunAndWaitForFileChooserOptions { Timeout = 10000 });
        Assert.IsNotNull(chooserBySpace, "Space on the choose-file button must open the file chooser");

        // The hidden input is visually hidden (not display:none) and out of the a11y tree.
        var input = dialog.Locator("input.tm-gantt__import-file-input");
        Assert.AreEqual("true", await input.GetAttributeAsync("aria-hidden"));
        Assert.AreEqual("-1", await input.GetAttributeAsync("tabindex"));
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
