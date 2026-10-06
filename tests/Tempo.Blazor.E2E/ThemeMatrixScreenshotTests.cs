using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Captures the six core surfaces in every combination of light/dark and default/indigo, so a
/// theme regression is a picture rather than a passing DOM assertion. The indigo switch persists
/// through localStorage, which is why each combination sets it explicitly instead of trusting
/// whatever the previous test left behind.
/// </summary>
[TestClass]
public sealed class ThemeMatrixScreenshotTests : WasmTestBase
{
    private static readonly (string Route, string Name, string Ready)[] Pages =
    [
        ("/buttons", "button", ".tm-btn"),
        ("/forms", "input", ".tm-input, input"),
        ("/modal-dialog", "modal", "[data-testid='open-basic-modal']"),
        ("/data-table", "datatable", ".tm-data-table"),
        ("/dashboard", "dashboard", ".tm-dashboard"),
        ("/scheduler", "scheduler", ".tm-scheduler"),
    ];

    [TestMethod]
    [TestCategory("WASM")]
    public async Task CorePages_RenderInEveryThemeCombination()
    {
        foreach (var dark in new[] { false, true })
        {
            foreach (var indigo in new[] { false, true })
            {
                var page = await CreatePageAsync();
                // Desktop width, so the sidebar (and its theme toggle) is shown rather than the
                // mobile header, whose copy of the toggle is hidden and not clickable.
                await page.SetViewportSizeAsync(1440, 900);
                await page.GotoAsync(BaseUrl);
                await page.WaitForFunctionAsync(
                    "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
                    null, new PageWaitForFunctionOptions { Timeout = 30000 });

                if (indigo)
                {
                    await ApplyIndigoAsync(page);
                }

                foreach (var (route, name, ready) in Pages)
                {
                    await page.GotoAsync(BaseUrl.TrimEnd('/') + route);
                    await page.WaitForFunctionAsync(
                        "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
                        null, new PageWaitForFunctionOptions { Timeout = 30000 });
                    await page.Locator(ready).First.WaitForAsync(
                        new LocatorWaitForOptions { Timeout = 30000 });

                    // Dark mode lives in the in-memory ThemeService, which a full navigation resets,
                    // so it has to be toggled on every page. The sidebar copy is the visible one.
                    if (dark)
                    {
                        await page.Locator("aside [data-testid='theme-toggle']").ClickAsync();
                        await page.WaitForFunctionAsync(
                            "() => document.querySelector('[data-theme=\"dark\"]') !== null",
                            null, new PageWaitForFunctionOptions { Timeout = 10000 });
                    }

                    // A closed modal screenshots as a button row. Open it so the shot shows the dialog.
                    if (name == "modal")
                    {
                        await page.Locator(ready).First.ClickAsync();
                        await page.Locator(".tm-modal").First.WaitForAsync(
                            new LocatorWaitForOptions { Timeout = 15000 });
                    }

                    var theme = (dark ? "dark" : "light") + "-" + (indigo ? "indigo" : "default");
                    await SaveAsync(page, $"{name}--{theme}");
                }
            }
        }
    }

    private static async Task ApplyIndigoAsync(IPage page)
    {
        // The switch restores this from localStorage on every load, so one write covers the run.
        await page.EvaluateAsync(
            "() => { localStorage.setItem('tm-demo-color-theme','indigo'); document.documentElement.setAttribute('data-tm-theme','indigo'); }");
    }

    private async Task SaveAsync(IPage page, string name)
    {
        var directory = Path.Combine(
            FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "theme-matrix");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = true,
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
