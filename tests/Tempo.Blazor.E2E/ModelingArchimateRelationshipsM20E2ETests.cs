using System.Diagnostics;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for modeling ArchiMate relationship matrix phase M20.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingArchimateRelationshipsM20E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("Valid ArchiMate 3.2 relationships generate without issues and render distinct edge stencils")]
    public async Task ValidArchimateRelationshipsGenerateWithoutIssues()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=archimate32&viewpoint=Layered&scenario=archimate32-relationships");

        await WaitForIssueCountAsync(page, 0);
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] [data-edge-id='arch-rel-serving-app-business']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] [data-edge-id='arch-rel-realization-requirement-goal']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] [data-edge-id='arch-rel-access-function-data']");
        await Assertions.Expect(page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-rel-app-service'][data-stencil-id='archimate3.application.service']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-rel-process'][data-stencil-id='archimate3.business.process']")).ToBeVisibleAsync();

        var servingHasArrowhead = await page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-edge-group[data-edge-id='arch-rel-serving-app-business'] .tm-diagram-arrowhead").CountAsync();
        Assert.IsTrue(servingHasArrowhead > 0, "Serving relationship should render an ArchiMate arrowhead.");

        await TakeScreenshotAsync(page, "archimate-relationships");
        await SaveStableScreenshotAsync(page, "archimate-relationships.png");
    }

    [TestMethod]
    [Description("Invalid ArchiMate 3.2 relationship reports a warning and skips the edge")]
    public async Task InvalidArchimateRelationshipShowsIssueAndSkipsEdge()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=archimate32&viewpoint=Layered&scenario=archimate32-invalid-relationship");

        await WaitForIssueCountAtLeastAsync(page, 1);
        await Assertions.Expect(page.Locator("[data-testid='modeling-issue-list']")).ToContainTextAsync("BusinessProcess");
        await Assertions.Expect(page.Locator("[data-testid='modeling-issue-list']")).ToContainTextAsync("ApplicationComponent");
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-diagram-preview'] [data-edge-id='arch-invalid-serving-process-app']").CountAsync());
    }

    [TestMethod]
    [Description("Large ArchiMate 3.2 relationship model completes generation promptly")]
    public async Task LargeArchimateRelationshipModelCompletesWithinThreeSeconds()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}{ModelingEditorUrl}?notation=archimate32&viewpoint=Business&scenario=archimate32-large-relationships");

        var stopwatch = Stopwatch.StartNew();
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 3000 });
        await page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-edge-group[data-edge-id='arch-large-flow-107']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 3000 });
        stopwatch.Stop();

        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Large ArchiMate relationship generation should finish within 3s after app readiness, actual {stopwatch.Elapsed}.");
        await WaitForIssueCountAsync(page, 0);
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

    private static Task WaitForIssueCountAsync(IPage page, int expectedCount) =>
        page.WaitForFunctionAsync(
            """
            expectedCount => {
                const editor = document.querySelector("[data-testid='modeling-editor']");
                return Number(editor?.getAttribute('data-issue-count') ?? '-1') === expectedCount;
            }
            """,
            expectedCount,
            new PageWaitForFunctionOptions { Timeout = 10000 });

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
            Path.GetDirectoryName(typeof(ModelingArchimateRelationshipsM20E2ETests).Assembly.Location)!,
            "..",
            "..",
            "..",
            "TestResults",
            "modeling-m20");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the ArchiMate relationship editor.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }
}
