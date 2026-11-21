using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class NotionActionItemsE2ETests : NotionE2ETestBase
{
    private const string UnassignedTaskId = "cf300000-0000-0000-0000-000000000002";
    private const string OverdueTaskId = "cf300000-0000-0000-0000-000000000003";
    private const string NormalTaskId = "cf300000-0000-0000-0000-000000000004";
    private const string CompletedTaskId = "cf300000-0000-0000-0000-000000000005";

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("CF3: action item can be assigned, given an overdue date, checked, persisted through Demo API, and captured as UX baseline")]
    public async Task CF3_ActionItems_AssignDueDateCheckAndCaptureBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedActionItemsPageAsync();

        var unassigned = Block(page, UnassignedTaskId);
        await unassigned.ScrollIntoViewIfNeededAsync();
        Assert.AreEqual(0, await unassigned.Locator(".tm-notion-todo__assignee").CountAsync(),
            "Seeded unassigned action item should not render an assignee chip.");
        await CaptureTodoBaselineAsync(page, "cf3-todo-without-assignee", unassigned);

        var overdue = Block(page, OverdueTaskId);
        await ExpectClassContainsAsync(overdue.Locator(".tm-notion-todo").First, "tm-notion-todo--overdue");
        await CaptureTodoBaselineAsync(page, "cf3-todo-assignee-overdue", overdue);

        var normal = Block(page, NormalTaskId);
        await ExpectClassContainsAsync(normal.Locator(".tm-notion-todo__due").First, "tm-notion-todo__due--tomorrow");
        await CaptureTodoBaselineAsync(page, "cf3-todo-assignee-normal-due", normal);

        await AssignUserAsync(unassigned, "Diana Prince");
        await ExpectTextAsync(unassigned.Locator(".tm-notion-todo__assignee-name").First, "Diana Prince");
        await CaptureTodoBaselineAsync(page, "cf3-todo-assignee-added-unchecked", unassigned);

        await SetDueDateAsync(unassigned, DateTime.Today.AddDays(-1));
        await ExpectClassContainsAsync(unassigned.Locator(".tm-notion-todo").First, "tm-notion-todo--overdue");
        await CaptureTodoBaselineAsync(page, "cf3-todo-unchecked-overdue-after-edit", unassigned);

        await unassigned.Locator(".tm-notion-todo__check-wrap").First.ClickAsync();
        await page.WaitForTimeoutAsync(900);
        await ExpectClassContainsAsync(unassigned.Locator(".tm-notion-todo").First, "tm-notion-todo--checked");
        await ExpectClassNotContainsAsync(unassigned.Locator(".tm-notion-todo").First, "tm-notion-todo--overdue");
        await CaptureTodoBaselineAsync(page, "cf3-todo-checked-after-click", unassigned);

        var completed = Block(page, CompletedTaskId);
        await ExpectClassContainsAsync(completed.Locator(".tm-notion-todo").First, "tm-notion-todo--checked");
        await CaptureTodoBaselineAsync(page, "cf3-todo-checked-seeded-with-metadata", completed);

        await ReloadAppReadyAsync(page, WaitUntilState.DOMContentLoaded);
        await page.WaitForSelectorAsync($"[data-block-id='{UnassignedTaskId}'] .tm-notion-todo__assignee-name", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });

        var reloaded = Block(page, UnassignedTaskId);
        await ExpectTextAsync(reloaded.Locator(".tm-notion-todo__assignee-name").First, "Diana Prince");
        await ExpectClassContainsAsync(reloaded.Locator(".tm-notion-todo").First, "tm-notion-todo--checked");
        await ExpectClassNotContainsAsync(reloaded.Locator(".tm-notion-todo").First, "tm-notion-todo--overdue");

        normal = Block(page, NormalTaskId);
        await ExpectClassContainsAsync(normal.Locator(".tm-notion-todo__due").First, "tm-notion-todo__due--tomorrow");

        var capture = await CaptureBaselineAsync("action-items", "cf3-todo-assignee-due-overdue-normal", page.Locator(".tm-notion-page").First);
        Assert.IsTrue(File.Exists(capture.RegionPath), "CF3 action-item UX baseline should be written.");

        await AssertActionItemLayoutAsync(page);
    }

    [TestMethod]
    [Description("CF3: providerless action item hides Assign, keeps due-date editing, supports overdue and metadata removal")]
    public async Task CF3_ActionItems_ProviderlessAndRemovalEdges()
    {
        var page = await OpenNotionEditorAsync("?disableMentionProvider=true");
        await SeedActionItemsPageAsync();

        var unassigned = Block(page, UnassignedTaskId);
        var actionLabels = await unassigned.Locator(".tm-notion-todo__action").AllInnerTextsAsync();
        Assert.IsFalse(actionLabels.Any(label => label.Trim().Equals("Assign", StringComparison.Ordinal)),
            "Assign action should be hidden when no mention provider is configured.");
        Assert.IsTrue(actionLabels.Any(label => label.Trim().Equals("Due date", StringComparison.Ordinal)),
            "Due date action should remain available without a mention provider.");

        var overdue = Block(page, OverdueTaskId);
        await ExpectClassContainsAsync(overdue.Locator(".tm-notion-todo").First, "tm-notion-todo--overdue");

        await overdue.Locator(".tm-notion-todo__chip-remove").First.ClickAsync();
        await page.WaitForTimeoutAsync(900);
        Assert.AreEqual(0, await overdue.Locator(".tm-notion-todo__assignee-name").CountAsync(),
            "Assignee chip should be removed after unassign.");

        await overdue.Locator(".tm-notion-todo__due").First.ClickAsync();
        await overdue.Locator(".tm-picker-clear").First.ClickAsync();
        await page.WaitForTimeoutAsync(900);
        Assert.AreEqual(0, await overdue.Locator(".tm-notion-todo__due").CountAsync(),
            "Due date badge should be removed after clearing the date picker.");
        await ExpectClassNotContainsAsync(overdue.Locator(".tm-notion-todo").First, "tm-notion-todo--overdue");
    }

    private static ILocator Block(IPage page, string id) =>
        page.Locator($"[data-block-id='{id}']").First;

    private static async Task AssignUserAsync(ILocator block, string displayName)
    {
        await block.Locator(".tm-notion-todo__action", new LocatorLocatorOptions { HasTextString = "Assign" }).First.ClickAsync();
        var picker = block.Locator(".tm-notion-todo__picker--assignee").First;
        await picker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await picker.Locator(".tm-notion-todo__search").FillAsync(displayName);
        var user = picker.Locator(".tm-notion-todo__user", new LocatorLocatorOptions { HasTextString = displayName }).First;
        await user.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await user.ClickAsync();
    }

    private static async Task SetDueDateAsync(ILocator block, DateTime dueDate)
    {
        await block.Locator(".tm-notion-todo__action", new LocatorLocatorOptions { HasTextString = "Due date" }).First.ClickAsync();
        var picker = block.Locator(".tm-notion-todo__picker--date").First;
        await picker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await picker.Locator(".tm-date-picker-trigger").First.ClickAsync();
        await picker.Locator($".tm-cal-day[data-date='{dueDate:yyyy-MM-dd}']").First.ClickAsync();
    }

    private static async Task ExpectTextAsync(ILocator locator, string expected)
    {
        await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.AreEqual(expected, (await locator.InnerTextAsync()).Trim());
    }

    private static async Task ExpectClassContainsAsync(ILocator locator, string expectedClass)
    {
        await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        var classes = await locator.GetAttributeAsync("class") ?? string.Empty;
        StringAssert.Contains(classes, expectedClass);
    }

    private static async Task ExpectClassNotContainsAsync(ILocator locator, string unexpectedClass)
    {
        await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        var classes = await locator.GetAttributeAsync("class") ?? string.Empty;
        Assert.IsFalse(classes.Contains(unexpectedClass, StringComparison.Ordinal),
            $"Class list should not contain {unexpectedClass}. Actual: {classes}");
    }

    private static async Task AssertActionItemLayoutAsync(IPage page)
    {
        var hasOverflow = await page.Locator(".tm-notion-todo").EvaluateAllAsync<bool>(
            """
            todos => todos.some(todo => {
                const todoRect = todo.getBoundingClientRect();
                return Array.from(todo.querySelectorAll('.tm-notion-todo__assignee, .tm-notion-todo__due, .tm-notion-todo__action'))
                    .some(item => {
                        const rect = item.getBoundingClientRect();
                        return rect.right > todoRect.right + 1 || rect.left < todoRect.left - 1;
                    });
            })
            """);

        Assert.IsFalse(hasOverflow, "Action item chips and badges should stay inside their todo row.");
    }

    private async Task CaptureTodoBaselineAsync(IPage page, string state, ILocator block)
    {
        await block.ScrollIntoViewIfNeededAsync();
        await BlurEditorAsync(page);
        var capture = await CaptureBaselineAsync("action-items", state, block);
        Assert.IsTrue(File.Exists(capture.FullPagePath), $"CF3 full-page baseline should be written for {state}.");
        Assert.IsTrue(File.Exists(capture.RegionPath), $"CF3 region baseline should be written for {state}.");
    }

    private static async Task BlurEditorAsync(IPage page)
    {
        await page.Locator(".tm-notion-h1").First.ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForTimeoutAsync(250);
    }
}
