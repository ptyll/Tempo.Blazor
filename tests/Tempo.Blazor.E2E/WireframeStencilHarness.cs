using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// The browser-driving half of the wireframe-stencil gallery specs: insert a stencil through the editor's
/// own drop handler, select it, flip ONE schema prop through the properties panel, frame the element and
/// capture it. It exists so a new phase's gallery spec does not grow a second copy of every helper.
/// <para>
/// Every helper drives the REAL editor — the palette search a designer types into, the drop handler the
/// canvas calls, the properties panel's own controls. Nothing here asserts pixels: the callers assert that
/// the palette entry is reachable and that the sketch CHANGES the way the prop promises, and the captures
/// are the artifact a UX review reads.
/// </para>
/// </summary>
internal static class WireframeStencilHarness
{
    /// <summary>The first editor on the demo page (the page hosts several).</summary>
    internal static ILocator FirstEditor(IPage page) => page.Locator(".tm-wd-editor").First;

    /// <summary>The toolbox of the first editor.</summary>
    internal static ILocator Toolbox(IPage page) => FirstEditor(page).Locator(".tm-wd-toolbox");

    /// <summary>One palette entry, addressed by the schema type the drop handler receives.</summary>
    internal static ILocator PaletteItem(IPage page, string type)
        => Toolbox(page).Locator($".tm-wd-toolbox__item[data-component-type='{type}']");

    /// <summary>
    /// Boots the demo page with the sidebar margin removed (so the editor resolves its DESKTOP layout) and
    /// waits for the first editor's toolbar. The caller owns the context, the page and the navigation.
    /// </summary>
    internal static async Task PrepareAsync(IPage page)
    {
        await FirstEditor(page).Locator(".tm-wd-editor__toolbar")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });
        await page.EvaluateAsync(
            "() => { const style = document.createElement('style'); " +
            "style.textContent = '.tm-wd-editor__minimap-wrap { display: none !important; }'; " +
            "document.head.appendChild(style); }");
        await page.WaitForTimeoutAsync(1200);
    }

    /// <summary>Filters the palette to one stencil so a capture shows the entry a designer would click.</summary>
    internal static async Task FilterPaletteAsync(IPage page, string term)
    {
        var search = Toolbox(page).Locator(".tm-wd-toolbox__search-input");
        await search.FillAsync(term);
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>
    /// Inserts a stencil through the canvas drop handler and returns the DOM id of the NEW element. The id is
    /// DIFFED against the ids the canvas already had, never taken from the last node in DOM order: the demo
    /// document is pre-seeded and the paint order follows z-index.
    /// </summary>
    internal static async Task<string> InsertAsync(IPage page, string type, int x = 300, int y = 200)
    {
        var canvasId = await FirstEditor(page).Locator(".tm-wd-canvas__svg").EvaluateAsync<string>("el => el.id");
        var beforeIds = await FirstEditor(page).Locator("g[data-el-id]")
            .EvaluateAllAsync<string[]>("groups => groups.map(group => group.getAttribute('data-el-id'))");

        var result = await page.EvaluateAsync<string>(
            """
            async ([type, canvasId, x, y]) => {
                const inst = window.tmWireframeDesigner.instances.get(canvasId);
                if (!inst) return JSON.stringify({ error: 'no-inst' });
                try {
                    await inst.dotNetRef.invokeMethodAsync('OnElementDropped', type, x, y);
                    return JSON.stringify({ ok: true });
                } catch (err) {
                    return JSON.stringify({ error: err.message });
                }
            }
            """,
            new object[] { type, canvasId, x, y });

        Assert.IsTrue(result.Contains("\"ok\":true"), $"the {type} stencil must insert onto the canvas (was {result})");
        await page.WaitForFunctionAsync(
            "n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length === n",
            beforeIds.Length + 1,
            new PageWaitForFunctionOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(500);

        var added = await page.EvaluateAsync<string>(
            """
            (before) => {
                const groups = document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]');
                const id = [...groups]
                    .map(group => group.getAttribute('data-el-id'))
                    .find(candidate => !before.includes(candidate));
                return id ?? '';
            }
            """,
            beforeIds);

        Assert.IsFalse(string.IsNullOrWhiteSpace(added), $"the inserted {type} element is findable on the canvas");
        return added;
    }

    /// <summary>Selects the inserted element so the properties panel shows ITS surface props.</summary>
    internal static async Task SelectAsync(IPage page, string elementId)
    {
        var group = FirstEditor(page).Locator($"g[data-el-id='{elementId}']").First;
        await group.ScrollIntoViewIfNeededAsync();
        await group.ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>
    /// Switches a boolean schema prop on through the properties panel's own toggle. The panel paints a designed
    /// switch over a visually hidden <c>&lt;input type="checkbox"&gt;</c> (0×0, <c>pointer-events: none</c>), so
    /// Playwright cannot click the input itself — the editor's own specs dispatch the DOM click the label would.
    /// </summary>
    internal static async Task TogglePropAsync(IPage page, string prop)
    {
        var input = FirstEditor(page).Locator($"input[id$='-p-{prop}']");
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        if (await input.IsCheckedAsync())
        {
            return;
        }

        await input.EvaluateAsync("element => element.click()");
        await page.WaitForTimeoutAsync(600);

        Assert.IsTrue(await input.IsCheckedAsync(),
            $"the properties panel must switch '{prop}' on — Blazor's change handler has to receive the click");
    }

    /// <summary>
    /// Sets a boolean schema prop to an EXPLICIT value. <see cref="TogglePropAsync"/> only ever switches a prop
    /// ON (it is the "opt in" path a gallery uses most), which is the wrong tool for a prop whose schema default
    /// is already true — that call would return early and the assertion would read the unchanged sketch.
    /// </summary>
    internal static async Task SetBoolPropAsync(IPage page, string prop, bool value)
    {
        var input = FirstEditor(page).Locator($"input[id$='-p-{prop}']");
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        if (await input.IsCheckedAsync() == value)
        {
            return;
        }

        await input.EvaluateAsync("element => element.click()");
        await page.WaitForTimeoutAsync(600);

        Assert.AreEqual(value, await input.IsCheckedAsync(),
            $"the properties panel must set '{prop}' to {value} — Blazor's change handler has to receive the click");
    }

    /// <summary>Chooses a value of an enum schema prop through the properties panel's select.</summary>
    internal static async Task SelectEnumPropAsync(IPage page, string prop, string value)
    {
        var select = FirstEditor(page).Locator($"select[id$='-p-{prop}']");
        await select.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await select.SelectOptionAsync(value);
        await page.WaitForTimeoutAsync(500);
    }

    /// <summary>Types a number into an Int/Double schema prop. The panel saves on input, so fill + blur is enough.</summary>
    internal static async Task SetNumberPropAsync(IPage page, string prop, string value)
    {
        var input = FirstEditor(page).Locator($"input[type='number'][id$='-p-{prop}']");
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        await input.FillAsync(value);
        await page.WaitForTimeoutAsync(600);
        Assert.AreEqual(value, await input.InputValueAsync(), $"the properties panel accepted '{value}' for {prop}");
    }

    /// <summary>Types into a String or StringList (comma separated) schema prop.</summary>
    internal static async Task SetTextPropAsync(IPage page, string prop, string value)
    {
        var input = FirstEditor(page).Locator($"input[type='text'][id$='-p-{prop}']");
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        await input.FillAsync(value);
        await page.WaitForTimeoutAsync(600);
        Assert.AreEqual(value, await input.InputValueAsync(), $"the properties panel accepted '{value}' for {prop}");
    }

    /// <summary>Counts how many times a literal appears in a piece of markup.</summary>
    internal static int Occurrences(string markup, string literal)
    {
        var count = 0;
        var index = 0;
        while ((index = markup.IndexOf(literal, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += literal.Length;
        }

        return count;
    }

    /// <summary>The rendered inner markup of one canvas element.</summary>
    internal static async Task<string> ElementSvgAsync(IPage page, string elementId)
        => await FirstEditor(page).Locator($"g[data-el-id='{elementId}']").First.InnerHTMLAsync();

    /// <summary>
    /// Frames ONE element to fill the canvas (instead of the page-wide fit, where a 10 px stencil label lands
    /// 4 px tall), using the editor's own <c>getElementBounds</c> and the viewBox it pans/zooms with — no
    /// test-only hooks.
    /// <para>
    /// The zoom is the SMALLER of the width and the height ratio. Fitting by width alone (which the first
    /// version did) makes any viewBox that is narrower than the canvas — the 280×200 usage meter on a wide
    /// canvas — taller than the canvas' own box, and the SVG clips its bottom: the category table was cut
    /// off every capture. The viewBox is also re-applied until it STICKS, because selecting an element or
    /// flipping a prop re-renders the editor and a re-render re-fits the canvas to the whole page.
    /// </para>
    /// </summary>
    internal static async Task FrameAsync(IPage page, string elementId, double padding = 48, double reserveBelow = 0)
    {
        var canvasId = await FirstEditor(page).Locator(".tm-wd-canvas__svg").EvaluateAsync<string>("el => el.id");
        var modelAspect = 0.0;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var framed = await page.EvaluateAsync<double[]>(
                """
                ([canvasId, id, pad, reserve]) => {
                    const designer = window.tmWireframeDesigner;
                    const inst = designer.instances.get(canvasId);
                    if (!inst) return null;
                    const bounds = designer.getElementBounds(inst.svg, id);
                    if (!bounds || !bounds.w || !bounds.h) return null;
                    const box = inst.svg.getBoundingClientRect();
                    const viewWidth = bounds.w + 2 * pad;
                    const viewHeight = bounds.h + 2 * pad + reserve;
                    designer._setViewBox(inst, bounds.x - pad, bounds.y - pad, viewWidth, viewHeight);
                    // Fit BOTH axes: a width-only fit overflows a canvas that is wider than the element's
                    // aspect, and the SVG then clips its own content.
                    inst.scale = Math.min(box.width / viewWidth, box.height / viewHeight);
                    return [bounds.w, bounds.h];
                }
                """,
                new object[] { canvasId, elementId, padding, reserveBelow });

            Assert.IsNotNull(framed, $"the canvas framed {elementId} for its capture");
            modelAspect = (framed![0] + 2 * padding) / (framed[1] + 2 * padding + reserveBelow);

            await page.WaitForTimeoutAsync(300);

            // The element's CLIENT box must now carry the framed aspect; when a re-render re-fitted the
            // canvas it does not, and the frame is applied again.
            var client = await FirstEditor(page).Locator($"g[data-el-id='{elementId}']").First
                .EvaluateAsync<double[]>("el => { const b = el.getBoundingClientRect(); return [b.width, b.height]; }");
            if (client is { Length: 2 } && client[1] > 0 && Math.Abs(client[0] / client[1] - modelAspect) < 0.04)
            {
                break;
            }

            // A prop flip or the selection repaint is still settling; a fresh attempt is what fixes it.
            await page.WaitForTimeoutAsync(400);
        }

        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>
    /// Captures the box of <paramref name="target"/> (plus a small margin) into <paramref name="directory"/>.
    /// With <paramref name="clipToCanvas"/> the clip is intersected with the canvas, whose SVG clips its own
    /// content — a wider clip would capture the surrounding editor chrome.
    /// </summary>
    internal static async Task ShootAsync(
        IPage page,
        ILocator target,
        string directory,
        string name,
        int pad = 8,
        bool clipToCanvas = false,
        int extraBottom = 0,
        bool alreadyPrepared = false)
    {
        await page.WaitForTimeoutAsync(250);
        if (!alreadyPrepared)
        {
            await page.Mouse.MoveAsync(0, 0);
            await page.EvaluateAsync(
                "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        }

        var box = await target.BoundingBoxAsync()
                  ?? throw new InvalidOperationException($"{name}: the target has no bounding box");
        var left = box.X - pad;
        var top = box.Y - pad;
        var right = box.X + box.Width + pad;
        var bottom = box.Y + box.Height + pad + extraBottom;

        if (clipToCanvas)
        {
            var canvas = await FirstEditor(page).Locator(".tm-wd-canvas__svg").BoundingBoxAsync()
                         ?? throw new InvalidOperationException($"{name}: the canvas has no bounding box");
            left = Math.Max(left, canvas.X);
            top = Math.Max(top, canvas.Y);
            right = Math.Min(right, canvas.X + canvas.Width);
            bottom = Math.Min(bottom, canvas.Y + canvas.Height);
        }

        Assert.IsTrue(right - left > 40 && bottom - top > 20,
            $"{name}: the element is visible on the canvas ({right - left}×{bottom - top} px)");

        var path = Path.Combine(directory, $"{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Clip = new Clip { X = left, Y = top, Width = right - left, Height = bottom - top },
        });
        Assert.IsTrue(File.Exists(path), $"{name} lands on disk");
    }
}
