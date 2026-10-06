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
        ("/feedback", "modal", ".tm-modal, .tm-dialog, .tm-alert"),
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
                await page.GotoAsync(BaseUrl);
                await page.WaitForFunctionAsync(
                    "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
                    null, new PageWaitForFunctionOptions { Timeout = 30000 });

                await ApplyThemeAsync(page, dark, indigo);

                foreach (var (route, name, ready) in Pages)
                {
                    await page.GotoAsync(BaseUrl.TrimEnd('/') + route);
                    await page.WaitForFunctionAsync(
                        "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
                        null, new PageWaitForFunctionOptions { Timeout = 30000 });
                    await page.Locator(ready).First.WaitForAsync(
                        new LocatorWaitForOptions { Timeout = 30000 });

                    var theme = (dark ? "dark" : "light") + "-" + (indigo ? "indigo" : "default");
                    await SaveAsync(page, $"{name}--{theme}");
                }
            }
        }
    }

    private static async Task ApplyThemeAsync(IPage page, bool dark, bool indigo)
    {
        if (dark)
        {
            var toggle = page.Locator("[data-testid='theme-toggle']").First;
            await toggle.ClickAsync();
            await page.WaitForFunctionAsync(
                "() => document.querySelector('[data-theme=\"dark\"]') !== null",
                null, new PageWaitForFunctionOptions { Timeout = 10000 });
        }

        if (indigo)
        {
            await page.EvaluateAsync(
                "() => { localStorage.setItem('tm-demo-color-theme','indigo'); document.documentElement.setAttribute('data-tm-theme','indigo'); }");
        }
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
