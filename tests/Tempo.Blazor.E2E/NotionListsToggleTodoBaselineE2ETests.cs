using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// EB2 screenshot recovery coverage for Notion lists, toggles, and todo blocks.
/// </summary>
[TestClass]
public class NotionListsToggleTodoBaselineE2ETests : NotionE2ETestBase
{
    private const string NestedBulletBlockId = "eb200000-0000-0000-0000-000000000002";
    private const string ConvertListBlockId = "eb200000-0000-0000-0000-000000000006";
    private const string TodoUncheckedBlockId = "eb200000-0000-0000-0000-000000000009";
    private const string ToggleWithChildrenBlockId = "eb200000-0000-0000-0000-000000000010";
    private const string ToggleChildTodoBlockId = "eb200000-0000-0000-0000-000000000012";
    private const string EmptyToggleBlockId = "eb200000-0000-0000-0000-000000000020";
    private const string LongTodoFirstBlockId = "eb200000-0000-0000-0000-000000000100";

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB2: captures nested list indentation and inline Turn Into baselines for numbered and todo list blocks")]
    public async Task EB2_ListsAndTurnInto_CaptureBaselineScreenshots()
    {
        await SetViewportAsync(1280, 720);
        var page = await OpenNotionEditorAsync();
        await SeedListTodoPageAsync();

        var bulletBody = page.Locator($"[data-block-id='{NestedBulletBlockId}'] .tm-notion-bullet__body").First;
        await bulletBody.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await CenterLocatorAsync(bulletBody);
        await bulletBody.ClickAsync();

        await page.Keyboard.PressAsync("Tab");
        await WaitForIndentAsync(page, NestedBulletBlockId, 1);
        await page.Keyboard.PressAsync("Tab");
        await WaitForIndentAsync(page, NestedBulletBlockId, 2);
        await page.Keyboard.PressAsync("Shift+Tab");
        await WaitForIndentAsync(page, NestedBulletBlockId, 1);
        await page.Keyboard.PressAsync("Tab");
        await WaitForIndentAsync(page, NestedBulletBlockId, 2);
        await page.Keyboard.PressAsync("Tab");
        await WaitForIndentAsync(page, NestedBulletBlockId, 3);

        await AssertNestedListHierarchyAsync(page);
        await AssertNoHorizontalOverflowAsync(page, ".tm-notion-editor", "EB2 nested list editor");
        await CaptureContentClipBaselineAsync("lists-toggle-todo", "nested-bullet-indent", page.Locator($"[data-block-id='{NestedBulletBlockId}']").First, 640, 360);

        await ConvertBlockWithContextMenuAsync(page, ConvertListBlockId, "Numbered list");
        var convertedNumbered = page.Locator($"[data-block-id='{ConvertListBlockId}'] .tm-notion-numbered").First;
        await convertedNumbered.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.AreEqual("NumberedList", await page.Locator($"[data-block-id='{ConvertListBlockId}']").First.GetAttributeAsync("data-block-type"));
        Assert.IsTrue((await convertedNumbered.InnerTextAsync()).Contains("Convert this list item", StringComparison.Ordinal));
        await CaptureBaselineAsync("lists-toggle-todo", "turn-into-numbered", convertedNumbered);

        await ConvertBlockWithContextMenuAsync(page, ConvertListBlockId, "To-do list");
        var convertedTodo = page.Locator($"[data-block-id='{ConvertListBlockId}'] .tm-notion-todo").First;
        await convertedTodo.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.AreEqual("TodoItem", await page.Locator($"[data-block-id='{ConvertListBlockId}']").First.GetAttributeAsync("data-block-type"));
        Assert.IsTrue((await convertedTodo.InnerTextAsync()).Contains("Convert this list item", StringComparison.Ordinal));
        await CaptureBaselineAsync("lists-toggle-todo", "turn-into-todo", convertedTodo);
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB2: captures expanded, collapsed, and empty toggle baselines with child block assertions")]
    public async Task EB2_Toggles_CaptureExpandedCollapsedAndEmptyScreenshots()
    {
        await SetViewportAsync(1280, 720);
        var page = await OpenNotionEditorAsync();
        await SeedListTodoPageAsync();

        var toggle = page.Locator($"[data-block-id='{ToggleWithChildrenBlockId}'] .tm-notion-toggle").First;
        await toggle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await CenterLocatorAsync(toggle);
        await page.Locator($"[data-block-id='{ToggleChildTodoBlockId}'] .tm-notion-todo").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        Assert.IsTrue(await toggle.Locator(".tm-notion-toggle__children").First.IsVisibleAsync(), "Expanded toggle should render its child container.");
        await CaptureBaselineAsync("lists-toggle-todo", "toggle-expanded-with-children", toggle);

        await toggle.Locator(".tm-notion-toggle__arrow").First.ClickAsync();
        await WaitForToggleOpenAsync(page, ToggleWithChildrenBlockId, false);
        Assert.AreEqual(0, await toggle.Locator(".tm-notion-toggle__children").CountAsync(), "Collapsed toggle should not render child blocks.");
        await CaptureBaselineAsync("lists-toggle-todo", "toggle-collapsed-with-children", toggle);

        var emptyToggle = page.Locator($"[data-block-id='{EmptyToggleBlockId}'] .tm-notion-toggle").First;
        await CenterLocatorAsync(emptyToggle);
        await emptyToggle.Locator(".tm-notion-toggle__arrow").First.ClickAsync();
        await WaitForToggleOpenAsync(page, EmptyToggleBlockId, true);
        Assert.IsTrue(await emptyToggle.Locator(".tm-notion-toggle__children").First.IsVisibleAsync(), "Open empty toggle should show an empty child area.");
        await CaptureBaselineAsync("lists-toggle-todo", "toggle-empty-open", emptyToggle);
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB2: captures checked, unchecked, and long todo/list edge baselines")]
    public async Task EB2_TodosAndLongList_CaptureBaselineScreenshots()
    {
        await SetViewportAsync(1280, 720);
        var page = await OpenNotionEditorAsync();
        await SeedListTodoPageAsync();

        var todo = page.Locator($"[data-block-id='{TodoUncheckedBlockId}'] .tm-notion-todo").First;
        await todo.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await CenterLocatorAsync(todo);
        Assert.IsFalse((await todo.GetAttributeAsync("class") ?? string.Empty).Contains("tm-notion-todo--checked", StringComparison.Ordinal));
        await CaptureBaselineAsync("lists-toggle-todo", "todo-unchecked", todo);

        await todo.Locator(".tm-notion-todo__input").First.ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForFunctionAsync(
            "blockId => document.querySelector(`[data-block-id='${blockId}'] .tm-notion-todo`)?.classList.contains('tm-notion-todo--checked') === true",
            TodoUncheckedBlockId,
            new PageWaitForFunctionOptions { Timeout = 10000 });
        var decoration = await todo.Locator(".tm-notion-todo__text").First.EvaluateAsync<string>("el => getComputedStyle(el).textDecorationLine");
        Assert.IsTrue(decoration.Contains("line-through", StringComparison.Ordinal), $"Checked todo text should render line-through decoration. Actual: {decoration}");
        await CaptureBaselineAsync("lists-toggle-todo", "todo-checked-after-click", todo);

        var longListFirst = page.Locator($"[data-block-id='{LongTodoFirstBlockId}']").First;
        await longListFirst.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await CenterLocatorAsync(longListFirst);
        var longTodoCount = await page.Locator("[data-block-id^='eb200000-0000-0000-0000-0000000001'] .tm-notion-todo").CountAsync();
        Assert.IsTrue(longTodoCount >= 26, $"Long todo edge state should render at least 26 rows. Actual: {longTodoCount}.");
        await AssertNoHorizontalOverflowAsync(page, ".tm-notion-editor", "EB2 long todo editor");
        await CaptureContentClipBaselineAsync("lists-toggle-todo", "todo-large-list", longListFirst, 640, 600);
    }

    private static async Task ConvertBlockWithContextMenuAsync(IPage page, string blockId, string targetText)
    {
        var block = page.Locator($"[data-block-id='{blockId}']").First;
        await CenterLocatorAsync(block);
        await block.HoverAsync();
        await page.WaitForTimeoutAsync(250);

        var menuButton = block.Locator(".tm-notion-handle__menu-anchor > .tm-notion-handle__btn").First;
        await menuButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await menuButton.EvaluateAsync("el => el.click()");
        await page.Locator(".tm-notion-ctx").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var turnIntoItem = page.Locator(".tm-notion-ctx__item--sub").First;
        await turnIntoItem.DispatchEventAsync("mouseenter");
        await page.Locator(".tm-notion-ctx-sub").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var target = page.Locator(".tm-notion-ctx-sub .tm-notion-ctx__item").Filter(new LocatorFilterOptions
        {
            HasText = targetText
        }).First;
        await target.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await target.EvaluateAsync("el => el.click()");
        await page.WaitForTimeoutAsync(500);
    }

    private static async Task AssertNestedListHierarchyAsync(IPage page)
    {
        var expected = new Dictionary<string, int>
        {
            [NestedBulletBlockId] = 3,
            ["eb200000-0000-0000-0000-000000000003"] = 1,
            ["eb200000-0000-0000-0000-000000000004"] = 2,
            ["eb200000-0000-0000-0000-000000000005"] = 3
        };

        foreach (var item in expected)
        {
            var indent = await GetIndentAsync(page, item.Key);
            Assert.AreEqual(item.Value, indent, $"Block {item.Key} should render list indentation level {item.Value}.");
        }
    }

    private static async Task<int> GetIndentAsync(IPage page, string blockId)
    {
        var value = await page.Locator($"[data-block-id='{blockId}'] .tm-notion-bullet").First.GetAttributeAsync("data-indent");
        return int.TryParse(value, out var indent) ? indent : -1;
    }

    private static async Task WaitForIndentAsync(IPage page, string blockId, int expectedIndent)
    {
        await page.WaitForFunctionAsync(
            "args => document.querySelector(`[data-block-id='${args.blockId}'] .tm-notion-bullet`)?.getAttribute('data-indent') === String(args.expectedIndent)",
            new { blockId, expectedIndent },
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    private static async Task WaitForToggleOpenAsync(IPage page, string blockId, bool expectedOpen)
    {
        await page.WaitForFunctionAsync(
            "args => document.querySelector(`[data-block-id='${args.blockId}'] .tm-notion-toggle`)?.classList.contains('tm-notion-toggle--open') === args.expectedOpen",
            new { blockId, expectedOpen },
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    private static async Task CenterLocatorAsync(ILocator locator)
    {
        await locator.EvaluateAsync("el => el.scrollIntoView({ block: 'center', inline: 'nearest' })");
        await locator.Page.WaitForTimeoutAsync(150);
    }

    private static async Task AssertNoHorizontalOverflowAsync(IPage page, string selector, string label)
    {
        var overflow = await page.Locator(selector).First.EvaluateAsync<double>(
            "el => Math.max(0, el.scrollWidth - el.clientWidth)");
        Assert.IsTrue(overflow <= 2, $"{label} should not horizontally overflow its own shell. Overflow={overflow}.");
    }

    private async Task<NotionBaselineCapture> CaptureContentClipBaselineAsync(string area, string state, ILocator anchor, double width, double height)
    {
        var outputDir = GetBaselineDirectory(area);
        var safeState = SanitizePathPart(state);
        var fullPath = Path.Combine(outputDir, $"{safeState}.png");
        var regionPath = Path.Combine(outputDir, $"{safeState}.region.png");

        await Page.WaitForTimeoutAsync(250);
        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            Type = ScreenshotType.Png,
            FullPage = true
        });

        var box = await anchor.BoundingBoxAsync();
        Assert.IsNotNull(box, $"EB2 baseline anchor for {state} should have a visible bounding box.");
        var viewport = Page.ViewportSize ?? new() { Width = 1280, Height = 720 };
        var x = Math.Max(0, box.X - 80);
        var y = Math.Max(0, box.Y - 8);
        var clipWidth = Math.Min(width, viewport.Width - x);
        var clipHeight = Math.Min(height, viewport.Height - y);

        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = regionPath,
            Type = ScreenshotType.Png,
            Clip = new Clip
            {
                X = (float)x,
                Y = (float)y,
                Width = (float)clipWidth,
                Height = (float)clipHeight
            }
        });

        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(regionPath);
        return new NotionBaselineCapture(fullPath, regionPath);
    }

    /// <summary>
    /// Routed through <see cref="BaselineOutput"/>: without TM_WRITE_BASELINES the capture lands in
    /// TestResults, not under the baseline root. The redirect is deliberately NOT a skip — the
    /// tests around these captures assert behaviour.
    /// </summary>
    private string GetBaselineDirectory(string area) =>
        BaselineOutput.DirectoryFor(TestContext, "notion", SanitizePathPart(area));

    private static string SanitizePathPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : char.ToLowerInvariant(ch)).ToArray();
        return new string(chars);
    }
}
