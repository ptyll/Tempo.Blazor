using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for modeling view selector phase M11.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingViewSelectorM11E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("Switching from BPMN to ArchiMate immediately refreshes available viewpoints")]
    public async Task ViewSelector_SwitchesBpmnToArchimateAndUpdatesViewpoints()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=bpmn&viewpoint=process");

        await ExpectVisibleAsync(page, "[data-testid='modeling-view-selector']");
        await page.Locator("[data-testid='modeling-notation-select']").SelectOptionAsync(["archimate"]);

        await WaitForEditorAttributeAsync(page, "data-notation", "archimate");
        await WaitForSelectOptionAsync(page, "[data-testid='modeling-viewpoint-select']", "application");

        var viewpointText = await page.Locator("[data-testid='modeling-viewpoint-select']").TextContentAsync() ?? string.Empty;
        StringAssert.Contains(viewpointText, "Application usage");
    }

    [TestMethod]
    [Description("Selecting ArchiMate application usage emits the selected viewpoint into editor state")]
    public async Task ViewSelector_SelectsArchimateApplicationUsage()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=bpmn&viewpoint=process");

        await page.Locator("[data-testid='modeling-notation-select']").SelectOptionAsync(["archimate"]);
        await WaitForSelectOptionAsync(page, "[data-testid='modeling-viewpoint-select']", "application");
        await page.Locator("[data-testid='modeling-viewpoint-select']").SelectOptionAsync(["application"]);

        await WaitForEditorAttributeAsync(page, "data-notation", "archimate");
        await WaitForEditorAttributeAsync(page, "data-viewpoint", "application");
        Assert.AreEqual("application", await page.Locator("[data-testid='modeling-view-selector']").GetAttributeAsync("data-viewpoint"));
    }

    [TestMethod]
    [Description("Switching notation with a loaded model keeps the model tree and surfaces incompatible element issues")]
    public async Task ViewSelector_NotationSwitchKeepsTreeAndShowsIssues()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=bpmn&viewpoint=overview");

        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-node-bpmn-validate-order']");
        await page.Locator("[data-testid='modeling-notation-select']").SelectOptionAsync(["erd"]);

        await WaitForEditorAttributeAsync(page, "data-notation", "erd");
        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-node-bpmn-validate-order']");
        await WaitForIssueCountAtLeastAsync(page, 1);
    }

    [TestMethod]
    [Description("Repeated rapid notation switches settle on the last selected notation")]
    public async Task ViewSelector_RapidNotationSwitchingEndsInLastSelection()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=bpmn&viewpoint=overview");
        var select = page.Locator("[data-testid='modeling-notation-select']");

        await select.SelectOptionAsync(["archimate"]);
        await select.SelectOptionAsync(["uml"]);
        await select.SelectOptionAsync(["bpmn"]);
        await select.SelectOptionAsync(["erd"]);
        await select.SelectOptionAsync(["archimate"]);

        await WaitForEditorAttributeAsync(page, "data-notation", "archimate");
        await WaitForSelectOptionAsync(page, "[data-testid='modeling-viewpoint-select']", "application");

        Assert.AreEqual("archimate", await select.InputValueAsync());
    }

    [TestMethod]
    [Description("Captures required M11 BPMN and ArchiMate selector screenshots")]
    public async Task ViewSelector_CapturesM11Screenshots()
    {
        var bpmn = await OpenLoadedModelingPageAsync("?notation=bpmn&viewpoint=process");
        await ExpectVisibleAsync(bpmn, "[data-testid='modeling-view-selector'][data-notation='bpmn']");
        await TakeScreenshotAsync(bpmn, "view-selector-bpmn");
        await SaveStableScreenshotAsync(bpmn, "view-selector-bpmn.png");

        var archimate = await OpenLoadedModelingPageAsync("?notation=archimate&viewpoint=application");
        await ExpectVisibleAsync(archimate, "[data-testid='modeling-view-selector'][data-notation='archimate']");
        await ExpectVisibleAsync(archimate, "[data-testid='modeling-view-selector'][data-viewpoint='application']");
        await TakeScreenshotAsync(archimate, "view-selector-archimate");
        await SaveStableScreenshotAsync(archimate, "view-selector-archimate.png");
    }

    private async Task<IPage> OpenLoadedModelingPageAsync(string query = "")
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}{ModelingEditorUrl}{query}");
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        return page;
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector, int timeout = 5000)
    {
        await page.Locator(selector).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });
    }

    private static Task WaitForEditorAttributeAsync(IPage page, string attributeName, string expectedValue) =>
        page.WaitForFunctionAsync(
            """
            ([attributeName, expectedValue]) => {
                const editor = document.querySelector("[data-testid='modeling-editor']");
                return editor?.getAttribute(attributeName) === expectedValue;
            }
            """,
            new[] { attributeName, expectedValue },
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

    private static Task WaitForSelectOptionAsync(IPage page, string selector, string optionValue) =>
        page.WaitForFunctionAsync(
            """
            ([selector, optionValue]) => {
                const select = document.querySelector(selector);
                return Array.from(select?.options ?? []).some(option => option.value === optionValue);
            }
            """,
            new[] { selector, optionValue },
            new PageWaitForFunctionOptions { Timeout = 10000 });

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingViewSelectorM11E2ETests).Assembly.Location)!,
            "..",
            "..",
            "..",
            "TestResults",
            "modeling-m11");
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
