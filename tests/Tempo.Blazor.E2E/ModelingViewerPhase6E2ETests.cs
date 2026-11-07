using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan eadbf967 phase 6 E2E for the read-only viewer demo (<c>/modeling-viewer</c>): the version
/// diff, the heatmap, element click (canvas and the keyboard list), mobile fit-to-width, keyboard
/// navigation and the empty-model edge case. The durable screenshots live in
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/modeling-viewer/</c> and are written by
/// <see cref="ModelingViewerPhase6Screenshots"/>.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingViewerPhase6E2ETests : WasmTestBase
{
    private const string ViewerUrl = "/modeling-viewer";
    private const string ViewerSelector = "[data-testid='modeling-viewer-demo']";

    [TestMethod]
    [Description("Desktop: the diff scenario renders the legend counts, state hooks and a named selection")]
    public async Task DiffScenario_RendersLegendStatesAndSelection()
    {
        var page = await OpenViewerAsync(1440, 900, "?scenario=diff");

        var viewer = page.Locator(ViewerSelector);
        Assert.AreEqual("diff", await viewer.GetAttributeAsync("data-overlay"));
        Assert.IsTrue(await ReadIntAsync(page, ViewerSelector, "data-node-count") >= 5);

        var legend = page.Locator("[data-testid='modeling-viewer-diff-legend']");
        await legend.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        Assert.AreEqual("2", await legend.Locator("[data-state='diff-added']").GetAttributeAsync("data-count"));
        Assert.AreEqual("2", await legend.Locator("[data-state='diff-removed']").GetAttributeAsync("data-count"));
        Assert.AreEqual("1", await legend.Locator("[data-state='diff-changed']").GetAttributeAsync("data-count"));
        Assert.AreEqual("1", await legend.Locator("[data-state='diff-layout']").GetAttributeAsync("data-count"));

        Assert.AreEqual(1, await page.Locator(".tm-diagram-node[data-viewer-state='diff-added']").CountAsync(),
            "the added element is marked on the canvas through the public data hook");

        // Non-colour cues: one badge per marked node, and the legend strip is docked BELOW the canvas
        // instead of floating over the diagram it explains.
        Assert.AreEqual(3, await page.Locator(".tm-diagram-node__viewer-badge").CountAsync(),
            "added, changed and layout-only each carry their own glyph");
        var canvasBox = await page.Locator("[data-testid='modeling-viewer-canvas']").BoundingBoxAsync();
        var legendBox = await legend.BoundingBoxAsync();
        Assert.IsNotNull(canvasBox);
        Assert.IsNotNull(legendBox);
        Assert.IsTrue(
            legendBox!.Y >= canvasBox!.Y + canvasBox.Height - 1,
            $"the diff legend ({legendBox.Y}) must sit below the canvas ({canvasBox.Y + canvasBox.Height})");

        // The removed element is not drawn but stays named in the text alternative.
        Assert.AreEqual(0, await page.Locator(".tm-diagram-node[data-model-element-id='e-end']").CountAsync(),
            "a removed element is never drawn");
        var removed = page.Locator("[data-testid='modeling-viewer-element-list'] li[data-removed='true']");
        Assert.AreEqual(2, await removed.CountAsync(), "one removed element and one removed relationship are listed");
        StringAssert.Contains(await removed.First.InnerTextAsync(), "Order completed");

        // The accessible row click drives the two-way selection and the host's detail panel.
        await page.Locator("[data-testid='modeling-viewer-element-list'] [data-element-id='e-check']")
            .ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        await WaitForTextAsync(page, "[data-testid='modeling-viewer-demo-selection']", "Check availability");
        StringAssert.Contains(await page.Locator("[data-testid='modeling-viewer-demo-selection-meta']").InnerTextAsync(), "User task");
    }

    [TestMethod]
    [Description("Desktop: a canvas node click maps the diagram id back to the model id and raises OnElementClick")]
    public async Task CanvasNodeClick_SelectsTheModelElement()
    {
        var page = await OpenViewerAsync(1440, 900, "?scenario=diff");
        var node = page.Locator(".tm-diagram-node[data-model-element-id='e-check']");
        await node.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await node.ClickAsync(new LocatorClickOptions { Force = true, Timeout = 15000 });

        await WaitForTextAsync(page, "[data-testid='modeling-viewer-demo-selection']", "Check availability");
        StringAssert.Contains(await page.Locator("[data-testid='modeling-viewer-demo-selection-meta']").InnerTextAsync(), "User task");
    }

    [TestMethod]
    [Description("Desktop: the heatmap scenario tints the canvas and carries the value as text (legend + list + tooltip)")]
    public async Task HeatmapScenario_CarriesTheValueAsText()
    {
        var page = await OpenViewerAsync(1440, 900, "?scenario=heatmap");

        var viewer = page.Locator(ViewerSelector);
        Assert.AreEqual("heatmap", await viewer.GetAttributeAsync("data-overlay"));
        Assert.IsTrue(await page.Locator(".tm-diagram-node[data-viewer-state='heatmap']").CountAsync() >= 1);

        var legend = page.Locator("[data-testid='modeling-viewer-heatmap-legend']");
        await legend.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        var legendText = await legend.InnerTextAsync();
        StringAssert.Contains(legendText, "SLA risk");
        StringAssert.Contains(legendText, "Low");
        StringAssert.Contains(legendText, "High");

        var shipRow = page.Locator("[data-testid='modeling-viewer-element-list'] [data-element-id='e-ship']");
        StringAssert.Contains(await shipRow.InnerTextAsync(), "90");
        StringAssert.Contains(await shipRow.GetAttributeAsync("title") ?? string.Empty, "SLA risk");

        await shipRow.ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        await WaitForTextAsync(page, "[data-testid='modeling-viewer-demo-selection-heat']", "SLA risk");
        StringAssert.Contains(await page.Locator("[data-testid='modeling-viewer-demo-selection-heat']").InnerTextAsync(), "90");
    }

    [TestMethod]
    [Description("Export: both themes produce an SVG through the real JS token bridge")]
    public async Task ExportButtons_ProduceSvgForBothThemes()
    {
        var page = await OpenViewerAsync(1440, 900, "?scenario=heatmap");

        await page.Locator("[data-testid='modeling-viewer-export-light']")
            .ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        await WaitForTextAsync(page, "[data-testid='modeling-viewer-export-info']", "characters of SVG");
        StringAssert.Contains(
            await page.Locator("[data-testid='modeling-viewer-export-info']").InnerTextAsync(),
            "Light");

        await page.Locator("[data-testid='modeling-viewer-export-dark']")
            .ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => (document.querySelector(\"[data-testid='modeling-viewer-export-info']\")?.textContent || '').includes('Dark')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    [TestMethod]
    [Description("Mobile 390 touch: the list is visible, the canvas fits the width and the page does not overflow")]
    public async Task Mobile390_ListIsVisibleAndCanvasFitsTheWidth()
    {
        var page = await OpenViewerAsync(390, 844, "?scenario=heatmap", touch: true);

        await page.Locator($"{ViewerSelector}[data-layout='mobile']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });
        var viewer = page.Locator(ViewerSelector);
        Assert.AreEqual("width", await viewer.GetAttributeAsync("data-fit-mode"));
        Assert.AreEqual("true", await page.Locator("[data-testid='modeling-viewer-element-list']").GetAttributeAsync("data-mobile"));
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-viewer-element-list-toggle']").CountAsync(),
            "the mobile band shows the text alternative without a collapse toggle");

        var hasHorizontalOverflow = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.IsFalse(hasHorizontalOverflow, "the mobile viewer must fit the viewport width");

        var canvasWidth = await viewer.Locator("[data-testid='modeling-viewer-canvas']").EvaluateAsync<double>(
            "element => element.getBoundingClientRect().width");
        var viewportWidth = await page.EvaluateAsync<double>("() => window.innerWidth");
        Assert.IsTrue(canvasWidth <= viewportWidth, $"the canvas ({canvasWidth}) must fit the viewport ({viewportWidth})");
    }

    [TestMethod]
    [Description("Keyboard: arrows move focus through the element list and Enter selects the focused element")]
    public async Task KeyboardNavigation_MovesFocusAndSelects()
    {
        var page = await OpenViewerAsync(1440, 900, "?scenario=heatmap");

        var first = page.Locator("[data-testid='modeling-viewer-element-list'] [data-element-id]").First;
        await first.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("ArrowDown");
        var focusedId = await page.EvaluateAsync<string?>(
            "() => document.activeElement?.getAttribute('data-element-id')");
        Assert.AreEqual("e-check", focusedId, "ArrowDown walks the topological list (start → relationship → check)");

        await page.Keyboard.PressAsync("Enter");
        await WaitForTextAsync(page, "[data-testid='modeling-viewer-demo-selection']", "Check stock");
    }

    [TestMethod]
    [Description("Empty model: the empty state replaces the canvas and the page stays intact")]
    public async Task EmptyModel_RendersTheEmptyState()
    {
        var page = await OpenViewerAsync(1440, 900, "?scenario=empty");

        var viewer = page.Locator(ViewerSelector);
        Assert.AreEqual("empty", await viewer.GetAttributeAsync("data-state"));
        await page.Locator("[data-testid='modeling-viewer-empty']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-viewer-canvas']").CountAsync(),
            "an empty model renders no canvas");
        Assert.AreEqual(0, await ReadIntAsync(page, ViewerSelector, "data-node-count"));
    }

    [TestMethod]
    [Description("Desktop: flipping the host's Fit select re-fits the diagram instead of only repainting the attribute")]
    public async Task FitModeSelect_ReFitsTheDiagramAtRuntime()
    {
        var page = await OpenViewerAsync(1440, 900, "?scenario=diff");

        var viewer = page.Locator(ViewerSelector);
        Assert.AreEqual("width", await viewer.GetAttributeAsync("data-fit-mode"));

        var widthZoom = await ReadZoomLabelAsync(page);
        Assert.IsFalse(string.IsNullOrWhiteSpace(widthZoom), "the fitted zoom must be visible before the flip");

        await page.Locator("[data-testid='modeling-viewer-fit-mode']")
            .SelectOptionAsync("page", new LocatorSelectOptionOptions { Timeout = 15000 });

        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='modeling-viewer-demo']\")?.getAttribute('data-fit-mode') === 'page'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        await page.WaitForFunctionAsync(
            """
            previous => {
                const label = document.querySelector('.tm-diagram-editor__zoom-label');
                return !!label && (label.textContent || '').trim() !== previous;
            }
            """,
            widthZoom,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        Assert.AreNotEqual(widthZoom, await ReadZoomLabelAsync(page),
            "a page fit scales both axes, so the zoom reading has to change");
    }

    private static async Task<string> ReadZoomLabelAsync(IPage page)
    {
        var label = page.Locator(".tm-diagram-editor__zoom-label").First;
        await label.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 15000 });
        return (await label.TextContentAsync() ?? string.Empty).Trim();
    }

    private async Task<IPage> OpenViewerAsync(int width, int height, string query, bool touch = false)
    {
        var context = touch
            ? await Browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
                HasTouch = true,
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
            })
            : await CreateContextAsync();
        RegisterContext(context);
        await context.AddInitScriptAsync(
            """
            localStorage.setItem('tm-demo-culture', 'en');
            localStorage.removeItem('tm-demo-theme');
            """);

        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}{ViewerUrl}{query}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 90000,
        });
        await WaitForAppReadyAsync(page);
        await page.Locator(ViewerSelector)
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='modeling-viewer-demo']\")?.getAttribute('data-state') === 'populated'"
            + " || document.querySelector(\"[data-testid='modeling-viewer-demo']\")?.getAttribute('data-state') === 'empty'",
            null,
            new PageWaitForFunctionOptions { Timeout = 30000 });
        return page;
    }

    private static async Task WaitForTextAsync(IPage page, string selector, string expected)
    {
        await page.WaitForFunctionAsync(
            """
            ([selector, expected]) => {
                const element = document.querySelector(selector);
                return !!element && (element.textContent || '').includes(expected);
            }
            """,
            new[] { selector, expected },
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    private static async Task<int> ReadIntAsync(IPage page, string selector, string attributeName)
    {
        var value = await page.Locator(selector).GetAttributeAsync(attributeName);
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }
}
