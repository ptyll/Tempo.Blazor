using System.Globalization;
using Microsoft.Playwright;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Shared plumbing for the phase 13 table/list specs. The specs themselves are static bodies
/// (<see cref="DataTableSpecs"/>) so the same assertions run on the WASM and the Server host.
/// </summary>
internal static class DataTableE2EKit
{
    internal sealed record Options(
        int Width = 1440,
        int Height = 1100,
        bool Touch = false,
        bool IsMobile = false,
        string Culture = "en",
        float DeviceScaleFactor = 1,
        string Theme = "light",
        bool Indigo = false);

    /// <summary>Opens a route in a fresh context with a pinned culture, reduced motion and (optionally) touch emulation.</summary>
    internal static async Task<IPage> OpenAsync(IBrowser browser, string baseUrl, string route, Options? options = null, Action<IBrowserContext>? register = null)
    {
        options ??= new Options();
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = options.Width, Height = options.Height },
            HasTouch = options.Touch,
            IsMobile = options.IsMobile,
            DeviceScaleFactor = options.DeviceScaleFactor,
            IgnoreHTTPSErrors = true,
            AcceptDownloads = true,
            Locale = options.Culture + "-US",
            ReducedMotion = ReducedMotion.Reduce,
        });
        register?.Invoke(context);
        var init = string.Create(CultureInfo.InvariantCulture,
            $"localStorage.setItem('tm-demo-culture','{options.Culture}'); document.cookie='tm-demo-culture={options.Culture};path=/;max-age=31536000;samesite=lax';");
        if (options.Indigo)
        {
            init += "localStorage.setItem('tm-demo-color-theme','indigo');";
        }

        await context.AddInitScriptAsync(init);
        var page = await context.NewPageAsync();
        await page.GotoAsync(baseUrl.TrimEnd('/') + route, new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90_000 });
        await page.WaitForFunctionAsync("() => document.body !== null && document.body.hasAttribute('data-blazor-ready')", null,
            new PageWaitForFunctionOptions { Timeout = 90_000 });
        if (options.Theme == "dark")
        {
            await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
            await page.WaitForSelectorAsync("[data-theme='dark']", new PageWaitForSelectorOptions { Timeout = 15_000 });
        }

        return page;
    }

    /// <summary>The page is still moving when it reports ready: fonts, the Tailwind CDN, smooth scroll. Two frames after fonts settle.</summary>
    internal static Task SettleAsync(IPage page) => page.EvaluateAsync(
        "async () => { await document.fonts.ready; await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");

    /// <summary>`html { scroll-behavior: smooth }` makes every focus scroll an animation; a scroll baseline is only valid once the position has stopped moving.</summary>
    internal static Task SettleScrollAsync(IPage page) => page.EvaluateAsync(
        "async () => { let last = -1, stable = 0; while (stable < 4) { await new Promise(r => requestAnimationFrame(r)); const y = window.scrollY; stable = y === last ? stable + 1 : 0; last = y; } }");

    internal static async Task SetViewportAsync(IPage page, int width, int height)
    {
        await page.SetViewportSizeAsync(width, height);
        await SettleAsync(page);
    }

    /// <summary>Freezes every animation and hides the demo's fixed chrome so it cannot overlap a captured element.</summary>
    internal static Task PrepareForCaptureAsync(IPage page) => page.AddStyleTagAsync(new PageAddStyleTagOptions
    {
        Content = "*,*::before,*::after{animation:none!important;transition:none!important;caret-color:transparent!important}"
                  + "div.fixed.top-0.z-40,div.sticky.top-0.z-30,aside.fixed{display:none!important}html{scroll-behavior:auto!important}",
    });

    internal static async Task ToggleAsync(IPage page, string testId)
        => await page.Locator($"[data-testid='{testId}'] .tm-toggle-track").ClickAsync();

    internal static async Task SelectAsync(IPage page, string testId, string value)
        => await page.Locator($"[data-testid='{testId}'] select").SelectOptionAsync(value);

    /// <summary>Inserts a focusable probe right before the table's scroller, so a test can press Tab "from outside".</summary>
    internal static async Task InsertProbeAsync(IPage page, string sectionSelector, string id = "tm-probe")
    {
        await page.EvaluateAsync(
            "([selector, id]) => { const scroll = document.querySelector(selector + ' .tm-data-table-scroll'); const b = document.createElement('button'); b.id = id; b.textContent = 'probe'; scroll.parentElement.insertBefore(b, scroll); }",
            new object[] { sectionSelector, id });
    }

    internal static Task<string> ActiveAsync(IPage page) => page.EvaluateAsync<string>(
        "() => { const a = document.activeElement; if (!a) return ''; return (a.tagName + '|' + (a.getAttribute('data-grid-cell') || '') + '|' + (a.getAttribute('data-action') || '') + '|' + (a.className || '').toString()); }");

    internal static async Task<LocatorBoundingBoxResult> BoxAsync(ILocator locator)
        => (await locator.BoundingBoxAsync()) ?? throw new InvalidOperationException("Element has no bounding box.");

    internal static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
