using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for modeling drag/drop phase M16.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingDragDropM16E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";
    private const string DragElementId = "bpmn-validate-order";
    private const string DragElementSourceId = "demo/bpmn/validate-order";

    [TestMethod]
    [Description("Dragging a model tree element onto the canvas creates a visible reused diagram node")]
    public async Task DragModelTreeElementToCanvasCreatesNode()
    {
        var page = await OpenLoadedModelingPageAsync();
        var before = await GetPreviewNodeCountAsync(page);
        var sourceCountBefore = await GetSourceNodeCountAsync(page, DragElementSourceId);

        await DragElementToCanvasAsync(page, DragElementId, 180, 220);

        await WaitForPreviewNodeCountAsync(page, before + 1);
        var sourceCountAfter = await GetSourceNodeCountAsync(page, DragElementSourceId);
        Assert.AreEqual(sourceCountBefore + 1, sourceCountAfter, "Dropped node should reuse the original model element SourceId.");
    }

    [TestMethod]
    [Description("Canceling a drag before drop leaves the preview unchanged")]
    public async Task DragCancelDoesNotChangeCanvas()
    {
        var page = await OpenLoadedModelingPageAsync();
        var before = await GetPreviewNodeCountAsync(page);
        var treeNode = page.Locator($"[data-testid='modeling-tree-node-{DragElementId}']");
        var box = await treeNode.BoundingBoxAsync();
        Assert.IsNotNull(box);

        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width + 80, box.Y + 10);
        await page.Keyboard.PressAsync("Escape");
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual(before, await GetPreviewNodeCountAsync(page));
    }

    [TestMethod]
    [Description("Dropping near the canvas edge is handled consistently without errors")]
    public async Task DropNearCanvasEdgeIsHandledConsistently()
    {
        var page = await OpenLoadedModelingPageAsync();
        var before = await GetPreviewNodeCountAsync(page);

        await DragElementToCanvasAsync(page, DragElementId, 8, 8);
        await page.WaitForTimeoutAsync(500);

        var after = await GetPreviewNodeCountAsync(page);
        Assert.IsTrue(after == before || after == before + 1, "Edge drop should either be ignored or add one node consistently.");
    }

    [TestMethod]
    [Description("Dropping on an occupied node creates a nearby occurrence instead of stacking on the same center")]
    public async Task DropOnOccupiedNodeCreatesNearbyOccurrence()
    {
        var page = await OpenLoadedModelingPageAsync();
        var existingNode = page.Locator("[data-testid='modeling-diagram-preview'] [data-model-element-id='bpmn-ship-order']").First;
        await existingNode.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        var before = await GetPreviewNodeCountAsync(page);
        var existingTransform = await existingNode.GetAttributeAsync("transform") ?? string.Empty;

        var existingBox = await existingNode.BoundingBoxAsync();
        Assert.IsNotNull(existingBox);
        var canvasBox = await page.Locator("[data-testid='modeling-diagram-preview-canvas-shell']").BoundingBoxAsync();
        Assert.IsNotNull(canvasBox);

        await DragElementToCanvasAsync(
            page,
            "bpmn-ship-order",
            existingBox.X + existingBox.Width / 2 - canvasBox.X,
            existingBox.Y + existingBox.Height / 2 - canvasBox.Y);

        await WaitForPreviewNodeCountAsync(page, before + 1);
        var duplicatedNodes = page.Locator("[data-testid='modeling-diagram-preview'] [data-model-element-id='bpmn-ship-order']");
        await Assertions.Expect(duplicatedNodes).ToHaveCountAsync(2);

        var transforms = await duplicatedNodes.EvaluateAllAsync<string[]>(
            "nodes => nodes.map(node => node.getAttribute('transform') || '')");
        Assert.IsTrue(transforms.Any(transform => !string.Equals(transform, existingTransform, StringComparison.Ordinal)), "Dropped duplicate should be offset from the occupied node.");
    }

    [TestMethod]
    [Description("Read-only drop mode ignores dragged tree elements without throwing")]
    public async Task ReadOnlyDropModeIgnoresDrag()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=drag-readonly");
        var before = await GetPreviewNodeCountAsync(page);

        await DragElementToCanvasAsync(page, DragElementId, 180, 220);
        await page.WaitForTimeoutAsync(500);

        Assert.AreEqual(before, await GetPreviewNodeCountAsync(page));
    }

    [TestMethod]
    [Description("Captures required M16 drag before and after screenshots")]
    public async Task DragDropCapturesM16Screenshots()
    {
        var page = await OpenLoadedModelingPageAsync();
        await page.Locator($"[data-testid='modeling-tree-node-{DragElementId}']").HoverAsync();
        await TakeScreenshotAsync(page, "drag-before");
        await SaveStableScreenshotAsync(page, "drag-before.png");

        var before = await GetPreviewNodeCountAsync(page);
        await DragElementToCanvasAsync(page, DragElementId, 180, 220);
        await WaitForPreviewNodeCountAsync(page, before + 1);
        await TakeScreenshotAsync(page, "drag-after");
        await SaveStableScreenshotAsync(page, "drag-after.png");
    }

    [TestMethod]
    [Description("Dragging a NESTED hierarchy node (a level-2 child of the ?scenario=hierarchy subprocess) onto the canvas creates a reused node")]
    public async Task DragNestedHierarchyNodeToCanvasCreatesNode()
    {
        // Phase 12: the hierarchy tree nests the order activities under the BPMN subprocess —
        // the same drag bridge must work from an indented, aria-level=2 row.
        var page = await OpenLoadedModelingPageAsync("?scenario=hierarchy&notation=bpmn");
        var nested = page.Locator($"[data-testid='modeling-tree-node-{DragElementId}']");
        Assert.AreEqual("2", await nested.GetAttributeAsync("aria-level"), "the dragged row is a nested hierarchy node");
        var before = await GetPreviewNodeCountAsync(page);

        await DragElementToCanvasAsync(page, DragElementId, 180, 220);

        await WaitForPreviewNodeCountAsync(page, before + 1);
        var sourceCountAfter = await GetSourceNodeCountAsync(page, DragElementSourceId);
        Assert.IsTrue(sourceCountAfter >= 1, "the dropped node reuses the nested element's SourceId.");
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
        await page.Locator($"[data-testid='modeling-tree-node-{DragElementId}']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.Locator("[data-testid='modeling-diagram-preview-canvas-shell']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        return page;
    }

    private static async Task DragElementToCanvasAsync(IPage page, string elementId, double targetX, double targetY)
    {
        var source = page.Locator($"[data-testid='modeling-tree-node-{elementId}']");
        var target = page.Locator("[data-testid='modeling-diagram-preview-canvas-shell']");
        await source.DragToAsync(target, new LocatorDragToOptions
        {
            TargetPosition = new TargetPosition
            {
                X = (float)targetX,
                Y = (float)targetY
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
            Timeout = 5000
        });
    }

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingDragDropM16E2ETests).Assembly.Location)!,
            "..",
            "..",
            "..",
            "TestResults",
            "modeling-m16");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the modeling drag/drop UI.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }
}
