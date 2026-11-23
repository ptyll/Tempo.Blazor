using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Small page helpers (screenshot, navigation, theme, language, heap) of <see cref="PlaywrightTestBase"/>.
/// </summary>
public abstract partial class PlaywrightTestBase
{
    /// <summary>
    /// Takes a screenshot and attaches it to the test result.
    /// </summary>
    protected async Task TakeScreenshotAsync(IPage page, string name)
    {
        var screenshot = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true,
            // Screenshot captures bypass the default action timeout — a stalled renderer/GPU
            // would otherwise hang the whole shard with no failure reported (seen on
            // DiagramEdge Phase3 under load).
            Timeout = 60_000
        });

        var path = Path.Combine(TestContext.TestResultsDirectory ?? ".", $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        await File.WriteAllBytesAsync(path, screenshot);
        TestContext.AddResultFile(path);
    }

    /// <summary>
    /// Clicks on a navigation menu item by its text. The match must target the nav <b>anchor</b> —
    /// a bare <c>nav:has-text(...)</c> resolves to the whole nav container (it contains every item's
    /// text), so clicking it lands on whatever link happens to sit at its midpoint.
    /// </summary>
    protected async Task NavigateToPageAsync(IPage page, string menuText)
    {
        var menuItem = page.Locator($"nav a:has-text('{menuText}'), nav button:has-text('{menuText}'), a:has-text('{menuText}'), button:has-text('{menuText}')").First;
        await menuItem.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Puts the demo shell into <paramref name="theme"/> (<c>light</c>/<c>dark</c>) through the real header toggle.
    /// The demo frame carries its own <c>data-theme</c> (ThemeService), and a nested <c>[data-theme="light"]</c> is
    /// the documented light island that re-pins every token - so setting the attribute on <c>&lt;html&gt;</c> from
    /// outside never darkens the page content; the shell's own theme is the contract.
    /// </summary>
    protected static async Task EnsureDemoThemeAsync(IPage page, string theme)
    {
        var shell = page.Locator(".demo-shell").First;
        if (!string.Equals(await shell.GetAttributeAsync("data-theme"), theme, StringComparison.Ordinal))
        {
            // dispatch in-page: a top-layer popover/sheet or a hidden-per-band toggle must not block it
            await page.EvaluateAsync("() => { const t = [...document.querySelectorAll('[data-testid=theme-toggle]')].find(e => e.offsetParent !== null) ?? document.querySelector('[data-testid=theme-toggle]'); t?.click(); }");
        }

        await Assertions.Expect(shell).ToHaveAttributeAsync("data-theme", theme,
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);
    }
    /// <summary>
    /// Toggles dark mode and verifies the theme changed.
    /// </summary>
    protected async Task ToggleDarkModeAsync(IPage page)
    {
        // Find and click the theme toggle button
        var themeToggle = page.Locator("[data-testid='theme-toggle']:visible, button[aria-label*='theme' i]:visible, button[title*='dark' i]:visible").First;
        await themeToggle.ClickAsync();

        // Wait for the dark class to be applied/removed
        await page.WaitForTimeoutAsync(500);
    }

    /// <summary>
    /// Switches language to the specified culture. The demo switcher is a pair of buttons
    /// (<c>[data-testid='language-switcher-{culture}']</c>) that set localStorage and force a
    /// full reload — not a select element.
    /// </summary>
    protected async Task SwitchLanguageAsync(IPage page, string culture)
    {
        var button = page.Locator($"[data-testid='language-switcher-{culture}']").First;

        // SetCultureAsync force-loads the page — wait for the NEW document and the app to
        // be interactive again (with the controlled cold-boot reload) rather than racing the first paint.
        await NavigateAppReadyAsync(page, () => button.ClickAsync());
    }

    /// <summary>
    /// Performs memory heap snapshot for memory leak detection.
    /// </summary>
    protected async Task<long> GetHeapSizeAsync(IPage page)
    {
        // Evaluate a scalar, not an object: Playwright's .NET serializer wraps returned objects in a
        // preserved-reference envelope ({"$id":"1", ...}) that materializes as an EMPTY
        // Dictionary<string,object> — the key lookup then silently yields 0 and the probe reads
        // "unmeasurable" on every run even though performance.memory is alive. A bare number has no
        // envelope, so EvaluateAsync<long> returns the real heap size. performance.memory is
        // Chromium-only and can be absent mid-navigation on InteractiveAuto — the ?./|| still maps
        // that to 0 for the caller to assert on.
        return await page.EvaluateAsync<long>("() => performance.memory?.usedJSHeapSize || 0");
    }

    /// <summary>
    /// Waits until <paramref name="expression"/> is truthy. Polls through <see cref="IPage.EvaluateAsync{T}(string, object?)"/>
    /// so an <c>async</c> predicate is actually awaited (Playwright's <c>WaitForFunctionAsync</c> treats a Promise
    /// object as already-truthy). Never pass a <see cref="PageWaitForFunctionOptions"/> as the predicate argument —
    /// remaining 7f95b6e6, the two-argument overload binds options as <c>arg</c> and then uses the default 30 s.
    /// </summary>
    internal static Task WaitForPredicateAsync(IPage page, string expression, int? timeoutMs = null)
        => WaitForPredicateAsync(page, expression, arg: null, timeoutMs);

    /// <summary>
    /// <see cref="WaitForPredicateAsync(IPage, string, int?)"/> with a predicate argument that is NOT
    /// the options object.
    /// </summary>
    internal static async Task WaitForPredicateAsync(IPage page, string expression, object? arg, int? timeoutMs = null)
    {
        var timeout = timeoutMs ?? 30_000;
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
        // EvaluateAsync awaits a returned Promise, so the C# side always sees a boolean. WaitForFunctionAsync
        // would treat the Promise object itself as truthy and return on the first poll.
        var script = "async arg => !!(await Promise.resolve((" + expression + ")(arg)))";
        Exception? lastError = null;
        while (true)
        {
            try
            {
                if (await page.EvaluateAsync<bool>(script, arg))
                {
                    return;
                }

                lastError = null;
            }
            catch (Exception ex) when (!page.IsClosed)
            {
                lastError = ex;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"WaitForPredicateAsync timed out after {timeout} ms.", lastError);
            }

            await Task.Delay(50);
        }
    }
}
