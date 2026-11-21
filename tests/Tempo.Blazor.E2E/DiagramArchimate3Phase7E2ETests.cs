using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for phase 7 ArchiMate 3.2 stencils.</summary>
[TestClass]
[TestCategory("WASM")]
public class DiagramArchimate3Phase7E2ETests : PlaywrightTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";

    /// <inheritdoc />
    protected override string BaseUrl => "http://localhost:5010";

    [TestMethod]
    [Description("ArchiMate 3.2 stencils are searchable, droppable, and render a readable architecture view on the canvas")]
    public async Task Canvas_DropsArchimate3View_WithReadableRoleMarkers()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1600, 900);

        await GotoAppReadyAsync(page, $"{BaseUrl}{DiagramEditorUrl}");

        var toolbox = page.Locator(".tm-diagram-toolbox");
        await toolbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });

        var search = page.Locator(".tm-diagram-toolbox__search input");
        await search.FillAsync("ArchiMate 3.2 Business Actor");
        await page.WaitForTimeoutAsync(300);

        var archimateLibrary = page.Locator("[data-library-id='archimate3']");
        await archimateLibrary.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var actorStencil = page.Locator(".tm-diagram-toolbox__item[data-stencil-id='archimate3.business.actor']");
        await actorStencil.ScrollIntoViewIfNeededAsync();
        await actorStencil.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        Assert.AreEqual("node", await actorStencil.GetAttributeAsync("data-stencil-kind"));
        Assert.AreEqual("0", await actorStencil.GetAttributeAsync("tabindex"));
        Assert.AreEqual("button", await actorStencil.GetAttributeAsync("role"));

        await search.FillAsync(string.Empty);
        await page.WaitForTimeoutAsync(300);

        var canvas = page.Locator(".tm-diagram-canvas").First;
        await canvas.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New document" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-node[data-stencil-id=\"uml25.class\"]').length === 0",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        await DropStencilAsync(page, "archimate3.business.actor", 0.10, 0.20);
        await DropStencilAsync(page, "archimate3.application.component", 0.39, 0.20);
        await DropStencilAsync(page, "archimate3.technology.node", 0.70, 0.20);
        await DropStencilAsync(page, "archimate3.motivation.goal", 0.22, 0.66);
        await DropStencilAsync(page, "archimate3.strategy.capability", 0.58, 0.66);

        await WaitForStencilCountAsync(page, "archimate3.business.actor", 1);
        await WaitForStencilCountAsync(page, "archimate3.application.component", 1);
        await WaitForStencilCountAsync(page, "archimate3.technology.node", 1);
        await WaitForStencilCountAsync(page, "archimate3.motivation.goal", 1);
        await WaitForStencilCountAsync(page, "archimate3.strategy.capability", 1);

        await AssertDroppedTextAsync(page, "archimate3.business.actor", "Business Actor");
        await AssertDroppedTextAsync(page, "archimate3.application.component", "Application Component");
        await AssertDroppedTextAsync(page, "archimate3.technology.node", "Node");
        await AssertDroppedTextAsync(page, "archimate3.motivation.goal", "Goal");
        await AssertDroppedTextAsync(page, "archimate3.strategy.capability", "Capability");

        Assert.IsTrue(await CountAsync(page, "g.tm-diagram-node[data-stencil-id='archimate3.business.actor'] .tm-archimate3-marker-actor") > 0, "Business actor marker should render from the custom ArchiMate SVG.");
        Assert.IsTrue(await CountAsync(page, "g.tm-diagram-node[data-stencil-id='archimate3.application.component'] .tm-archimate3-marker-component") > 0, "Application component marker should render from the custom ArchiMate SVG.");
        Assert.IsTrue(await CountAsync(page, "g.tm-diagram-node[data-stencil-id='archimate3.technology.node'] .tm-archimate3-marker-node") > 0, "Technology node marker should render from the custom ArchiMate SVG.");
        Assert.IsTrue(await CountAsync(page, "g.tm-diagram-node[data-stencil-id='archimate3.motivation.goal'] .tm-archimate3-marker-goal") > 0, "Motivation goal marker should render from the custom ArchiMate SVG.");
        Assert.IsTrue(await CountAsync(page, "g.tm-diagram-node[data-stencil-id='archimate3.strategy.capability'] .tm-archimate3-marker-capability") > 0, "Strategy capability marker should render from the custom ArchiMate SVG.");

        await AssertNoOverlapAsync(page);

        await SaveStableScreenshotAsync(page);
        await TakeScreenshotAsync(page, "diagram_archimate3_phase7_view");
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

    private static async Task SaveStableScreenshotAsync(IPage page)
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "TestResults",
            "phase7-archimate3");
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        await File.WriteAllBytesAsync(Path.Combine(directory, "diagram_archimate3_phase7_view.png"), bytes);
    }

    private static async Task AssertNoOverlapAsync(IPage page)
    {
        var selectors = new[]
        {
            ".tm-diagram-node[data-stencil-id='archimate3.business.actor']",
            ".tm-diagram-node[data-stencil-id='archimate3.application.component']",
            ".tm-diagram-node[data-stencil-id='archimate3.technology.node']",
            ".tm-diagram-node[data-stencil-id='archimate3.motivation.goal']",
            ".tm-diagram-node[data-stencil-id='archimate3.strategy.capability']"
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
                Assert.IsTrue(overlapX * overlapY < 1, "ArchiMate view shapes should remain visually separated in the phase 7 screenshot.");
            }
        }
    }
}
