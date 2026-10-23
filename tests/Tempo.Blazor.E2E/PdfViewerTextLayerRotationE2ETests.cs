using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// PDF.js text-layer rotation: the TextLayer container keeps the <b>unrotated</b> page
/// box (PDF.js <c>setLayerDimensions</c>) and is mapped onto the rotated canvas purely by the
/// package's <c>[data-main-rotation]</c> CSS rules. The E2E proves the mapping geometrically:
/// rotate the page, then measure with Playwright that (a) the text layer's bounding box
/// exactly overlays the canvas box and (b) a text selection's <c>getBoundingClientRect</c>
/// lies inside the rotated canvas box. Covered for the single-page viewer (layer anchored at
/// the canvas center-x) and the continuous renderer's per-page layers (anchored at the page
/// top-left; JS-built DOM).
/// </summary>
[TestClass]
public class PdfViewerTextLayerRotationE2ETests : WasmTestBase
{
    private const string PdfPage = "/pdf-viewer";
    private const int DesktopWidth = 1440;

    /// <summary>Selection rect may touch the canvas edge; glyph boxes get a 2px grace.</summary>
    private const double EdgeTolerance = 2.0;

    /// <summary>The layer box must overlay the canvas box (sub-pixel layout rounding).</summary>
    private const double OverlayTolerance = 1.5;

    private async Task<IPage> OpenPdfPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(DesktopWidth, 1000);
        await page.GotoAsync($"{BaseUrl}{PdfPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static ILocator Section(IPage page, string title)
        => page.Locator($"section:has(h2:has-text('{title}'))").First;

    /// <summary>
    /// Clicks the section's Rotate action. On toolbars without room for it (the continuous
    /// viewer's row also carries the view toggle and search) the primary button is collapsed
    /// into the "More" menu and must be triggered from there.
    /// </summary>
    private static async Task ClickRotateAsync(ILocator section)
    {
        var direct = section.Locator("[data-tm-group='rotate'] button").First;
        if (await direct.IsVisibleAsync())
        {
            await direct.ClickAsync();
            return;
        }

        await section.Locator(".tm-toolbar-more").First.ClickAsync();
        await section.Locator(".tm-toolbar-more__item").Filter(new() { HasText = "Rotate" }).First
            .ClickAsync(new LocatorClickOptions { Timeout = 15000 });
    }

    /// <summary>
    /// Measures canvas box, text-layer box and a real text-selection rect in one DOM pass
    /// (viewport coordinates, so the three stay comparable even inside scroll containers).
    /// </summary>
    private const string MeasureScript = """
        (args) => {
            const section = [...document.querySelectorAll('section.demo-section')]
                .find(s => s.querySelector('h2')?.textContent?.trim() === args.title);
            if (!section) return JSON.stringify({ error: 'section not found: ' + args.title });

            const pageWrap = args.page
                ? section.querySelector(`.tm-pdf-viewer__page[data-page='${args.page}']`)
                : null;
            const canvas = pageWrap
                ? pageWrap.querySelector('canvas')
                : section.querySelector('.tm-pdf-viewer__canvas');
            const layer = pageWrap
                ? pageWrap.querySelector('.tm-pdf-viewer__text-layer')
                : section.querySelector('.tm-pdf-viewer__text-layer');
            if (!canvas || !layer) {
                return JSON.stringify({ error: 'canvas/text layer missing', canvas: !!canvas, layer: !!layer });
            }

            const spans = [...layer.querySelectorAll('span')]
                .filter(s => (s.textContent || '').trim().length > 8);
            if (!spans.length) {
                return JSON.stringify({
                    error: 'no text span in layer',
                    rotation: layer.getAttribute('data-main-rotation')
                });
            }

            const box = (el) => {
                const r = el.getBoundingClientRect();
                return { x: r.x, y: r.y, w: r.width, h: r.height };
            };

            const range = document.createRange();
            range.selectNodeContents(spans[0]);
            const selection = window.getSelection();
            selection.removeAllRanges();
            selection.addRange(range);
            const r = range.getBoundingClientRect();

            return JSON.stringify({
                rotation: layer.getAttribute('data-main-rotation'),
                canvas: box(canvas),
                layer: box(layer),
                selection: { x: r.x, y: r.y, w: r.width, h: r.height },
                transform: getComputedStyle(layer).transform,
                text: (spans[0].textContent || '').slice(0, 40)
            });
        }
        """;

    private static async Task<JsonElement> MeasureAsync(IPage page, string title, int? pageNumber)
    {
        var json = await page.EvaluateAsync<string>(MeasureScript, new { title, page = pageNumber });
        return JsonDocument.Parse(json).RootElement;
    }

    private static string Box(JsonElement box)
        => $"(x={box.GetProperty("x").GetDouble():F1}, y={box.GetProperty("y").GetDouble():F1}, " +
           $"w={box.GetProperty("w").GetDouble():F1}, h={box.GetProperty("h").GetDouble():F1})";

    /// <summary>Asserts the layer overlays the canvas and the selection rect sits inside it.</summary>
    private static void AssertAligned(JsonElement m, string context)
    {
        Assert.IsFalse(m.TryGetProperty("error", out var error), $"{context}: measurement failed: {error}");

        var rotation = m.GetProperty("rotation").GetString();
        var canvas = m.GetProperty("canvas");
        var layer = m.GetProperty("layer");
        var selection = m.GetProperty("selection");

        // (a) The transformed text-layer box must exactly overlay the canvas box.
        var dx = Math.Abs(layer.GetProperty("x").GetDouble() - canvas.GetProperty("x").GetDouble());
        var dy = Math.Abs(layer.GetProperty("y").GetDouble() - canvas.GetProperty("y").GetDouble());
        var dw = Math.Abs(layer.GetProperty("w").GetDouble() - canvas.GetProperty("w").GetDouble());
        var dh = Math.Abs(layer.GetProperty("h").GetDouble() - canvas.GetProperty("h").GetDouble());
        Assert.IsTrue(dx <= OverlayTolerance && dy <= OverlayTolerance && dw <= OverlayTolerance && dh <= OverlayTolerance,
            $"{context} (rotation {rotation}): the text layer {Box(layer)} must overlay the canvas {Box(canvas)}; " +
            $"deltas dx={dx:F1} dy={dy:F1} dw={dw:F1} dh={dh:F1}; transform={m.GetProperty("transform").GetString()}");

        // (b) The selection's bounding rect must lie within the rotated canvas box.
        var sx = selection.GetProperty("x").GetDouble();
        var sy = selection.GetProperty("y").GetDouble();
        var sw = selection.GetProperty("w").GetDouble();
        var sh = selection.GetProperty("h").GetDouble();
        var cx = canvas.GetProperty("x").GetDouble();
        var cy = canvas.GetProperty("y").GetDouble();
        var cw = canvas.GetProperty("w").GetDouble();
        var ch = canvas.GetProperty("h").GetDouble();
        Assert.IsTrue(
            sx >= cx - EdgeTolerance && sy >= cy - EdgeTolerance &&
            sx + sw <= cx + cw + EdgeTolerance && sy + sh <= cy + ch + EdgeTolerance,
            $"{context} (rotation {rotation}): the selection rect (x={sx:F1}, y={sy:F1}, w={sw:F1}, h={sh:F1}) " +
            $"must lie within the canvas box {Box(canvas)}; selected text '{m.GetProperty("text").GetString()}'");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_TextLayerRotation_SinglePage_SelectionStaysOnCanvas()
    {
        var page = await OpenPdfPageAsync();
        var section = Section(page, "Page Rotation");

        // The document is loaded once the toolbar's page input renders. The plain
        // ShowTextLayer viewer paints its first text layer on the first explicit
        // render — the rotate below is that trigger.
        await section.Locator(".tm-pdf-viewer__page-input").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        // 90° is the hardest case (the page axes swap); 180/270 pin the remaining rules.
        foreach (var rotation in new[] { 90, 180, 270 })
        {
            await ClickRotateAsync(section);
            await WaitForLayerAsync(page, "Page Rotation", null, rotation);

            var m = await MeasureAsync(page, "Page Rotation", null);
            AssertAligned(m, $"single page after rotate to {rotation}°");
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_TextLayerRotation_Continuous_SelectionStaysOnCanvas()
    {
        var page = await OpenPdfPageAsync();
        var section = Section(page, "Continuous Scroll");

        // Lazy render queue: page 1 is painted (canvas + text layer spans) before the rotate.
        await WaitForLayerAsync(page, "Continuous Scroll", 1, 0);

        await ClickRotateAsync(section);

        // The rebuild replaces the per-page DOM; wait for the re-rendered page-1 layer.
        await WaitForLayerAsync(page, "Continuous Scroll", 1, 90);

        var m = await MeasureAsync(page, "Continuous Scroll", 1);
        AssertAligned(m, "continuous page 1 after rotate to 90°");
    }

    /// <summary>Waits until the section's text layer (optionally for one continuous page)
    /// carries the expected data-main-rotation and holds rendered spans.</summary>
    private Task WaitForLayerAsync(IPage page, string title, int? pageNumber, int rotation)
        => page.WaitForFunctionAsync(
            """
            (args) => {
                const section = [...document.querySelectorAll('section.demo-section')]
                    .find(s => s.querySelector('h2')?.textContent?.trim() === args.title);
                const layer = args.page
                    ? section?.querySelector(`.tm-pdf-viewer__page[data-page='${args.page}'] .tm-pdf-viewer__text-layer`)
                    : section?.querySelector('.tm-pdf-viewer__text-layer');
                return !!layer
                    && layer.getAttribute('data-main-rotation') === String(args.rotation)
                    && layer.querySelector('span') !== null;
            }
            """,
            new { title, page = pageNumber, rotation },
            new PageWaitForFunctionOptions { Timeout = 60000 });
}
