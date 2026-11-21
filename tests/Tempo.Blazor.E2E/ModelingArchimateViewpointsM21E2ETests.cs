using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for modeling ArchiMate 3.2 viewpoint filtering phase M21.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingArchimateViewpointsM21E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("ApplicationUsage viewpoint scopes the model tree and diagram to application and technology elements")]
    public async Task ApplicationUsageViewpointShowsOnlyApplicationAndTechnologyScope()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=archimate32&viewpoint=ApplicationUsage&scenario=archimate32-viewpoints");

        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-node-arch-vp-app'][data-semantic-type='ApplicationComponent']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-node-arch-vp-tech-service'][data-semantic-type='TechnologyService']");
        await WaitForTreeNodeCountAsync(page, "arch-vp-business-process", 0);
        await WaitForTreeNodeCountAsync(page, "arch-vp-stakeholder", 0);
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-vp-app'][data-stencil-id='archimate3.application.component']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-vp-tech-service'][data-stencil-id='archimate3.technology.service']");
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-vp-business-process']").CountAsync());

        await TakeScreenshotAsync(page, "archimate-viewpoint-application-usage");
        await SaveStableScreenshotAsync(page, "archimate-viewpoint-application-usage.png");
    }

    [TestMethod]
    [Description("Motivation viewpoint scopes the model tree to motivation elements and common ArchiMate junction/grouping helpers")]
    public async Task MotivationViewpointShowsMotivationScope()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=archimate32&viewpoint=Motivation&scenario=archimate32-viewpoints");

        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-node-arch-vp-stakeholder'][data-semantic-type='Stakeholder']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-node-arch-vp-goal'][data-semantic-type='Goal']");
        await WaitForTreeNodeCountAsync(page, "arch-vp-app", 0);
        await WaitForTreeNodeCountAsync(page, "arch-vp-business-process", 0);
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-vp-goal'][data-stencil-id='archimate3.motivation.goal']");
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-vp-app']").CountAsync());
    }

    [TestMethod]
    [Description("Elements outside the selected viewpoint are reported in the issues panel")]
    public async Task ViewpointIssuesExplainSkippedElements()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=archimate32&viewpoint=ApplicationUsage&scenario=archimate32-viewpoints");

        await WaitForIssueCountAtLeastAsync(page, 5);
        await Assertions.Expect(page.Locator("[data-testid='modeling-issue-list']")).ToContainTextAsync("BusinessActor");
        await Assertions.Expect(page.Locator("[data-testid='modeling-issue-list']")).ToContainTextAsync("BusinessProcess");
        await Assertions.Expect(page.Locator("[data-testid='modeling-issue-list']")).ToContainTextAsync("Stakeholder");
        await Assertions.Expect(page.Locator("[data-testid='modeling-issue-list']")).ToContainTextAsync("ApplicationUsage");

        await TakeScreenshotAsync(page, "archimate-viewpoint-issues");
        await SaveStableScreenshotAsync(page, "archimate-viewpoint-issues.png");
    }

    [TestMethod]
    [Description("Switching viewpoint regenerates the diagram and omits elements outside the new scope")]
    public async Task SwitchingViewpointRegeneratesDiagramWithNewScope()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=archimate32&viewpoint=ApplicationUsage&scenario=archimate32-viewpoints");

        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-vp-app']");
        await page.Locator("[data-testid='modeling-viewpoint-select']").SelectOptionAsync(["Motivation"]);
        await Assertions.Expect(page.Locator("[data-testid='modeling-view-selector']")).ToHaveAttributeAsync("data-viewpoint", "Motivation");

        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-node-arch-vp-goal'][data-semantic-type='Goal']");
        await WaitForTreeNodeCountAsync(page, "arch-vp-app", 0);
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-vp-goal']");
        await WaitForDiagramNodeCountAsync(page, "arch-vp-app", 0);
    }

    private async Task<IPage> OpenLoadedModelingPageAsync(string query = "")
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}{ModelingEditorUrl}{query}");
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.Locator("[data-testid='modeling-diagram-preview']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        return page;
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector, int timeout = 10000)
    {
        await page.Locator(selector).First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });
    }

    private static Task WaitForIssueCountAtLeastAsync(IPage page, int minimumCount) =>
        page.WaitForFunctionAsync(
            """
            minimumCount => {
                const editor = document.querySelector("[data-testid='modeling-editor']");
                return Number(editor?.getAttribute('data-issue-count') ?? '0') >= minimumCount;
            }
            """,
            minimumCount,
            new PageWaitForFunctionOptions { Timeout = 10000 });

    private static Task WaitForTreeNodeCountAsync(IPage page, string elementId, int expectedCount) =>
        page.WaitForFunctionAsync(
            """
            ([elementId, expectedCount]) => document.querySelectorAll(`[data-testid="modeling-tree-node-${elementId}"]`).length === expectedCount
            """,
            new object[] { elementId, expectedCount },
            new PageWaitForFunctionOptions { Timeout = 10000 });

    private static Task WaitForDiagramNodeCountAsync(IPage page, string elementId, int expectedCount) =>
        page.WaitForFunctionAsync(
            """
            ([elementId, expectedCount]) => document.querySelectorAll(`[data-testid="modeling-diagram-preview"] g.tm-diagram-node[data-model-element-id="${elementId}"]`).length === expectedCount
            """,
            new object[] { elementId, expectedCount },
            new PageWaitForFunctionOptions { Timeout = 10000 });

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingArchimateViewpointsM21E2ETests).Assembly.Location)!,
            "..",
            "..",
            "..",
            "TestResults",
            "modeling-m21");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the ArchiMate viewpoint editor.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }
}
