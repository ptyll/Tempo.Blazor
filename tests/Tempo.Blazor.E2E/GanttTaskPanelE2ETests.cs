using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests for TmGanttTaskPanel against the Blazor Server demo.
/// Server mode is used because WASM boot is unstable in the CI/agent environment.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class GanttTaskPanelE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("Gantt task panel appears when a task is selected")]
    public async Task Gantt_TaskPanel_OpensOnTaskSelect()
    {
        var page = await CreatePageAsync();
        // The 1280 default classifies the demo Gantt as a tablet (TaskPanelMode Auto -> the panel is a
        // sheet): the legacy desktop specs pin the wide viewport, like every phase-7 desktop spec.
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Panel should not be visible initially (no task selected)
        var panel = gantt.Locator(".tm-gantt-task-panel").First;
        Assert.AreEqual(0, await panel.CountAsync(), "Panel should not be visible before selection");

        // Click first task bar to select a task
        var firstBar = gantt.Locator(".tm-gantt__bar").First;
        await firstBar.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Panel should now be visible
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.IsTrue(await panel.IsVisibleAsync(), "Task panel should appear after selecting a task");

        // Verify panel contains expected fields
        var titleInput = panel.Locator("[data-testid='task-title'] input");
        await Expect(titleInput).ToBeVisibleAsync();

        var saveBtn = panel.Locator("button[data-testid='task-save']");
        await Expect(saveBtn).ToBeVisibleAsync();

        await TakeScreenshotAsync(page, "gantt_task_panel_open");
    }

    [TestMethod]
    [Description("Gantt task panel edits task title and saves changes")]
    public async Task Gantt_TaskPanel_EditsTaskTitle()
    {
        var page = await CreatePageAsync();
        // The 1280 default classifies the demo Gantt as a tablet (TaskPanelMode Auto -> the panel is a
        // sheet): the legacy desktop specs pin the wide viewport, like every phase-7 desktop spec.
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Select first task
        var firstBar = gantt.Locator(".tm-gantt__bar").First;
        await firstBar.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var panel = gantt.Locator(".tm-gantt-task-panel").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Change title
        var titleInput = panel.Locator("[data-testid='task-title'] input");
        await titleInput.FillAsync("Updated Task Title");
        await titleInput.BlurAsync();

        // Click Save
        var saveBtn = panel.Locator("button[data-testid='task-save']");
        await saveBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Verify tree row shows updated title
        var firstRow = gantt.Locator(".tm-gantt__tree-row").First;
        await Expect(firstRow).ToContainTextAsync("Updated Task Title");

        // Verify bar tooltip/label also updated
        var updatedBar = gantt.Locator(".tm-gantt__bar").First;
        var titleAttr = await updatedBar.GetAttributeAsync("title");
        StringAssert.Contains(titleAttr, "Updated Task Title");

        await TakeScreenshotAsync(page, "gantt_task_panel_edit_title");
    }

    [TestMethod]
    [Description("Gantt task panel shows validation error when Start is after End")]
    public async Task Gantt_TaskPanel_Validation_StartAfterEnd()
    {
        var page = await CreatePageAsync();
        // The 1280 default classifies the demo Gantt as a tablet (TaskPanelMode Auto -> the panel is a
        // sheet): the legacy desktop specs pin the wide viewport, like every phase-7 desktop spec.
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Select first task
        var firstBar = gantt.Locator(".tm-gantt__bar").First;
        await firstBar.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var panel = gantt.Locator(".tm-gantt-task-panel").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Set Start after End
        var startInput = panel.Locator("[data-testid='task-start'] input");
        var endInput = panel.Locator("[data-testid='task-end'] input");

        await endInput.FillAsync("2024-01-01");
        await startInput.FillAsync("2024-06-15");
        await startInput.BlurAsync();
        await page.WaitForTimeoutAsync(200);

        // Click Save
        var saveBtn = panel.Locator("button[data-testid='task-save']");
        await saveBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Error message should appear
        var error = panel.Locator("[data-testid='task-validation-error']");
        await error.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var errorText = await error.TextContentAsync();
        StringAssert.Contains(errorText, "Start");

        await TakeScreenshotAsync(page, "gantt_task_panel_validation");
    }

    [TestMethod]
    [Description("Gantt task panel adds a new dependency")]
    public async Task Gantt_TaskPanel_AddsDependency()
    {
        var page = await CreatePageAsync();
        // The 1280 default classifies the demo Gantt as a tablet (TaskPanelMode Auto -> the panel is a
        // sheet): the legacy desktop specs pin the wide viewport, like every phase-7 desktop spec.
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Select task 11 (Frontend Implementation) – it has an incoming dependency from 10
        var bars = gantt.Locator(".tm-gantt__bar");
        // Task 11 is the 11th bar (0-indexed: 10)
        var task11Bar = bars.Nth(10);
        await task11Bar.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var panel = gantt.Locator(".tm-gantt-task-panel").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Click add dependency
        var addBtn = panel.Locator("button[data-testid='task-add-dep']");
        await addBtn.ClickAsync();
        await page.WaitForTimeoutAsync(200);

        // Select from task 5 (Design Phase)
        var fromSelect = panel.Locator("[data-testid='task-add-dep-from'] select");
        await fromSelect.SelectOptionAsync("5");

        // Select type SS (1)
        var typeSelect = panel.Locator("[data-testid='task-add-dep-type'] select");
        await typeSelect.SelectOptionAsync("1");

        // Confirm
        var confirmBtn = panel.Locator("button[data-testid='task-add-dep-confirm']");
        await confirmBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Verify new chip appears
        var chips = panel.Locator("[data-testid='task-dep-item']");
        var chipTexts = await chips.AllTextContentsAsync();
        Assert.IsTrue(chipTexts.Any(t => t.Contains("Design Phase") && t.Contains("SS")),
            "New dependency chip should appear with correct label and type");

        await TakeScreenshotAsync(page, "gantt_task_panel_add_dep");
    }

    [TestMethod]
    [Description("Gantt task panel removes a dependency after confirmation")]
    public async Task Gantt_TaskPanel_RemovesDependency_WithConfirm()
    {
        var page = await CreatePageAsync();
        // The 1280 default classifies the demo Gantt as a tablet (TaskPanelMode Auto -> the panel is a
        // sheet): the legacy desktop specs pin the wide viewport, like every phase-7 desktop spec.
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Select task 4 (Document Requirements) – it has incoming dependency d1 from task 3
        var bars = gantt.Locator(".tm-gantt__bar");
        var task4Bar = bars.Nth(3);
        await task4Bar.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var panel = gantt.Locator(".tm-gantt-task-panel").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Verify chip exists
        var chips = panel.Locator("[data-testid='task-dep-item']");
        await Expect(chips.First).ToBeVisibleAsync();
        var initialCount = await chips.CountAsync();
        Assert.IsTrue(initialCount > 0, "Task 4 should have at least one dependency chip");

        // Click remove on first chip
        var removeBtn = panel.Locator("button[data-testid^='task-dep-remove-']").First;
        await removeBtn.ClickAsync();
        await page.WaitForTimeoutAsync(200);

        // Confirm dialog should appear
        var yesBtn = panel.Locator("button[data-testid='task-dep-confirm-yes']");
        await Expect(yesBtn).ToBeVisibleAsync();
        await yesBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Chip should be gone
        var finalCount = await chips.CountAsync();
        Assert.AreEqual(initialCount - 1, finalCount, "Dependency chip should be removed after confirmation");

        await TakeScreenshotAsync(page, "gantt_task_panel_remove_dep");
    }

    [TestMethod]
    [Description("Gantt task panel prevents adding a cyclic dependency")]
    public async Task Gantt_TaskPanel_PreventsCyclicDependency()
    {
        var page = await CreatePageAsync();
        // The 1280 default classifies the demo Gantt as a tablet (TaskPanelMode Auto -> the panel is a
        // sheet): the legacy desktop specs pin the wide viewport, like every phase-7 desktop spec.
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}/gantt");
        await WaitForAppReadyAsync(page);

        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Select task 3 (Stakeholder Interviews) – d1 is 3->4
        var bars = gantt.Locator(".tm-gantt__bar");
        var task3Bar = bars.Nth(2);
        await task3Bar.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var panel = gantt.Locator(".tm-gantt-task-panel").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Click add dependency
        var addBtn = panel.Locator("button[data-testid='task-add-dep']");
        await addBtn.ClickAsync();
        await page.WaitForTimeoutAsync(200);

        // Try to add 4 -> 3 (would create cycle 3->4->3)
        var fromSelect = panel.Locator("[data-testid='task-add-dep-from'] select");
        await fromSelect.SelectOptionAsync("4");

        var confirmBtn = panel.Locator("button[data-testid='task-add-dep-confirm']");
        await confirmBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Error should appear
        var error = panel.Locator("[data-testid='task-dependency-error']");
        await error.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var errorText = await error.TextContentAsync();
        StringAssert.Contains(errorText, "cyclic");

        await TakeScreenshotAsync(page, "gantt_task_panel_cyclic_dep");
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
