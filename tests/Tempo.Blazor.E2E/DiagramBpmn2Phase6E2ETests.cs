using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for phase 6 BPMN 2.0 stencils.</summary>
[TestClass]
[TestCategory("WASM")]
public class DiagramBpmn2Phase6E2ETests : PlaywrightTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";

    /// <inheritdoc />
    protected override string BaseUrl => "http://localhost:5010";

    [TestMethod]
    [Description("BPMN 2.0 stencils are searchable, droppable, and render a lane-based process on the canvas")]
    public async Task Canvas_DropsBpmn2ProcessWithLane_AndReadableMarkers()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1600, 900);

        await GotoAppReadyAsync(page, $"{BaseUrl}{DiagramEditorUrl}");

        var toolbox = page.Locator(".tm-diagram-toolbox");
        await toolbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });

        var search = page.Locator(".tm-diagram-toolbox__search input");
        await search.FillAsync("BPMN 2.0 User Task");
        await page.WaitForTimeoutAsync(300);

        var bpmnLibrary = page.Locator("[data-library-id='bpmn2']");
        await bpmnLibrary.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var userTaskStencil = page.Locator(".tm-diagram-toolbox__item[data-stencil-id='bpmn2.task.user']");
        await userTaskStencil.ScrollIntoViewIfNeededAsync();
        await userTaskStencil.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        Assert.AreEqual("node", await userTaskStencil.GetAttributeAsync("data-stencil-kind"));
        Assert.AreEqual("0", await userTaskStencil.GetAttributeAsync("tabindex"));
        Assert.AreEqual("button", await userTaskStencil.GetAttributeAsync("role"));

        await search.FillAsync(string.Empty);
        await page.WaitForTimeoutAsync(300);

        var canvas = page.Locator(".tm-diagram-canvas").First;
        await canvas.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });

        var poolCountBefore = await page.Locator(".tm-diagram-node[data-stencil-id='bpmn2.pool']").CountAsync();
        await DropStencilAsync(page, "bpmn2.pool", 0.08, 0.25);
        await WaitForStencilCountAsync(page, "bpmn2.pool", poolCountBefore + 1);

        var droppedPool = page.Locator(".tm-diagram-node[data-stencil-id='bpmn2.pool']").Last;
        var poolBox = await droppedPool.BoundingBoxAsync()
            ?? throw new AssertFailedException("Dropped BPMN pool should have a measurable screen bounding box.");

        await DropStencilAtScreenPointAsync(page, "bpmn2.event.start", poolBox.X + poolBox.Width * 0.13, poolBox.Y + poolBox.Height * 0.32);
        await DropStencilAtScreenPointAsync(page, "bpmn2.task.user", poolBox.X + poolBox.Width * 0.28, poolBox.Y + poolBox.Height * 0.32);
        await DropStencilAtScreenPointAsync(page, "bpmn2.gateway.exclusive", poolBox.X + poolBox.Width * 0.62, poolBox.Y + poolBox.Height * 0.32);
        await DropStencilAtScreenPointAsync(page, "bpmn2.event.end", poolBox.X + poolBox.Width * 0.84, poolBox.Y + poolBox.Height * 0.32);

        await WaitForStencilCountAsync(page, "bpmn2.event.start", 1);
        await WaitForStencilCountAsync(page, "bpmn2.task.user", 1);
        await WaitForStencilCountAsync(page, "bpmn2.gateway.exclusive", 1);
        await WaitForStencilCountAsync(page, "bpmn2.event.end", 1);

        var droppedTask = page.Locator(".tm-diagram-node[data-stencil-id='bpmn2.task.user']").Last;
        var droppedGateway = page.Locator(".tm-diagram-node[data-stencil-id='bpmn2.gateway.exclusive']").Last;

        var poolText = await droppedPool.TextContentAsync() ?? string.Empty;
        StringAssert.Contains(poolText, "Lane 1");
        StringAssert.Contains(poolText, "Lane 2");

        var taskText = await droppedTask.TextContentAsync() ?? string.Empty;
        StringAssert.Contains(taskText, "User Task");

        var userTaskMarkerCount = await CountAsync(page, ".tm-bpmn-task-marker-user");
        var exclusiveGatewayMarkerCount = await CountAsync(page, ".tm-bpmn-gateway-marker-exclusive");
        var eventRingCount = await CountAsync(page, "g.tm-diagram-node[data-stencil-id^='bpmn2.event.'] .tm-diagram-node__shape-bg");
        var poolChildNodeCount = await CountAsync(page, "g.tm-diagram-node[data-stencil-id^='bpmn2.'][data-parent-id]");

        Assert.IsTrue(userTaskMarkerCount > 0, "User task marker should be rendered from the custom BPMN stencil SVG.");
        Assert.IsTrue(exclusiveGatewayMarkerCount > 0, "Exclusive gateway marker should be rendered from the custom BPMN stencil SVG.");
        Assert.IsTrue(eventRingCount >= 2, "Start and end event rings should render as SVG stencil shapes.");
        Assert.IsTrue(poolChildNodeCount >= 4, "Dropped BPMN process nodes should be assigned to the pool lane.");
        Assert.IsTrue(await droppedGateway.IsVisibleAsync(), "Gateway should remain visible inside the lane-based process.");
        await AssertNoHorizontalOverlapAsync(page);

        await SaveStableScreenshotAsync(page);
        await TakeScreenshotAsync(page, "diagram_bpmn2_phase6_process_lane");
    }

    private static async Task DropStencilAsync(IPage page, string stencilId, double xRatio, double yRatio)
    {
        await page.EvaluateAsync(
            """
            ({ stencilId, xRatio, yRatio }) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const rect = canvas.getBoundingClientRect();
                window.__tmDiagramDragStencil = stencilId;
                canvas.dispatchEvent(new DragEvent('drop', {
                    bubbles: true,
                    cancelable: true,
                    clientX: rect.left + rect.width * xRatio,
                    clientY: rect.top + rect.height * yRatio,
                    dataTransfer: new DataTransfer()
                }));
            }
            """,
            new { stencilId, xRatio, yRatio });
    }

    private static async Task DropStencilAtScreenPointAsync(IPage page, string stencilId, double clientX, double clientY)
    {
        await page.EvaluateAsync(
            """
            ({ stencilId, clientX, clientY }) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                window.__tmDiagramDragStencil = stencilId;
                canvas.dispatchEvent(new DragEvent('drop', {
                    bubbles: true,
                    cancelable: true,
                    clientX,
                    clientY,
                    dataTransfer: new DataTransfer()
                }));
            }
            """,
            new { stencilId, clientX, clientY });
    }

    private static async Task WaitForStencilCountAsync(IPage page, string stencilId, int minimumCount)
    {
        await page.WaitForFunctionAsync(
            """
            ({ stencilId, minimumCount }) => document.querySelectorAll(`.tm-diagram-node[data-stencil-id='${stencilId}']`).length >= minimumCount
            """,
            new { stencilId, minimumCount },
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    private static Task<int> CountAsync(IPage page, string selector)
        => page.EvaluateAsync<int>(
            "selector => document.querySelectorAll(selector).length",
            selector);

    private static async Task SaveStableScreenshotAsync(IPage page)
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "TestResults",
            "phase6-bpmn2");
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        await File.WriteAllBytesAsync(Path.Combine(directory, "diagram_bpmn2_phase6_process_lane.png"), bytes);
    }

    private static async Task AssertNoHorizontalOverlapAsync(IPage page)
    {
        var selectors = new[]
        {
            ".tm-diagram-node[data-stencil-id='bpmn2.event.start']",
            ".tm-diagram-node[data-stencil-id='bpmn2.task.user']",
            ".tm-diagram-node[data-stencil-id='bpmn2.gateway.exclusive']",
            ".tm-diagram-node[data-stencil-id='bpmn2.event.end']"
        };
        var boxes = new List<LocatorBoundingBoxResult>();
        foreach (var selector in selectors)
        {
            var box = await page.Locator(selector).Last.BoundingBoxAsync()
                ?? throw new AssertFailedException($"{selector} should have a measurable bounding box.");
            boxes.Add(box);
        }

        for (var i = 0; i < boxes.Count - 1; i++)
        {
            Assert.IsTrue(
                boxes[i].X + boxes[i].Width + 8 <= boxes[i + 1].X,
                "BPMN process shapes should remain visually separated in the lane screenshot.");
        }
    }
}
