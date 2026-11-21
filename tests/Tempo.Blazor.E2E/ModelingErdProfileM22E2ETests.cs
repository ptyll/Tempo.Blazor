using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for consumer-registered ERD notation profile phase M22.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingErdProfileM22E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("Demo app registers the optional ERD profile and exposes it in the notation dropdown")]
    public async Task ErdProfileAppearsInNotationDropdown()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=erd&scenario=erd-no-stencils");

        await Assertions.Expect(page.Locator("[data-testid='modeling-view-selector']")).ToHaveAttributeAsync("data-notation", "erd");
        await Assertions.Expect(page.Locator("[data-testid='modeling-notation-select'] option[value='erd']")).ToHaveTextAsync("ERD");
    }

    [TestMethod]
    [Description("Selecting ERD without registered stencils reports actionable mapping issues and leaves the canvas empty")]
    public async Task ErdWithoutStencilsShowsIssuesAndEmptyCanvas()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=erd&scenario=erd-no-stencils");

        await WaitForIssueCountAsync(page, 8);
        await Assertions.Expect(page.Locator("[data-testid='modeling-diagram-preview']")).ToHaveAttributeAsync("data-node-count", "0");
        await Assertions.Expect(page.Locator("[data-testid='modeling-diagram-preview']")).ToHaveAttributeAsync("data-edge-count", "0");
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview-empty-diagram-hint']");
        await Assertions.Expect(page.Locator("[data-testid='modeling-issue-list']")).ToContainTextAsync("Entity");
        await Assertions.Expect(page.Locator("[data-testid='modeling-issue-list']")).ToContainTextAsync("WeakEntity");
        await Assertions.Expect(page.Locator("[data-testid='modeling-issue-list']")).ToContainTextAsync("No node stencil mapping");
        await Assertions.Expect(page.Locator("[data-testid='modeling-issue-list']")).ToContainTextAsync("Register ERD diagram stencils");
        Assert.AreEqual(8, await page.Locator("[data-testid='modeling-issue-list'] [data-severity='warning']").CountAsync());
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-node").CountAsync());

        await TakeScreenshotAsync(page, "erd-no-stencils-issues");
        await SaveStableScreenshotAsync(page, "erd-no-stencils-issues.png");
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

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingErdProfileM22E2ETests).Assembly.Location)!,
            "..",
            "..",
            "..",
            "TestResults",
            "modeling-m22");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the ERD no-stencils issue state.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }
}
