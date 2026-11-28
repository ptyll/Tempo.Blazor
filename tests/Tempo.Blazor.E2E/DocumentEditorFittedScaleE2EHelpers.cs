using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 11 (fix round): the canvas surfaces (image, table, shape) keep their pixel-geometry specs at 100 percent, but each of them also has ONE spec on a page
/// the engine FITTED to a tablet-wide column, asserting the result in model space (a pointer distance divided by the scale), so a hit-test or handle
/// geometry that forgot the scale shows up.
/// </summary>
internal static class DocumentEditorFittedScaleE2EHelpers
{
    /// <summary>Narrows the editor host to a tablet-wide column (700 px) and waits until the engine fitted the page to it. Returns the zoom scale (below 1).</summary>
    public static async Task<double> NarrowTheColumnAndWaitForTheFitAsync(IPage page, int hostWidth = 700)
    {
        await page.EvaluateAsync(
            "width => { const host = document.querySelector('.tm-document-editor-host'); host.style.width = width + 'px'; host.style.maxWidth = width + 'px'; }",
            hostWidth);
        await page.WaitForFunctionAsync(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute('data-canvas-zoom-preset') === 'fitWidth' && root?.getAttribute('data-canvas-zoom-auto-fit') === 'narrow';
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        // the fit has settled once the zoom percent and the page geometry stay put for a dozen animation frames (the observer's fit, the repaint at the new
        // scale, the progressive re-layout and the host's re-render all land within that)
        await page.EvaluateAsync(
            """
            () => new Promise(resolve => {
                const read = () => {
                    const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                    const pageEl = document.querySelector('[data-testid="document-canvas-page"]');
                    return root.getAttribute('data-canvas-zoom-percent') + '|' + Math.round(pageEl.getBoundingClientRect().width) + '|' + Math.round(pageEl.getBoundingClientRect().top);
                };
                let last = read();
                let stable = 0;
                const tick = () => {
                    const now = read();
                    stable = now === last ? stable + 1 : 0;
                    last = now;
                    if (stable >= 12) { resolve(); } else { requestAnimationFrame(tick); }
                };
                requestAnimationFrame(tick);
            })
            """);
        var percent = await page.EvaluateAsync<double>("() => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]').getAttribute('data-canvas-zoom-percent'))");
        var scale = percent / 100d;
        Assert.IsTrue(scale is > 0.3 and < 0.95, $"the spec needs a clearly fitted page, got {percent}%");
        return scale;
    }
}
