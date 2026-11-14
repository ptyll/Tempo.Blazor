using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E workflow and screenshot baselines for phase 11 diagram editor coverage.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class DiagramWorkflowPhase11E2ETests : PlaywrightTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";
    private const string DecisionStencilId = "tempo-flowchart.decision";
    private const string DependencyStencilId = "relationships.dependency";

    /// <inheritdoc />
    protected override string BaseUrl => "http://localhost:5010";

    [TestMethod]
    [Description("Diagram editor route exposes stable selectors for future E2E tests")]
    public async Task DiagramEditorRoute_RendersStableE2ESelectors()
    {
        var page = await CreateDiagramPageAsync();

        await page.GetByTestId("diagram-editor").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.GetByTestId("diagram-toolbox").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.GetByTestId("diagram-canvas").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
    }

    [TestMethod]
    [Description("Node stencils can be inserted from the toolbox with a stable screenshot artifact")]
    public async Task Toolbox_NodeStencil_DropsIntoCanvasAndCapturesScreenshot()
    {
        var page = await CreateDiagramPageAsync();
        await ResetDocumentAsync(page);
        await SearchToolboxAsync(page, "Decision");

        var stencil = page.Locator($".tm-diagram-toolbox__item[data-stencil-id='{DecisionStencilId}']").First;
        await stencil.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await DropStencilAsync(page, DecisionStencilId, 0.48, 0.45);
        await WaitForStencilCountAsync(page, DecisionStencilId, 1);
        await AssertNodeTextAsync(page, DecisionStencilId, "Decision");

        await SaveStableScreenshotAsync(page, "diagram_phase11_node_drop_light");
        TestContext.WriteLine("UX: node drop baseline keeps the searched toolbox item, inserted node, selection handles, and canvas grid visible without overlap.");
    }

    [TestMethod]
    [Description("Edge stencils can be activated from the toolbox and applied to newly drawn edges")]
    public async Task Toolbox_EdgeStencil_ActivatesAndStylesNewEdge()
    {
        var page = await CreateDiagramPageAsync();
        await LoadUmlSampleAsync(page);

        await SearchToolboxAsync(page, "Dependency");
        var dependency = page.Locator($".tm-diagram-toolbox__item[data-stencil-id='{DependencyStencilId}']").First;
        await dependency.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.AreEqual("edge", await dependency.GetAttributeAsync("data-stencil-kind"));

        var edgeCountBefore = await page.Locator(".tm-diagram-edge-group").CountAsync();
        await DropStencilAsync(page, DependencyStencilId, 0.32, 0.28);
        await DrawEdgeBetweenPortsAsync(page, "class1", "right", "class2", "left");

        await page.WaitForFunctionAsync(
            "count => document.querySelectorAll('.tm-diagram-edge-group').length > count",
            edgeCountBefore,
            new PageWaitForFunctionOptions { Timeout = 10000 });

        var hasDashedDependency = await page.EvaluateAsync<bool>(
            "() => Array.from(document.querySelectorAll('.tm-diagram-edge-path')).some(path => path.getAttribute('stroke-dasharray') === '5,5')");
        Assert.IsTrue(hasDashedDependency, "Dependency edge stencil should apply dashed stroke defaults to a newly drawn edge.");

        await SaveStableScreenshotAsync(page, "diagram_phase11_edge_dependency_light");
        TestContext.WriteLine("UX: edge stencil workflow leaves the dependency line visually distinct with dashed stroke and readable UML node labels.");
    }

    [TestMethod]
    [Description("UML, BPMN, and ArchiMate screenshots are captured with stable light-mode baselines")]
    public async Task DiagramScreenshots_CaptureUmlBpmnAndArchimateLightBaselines()
    {
        var page = await CreateDiagramPageAsync();

        await LoadUmlSampleAsync(page);
        await ClearSelectionAsync(page);
        await SaveStableScreenshotAsync(page, "diagram_phase11_uml_light");
        TestContext.WriteLine("UX UML: class labels, ports, and relationship line remain readable at the default desktop viewport.");

        await BuildBpmnBaselineAsync(page);
        await ClearSelectionAsync(page);
        await SaveStableScreenshotAsync(page, "diagram_phase11_bpmn_light");
        TestContext.WriteLine("UX BPMN: start event, user task, gateway, and end event are separated enough for quick scanning.");

        await BuildArchimateBaselineAsync(page);
        await ClearSelectionAsync(page);
        await SaveStableScreenshotAsync(page, "diagram_phase11_archimate_light");
        TestContext.WriteLine("UX ArchiMate: business, application, and technology colors are differentiated without overpowering the editor chrome.");
    }

    [TestMethod]
    [Description("Diagram editor captures light and dark mode screenshots without text fitting regressions")]
    public async Task DiagramScreenshots_CaptureLightAndDarkThemeBaselines()
    {
        var lightPage = await CreateDiagramPageAsync();
        await LoadUmlSampleAsync(lightPage);
        await ClearSelectionAsync(lightPage);
        await AssertNoTextOverflowAsync(lightPage);
        await AssertDiagramNodeTextContrastAsync(lightPage);
        await SaveStableScreenshotAsync(lightPage, "diagram_phase11_theme_light");
        TestContext.WriteLine("UX theme light: toolbar labels and toolbox text fit their containers with readable contrast.");

        var darkPage = await CreateDiagramPageAsync();
        await darkPage.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Switch to dark mode" }).ClickAsync();
        await darkPage.WaitForSelectorAsync("[data-theme='dark']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await AssertDarkShellContrastAsync(darkPage);
        await LoadUmlSampleAsync(darkPage);
        await ClearSelectionAsync(darkPage);
        await AssertNoTextOverflowAsync(darkPage);
        await AssertDiagramNodeTextContrastAsync(darkPage);
        await SaveStableScreenshotAsync(darkPage, "diagram_phase11_theme_dark");
        TestContext.WriteLine("UX theme dark: dark chrome is applied consistently and diagram labels remain legible against the canvas.");
    }

    private async Task<IPage> CreateDiagramPageAsync()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1600, 900);
        await page.GotoAsync($"{BaseUrl}{DiagramEditorUrl}");
        await WaitForAppReadyAsync(page);
        await WaitForDiagramReadyAsync(page);
        return page;
    }

    private static async Task WaitForDiagramReadyAsync(IPage page)
    {
        await page.GetByTestId("diagram-canvas").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 20000
        });

        await page.WaitForFunctionAsync(
            """
            () => {
                const canvas = document.querySelector('[data-testid="diagram-canvas"]');
                if (!canvas || !canvas.id) return false;
                const editor = window.tmDiagramEditor;
                return !!(editor && editor.instances && editor.instances.get(canvas.id));
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 20000 });
    }

    private static async Task ResetDocumentAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New document" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-node').length === 0 && document.querySelectorAll('.tm-diagram-edge-group').length === 0",
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    private static async Task LoadUmlSampleAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Load UML sample" }).ClickAsync();
        await page.Locator(".tm-diagram-node[data-node-id='class1']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        await page.Locator(".tm-diagram-node[data-node-id='class2']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-edge-group').length >= 1",
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    private static async Task SearchToolboxAsync(IPage page, string query)
    {
        var search = page.GetByLabel("Search diagram stencils");
        await search.FillAsync(query);
        await page.WaitForFunctionAsync(
            "value => document.querySelector('.tm-diagram-toolbox__search input')?.value === value",
            query,
            new PageWaitForFunctionOptions { Timeout = 5000 });
    }

    private static async Task DropStencilAsync(IPage page, string stencilId, double xRatio, double yRatio)
    {
        await page.EvaluateAsync(
            """
            ({ stencilId, xRatio, yRatio }) => {
                const canvas = document.querySelector('[data-testid="diagram-canvas"]');
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

    private static async Task DrawEdgeBetweenPortsAsync(IPage page, string sourceNodeId, string sourcePortId, string targetNodeId, string targetPortId)
    {
        var source = page.Locator($".tm-diagram-node[data-node-id='{sourceNodeId}'] .tm-diagram-port[data-port-id='{sourcePortId}']");
        var target = page.Locator($".tm-diagram-node[data-node-id='{targetNodeId}'] .tm-diagram-port[data-port-id='{targetPortId}']");
        await source.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        await target.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });

        var sourceBox = await source.BoundingBoxAsync() ?? throw new AssertFailedException("Source port should have a bounding box.");
        var targetBox = await target.BoundingBoxAsync() ?? throw new AssertFailedException("Target port should have a bounding box.");
        var sx = sourceBox.X + sourceBox.Width / 2;
        var sy = sourceBox.Y + sourceBox.Height / 2;
        var tx = targetBox.X + targetBox.Width / 2;
        var ty = targetBox.Y + targetBox.Height / 2;

        await page.Mouse.MoveAsync((float)sx, (float)sy);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)tx, (float)ty);
        await page.Mouse.UpAsync();
    }

    private static async Task BuildBpmnBaselineAsync(IPage page)
    {
        await ResetDocumentAsync(page);
        await DropStencilAsync(page, "bpmn2.event.start", 0.10, 0.44);
        await DropStencilAsync(page, "bpmn2.task.user", 0.28, 0.44);
        await DropStencilAsync(page, "bpmn2.gateway.exclusive", 0.60, 0.44);
        await DropStencilAsync(page, "bpmn2.event.end", 0.82, 0.44);
        await WaitForStencilCountAsync(page, "bpmn2.event.start", 1);
        await WaitForStencilCountAsync(page, "bpmn2.task.user", 1);
        await WaitForStencilCountAsync(page, "bpmn2.gateway.exclusive", 1);
        await WaitForStencilCountAsync(page, "bpmn2.event.end", 1);
        await ClearSelectionAsync(page);
        await AssertNodesDoNotOverlapAsync(
            page,
            "bpmn2.event.start",
            "bpmn2.task.user",
            "bpmn2.gateway.exclusive",
            "bpmn2.event.end");
    }

    private static async Task BuildArchimateBaselineAsync(IPage page)
    {
        await ResetDocumentAsync(page);
        await DropStencilAsync(page, "archimate3.business.actor", 0.16, 0.24);
        await DropStencilAsync(page, "archimate3.application.component", 0.56, 0.24);
        await DropStencilAsync(page, "archimate3.technology.node", 0.36, 0.64);
        await WaitForStencilCountAsync(page, "archimate3.business.actor", 1);
        await WaitForStencilCountAsync(page, "archimate3.application.component", 1);
        await WaitForStencilCountAsync(page, "archimate3.technology.node", 1);
        await AssertNodeTextAsync(page, "archimate3.business.actor", "Business Actor");
        await AssertNodeTextAsync(page, "archimate3.application.component", "Application Component");
        await AssertNodeTextAsync(page, "archimate3.technology.node", "Node");
        await ClearSelectionAsync(page);
        await AssertNodesDoNotOverlapAsync(
            page,
            "archimate3.business.actor",
            "archimate3.application.component",
            "archimate3.technology.node");
    }

    private static async Task WaitForStencilCountAsync(IPage page, string stencilId, int minimumCount)
    {
        await page.WaitForFunctionAsync(
            "args => document.querySelectorAll(`.tm-diagram-node[data-stencil-id='${args.stencilId}']`).length >= args.minimumCount",
            new { stencilId, minimumCount },
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    private static async Task AssertNodeTextAsync(IPage page, string stencilId, string expectedText)
    {
        var node = page.Locator($".tm-diagram-node[data-stencil-id='{stencilId}']").Last;
        await node.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        var text = await node.TextContentAsync() ?? string.Empty;
        StringAssert.Contains(text, expectedText);
    }

    private static async Task AssertNoTextOverflowAsync(IPage page)
    {
        var overflowing = await page.EvaluateAsync<string[]>(
            """
            () => Array.from(document.querySelectorAll('[data-testid="diagram-editor"] button, [data-testid="diagram-editor"] input, [data-testid="diagram-toolbox"] .tm-diagram-toolbox__label'))
                .filter(el => el.scrollWidth > el.clientWidth + 2)
                .map(el => el.textContent?.trim() || el.getAttribute('aria-label') || el.getAttribute('title') || el.tagName)
                .slice(0, 8)
            """);
        Assert.AreEqual(0, overflowing.Length, "Diagram editor text should fit without clipped controls: " + string.Join(", ", overflowing));
    }

    private static async Task AssertDarkShellContrastAsync(IPage page)
    {
        var rgb = await page.EvaluateAsync<int[]>(
            """
            () => {
                const shell = document.querySelector('[data-theme="dark"]');
                const color = getComputedStyle(shell).backgroundColor;
                const match = color.match(/\d+/g) || [];
                return match.slice(0, 3).map(value => parseInt(value, 10));
            }
            """);

        Assert.AreEqual(3, rgb.Length, "Dark shell background should resolve to an RGB color.");
        Assert.IsTrue(rgb.Average() < 90, $"Dark shell background should be dark enough, got rgb({string.Join(", ", rgb)}).");
    }

    private static async Task AssertDiagramNodeTextContrastAsync(IPage page)
    {
        // Plan 92e96b25 TG-004: the diagram canvas is themed (dark paper `--tm-canvas-paper`, light ink in the
        // dark theme; white paper + dark ink in the light one), so the contract is no longer "text is dark" but
        // "node text reads on the paper it sits on" - WCAG AA 4.5:1 against the resolved paper token.
        var colors = await page.EvaluateAsync<int[][]>(
            """
            () => {
                const text = document.querySelector(".tm-diagram-node[data-node-id='class1'] .tm-diagram-node__text")
                    || document.querySelector(".tm-diagram-node[data-node-id='class1'] .tm-diagram-node__list-item");
                const probe = document.createElement('span');
                probe.style.color = 'var(--tm-canvas-paper)';
                text.parentElement.appendChild(probe);
                const paper = getComputedStyle(probe).color;
                probe.remove();
                const parse = c => (c.match(/[\d.]+/g) || []).slice(0, 3).map(v => Math.round(parseFloat(v)));
                return [parse(getComputedStyle(text).color), parse(paper)];
            }
            """);

        Assert.AreEqual(3, colors[0].Length, "Diagram node text should resolve to an RGB color.");
        Assert.AreEqual(3, colors[1].Length, "The canvas paper token should resolve to an RGB color.");
        static double Lum(int[] c)
        {
            double Ch(int v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(c[0]) + 0.7152 * Ch(c[1]) + 0.0722 * Ch(c[2]);
        }
        var (hi, lo) = (Math.Max(Lum(colors[0]), Lum(colors[1])), Math.Min(Lum(colors[0]), Lum(colors[1])));
        var ratio = (hi + 0.05) / (lo + 0.05);
        Assert.IsTrue(ratio >= 4.5,
            $"Diagram node text rgb({string.Join(", ", colors[0])}) must read on the canvas paper rgb({string.Join(", ", colors[1])}): {ratio:0.00}:1.");
    }

    private static async Task ClearSelectionAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            () => {
                const canvas = document.querySelector('[data-testid="diagram-canvas"]');
                const instance = canvas && window.tmDiagramEditor?.instances?.get(canvas.id);
                if (!instance) {
                    return;
                }

                instance.selectedIds = new Set();
                window.tmDiagramEditor._updateSelection(instance);
                if (instance.dotNetRef) {
                    instance.dotNetRef.invokeMethodAsync('OnSelectionChanged', []);
                }
            }
            """);
        await page.WaitForTimeoutAsync(250);
    }

    private static async Task AssertNodesDoNotOverlapAsync(IPage page, params string[] stencilIds)
    {
        var boxes = await page.EvaluateAsync<NodeBox[]>(
            """
            stencilIds => stencilIds.map(stencilId => {
                const node = document.querySelector(`.tm-diagram-node[data-stencil-id='${stencilId}']`);
                if (!node) {
                    return { stencilId, exists: false, x: 0, y: 0, width: 0, height: 0 };
                }

                const visual = node.querySelector('.tm-diagram-node__shape') || node.querySelector('.tm-diagram-node__shape-bg') || node;
                const rect = visual.getBoundingClientRect();
                return { stencilId, exists: true, x: rect.left, y: rect.top, width: rect.width, height: rect.height };
            })
            """,
            stencilIds);

        foreach (var box in boxes)
        {
            Assert.IsTrue(box.Exists, $"{box.StencilId} should be rendered before overlap checks.");
        }

        for (var i = 0; i < boxes.Length; i++)
        {
            for (var j = i + 1; j < boxes.Length; j++)
            {
                var overlapX = Math.Max(0, Math.Min(boxes[i].X + boxes[i].Width, boxes[j].X + boxes[j].Width) - Math.Max(boxes[i].X, boxes[j].X));
                var overlapY = Math.Max(0, Math.Min(boxes[i].Y + boxes[i].Height, boxes[j].Y + boxes[j].Height) - Math.Max(boxes[i].Y, boxes[j].Y));
                Assert.IsTrue(overlapX * overlapY < 1, $"{boxes[i].StencilId} should not overlap {boxes[j].StencilId} in the screenshot baseline.");
            }
        }
    }

    public sealed class NodeBox
    {
        public string StencilId { get; set; } = string.Empty;

        public bool Exists { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }
    }

    private static async Task SaveStableScreenshotAsync(IPage page, string name)
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "TestResults",
            "phase11-e2e");
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });
        Assert.IsTrue(bytes.Length > 20_000, $"{name} screenshot should contain a rendered editor, not a blank page.");

        await File.WriteAllBytesAsync(Path.Combine(directory, $"{name}.png"), bytes);
    }
}
