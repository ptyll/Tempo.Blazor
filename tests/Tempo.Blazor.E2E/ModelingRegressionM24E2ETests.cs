using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>Cross-phase regression E2E checks for modeling editor phase M24.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingRegressionM24E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("Loaded model can switch to ArchiMate, regenerate, accept a tree drop, regenerate again, and keep inspector selection")]
    public async Task FullWorkflow_ArchimateGenerateDragRegenerateKeepsInspectorSelection()
    {
        var page = await OpenLoadedModelingPageAsync();

        await page.Locator("[data-testid='modeling-notation-select']").SelectOptionAsync(["archimate"]);
        await WaitForEditorAttributeAsync(page, "data-notation", "archimate");
        await page.Locator("[data-testid='modeling-generate-diagram-button']").ClickAsync();
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-customer-portal']");

        var before = await GetPreviewNodeCountAsync(page);
        await DragElementToCanvasAsync(page, "arch-customer-portal", 220, 250);
        await WaitForPreviewNodeCountAsync(page, before + 1);
        await WaitForEditorAttributeAsync(page, "data-selected-element-id", "arch-customer-portal");

        await page.Locator("[data-testid='modeling-generate-diagram-button']").ClickAsync();
        await WaitForEditorAttributeAsync(page, "data-selected-element-id", "arch-customer-portal");
        await Assertions.Expect(page.Locator("[data-testid='modeling-inspector']")).ToHaveAttributeAsync("data-selected-element-id", "arch-customer-portal");
        await Assertions.Expect(page.Locator("[data-testid='modeling-inspector-name']")).ToContainTextAsync("Customer portal");
        Assert.IsFalse(await HasStaleNotationMismatchAsync(page), "Preview should not show stale BPMN nodes after the ArchiMate workflow.");

        await SaveStableScreenshotAsync(page, "regression-full-workflow.png");
    }

    [TestMethod]
    [Description("Mixed BPMN and ArchiMate source model reports elements that are outside the selected BPMN notation")]
    public async Task MixedModel_BpmnNotationReportsArchimateElementsAsIssues()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=bpmn&viewpoint=overview");

        await WaitForIssueCountAtLeastAsync(page, 2);
        await ExpectVisibleAsync(page, "[data-testid='modeling-issue-panel'] [data-source-element-id='arch-customer-portal']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-issue-panel'] [data-source-element-id='arch-order-service']");
        await Assertions.Expect(page.Locator("[data-testid='modeling-issue-list']")).ToContainTextAsync("applicationComponent");
    }

    [TestMethod]
    [Description("Issue click synchronizes issue panel, tree selection, tree scroll, and inspector detail")]
    public async Task IssueClick_SelectsTreeNodeAndInspectorDetail()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-mixed");

        await page.Locator("[data-testid='modeling-issue-panel'] [data-source-element-id='bpmn-ship-order']").First.ClickAsync();

        await WaitForEditorAttributeAsync(page, "data-selected-element-id", "bpmn-ship-order");
        await Assertions.Expect(page.Locator("[data-testid='modeling-tree-node-bpmn-ship-order']")).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(page.Locator("[data-testid='modeling-inspector']")).ToHaveAttributeAsync("data-selected-element-id", "bpmn-ship-order");
        await Assertions.Expect(page.Locator("[data-testid='modeling-inspector-name']")).ToContainTextAsync("Ship order");
        Assert.IsTrue(await IsTreeNodeInsideScrollViewportAsync(page, "bpmn-ship-order"), "Issue click should reveal the matching tree node.");
    }

    [TestMethod]
    [Description("Switching notation after a generated diagram replaces old nodes with the newly generated notation")]
    public async Task NotationSwitch_RegeneratesDiagramWithoutStaleNodes()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=bpmn&viewpoint=overview");

        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='bpmn-validate-order']");
        await WaitForDiagramNodeCountAsync(page, "arch-customer-portal", 0);

        await page.Locator("[data-testid='modeling-notation-select']").SelectOptionAsync(["archimate"]);
        await WaitForEditorAttributeAsync(page, "data-notation", "archimate");

        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-customer-portal']");
        await WaitForDiagramNodeCountAsync(page, "bpmn-validate-order", 0);
        Assert.IsFalse(await HasStaleNotationMismatchAsync(page), "Diagram should be fully regenerated for ArchiMate.");
    }

    [TestMethod]
    [Description("Dragging three elements with one duplicate creates four canvas nodes and two occurrences of the same source")]
    public async Task DragMultipleElements_AllOccurrencesAreKept()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=regression-empty-canvas");

        await Assertions.Expect(page.Locator("[data-testid='modeling-diagram-preview']")).ToHaveAttributeAsync("data-node-count", "0");
        await DragElementToCanvasAsync(page, "bpmn-validate-order", 120, 180);
        await DragElementToCanvasAsync(page, "bpmn-ship-order", 260, 180);
        await DragElementToCanvasAsync(page, "arch-customer-portal", 400, 180);
        await DragElementToCanvasAsync(page, "bpmn-validate-order", 120, 340);

        await WaitForPreviewNodeCountAsync(page, 4);
        Assert.AreEqual(2, await GetSourceNodeCountAsync(page, "demo/bpmn/validate-order"));
        Assert.AreEqual(1, await GetSourceNodeCountAsync(page, "demo/bpmn/ship-order"));
        Assert.AreEqual(1, await GetSourceNodeCountAsync(page, "demo/arch/customer-portal"));
    }

    [TestMethod]
    [Description("ArchiMate ApplicationUsage viewpoint filters the tree and generated canvas to the viewpoint scope")]
    public async Task ArchimateApplicationUsage_FiltersTreeAndCanvasScope()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=archimate32&viewpoint=ApplicationUsage&scenario=archimate32-viewpoints");

        await ExpectVisibleAsync(page, "[data-testid='modeling-tree-node-arch-vp-app'][data-semantic-type='ApplicationComponent']");
        await WaitForTreeNodeCountAsync(page, "arch-vp-business-process", 0);
        await WaitForTreeNodeCountAsync(page, "arch-vp-stakeholder", 0);

        await page.Locator("[data-testid='modeling-generate-diagram-button']").ClickAsync();
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-vp-app']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='arch-vp-app-service']");
        await WaitForDiagramNodeCountAsync(page, "arch-vp-business-process", 0);
        await WaitForDiagramNodeCountAsync(page, "arch-vp-stakeholder", 0);
        await WaitForDiagramNodeCountAsync(page, "arch-vp-goal", 0);
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
        await page.Locator("[data-testid='modeling-diagram-preview']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        return page;
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector, int timeout = 10000)
    {
        await page.Locator(selector).First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });
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

    private static async Task DragElementToCanvasAsync(IPage page, string elementId, double targetX, double targetY)
    {
        var source = page.Locator($"[data-testid='modeling-tree-node-{elementId}']");
        var target = page.Locator("[data-testid='modeling-diagram-preview-canvas-shell']");
        // The requested offsets were written against a wider preview column. The workspace now
        // lays the panels out as cards with token gutters, so clamp every drop point INTO the
        // visible canvas shell — a drop outside it would land on a neighbouring panel.
        var box = await target.BoundingBoxAsync() ?? throw new InvalidOperationException("modeling diagram canvas shell is not visible");
        var x = Math.Clamp(targetX, 24, Math.Max(24, box.Width - 24));
        var y = Math.Clamp(targetY, 24, Math.Max(24, box.Height - 24));
        await source.DragToAsync(target, new LocatorDragToOptions
        {
            TargetPosition = new TargetPosition
            {
                X = (float)x,
                Y = (float)y
            },
            Timeout = 10000
        });
    }

    private static async Task<int> GetPreviewNodeCountAsync(IPage page)
    {
        var value = await page.Locator("[data-testid='modeling-diagram-preview']").GetAttributeAsync("data-node-count");
        return int.Parse(value ?? "0", CultureInfo.InvariantCulture);
    }

    private static async Task<int> GetSourceNodeCountAsync(IPage page, string sourceId)
        => await page.Locator($"[data-testid='modeling-diagram-preview'] [data-source-id='{sourceId}']").CountAsync();

    private static async Task WaitForPreviewNodeCountAsync(IPage page, int expected)
    {
        await Assertions.Expect(page.Locator("[data-testid='modeling-diagram-preview']")).ToHaveAttributeAsync("data-node-count", expected.ToString(CultureInfo.InvariantCulture), new LocatorAssertionsToHaveAttributeOptions
        {
            Timeout = 10000
        });
    }

    private static Task<bool> IsTreeNodeInsideScrollViewportAsync(IPage page, string elementId) =>
        page.EvaluateAsync<bool>(
            """
            elementId => {
                const node = document.querySelector(`[data-testid='modeling-tree-node-${elementId}']`);
                const viewport = document.querySelector('.tm-modeling-model-tree__groups');
                if (!node || !viewport) {
                    return false;
                }

                const nodeRect = node.getBoundingClientRect();
                const viewportRect = viewport.getBoundingClientRect();
                return nodeRect.top >= viewportRect.top && nodeRect.bottom <= viewportRect.bottom;
            }
            """,
            elementId);

    private static Task<bool> HasStaleNotationMismatchAsync(IPage page) =>
        page.EvaluateAsync<bool>(
            """
            () => {
                const notation = document.querySelector("[data-testid='modeling-editor']")?.getAttribute("data-notation") || "";
                const nodes = Array.from(document.querySelectorAll("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-stencil-id]"));
                if (notation === "archimate") {
                    return nodes.some(node => !String(node.getAttribute("data-stencil-id") || "").startsWith("archimate"));
                }

                if (notation === "bpmn") {
                    return nodes.some(node => !String(node.getAttribute("data-stencil-id") || "").startsWith("bpmn"));
                }

                return false;
            }
            """);

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingRegressionM24E2ETests).Assembly.Location)!,
            "..",
            "..",
            "..",
            "TestResults",
            "modeling-m24");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the modeling regression workflow UI.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }
}
