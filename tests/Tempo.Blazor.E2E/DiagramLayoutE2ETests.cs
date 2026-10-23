using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests for diagram editor layout algorithms.
/// Verifies that all layout options correctly reposition nodes without NaN/invalid coordinates.
/// </summary>
[TestClass]
public class DiagramLayoutE2ETests : WasmTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";

    private async Task<IPage> PrepareDiagramPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.AddInitScriptAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
        // The cold-boot recovery (one controlled reload) lives in PlaywrightTestBase.GotoAppReadyAsync.
        await GotoAppReadyAsync(page, $"{BaseUrl}{DiagramEditorUrl}", WaitUntilState.DOMContentLoaded);
        await WaitForDiagramEditorReadyAsync(page);

        await page.WaitForTimeoutAsync(2000);

        // Load UML sample (creates multiple nodes locally – no HTTP wait needed)
        await page.GetByRole(AriaRole.Button, new() { Name = "Load UML sample" }).ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Ensure nodes rendered
        await page.WaitForSelectorAsync(".tm-diagram-node", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await page.WaitForTimeoutAsync(300);

        return page;
    }

    private static async Task WaitForDiagramEditorReadyAsync(IPage page)
    {
        await page.WaitForSelectorAsync(".tm-diagram-editor__toolbar", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30_000
        });
        await page.WaitForSelectorAsync(".tm-diagram-canvas", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30_000
        });
        await page.WaitForFunctionAsync("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas || !canvas.id) return false;
                const editor = window.tmDiagramEditor;
                return !!(editor && editor.instances && editor.instances.get(canvas.id));
            }
        """, null, new PageWaitForFunctionOptions { Timeout = 30_000 });
    }


    private async Task SelectAllNodesAsync(IPage page)
    {
        // Use JS to reliably select all nodes and notify .NET
        await page.EvaluateAsync("""
            () => {
                const container = document.querySelector('.tm-diagram-canvas');
                if (!container) return;
                const inst = tmDiagramEditor.instances.get(container.id);
                if (!inst) return;
                const ids = Array.from(document.querySelectorAll('.tm-diagram-node'))
                    .map(n => n.getAttribute('data-node-id'))
                    .filter(id => id);
                inst.selectedIds = new Set(ids);
                tmDiagramEditor._updateSelection(inst);
                if (inst.dotNetRef) {
                    inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', ids);
                }
            }
        """);
        await page.WaitForTimeoutAsync(1000);
    }

    /// <summary>
    /// Phase 4 (UX/code review): the command row is a core TmToolbar — a collapsed group's
    /// trigger is inert and the commands live in the "More" menu as plain entries (not a nested
    /// dropdown). Open whichever surface is currently on screen and run the layout command.
    /// </summary>
    private async Task RunLayoutCommandAsync(IPage page, string optionName)
    {
        var trigger = page.Locator(".tm-diagram-editor__toolbar .tm-dropdown-trigger")
            .Filter(new() { HasTextRegex = new Regex("Layout|Rozložit") });
        if (await trigger.CountAsync() > 0 && await trigger.IsVisibleAsync() && await trigger.IsEnabledAsync())
        {
            await trigger.ClickAsync();
            await page.WaitForTimeoutAsync(200);
            await ClickLayoutOptionAsync(page, optionName);
            return;
        }

        await page.Locator(".tm-toolbar-more").First.ClickAsync();
        await page.WaitForTimeoutAsync(200);
        await page.Locator(".tm-toolbar-more__item")
            .Filter(new() { HasTextRegex = new Regex($"^{Regex.Escape(optionName)}$") }).First.ClickAsync();
        await page.WaitForTimeoutAsync(2000); // let layout + render settle
    }

    private async Task ClickLayoutOptionAsync(IPage page, string optionName)
    {
        var option = page.Locator(".tm-dropdown-item")
            .Filter(new() { HasTextRegex = new Regex($"^{Regex.Escape(optionName)}$") });
        await option.ClickAsync();
        await page.WaitForTimeoutAsync(2000); // let layout + render settle
    }

    private async Task<List<(string Id, double X, double Y)>> GetNodeLayoutPositionsAsync(IPage page)
    {
        // After F3.A the canvas does viewport culling (`IsNodeVisible`) which
        // can hide nodes that a layout pushed beyond the current viewBox. Call
        // fit-to-view before reading positions so every layout result is
        // inside the rendered `g.tm-diagram-node` set.
        await page.EvaluateAsync("""
            () => {
                const c = document.querySelector('.tm-diagram-canvas');
                if (c && window.tmDiagramEditor && typeof tmDiagramEditor.fitToView === 'function') {
                    try { tmDiagramEditor.fitToView(c, 40); } catch (_e) {}
                }
            }
        """);
        await page.WaitForTimeoutAsync(300);

        // After F3.A (planning/DIAGRAM_UNIFIED_SVG_PLAN.md) node position lives
        // on the per-node SVG <g>'s `transform` attribute (translate(x,y) rotate(θ cx cy))
        // — no more CSS `style.transform` with `px` units.
        var json = await page.EvaluateAsync<string>("""
            () => {
                const nodes = document.querySelectorAll('g.tm-diagram-node[data-node-id]');
                const arr = [];
                nodes.forEach(n => {
                    const t = n.getAttribute('transform') || '';
                    const m = t.match(/translate\(\s*([-+]?[0-9]*\.?[0-9]+)\s*,\s*([-+]?[0-9]*\.?[0-9]+)\s*\)/);
                    if (m) {
                        arr.push({ id: n.getAttribute('data-node-id'), x: parseFloat(m[1]), y: parseFloat(m[2]) });
                    } else {
                        arr.push({ id: n.getAttribute('data-node-id'), x: 0, y: 0 });
                    }
                });
                return JSON.stringify(arr);
            }
        """);
        Assert.IsNotNull(json);
        using var doc = JsonDocument.Parse(json);
        var list = new List<(string Id, double X, double Y)>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            list.Add((item.GetProperty("id").GetString()!, item.GetProperty("x").GetDouble(), item.GetProperty("y").GetDouble()));
        }
        return list;
    }

    private static void AssertAllNodesFinite(List<(string Id, double X, double Y)> positions)
    {
        Assert.IsTrue(positions.Count > 0, "Expected at least one node");
        foreach (var (id, x, y) in positions)
        {
            Assert.IsTrue(double.IsFinite(x), $"Node {id} X must be finite, got {x}");
            Assert.IsTrue(double.IsFinite(y), $"Node {id} Y must be finite, got {y}");
        }
    }

    private static void AssertNodesSpreadOut(List<(string Id, double X, double Y)> positions)
    {
        var xs = positions.Select(p => p.X).Distinct().OrderBy(v => v).ToList();
        var ys = positions.Select(p => p.Y).Distinct().OrderBy(v => v).ToList();
        Assert.IsTrue(xs.Count > 1 || ys.Count > 1, "Layout should spread nodes out; expected more than one distinct X or Y coordinate");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Dagre layouts
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task DagreTbLayout_SpreadsNodesAndIsFinite()
    {
        var page = await PrepareDiagramPageAsync();
        await SelectAllNodesAsync(page);
        await RunLayoutCommandAsync(page, "Hierarchical Top-Down");

        var positions = await GetNodeLayoutPositionsAsync(page);
        TestContext.WriteLine($"Nodes after layout: {string.Join(", ", positions.Select(p => $"{p.Id}=({p.X:F1},{p.Y:F1})"))}");
        AssertAllNodesFinite(positions);
        AssertNodesSpreadOut(positions);
    }

    [TestMethod]
    public async Task DagreLrLayout_SpreadsNodesAndIsFinite()
    {
        var page = await PrepareDiagramPageAsync();
        await SelectAllNodesAsync(page);
        await RunLayoutCommandAsync(page, "Hierarchical Left-Right");

        var positions = await GetNodeLayoutPositionsAsync(page);
        TestContext.WriteLine($"Nodes after layout: {string.Join(", ", positions.Select(p => $"{p.Id}=({p.X:F1},{p.Y:F1})"))}");
        AssertAllNodesFinite(positions);
        AssertNodesSpreadOut(positions);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Tree layouts
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task TreeTbLayout_SpreadsNodesAndIsFinite()
    {
        var page = await PrepareDiagramPageAsync();
        await SelectAllNodesAsync(page);
        await RunLayoutCommandAsync(page, "Tree");

        var positions = await GetNodeLayoutPositionsAsync(page);
        TestContext.WriteLine($"Nodes after layout: {string.Join(", ", positions.Select(p => $"{p.Id}=({p.X:F1},{p.Y:F1})"))}");
        AssertAllNodesFinite(positions);
        AssertNodesSpreadOut(positions);
    }

    [TestMethod]
    public async Task TreeLrLayout_SpreadsNodesAndIsFinite()
    {
        var page = await PrepareDiagramPageAsync();
        await SelectAllNodesAsync(page);
        await RunLayoutCommandAsync(page, "Tree Left-Right");

        var positions = await GetNodeLayoutPositionsAsync(page);
        TestContext.WriteLine($"Nodes after layout: {string.Join(", ", positions.Select(p => $"{p.Id}=({p.X:F1},{p.Y:F1})"))}");
        AssertAllNodesFinite(positions);
        AssertNodesSpreadOut(positions);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Force layout
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ForceLayout_SpreadsNodesAndIsFinite()
    {
        var page = await PrepareDiagramPageAsync();
        await SelectAllNodesAsync(page);
        await RunLayoutCommandAsync(page, "Force");

        var positions = await GetNodeLayoutPositionsAsync(page);
        TestContext.WriteLine($"Nodes after layout: {string.Join(", ", positions.Select(p => $"{p.Id}=({p.X:F1},{p.Y:F1})"))}");
        AssertAllNodesFinite(positions);
        AssertNodesSpreadOut(positions);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Circle layout
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task CircleLayout_SpreadsNodesAndIsFinite()
    {
        var page = await PrepareDiagramPageAsync();
        await SelectAllNodesAsync(page);
        await RunLayoutCommandAsync(page, "Circle");

        var positions = await GetNodeLayoutPositionsAsync(page);
        TestContext.WriteLine($"Nodes after layout: {string.Join(", ", positions.Select(p => $"{p.Id}=({p.X:F1},{p.Y:F1})"))}");
        AssertAllNodesFinite(positions);
        AssertNodesSpreadOut(positions);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Grid layout (the one reported broken in the video)
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task GridLayout_ArrangesNodesInGrid()
    {
        var page = await PrepareDiagramPageAsync();
        await SelectAllNodesAsync(page);
        await RunLayoutCommandAsync(page, "Grid");

        var positions = await GetNodeLayoutPositionsAsync(page);
        TestContext.WriteLine($"Nodes after layout: {string.Join(", ", positions.Select(p => $"{p.Id}=({p.X:F1},{p.Y:F1})"))}");
        AssertAllNodesFinite(positions);
        AssertNodesSpreadOut(positions);

        // Grid-specific assertion: nodes should align in rows and columns
        // meaning multiple nodes share the same X (different rows) or same Y (different columns)
        var xs = positions.Select(p => Math.Round(p.X, 1)).ToList();
        var ys = positions.Select(p => Math.Round(p.Y, 1)).ToList();

        var distinctX = xs.Distinct().Count();
        var distinctY = ys.Distinct().Count();

        // For N nodes we expect roughly sqrt(N) columns and rows.
        // We just assert that there is more than one distinct X AND more than one distinct Y
        // when there are enough nodes.
        if (positions.Count >= 4)
        {
            Assert.IsTrue(distinctX > 1, $"Grid layout should produce multiple columns, got {distinctX} distinct X values");
            Assert.IsTrue(distinctY > 1, $"Grid layout should produce multiple rows, got {distinctY} distinct Y values");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Align / Distribute (toolbar + context menu wiring)
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task AlignLeft_AlinesNodesToSameX()
    {
        var page = await PrepareDiagramPageAsync();
        await SelectAllNodesAsync(page);

        // Open Arrange (on the bar, or from the toolbar "More" menu once the group collapsed)
        // and click Align Left.
        await RunArrangeCommandAsync(page, "Align Left", "Zarovnat vlevo");
        await page.WaitForTimeoutAsync(1000);

        var positions = await GetNodeLayoutPositionsAsync(page);
        TestContext.WriteLine($"Nodes after align left: {string.Join(", ", positions.Select(p => $"{p.Id}=({p.X:F1},{p.Y:F1})"))}");
        AssertAllNodesFinite(positions);

        var xs = positions.Select(p => p.X).Distinct().ToList();
        Assert.AreEqual(1, xs.Count, "Align Left should make all nodes share the same X coordinate");
    }

    [TestMethod]
    public async Task DistributeHorizontal_SpreadsNodesEvenly()
    {
        var page = await PrepareDiagramPageAsync();
        await SelectAllNodesAsync(page);

        // Open Arrange (on the bar, or from the toolbar "More" menu once the group collapsed)
        // and click Distribute Horizontally.
        await RunArrangeCommandAsync(page, "Horizontally", "Vodorovně");
        await page.WaitForTimeoutAsync(1000);

        var positions = await GetNodeLayoutPositionsAsync(page);
        TestContext.WriteLine($"Nodes after distribute horizontal: {string.Join(", ", positions.Select(p => $"{p.Id}=({p.X:F1},{p.Y:F1})"))}");
        AssertAllNodesFinite(positions);

        // With 2 nodes they should end up at different X values
        var xs = positions.Select(p => p.X).Distinct().OrderBy(x => x).ToList();
        Assert.IsTrue(xs.Count > 1, "Distribute Horizontal should spread nodes across different X coordinates");
    }

    /// <summary>
    /// Phase 4: the command row is a core TmToolbar — a collapsed group's trigger is inert and
    /// the commands live in the "More" menu (the arrange group offers the same commands as a
    /// labelled menu section). Open whichever surface is currently on screen and run the command.
    /// </summary>
    private static async Task RunArrangeCommandAsync(IPage page, string optionEn, string optionCs)
    {
        var pattern = new Regex($"{Regex.Escape(optionEn)}|{Regex.Escape(optionCs)}");

        var trigger = page.Locator(".tm-diagram-arrange-dropdown .tm-dropdown-trigger");
        if (await trigger.CountAsync() > 0 && await trigger.IsVisibleAsync() && await trigger.IsEnabledAsync())
        {
            await trigger.ClickAsync();
            await page.WaitForTimeoutAsync(200);
            await page.Locator(".tm-dropdown-item").Filter(new() { HasTextRegex = pattern }).First.ClickAsync();
            return;
        }

        await page.Locator(".tm-toolbar-more").First.ClickAsync();
        await page.WaitForTimeoutAsync(200);
        await page.Locator(".tm-toolbar-more__item").Filter(new() { HasTextRegex = pattern }).First.ClickAsync();
    }
}

