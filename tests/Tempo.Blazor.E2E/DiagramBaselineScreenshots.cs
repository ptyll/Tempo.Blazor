using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Generator pre-refactor baseline screenshotů pro diagram editor
/// (Fáze 0.7 v <c>planning/DIAGRAM_UNIFIED_SVG_PLAN.md</c>).
///
/// <para>
/// Dědí <see cref="BaselineGeneratorTestBase"/>, takže bez <c>TM_WRITE_BASELINES</c> se přeskočí —
/// kategorie <c>BaselineGeneration</c> sama nikdy nikoho nevypnula, jen umožňovala filtr. Spouští
/// se ručně po zvednutí WASM dema (<c>https://localhost:7106</c>):
/// </para>
///
/// <code>
/// TM_WRITE_BASELINES=1 dotnet test tests/Tempo.Blazor.E2E --filter "TestCategory=BaselineGeneration"
/// </code>
///
/// <para>
/// Pořadí kroků je záměrné: od bohatě naplněného UML sample postupně odstraňujeme stav až
/// k prázdnému dokumentu. Tím se vyhneme křehkému Insert-dropdown flow po
/// <c>New document</c> (v empty stavu dropdown nepřidal node v rámci timeoutu), a všechny
/// mutace jedou přes stabilní <c>[JSInvokable]</c> entry-pointy
/// (<c>OnSelectionChanged</c>, <c>OnRotateEnded</c>, <c>OnDeleteSelected</c>).
/// </para>
/// </summary>
[TestClass]
public class DiagramBaselineScreenshots : BaselineGeneratorTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";

    /// <summary>
    /// Absolutní cesta k <c>artifacts/baseline/diagram/</c> spočítaná
    /// z <see cref="AppContext.BaseDirectory"/> (typicky
    /// <c>tests/Tempo.Blazor.E2E/bin/Debug/net10.0/</c>).
    /// </summary>
    private static string BaselineDir
    {
        get
        {
            var dir = BaselineOutput.DirectoryFor(null, "diagram");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private async Task<IPage> OpenDiagramEditorAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.AddInitScriptAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
        await page.GotoAsync($"{BaseUrl}{DiagramEditorUrl}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });
        await WaitForAppReadyAsync(page);

        await page.WaitForSelectorAsync(".tm-diagram-canvas", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });
        await page.WaitForFunctionAsync("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas || !canvas.id) return false;
                const ed = window.tmDiagramEditor;
                return !!(ed && ed.instances && ed.instances.get(canvas.id));
            }
        """, null, new PageWaitForFunctionOptions { Timeout = 15000 });

        // Wait for the UML sample (demo default) to have rendered at least one node.
        await page.WaitForSelectorAsync(".tm-diagram-node", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await page.WaitForTimeoutAsync(400);

        return page;
    }

    private static async Task ClickDemoButtonAsync(IPage page, string name)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = name }).ClickAsync();
        await page.WaitForTimeoutAsync(600);
    }

    private static async Task CaptureCanvasAsync(IPage page, string fileName)
    {
        var canvas = page.Locator(".tm-diagram-canvas");
        await canvas.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await page.WaitForTimeoutAsync(400);

        var path = Path.Combine(BaselineDir, fileName);
        await canvas.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            OmitBackground = false
        });
        Console.WriteLine($"[baseline] wrote {path}");
    }

    private static async Task<string[]> GetAllNodeIdsAsync(IPage page)
    {
        return await page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('.tm-diagram-node'))
                .map(n => n.getAttribute('data-node-id'))
                .filter(id => id)
        """);
    }

    private static async Task SelectNodeByIdAsync(IPage page, string nodeId)
    {
        await page.EvaluateAsync("""
            (id) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas) return;
                const inst = window.tmDiagramEditor.instances.get(canvas.id);
                if (!inst) return;
                inst.selectedIds = new Set([id]);
                window.tmDiagramEditor._updateSelection(inst);
                if (inst.dotNetRef) {
                    inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [id]);
                }
            }
        """, nodeId);
        await page.WaitForTimeoutAsync(500);
    }

    private static async Task RotateNodeAsync(IPage page, string nodeId, double angleDeg)
    {
        await page.EvaluateAsync("""
            async (args) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas) return;
                const inst = window.tmDiagramEditor.instances.get(canvas.id);
                if (!inst || !inst.dotNetRef) return;
                await inst.dotNetRef.invokeMethodAsync('OnRotateEnded', args.id, args.angle);
            }
        """, new { id = nodeId, angle = angleDeg });
        await page.WaitForTimeoutAsync(600);
    }

    private static async Task DeleteNodesAsync(IPage page, string[] ids)
    {
        if (ids.Length == 0) return;
        await page.EvaluateAsync("""
            async (ids) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas) return;
                const inst = window.tmDiagramEditor.instances.get(canvas.id);
                if (!inst || !inst.dotNetRef) return;
                await inst.dotNetRef.invokeMethodAsync('OnDeleteSelected', ids);
            }
        """, ids);
        await page.WaitForTimeoutAsync(700);
    }

    private static async Task ClearSelectionAsync(IPage page)
    {
        await page.EvaluateAsync("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas) return;
                const inst = window.tmDiagramEditor.instances.get(canvas.id);
                if (!inst) return;
                inst.selectedIds = new Set();
                window.tmDiagramEditor._updateSelection(inst);
                if (inst.dotNetRef) {
                    inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', []);
                }
            }
        """);
        await page.WaitForTimeoutAsync(300);
    }

    [TestMethod]
    public async Task GenerateAllBaselines()
    {
        var page = await OpenDiagramEditorAsync();

        // ── 03 Multi-node UML sample ────────────────────────────────────────
        await ClearSelectionAsync(page);
        await CaptureCanvasAsync(page, "baseline-03-sample-with-edges.png");

        // ── 04 Selected first node (handles visible) ────────────────────────
        var ids = await GetAllNodeIdsAsync(page);
        Assert.IsTrue(ids.Length >= 2, $"UML sample should have >=2 nodes (got {ids.Length})");
        var firstId = ids[0];

        await SelectNodeByIdAsync(page, firstId);
        await CaptureCanvasAsync(page, "baseline-04-selected-node.png");

        // ── 05 Rotated 45° (still selected) ─────────────────────────────────
        await RotateNodeAsync(page, firstId, 45);
        await SelectNodeByIdAsync(page, firstId); // re-select (rotation may rerender)
        await CaptureCanvasAsync(page, "baseline-05-rotated-node.png");

        // Reset rotation so 02 shows a normal (non-rotated) single node.
        await RotateNodeAsync(page, firstId, 0);

        // ── 02 Single node (delete everything else) ─────────────────────────
        await ClearSelectionAsync(page);
        var toDelete = ids.Skip(1).ToArray();
        await DeleteNodesAsync(page, toDelete);
        // All edges connected to deleted nodes are also removed by the command.
        await page.WaitForFunctionAsync("""
            () => document.querySelectorAll('.tm-diagram-node').length === 1
        """, null, new PageWaitForFunctionOptions { Timeout = 10000 });
        await CaptureCanvasAsync(page, "baseline-02-single-node.png");

        // ── 01 Empty (New document) ─────────────────────────────────────────
        await ClickDemoButtonAsync(page, "New document");
        await page.WaitForFunctionAsync("""
            () => document.querySelectorAll('.tm-diagram-node').length === 0
        """, null, new PageWaitForFunctionOptions { Timeout = 10000 });
        await CaptureCanvasAsync(page, "baseline-01-empty.png");
    }
}
