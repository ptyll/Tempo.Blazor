using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for modeling ArchiMate 3.2 notation profile phase M19.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingArchimate32ProfileM19E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("ArchiMate 3.2 Layered view renders business/application stencils and grouping around children")]
    public async Task ArchimateBusinessApplicationDiagramUsesArchimateStencilsAndGrouping()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=archimate32&viewpoint=Layered&scenario=archimate32-business-app");

        await Assertions.Expect(page.Locator("[data-testid='modeling-view-selector']")).ToHaveAttributeAsync("data-notation", "archimate32");
        await Assertions.Expect(page.Locator("[data-testid='modeling-view-selector']")).ToHaveAttributeAsync("data-viewpoint", "Layered");
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-order-process'][data-stencil-id='archimate3.business.process']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-order-app'][data-stencil-id='archimate3.application.component']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-order-group'][data-stencil-id='archimate3.cross.grouping']");
        await Assertions.Expect(page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-order-process']")).ToHaveAttributeAsync("data-parent-id", "arch-order-group");
        await AssertGroupingSurroundsChildrenAsync(page);

        await TakeScreenshotAsync(page, "archimate-business-app-diagram");
        await SaveStableScreenshotAsync(page, "archimate-business-app-diagram.png");
    }

    [TestMethod]
    [Description("ArchiMate 3.2 missing stencil mapping is reported and the element is skipped")]
    public async Task ArchimateMissingStencilShowsIssueAndSkipsElement()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=archimate32&viewpoint=Business&scenario=archimate32-missing-stencil");

        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-valid-process'][data-stencil-id='archimate3.business.process']");
        await WaitForIssueCountAtLeastAsync(page, 1);
        await Assertions.Expect(page.Locator("[data-testid='modeling-issue-list']")).ToContainTextAsync("UnmappedElement");
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-unmapped-element']").CountAsync());
    }

    [TestMethod]
    [Description("ArchiMate 3.2 Motivation scenario renders only motivation layer stencils")]
    public async Task ArchimateMotivationScenarioRendersOnlyMotivationLayer()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=archimate32&viewpoint=Motivation&scenario=archimate32-motivation");

        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-stakeholder'][data-stencil-id='archimate3.motivation.stakeholder']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-goal'][data-stencil-id='archimate3.motivation.goal']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-requirement'][data-stencil-id='archimate3.motivation.requirement']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-value'][data-stencil-id='archimate3.motivation.value']");
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-stencil-id^='archimate3.business.']").CountAsync());
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-stencil-id^='archimate3.application.']").CountAsync());
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-stencil-id^='archimate3.technology.']").CountAsync());
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

    private static async Task AssertGroupingSurroundsChildrenAsync(IPage page)
    {
        var surrounds = await page.EvaluateAsync<bool>(
            """
            () => {
                const box = selector => document.querySelector(selector)?.getBoundingClientRect();
                const group = box("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-order-group']");
                const process = box("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-order-process']");
                const app = box("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-order-app']");
                if (!group || !process || !app) {
                    return false;
                }

                return group.left < process.left
                    && group.top < process.top
                    && group.right > process.right
                    && group.bottom > process.bottom
                    && group.left < app.left
                    && group.top < app.top
                    && group.right > app.right
                    && group.bottom > app.bottom;
            }
            """);

        Assert.IsTrue(surrounds, "Grouping element should visually surround the ArchiMate child elements.");
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

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingArchimate32ProfileM19E2ETests).Assembly.Location)!,
            "..",
            "..",
            "..",
            "TestResults",
            "modeling-m19");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the ArchiMate modeling editor.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }
}
