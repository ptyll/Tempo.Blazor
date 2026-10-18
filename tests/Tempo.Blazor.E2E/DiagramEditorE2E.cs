using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Shared helpers for tests that need the diagram editor at a SPECIFIC container band.
/// <para>
/// The editor resolves its presentation from its own container (F1), and the demo page's
/// content column keeps every default viewport in the TABLET band (920px at 1280, 1080 even
/// at 1440) — while the mobile band took over below 768px in phase 7. Tests written for the
/// desktop chrome (docked toolbox, always-visible toolbar buttons) or explicitly for the
/// tablet/mobile presentation therefore pin the editor container, exactly like a full-bleed
/// host would, instead of relying on the viewport.
/// </para>
/// </summary>
internal static class DiagramEditorE2E
{
    /// <summary>
    /// Pins the editor host to <c>width</c> clamped to the real column width and waits until the
    /// editor resolves the requested layout (the editor mirrors the shell resolution on
    /// data-editor-layout), then asserts the editor still fits — the demo column is ~360px
    /// narrower than the viewport (18.5rem sidebar + gutters), so an unclamped pin used to push
    /// the editor up to 200px past the viewport where overflow-x clip hid it.
    /// Use when the test needs the demo chrome (sidebar) present or a width below the column;
    /// for "the editor owns the viewport" scenarios use <see cref="PinEditorFullBleedAsync"/>.
    /// </summary>
    public static async Task ForceEditorWidthAsync(IPage page, double width, string expectedLayout)
    {
        var effective = await page.EvaluateAsync<double>(
            """
            (w) => {
                const host = document.querySelector('.tm-diagram-editor')?.parentElement;
                if (!host) throw new Error('editor host not found');
                const avail = host.parentElement.getBoundingClientRect().width;
                const t = Math.min(w, avail);
                host.style.width = t + 'px';
                host.style.maxWidth = t + 'px';
                return t;
            }
            """,
            width);
        try
        {
            await page.WaitForFunctionAsync(
                """
                (expected) => document.querySelector('.tm-diagram-editor')?.getAttribute('data-editor-layout') === expected
                """,
                expectedLayout,
                new PageWaitForFunctionOptions { Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            throw new AssertFailedException(
                $"editor pinned to {effective}px (requested {width}px) did not resolve '{expectedLayout}'");
        }

        await page.Locator(".tm-diagram-editor").First.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(250);
        await AssertEditorFitsAsync(page);
    }

    /// <summary>
    /// Pins the editor to the DESKTOP band (≥ 1200 container width): a wide viewport plus the
    /// width pin, because the demo gutters would keep a 1280 viewport in the tablet band.
    /// </summary>
    public static async Task PinDesktopAsync(IPage page)
    {
        await page.SetViewportSizeAsync(1600, 900);
        await ForceEditorWidthAsync(page, 1600, "desktop");
    }

    /// <summary>
    /// Makes the demo page full-bleed (hides the 18.5rem sidebar and the column gutters) and pins the
    /// editor host to the viewport width minus a 16px gutter on each side, then waits for the band.
    /// Throws if the editor still overflows the viewport, so a harness overflow can never hide again.
    /// Preferred for band tests at real device widths.
    /// </summary>
    /// <remarks>
    /// Coupled to the demo shell layout: the injected CSS targets the sidebar (<c>aside.w-72</c>)
    /// and the content gutters (<c>main &gt; div[class*='py-8']</c>) as rendered by
    /// <c>src/Tempo.Blazor.Demo.SharedUI/Layout/MainLayout.razor</c> — when that file changes,
    /// the injected selectors and the staleness guards in the injected script must change with it.
    /// </remarks>
    public static async Task PinEditorFullBleedAsync(IPage page, string expectedLayout)
    {
        await page.EvaluateAsync(
            """
            () => {
                if (!document.getElementById('e2e-full-bleed')) {
                    const s = document.createElement('style');
                    s.id = 'e2e-full-bleed';
                    s.textContent = `
                        aside.w-72 { display: none !important; }
                        main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }
                        main > div[class*='py-8'] { padding-left: 16px !important; padding-right: 16px !important; }
                        main .max-w-7xl { max-width: none !important; }`;
                    document.head.appendChild(s);
                }
                if (window.matchMedia('(min-width: 1024px)').matches && !document.querySelector('aside.w-72')) throw new Error('e2e full-bleed: demo sidebar selector aside.w-72 is stale (MainLayout.razor changed)');
                if (!document.querySelector("main > div[class*='py-8']")) throw new Error('e2e full-bleed: demo gutter selector is stale');
                const host = document.querySelector('.tm-diagram-editor')?.parentElement;
                if (!host) throw new Error('editor host not found');
                host.style.width = '';
                host.style.maxWidth = '';
            }
            """);
        await page.WaitForFunctionAsync(
            "(e) => document.querySelector('.tm-diagram-editor')?.getAttribute('data-editor-layout') === e",
            expectedLayout, new PageWaitForFunctionOptions { Timeout = 15_000 });
        await page.Locator(".tm-diagram-editor").First.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(250);
        await AssertEditorFitsAsync(page);
    }

    /// <summary>Fails when the editor, or one of its chrome regions, leaves the viewport/editor box.</summary>
    public static async Task AssertEditorFitsAsync(IPage page)
    {
        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
              const ed = document.querySelector('.tm-diagram-editor');
              if (!ed) return ['editor .tm-diagram-editor not found'];
              const er = ed.getBoundingClientRect();
              const vw = document.documentElement.clientWidth;
              const bad = [];
              if (er.left < -0.5 || er.right > vw + 0.5) bad.push(`editor [${er.left|0},${er.right|0}] outside viewport ${vw}`);
              if (document.documentElement.scrollWidth > vw + 1) bad.push(`page scrollWidth ${document.documentElement.scrollWidth} > ${vw}`);
              if (ed.scrollWidth > ed.clientWidth + 1) bad.push(`editor scrollWidth ${ed.scrollWidth} > clientWidth ${ed.clientWidth}`);
              const sel = '.tm-diagram-editor__header-bar > *, .tm-diagram-editor__toolbar, [data-region="left"], [data-region="right"],'
                        + ' .tm-diagram-editor__inspector, .tm-diagram-editor__minimap-overlay, .tm-diagram-editor__zoom-overlay,'
                        + ' .tm-diagram-editor__statusbar, .tm-diagram-search-panel, .tm-diagram-layers__row-menu';
              for (const el of ed.querySelectorAll(sel)) {
                const r = el.getBoundingClientRect();
                if (r.width === 0 && r.height === 0) continue;
                if (r.left < er.left - 0.5 || r.right > er.right + 0.5)
                  bad.push(`${el.getAttribute('class')} [${r.left|0},${r.right|0}] outside editor [${er.left|0},${er.right|0}]`);
              }
              return bad;
            }
            """);
        Assert.AreEqual(0, problems.Length, string.Join("\n", problems));
    }

    /// <summary>
    /// Phase 4: the command row is a core TmToolbar — a command without room collapses into the
    /// "More" menu (its bar button is inert). Click it wherever it currently is.
    /// </summary>
    public static async Task OpenToolbarCommandAsync(IPage page, string barSelector, string moreEntryText)
    {
        var barButton = page.Locator(barSelector).First;
        if (await barButton.CountAsync() > 0 && await barButton.IsVisibleAsync() && await barButton.IsEnabledAsync())
        {
            await barButton.ClickAsync();
            return;
        }

        await page.Locator(".tm-toolbar-more").First.ClickAsync();
        var entry = page.Locator(".tm-toolbar-more__item")
            .Filter(new() { HasText = moreEntryText }).First;
        await entry.ScrollIntoViewIfNeededAsync();
        await entry.ClickAsync();
    }

    /// <summary>Real CDP touch: every listed point is down; an empty list lifts all fingers.</summary>
    public static Task CdpTouchAsync(ICDPSession session, string type, params (int id, double x, double y)[] points)
        => session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = points.Select(p => (object)new Dictionary<string, object>
                { ["x"] = p.x, ["y"] = p.y, ["id"] = p.id }).ToArray(),
        });

    /// <summary>A canvas point whose ±140 px horizontal neighbourhood hits no node/edge/port.</summary>
    public static async Task<(double x, double y)> EmptyCanvasPointAsync(IPage page)
    {
        var p = await page.EvaluateAsync<double[]?>(@"() => {
            const svg = document.querySelector('.tm-diagram-canvas__svg');
            const r = svg.getBoundingClientRect();
            const busy = (x, y) => { const el = document.elementFromPoint(x, y);
                return !el || !svg.contains(el) || !!el.closest('[data-node-id], .tm-diagram-edge-hit-path, .tm-diagram-edge-label-group, .tm-diagram-port, .tm-diagram-port-hit, .tm-diagram-connection-point'); };
            for (let fy = 0.85; fy > 0.15; fy -= 0.05)
                for (let fx = 0.25; fx < 0.75; fx += 0.05) {
                    const x = r.left + r.width * fx, y = r.top + r.height * fy;
                    if (![-140, -60, 0, 60, 140].some(d => busy(x + d, y))) return [x, y];
                }
            return null; }");
        Assert.IsNotNull(p, "no empty canvas strip to pinch on");
        return (p![0], p[1]);
    }

    /// <summary>Width of the canvas viewBox (shrinks when zooming in).</summary>
    public static async Task<double> CanvasViewBoxWidthAsync(IPage page)
    {
        var vb = await page.Locator(".tm-diagram-canvas__svg").First.GetAttributeAsync("viewBox") ?? "";
        return double.Parse(vb.Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Wraps the canvas instance's dotNetRef so every menu-open interop call
    /// (<paramref name="method"/>: OnNodeContextMenu / OnEdgeContextMenu / OnCanvasContextMenu)
    /// increments window.__menuOpenCount — the "opened exactly once" assertion rides on the
    /// real JS → .NET boundary, not on the DOM.</summary>
    public static async Task ArmMenuOpenCounterAsync(IPage page, string method)
    {
        await page.EvaluateAsync(
            """
            (method) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const inst = window.tmDiagramEditor.instances.get(canvas.id);
                window.__menuOpenCount = 0;
                const orig = inst.dotNetRef.invokeMethodAsync.bind(inst.dotNetRef);
                inst.dotNetRef.invokeMethodAsync = (name, ...args) => {
                    if (name === method) window.__menuOpenCount++;
                    return orig(name, ...args);
                };
            }
            """, method);
    }

    /// <summary>The count recorded by <see cref="ArmMenuOpenCounterAsync"/>.</summary>
    public static async Task<int> MenuOpenCountAsync(IPage page)
        => await page.EvaluateAsync<int>("() => window.__menuOpenCount ?? -1");
}
