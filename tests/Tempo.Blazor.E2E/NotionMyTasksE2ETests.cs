using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class NotionMyTasksE2ETests : NotionE2ETestBase
{
    private const string OverdueTaskId = "cf400000-0000-0000-0000-000000000101";
    private const string TodayTaskId = "cf400000-0000-0000-0000-000000000102";

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("CF4: My Tasks opens from the editor shell, updates completion through Demo API, navigates to the source block, and captures UX baseline")]
    public async Task CF4_MyTasks_OpenCompleteNavigateAndCaptureBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedTasksPageAsync();

        await OpenTasksPanelAsync(page);
        await ExpectTaskVisibleAsync(page, OverdueTaskId, "Prepare customer launch checklist");
        await ExpectTaskVisibleAsync(page, TodayTaskId, "Review onboarding copy");

        var capture = await CaptureBaselineAsync("tasks", "cf4-my-tasks-panel", page.Locator(".tm-notion-editor").First);
        Assert.IsTrue(File.Exists(capture.RegionPath), "CF4 My Tasks UX baseline should be written.");
        await AssertTaskPanelLayoutAsync(page);

        await ClickTaskFilterAsync(page, "Page");
        await page.WaitForSelectorAsync(".tm-my-tasks__group[data-group-key='CF4 Release Follow-up']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        var groupedCapture = await CaptureBaselineAsync("tasks", "cf4-my-tasks-grouped-by-page", page.Locator(".tm-notion-editor").First);
        Assert.IsTrue(File.Exists(groupedCapture.RegionPath), "CF4 grouped My Tasks UX baseline should be written.");
        await ClickTaskFilterAsync(page, "Due date");

        await page.Locator($"[data-task-id='{OverdueTaskId}'] .tm-my-tasks__check").First.ClickAsync();
        await page.WaitForSelectorAsync($"[data-task-id='{OverdueTaskId}']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 10000
        });

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        var overdueBlock = Block(page, OverdueTaskId);
        await overdueBlock.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });
        await ExpectClassContainsAsync(overdueBlock.Locator(".tm-notion-todo").First, "tm-notion-todo--checked");

        await OpenTasksPanelAsync(page);
        await page.Locator($"[data-task-id='{TodayTaskId}'] .tm-my-tasks__body").First.ClickAsync();
        await page.WaitForSelectorAsync(".tm-notion-tasks-panel", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 10000
        });
        await AssertBlockInViewportAsync(page, TodayTaskId);
    }

    [TestMethod]
    [Description("CF4: My Tasks entry point is hidden when the optional task provider is absent")]
    public async Task CF4_MyTasks_ProviderlessEntryPointHidden()
    {
        var page = await OpenNotionEditorAsync("?disableTaskProvider=true");
        await SeedTasksPageAsync();

        Assert.AreEqual(0, await page.Locator(".tm-notion-topbar__tasks").CountAsync(),
            "My Tasks shell entry point should be hidden when no task provider is configured.");
    }

    [TestMethod]
    [Description("CF4: My Tasks handles empty data, many tasks, and overdue filtering without layout overflow")]
    public async Task CF4_MyTasks_EmptyManyAndOverdueEdges()
    {
        var page = await OpenNotionEditorAsync();
        await SeedEmptyTasksPageAsync();

        await OpenTasksPanelAsync(page);
        await page.Locator(".tm-my-tasks__empty").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        StringAssert.Contains(await page.Locator(".tm-my-tasks__empty").InnerTextAsync(), "No tasks match");
        var emptyCapture = await CaptureBaselineAsync("tasks", "cf4-my-tasks-empty", page.Locator(".tm-notion-editor").First);
        Assert.IsTrue(File.Exists(emptyCapture.RegionPath), "CF4 empty My Tasks UX baseline should be written.");

        await SeedManyTasksPageAsync();
        await OpenTasksPanelAsync(page);
        await ClickTaskFilterAsync(page, "Overdue");
        await page.WaitForSelectorAsync(".tm-my-tasks__group[data-group-key='overdue']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var overdueCount = await page.Locator(".tm-my-tasks__item").CountAsync();
        Assert.IsTrue(overdueCount >= 5, $"Expected several overdue tasks in the many-task seed, got {overdueCount}.");

        var scrollable = await page.Locator(".tm-my-tasks__groups").EvaluateAsync<bool>(
            "el => el.scrollHeight > el.clientHeight");
        Assert.IsTrue(scrollable, "Many-task list should be internally scrollable.");

        await AssertTaskPanelLayoutAsync(page);
    }

    /// <summary>
    /// Below 640px the inline filters are hidden and "Filters (n)" opens them in a sheet.
    /// Click the visible copy, then close the sheet so the grouped list is reachable.
    /// </summary>
    private static async Task ClickTaskFilterAsync(IPage page, string label)
    {
        var inline = page.Locator(".tm-my-tasks__filters--inline .tm-my-tasks__filter",
            new PageLocatorOptions { HasTextString = label }).First;
        if (await inline.CountAsync() > 0 && await inline.IsVisibleAsync())
        {
            await inline.ClickAsync();
            return;
        }

        await page.GetByTestId("notion-tasks-filters-open").ClickAsync();
        var sheet = page.Locator(".tm-overlay-panel-sheet, .tm-sheet").Last;
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await sheet.Locator(".tm-my-tasks__filter", new LocatorLocatorOptions { HasTextString = label }).First.ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 8000 });
    }

    private static async Task OpenTasksPanelAsync(IPage page)
    {
        var panel = page.Locator(".tm-notion-tasks-panel");
        if (await panel.CountAsync() == 0)
        {
            await page.Locator(".tm-notion-topbar__tasks").First.ClickAsync();
        }

        await page.WaitForSelectorAsync(".tm-my-tasks", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
    }

    private static async Task ExpectTaskVisibleAsync(IPage page, string taskId, string text)
    {
        var task = page.Locator($"[data-task-id='{taskId}']").First;
        await task.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        StringAssert.Contains(await task.InnerTextAsync(), text);
    }

    private static ILocator Block(IPage page, string id) =>
        page.Locator($"[data-block-id='{id}']").First;

    private static async Task ExpectClassContainsAsync(ILocator locator, string expectedClass)
    {
        await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        var classes = await locator.GetAttributeAsync("class") ?? string.Empty;
        StringAssert.Contains(classes, expectedClass);
    }

    private static async Task AssertBlockInViewportAsync(IPage page, string blockId)
    {
        await page.WaitForFunctionAsync(
            """
            id => {
                const main = document.querySelector('.tm-notion-main');
                const el = document.querySelector(`[data-block-id="${CSS.escape(id)}"]`);
                if (!main || !el) return false;
                const mainRect = main.getBoundingClientRect();
                const rect = el.getBoundingClientRect();
                return rect.top >= mainRect.top && rect.top < mainRect.bottom && rect.bottom > mainRect.top;
            }
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10000, PollingInterval = 100 });
    }

    private static async Task AssertTaskPanelLayoutAsync(IPage page)
    {
        var hasOverflow = await page.Locator(".tm-my-tasks").EvaluateAsync<bool>(
            """
            panel => Array.from(panel.querySelectorAll('.tm-my-tasks__item, .tm-my-tasks__filter, .tm-my-tasks__header'))
                .filter(el => el.getClientRects().length > 0)
                .some(el => {
                    const rect = el.getBoundingClientRect();
                    const parent = panel.getBoundingClientRect();
                    return rect.right > parent.right + 1 || rect.left < parent.left - 1;
                })
            """);

        Assert.IsFalse(hasOverflow, "My Tasks content should stay within the task panel bounds.");
    }
}
