using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 6 — model-level group bounds render as SVG <c>&lt;rect class="tm-diagram-group-bounds"&gt;</c>
/// inside <c>.tm-diagram-bg-pane</c> (Razor + document model), not as imperative HTML in the scene/overlay.
/// </summary>
[TestClass]
public class DiagramF6E2ETests : WasmTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";

    private async Task<IPage> PrepareGroupedSamplePageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        await GotoAppReadyAsync(page, $"{BaseUrl}{DiagramEditorUrl}", WaitUntilState.DOMContentLoaded);

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

        await page.GetByRole(AriaRole.Button, new() { Name = "Load grouped sample" }).ClickAsync();
        await page.WaitForSelectorAsync(".tm-diagram-node", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await page.WaitForTimeoutAsync(500);

        return page;
    }

    /// <summary>F6.1 / F6.2 / F6.3 — one dashed SVG bounds rect in bg-pane; none in overlay/decorator.</summary>
    [TestMethod]
    public async Task GroupedSample_RendersBoundsRect_InBgPaneOnly_WithExpectedGeometry()
    {
        var page = await PrepareGroupedSamplePageAsync();

        var json = await page.EvaluateAsync<string>("""
            () => {
                const bg = document.querySelector('.tm-diagram-bg-pane');
                const ov = document.querySelector('.tm-diagram-overlay-pane');
                const deco = document.querySelector('.tm-diagram-decorator-pane');
                const inBg = bg ? bg.querySelectorAll('rect.tm-diagram-group-bounds') : [];
                const inOv = ov ? ov.querySelectorAll('.tm-diagram-group-bounds') : [];
                const inDeco = deco ? deco.querySelectorAll('.tm-diagram-group-bounds') : [];
                let first = null;
                if (inBg.length) {
                    const r = inBg[0];
                    first = {
                        dataGroupId: r.getAttribute('data-group-id'),
                        x: r.getAttribute('x'),
                        y: r.getAttribute('y'),
                        width: r.getAttribute('width'),
                        height: r.getAttribute('height'),
                    };
                }
                return JSON.stringify({
                    bgCount: inBg.length,
                    overlayCount: inOv.length,
                    decoratorCount: inDeco.length,
                    first
                });
            }
        """);

        Assert.IsNotNull(json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("bgCount").GetInt32().Should().Be(1,
            "F6 — exactly one group-bounds rect per GroupId in the model");
        root.GetProperty("overlayCount").GetInt32().Should().Be(0);
        root.GetProperty("decoratorCount").GetInt32().Should().Be(0);

        var first = root.GetProperty("first");
        first.GetProperty("dataGroupId").GetString().Should().Be("g1");
        // Matches TmDiagramCanvas.razor pad=8 and CreateGroupedBoundsSample layout.
        first.GetProperty("x").GetString().Should().Be("92");
        first.GetProperty("y").GetString().Should().Be("92");
        first.GetProperty("width").GetString().Should().Be("336");
        first.GetProperty("height").GetString().Should().Be("216");
    }
}
