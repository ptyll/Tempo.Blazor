using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for phase 8 extended diagram stencil libraries.</summary>
[TestClass]
[TestCategory("WASM")]
public class DiagramExtendedStencilPhase8E2ETests : PlaywrightTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";

    /// <inheritdoc />
    protected override string BaseUrl => "http://localhost:5010";

    [TestMethod]
    [Description("Extended Tempo-original libraries are searchable, droppable, and render readable neutral markers")]
    public async Task Canvas_DropsExtendedArchitectureLibraries_WithReadableNeutralMarkers()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1600, 900);

        await page.GotoAsync($"{BaseUrl}{DiagramEditorUrl}");
        await WaitForAppReadyAsync(page);

        var toolbox = page.Locator(".tm-diagram-toolbox");
        await toolbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });

        var search = page.Locator(".tm-diagram-toolbox__search input");
        await search.FillAsync("C4 Container");
        await page.WaitForTimeoutAsync(300);

        var c4Library = page.Locator("[data-library-id='c4']");
        await c4Library.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var c4ContainerStencil = page.Locator(".tm-diagram-toolbox__item[data-stencil-id='c4.container']");
        await c4ContainerStencil.ScrollIntoViewIfNeededAsync();
        await c4ContainerStencil.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        Assert.AreEqual("node", await c4ContainerStencil.GetAttributeAsync("data-stencil-kind"));
        Assert.AreEqual("0", await c4ContainerStencil.GetAttributeAsync("tabindex"));
        Assert.AreEqual("button", await c4ContainerStencil.GetAttributeAsync("role"));

        await search.FillAsync(string.Empty);
        await page.WaitForTimeoutAsync(300);

        var canvas = page.Locator(".tm-diagram-canvas").First;
        await canvas.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New document" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-node[data-stencil-id=\"uml25.class\"]').length === 0",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        await DropStencilAsync(page, "tempo-flowchart.decision", 0.08, 0.18);
        await DropStencilAsync(page, "tempo-erd.entity", 0.43, 0.18);
        await DropStencilAsync(page, "c4.container", 0.82, 0.18);
        await DropStencilAsync(page, "cloud.compute", 0.25, 0.72);
        await DropStencilAsync(page, "kubernetes.pod", 0.63, 0.72);

        await WaitForStencilCountAsync(page, "tempo-flowchart.decision", 1);
        await WaitForStencilCountAsync(page, "tempo-erd.entity", 1);
        await WaitForStencilCountAsync(page, "c4.container", 1);
        await WaitForStencilCountAsync(page, "cloud.compute", 1);
        await WaitForStencilCountAsync(page, "kubernetes.pod", 1);

        await AssertDroppedTextAsync(page, "tempo-flowchart.decision", "Decision");
        await AssertDroppedTextAsync(page, "tempo-erd.entity", "Entity");
        await AssertDroppedTextAsync(page, "c4.container", "Container");
        await AssertDroppedTextAsync(page, "cloud.compute", "Compute");
        await AssertDroppedTextAsync(page, "kubernetes.pod", "Pod");

        Assert.IsTrue(await CountAsync(page, "g.tm-diagram-node[data-stencil-id='tempo-flowchart.decision'] .tm-ext-marker-flow-decision") > 0, "Flowchart decision marker should render from the Tempo-original SVG.");
        Assert.IsTrue(await CountAsync(page, "g.tm-diagram-node[data-stencil-id='tempo-erd.entity'] .tm-ext-marker-erd-entity") > 0, "ERD entity marker should render from the Tempo-original SVG.");
        Assert.IsTrue(await CountAsync(page, "g.tm-diagram-node[data-stencil-id='c4.container'] .tm-ext-marker-container") > 0, "C4 container marker should render from the Tempo-original SVG.");
        Assert.IsTrue(await CountAsync(page, "g.tm-diagram-node[data-stencil-id='cloud.compute'] .tm-ext-marker-cloud-compute") > 0, "Cloud compute marker should render from generic Tempo iconography.");
        Assert.IsTrue(await CountAsync(page, "g.tm-diagram-node[data-stencil-id='kubernetes.pod'] .tm-ext-marker-k8s-pod") > 0, "Kubernetes-like pod marker should render without brand assets.");

        await HideRightPanelsAsync(page);
        await AssertNoOverlapAsync(page);

        await SaveStableScreenshotAsync(page);
        await TakeScreenshotAsync(page, "diagram_extended_phase8_libraries");
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

    private static async Task WaitForStencilCountAsync(IPage page, string stencilId, int minimumCount)
    {
        await page.WaitForFunctionAsync(
            """
            ({ stencilId, minimumCount }) => document.querySelectorAll(`.tm-diagram-node[data-stencil-id='${stencilId}']`).length >= minimumCount
            """,
            new { stencilId, minimumCount },
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    private static async Task AssertDroppedTextAsync(IPage page, string stencilId, string expectedText)
    {
        var node = page.Locator($".tm-diagram-node[data-stencil-id='{stencilId}']").Last;
        await node.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        var text = await node.TextContentAsync() ?? string.Empty;
        StringAssert.Contains(text, expectedText);
    }

    private static Task<int> CountAsync(IPage page, string selector)
        => page.EvaluateAsync<int>(
            "selector => document.querySelectorAll(selector).length",
            selector);

    private static async Task HideRightPanelsAsync(IPage page)
    {
        await TogglePanelIfVisibleAsync(page, ".tm-diagram-editor__inspector", "[data-testid='diagram-editor-toggle-inspector']");
        await TogglePanelIfVisibleAsync(page, ".tm-diagram-minimap", "[data-testid='diagram-editor-toggle-minimap']");
        await page.WaitForTimeoutAsync(300);
    }

    private static async Task TogglePanelIfVisibleAsync(IPage page, string panelSelector, string toggleSelector)
    {
        if (await CountAsync(page, panelSelector) == 0)
            return;

        var toggle = page.Locator(toggleSelector);
        if (await toggle.CountAsync() == 0)
            return;

        await toggle.First.ClickAsync();
    }

    private static async Task SaveStableScreenshotAsync(IPage page)
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "TestResults",
            "phase8-extended");
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        await File.WriteAllBytesAsync(Path.Combine(directory, "diagram_extended_phase8_libraries.png"), bytes);
    }

    private static async Task AssertNoOverlapAsync(IPage page)
    {
        var selectors = new[]
        {
            ".tm-diagram-node[data-stencil-id='tempo-flowchart.decision']",
            ".tm-diagram-node[data-stencil-id='tempo-erd.entity']",
            ".tm-diagram-node[data-stencil-id='c4.container']",
            ".tm-diagram-node[data-stencil-id='cloud.compute']",
            ".tm-diagram-node[data-stencil-id='kubernetes.pod']"
        };

        var boxes = new List<LocatorBoundingBoxResult>();
        foreach (var selector in selectors)
        {
            var box = await page.Locator(selector).Last.BoundingBoxAsync()
                ?? throw new AssertFailedException($"{selector} should have a measurable bounding box.");
            boxes.Add(box);
        }

        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                var overlapX = Math.Max(0, Math.Min(boxes[i].X + boxes[i].Width, boxes[j].X + boxes[j].Width) - Math.Max(boxes[i].X, boxes[j].X));
                var overlapY = Math.Max(0, Math.Min(boxes[i].Y + boxes[i].Height, boxes[j].Y + boxes[j].Height) - Math.Max(boxes[i].Y, boxes[j].Y));
                Assert.IsTrue(overlapX * overlapY < 1, "Extended phase 8 shapes should remain visually separated in the screenshot.");
            }
        }
    }
}
