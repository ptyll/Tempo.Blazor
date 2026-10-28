using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for modeling model tree phase M10.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingModelTreeM10E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("Model tree shows demo elements grouped by semantic type")]
    public async Task ModelTree_ShowsGroupedDemoElements()
    {
        var page = await OpenLoadedModelingPageAsync();

        await ExpectVisibleAsync(page, "[data-testid='modeling-model-tree']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-group-startEvent']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-group-userTask']");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-tree-node-bpmn-validate-order']", "Validate order");
    }

    [TestMethod]
    [Description("Typing into search filters immediately and clearing restores all nodes")]
    public async Task ModelTree_SearchFiltersLiveAndClearRestores()
    {
        var page = await OpenLoadedModelingPageAsync();

        await page.Locator("[data-testid='modeling-tree-search']").FillAsync("Ship");
        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-node-bpmn-ship-order']");
        await ExpectHiddenAsync(page, "[data-testid='modeling-tree-node-bpmn-validate-order']");
        Assert.AreEqual("1", await page.Locator("[data-testid='modeling-model-tree']").GetAttributeAsync("data-visible-count"));

        await page.Locator("[data-testid='modeling-tree-search']").FillAsync(string.Empty);
        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-node-bpmn-validate-order']");
        Assert.AreEqual("6", await page.Locator("[data-testid='modeling-model-tree']").GetAttributeAsync("data-visible-count"));
    }

    [TestMethod]
    [Description("Clicking a tree node shows element detail in the inspector")]
    public async Task ModelTree_ClickNodeUpdatesInspector()
    {
        var page = await OpenLoadedModelingPageAsync();

        await page.Locator("[data-testid='modeling-tree-node-bpmn-ship-order']").ClickAsync();

        await ExpectVisibleAsync(page, "[data-testid='modeling-inspector-selected-element']");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-panel']", "Ship order");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-panel']", "serviceTask");
    }

    [TestMethod]
    [Description("Special character search is rendered as text and does not execute markup")]
    public async Task ModelTree_SearchSpecialCharacters_DoesNotExecuteMarkup()
    {
        var page = await OpenLoadedModelingPageAsync();
        var payload = "<script>window.__m10Xss=1</script>";

        await page.Locator("[data-testid='modeling-tree-search']").FillAsync(payload);

        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-empty-filter']");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-tree-empty-filter']", payload);
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => window.__m10Xss === undefined"));
    }

    [TestMethod]
    [Description("Diacritic-insensitive search finds Czech names")]
    public async Task ModelTree_DiacriticSearch_FindsCzechElement()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=diacritics");

        await page.Locator("[data-testid='modeling-tree-search']").FillAsync("zakaznik");

        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-node-demo-zakaznik']");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-tree-node-demo-zakaznik']", "Zákazník");
    }

    [TestMethod]
    [Description("Large model tree remains scrollable with more than 500 elements")]
    public async Task ModelTree_LargeModel_RemainsScrollable()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=large-tree");

        await page.WaitForFunctionAsync(
            """
            () => Number(document.querySelector("[data-testid='modeling-model-tree']")?.getAttribute('data-visible-count') ?? '0') > 500
            """);

        var groups = page.Locator(".tm-modeling-model-tree__groups");
        Assert.IsTrue(await groups.EvaluateAsync<bool>("element => element.scrollHeight > element.clientHeight"));
        await groups.EvaluateAsync("element => element.scrollTop = element.scrollHeight");
        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-node-generated-element-519']");
    }

    [TestMethod]
    [Description("Very long element names stay on one line with ellipsis styling")]
    public async Task ModelTree_LongName_UsesEllipsis()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=long-name");
        var node = page.Locator("[data-testid='modeling-tree-node-demo-long-name']");
        await node.ScrollIntoViewIfNeededAsync();
        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-node-demo-long-name']");

        var hasEllipsis = await node.Locator(".tm-modeling-model-tree__node-name").EvaluateAsync<bool>(
            """
            element => {
                const style = getComputedStyle(element);
                return style.textOverflow === 'ellipsis' && style.whiteSpace === 'nowrap' && element.scrollWidth >= element.clientWidth;
            }
            """);

        Assert.IsTrue(hasEllipsis, "Long model tree labels should use single-line ellipsis styling.");
    }

    [TestMethod]
    [Description("Group disclosure: pointer collapse removes nodes from the DOM and the keyboard arrows collapse/expand")]
    public async Task ModelTree_GroupCollapsesAndExpands_ByPointerAndKeyboard()
    {
        var page = await OpenLoadedModelingPageAsync();
        // The APG treeitem carries the state; the header row div inside it is the click target.
        var treeitem = page.Locator("[data-testid='modeling-tree-group-startEvent']");
        var header = page.Locator("[data-testid='modeling-tree-group-header-startEvent']");

        Assert.AreEqual("true", await treeitem.GetAttributeAsync("aria-expanded"));
        await header.ClickAsync();
        Assert.AreEqual("false", await treeitem.GetAttributeAsync("aria-expanded"));
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-tree-node-bpmn-start-order']").CountAsync(),
            "a collapsed group renders no node buttons");
        Assert.AreEqual("false", await treeitem.GetAttributeAsync("data-expanded"));

        // ArrowLeft keeps it collapsed, ArrowRight expands it again (Enter/Space are emulated
        // on the group treeitem — the native button split).
        await treeitem.FocusAsync();
        await page.Keyboard.PressAsync("ArrowLeft");
        Assert.AreEqual("false", await treeitem.GetAttributeAsync("aria-expanded"));
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.AreEqual("true", await treeitem.GetAttributeAsync("aria-expanded"));
        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-node-bpmn-start-order']");
    }

    [TestMethod]
    [Description("Filtering temporarily expands a collapsed group; clearing the filter restores the user's collapsed state")]
    public async Task ModelTree_FilterExpandsCollapsedGroup_AndClearRestoresState()
    {
        var page = await OpenLoadedModelingPageAsync();

        await page.Locator("[data-testid='modeling-tree-group-header-serviceTask']").ClickAsync();
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-tree-node-bpmn-ship-order']").CountAsync(),
            "Ship order lives in the serviceTask group, which the user just collapsed");

        await page.Locator("[data-testid='modeling-tree-search']").FillAsync("Ship");
        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-node-bpmn-ship-order']");
        Assert.AreEqual("true", await page.Locator("[data-testid='modeling-tree-group-serviceTask']").GetAttributeAsync("aria-expanded"));

        // Edge case: an EMPTY filter (cleared) must hand the collapsed state back.
        await page.Locator("[data-testid='modeling-tree-search']").FillAsync(string.Empty);
        Assert.AreEqual("false", await page.Locator("[data-testid='modeling-tree-group-serviceTask']").GetAttributeAsync("aria-expanded"));
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-tree-node-bpmn-ship-order']").CountAsync());
        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-group-header-startEvent']");
    }

    [TestMethod]
    [Description("Edge case: a single-element group collapses and expands like any other")]
    public async Task ModelTree_SingleElementGroup_CollapsesAndExpands()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=long-name");
        var header = page.Locator("[data-testid='modeling-tree-group-header-generatedTask']");
        var treeitem = page.Locator("[data-testid='modeling-tree-group-generatedTask']");

        Assert.AreEqual("1", await header.Locator(".tm-modeling-model-tree__group-count").TextContentAsync(),
            "the single-element group advertises a count of 1");
        Assert.AreEqual("true", await treeitem.GetAttributeAsync("aria-expanded"));
        await header.ClickAsync();
        Assert.AreEqual("false", await treeitem.GetAttributeAsync("aria-expanded"));
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-tree-node-demo-long-name']").CountAsync());
        await header.ClickAsync();
        Assert.AreEqual("true", await treeitem.GetAttributeAsync("aria-expanded"));
        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-node-demo-long-name']");
    }

    [TestMethod]
    [Description("Captures required M10 populated, filtered and empty-filter screenshots")]
    public async Task ModelTree_CapturesM10Screenshots()
    {
        var populated = await OpenLoadedModelingPageAsync();
        await TakeScreenshotAsync(populated, "model-tree-populated");
        await SaveStableScreenshotAsync(populated, "model-tree-populated.png");

        var filtered = await OpenLoadedModelingPageAsync();
        await filtered.Locator("[data-testid='modeling-tree-search']").FillAsync("Ship");
        await ExpectVisibleAsync(filtered, "[data-testid='modeling-tree-node-bpmn-ship-order']");
        await TakeScreenshotAsync(filtered, "model-tree-filtered");
        await SaveStableScreenshotAsync(filtered, "model-tree-filtered.png");

        var emptyFilter = await OpenLoadedModelingPageAsync();
        await emptyFilter.Locator("[data-testid='modeling-tree-search']").FillAsync("xxxx");
        await ExpectVisibleAsync(emptyFilter, "[data-testid='modeling-tree-empty-filter']");
        await TakeScreenshotAsync(emptyFilter, "model-tree-empty-filter");
        await SaveStableScreenshotAsync(emptyFilter, "model-tree-empty-filter.png");
    }

    private async Task<IPage> OpenLoadedModelingPageAsync(string query = "")
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        return page;
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector, int timeout = 5000)
    {
        await page.Locator(selector).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });
    }

    private static async Task ExpectHiddenAsync(IPage page, string selector)
    {
        await page.Locator(selector).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
    }

    private static async Task ExpectTextContainsAsync(IPage page, string selector, string expectedText)
    {
        var text = await page.Locator(selector).TextContentAsync() ?? string.Empty;
        StringAssert.Contains(text, expectedText);
    }

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingModelTreeM10E2ETests).Assembly.Location)!,
            "..",
            "..",
            "..",
            "TestResults",
            "modeling-m10");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the rendered modeling editor.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }
}
