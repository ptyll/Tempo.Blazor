using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F2 UX-review captures of the redesigned TmDashboard chrome (dashboard redesign plan, phase F2):
/// desktop 1440, view mode / edit mode / open switcher, in every light-dark × default-indigo
/// combination. The shots land in <c>__screenshots__/dashboard/</c> and are reviewed against
/// output/imagegen/tempo-reimagined/TmDashboard/dashboard--desktop.png (header, cards, workspace).
/// </summary>
[TestClass]
public sealed class DashboardF2ScreenshotTests : WasmTestBase
{
    private static string OutputDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "dashboard");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Dashboard_Chrome_CapturesEveryStateAndThemeCombination()
    {
        foreach (var dark in new[] { false, true })
        {
            foreach (var indigo in new[] { false, true })
            {
                var page = await OpenDashboardAsync(indigo);
                if (dark)
                {
                    await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
                    await page.WaitForFunctionAsync(
                        "() => document.querySelector('[data-theme=\"dark\"]') !== null",
                        null, new PageWaitForFunctionOptions { Timeout = 10000 });
                }

                var theme = (dark ? "dark" : "light") + "-" + (indigo ? "indigo" : "default");

                // View: the Plain workspace, select-like switcher, primary Edit, widget cards
                // with category icon tiles.
                await SettleAsync(page);
                await CaptureAsync(page, $"{theme}--view");

                // Edit: subtle background grid, edit badge, Save primary / Add secondary.
                await page.Locator("[data-testid='dashboard-edit']").ClickAsync();
                await page.Locator(".tm-dashboard--edit").First.WaitForAsync(
                    new LocatorWaitForOptions { Timeout = 15000 });
                await SettleAsync(page);
                await CaptureAsync(page, $"{theme}--edit");

                // Open switcher (back in view mode): the menu under the left-aligned trigger,
                // at least as wide as the trigger, with the "Default" text label.
                await page.Locator(".tm-dashboard-toolbar-right .tm-btn-ghost").ClickAsync();
                await page.Locator(".tm-dashboard--edit").First.WaitForAsync(
                    new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15000 });
                await page.Locator(".tm-dashboard-switcher").ClickAsync();
                await page.Locator(".tm-dashboard-menu").First.WaitForAsync(
                    new LocatorWaitForOptions { Timeout = 15000 });
                await SettleAsync(page);
                await CaptureAsync(page, $"{theme}--switcher-open");

                await page.CloseAsync();
            }
        }
    }

    private async Task<IPage> OpenDashboardAsync(bool indigo)
    {
        var page = await CreatePageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync(BaseUrl.TrimEnd('/') + "/dashboard");

        if (indigo)
        {
            // The demo's boot script (index.html) applies data-tm-theme from localStorage, so the
            // flag must be written on the APP origin and the page reloaded to pick it up.
            await page.EvaluateAsync("() => localStorage.setItem('tm-demo-color-theme','indigo')");
            await page.ReloadAsync(new PageReloadOptions { Timeout = 30000 });
        }

        await page.WaitForFunctionAsync(
            "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });
        if (indigo)
        {
            await page.WaitForFunctionAsync(
                "() => document.documentElement.getAttribute('data-tm-theme') === 'indigo'",
                null, new PageWaitForFunctionOptions { Timeout = 10000 });
        }

        await page.Locator(".tm-dashboard .tm-widget").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    /// <summary>Parks pointer and focus so the shot shows the resting page, then lets fonts settle.</summary>
    private static async Task SettleAsync(IPage page)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        await page.WaitForTimeoutAsync(400);
    }

    private async Task CaptureAsync(IPage page, string name)
    {
        Directory.CreateDirectory(OutputDirectory);
        var path = Path.Combine(OutputDirectory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(path);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
