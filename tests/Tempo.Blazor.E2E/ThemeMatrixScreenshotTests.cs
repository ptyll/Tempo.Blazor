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
                        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
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

                    if (name == "button")
                    {
                        await AssertThemeAsync(page, dark, indigo);
                    }
                    else
                    {
                        await AssertWorkspaceAsync(page, dark, indigo);
                    }

                    // A hover or a focused control paints a state that is not the theme. Park the
                    // pointer and drop focus so the shot shows the resting page.
                    await page.Mouse.MoveAsync(0, 0);
                    await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");

                    var theme = (dark ? "dark" : "light") + "-" + (indigo ? "indigo" : "default");
                    await SaveAsync(page, $"{name}--{theme}", fullPage: name != "modal");
                }
            }
        }

        await CaptureCardModeAsync();
        await CaptureDashboardOverlayAsync();
        await CaptureCzechDialogAsync();
        await CaptureSameElementThemeAsync();
    }

    private async Task CaptureCardModeAsync()
    {
        foreach (var dark in new[] { false, true })
        {
            var page = await OpenAsync("/data-table", ".tm-data-table", dark, indigo: false, 390, 844);
            await SaveAsync(page, "datatable--card--" + (dark ? "dark" : "light"), fullPage: false);
        }
    }

    private async Task CaptureDashboardOverlayAsync()
    {
        var page = await OpenAsync("/dashboard", ".tm-dashboard", dark: false, indigo: false, 1440, 900);
        // The add-widget button only exists in edit mode, which the view-mode edit button enters.
        await page.Locator("[data-testid='dashboard-edit']").ClickAsync();
        await page.Locator("[data-testid='dashboard-add-widget']").ClickAsync();
        var overlay = page.Locator(".tm-widget-selector-overlay");
        await overlay.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var zIndex = await overlay.EvaluateAsync<string>("el => getComputedStyle(el).zIndex");
        Assert.AreEqual("1040", zIndex, "the widget selector overlay must sit at the overlay z-index");
        await SaveAsync(page, "dashboard--widget-selector", fullPage: false);
    }

    private async Task CaptureCzechDialogAsync()
    {
        var page = await OpenAsync("/modal-dialog", "[data-testid='open-basic-modal']", dark: false, indigo: false, 1440, 900);
        // The switcher persists the culture and force-reloads; the host reapplies it before the
        // renderer captures its context, which is what makes the dialog's buttons come back translated.
        await page.RunAndWaitForNavigationAsync(
            async () => await page.Locator("[data-testid='language-switcher-cs']").ClickAsync());
        await page.WaitForFunctionAsync(
            "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Confirm" }).ClickAsync();
        await page.Locator(".tm-dialog").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var dialog = await page.Locator(".tm-dialog").First.InnerTextAsync();
        Assert.IsTrue(dialog.Contains("Zrušit"),
            "the confirm dialog must render in Czech after the culture reload, but was: " + dialog);
        await SaveAsync(page, "dialog--cs", fullPage: false);
    }

    /// <summary>Both attributes on the same element, which is the supported way to combine them.</summary>
    private async Task CaptureSameElementThemeAsync()
    {
        // dark:true engages the demo's own dark mode, so the Tailwind chrome follows the attribute
        // the test then pins on <html>. Without it the cards stay light on a dark workspace.
        var page = await OpenAsync("/buttons", ".tm-btn", dark: true, indigo: true, 1440, 900);
        await page.EvaluateAsync(
            "() => { document.documentElement.setAttribute('data-theme','dark'); document.documentElement.setAttribute('data-tm-theme','indigo'); }");
        await AssertThemeAsync(page, dark: true, indigo: true);
        await SaveAsync(page, "button--same-element-dark-indigo", fullPage: false);
    }

    private async Task<IPage> OpenAsync(string route, string ready, bool dark, bool indigo, int width, int height)
    {
        var page = await CreatePageAsync();
        await page.SetViewportSizeAsync(width, height);
        if (indigo)
        {
            await page.AddInitScriptAsync(
                "() => { localStorage.setItem('tm-demo-color-theme','indigo'); document.documentElement.setAttribute('data-tm-theme','indigo'); }");
        }

        await page.GotoAsync(BaseUrl.TrimEnd('/') + route);
        await page.WaitForFunctionAsync(
            "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });
        await page.Locator(ready).First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        if (dark)
        {
            // The sidebar copy is hidden below the lg breakpoint, where the mobile header copy shows.
            await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        }

        return page;
    }

    /// <summary>
    /// The theme must be visible in computed style, not just in an attribute: the primary button
    /// takes the theme's primary, and the workspace token is the one the layout paints.
    /// </summary>
    private static async Task AssertThemeAsync(IPage page, bool dark, bool indigo)
    {
        await page.Locator(".tm-btn.tm-btn-primary").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        // The button transitions its background, so a read straight after the theme flips lands
        // mid-transition. Wait for the colour to arrive instead of sampling the animation.
        var expected = indigo
            ? (dark ? "rgb(129, 140, 248)" : "rgb(79, 70, 229)")
            : (dark ? "rgb(96, 165, 250)" : "rgb(37, 99, 235)");
        await page.WaitForFunctionAsync(
            "expected => getComputedStyle(document.querySelector('.tm-btn.tm-btn-primary')).backgroundColor === expected",
            expected, new PageWaitForFunctionOptions { Timeout = 5000 });

        await AssertWorkspaceAsync(page, dark, indigo);
    }

    private static async Task AssertWorkspaceAsync(IPage page, bool dark, bool indigo)
    {
        // The dark theme is set on the layout wrapper, not on <html>, so the token has to be read
        // from the element that actually carries the theme.
        var workspace = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('[data-theme]') || document.documentElement).getPropertyValue('--tm-bg-workspace').trim()");
        var expectedWorkspace = indigo ? (dark ? "#0b1120" : "#f7f8fc") : (dark ? "#0f172a" : "#ffffff");
        Assert.AreEqual(expectedWorkspace, workspace, "--tm-bg-workspace must follow the combination");
    }

    private static async Task ApplyIndigoAsync(IPage page)
    {
        // The switch restores this from localStorage on every load, so one write covers the run.
        await page.EvaluateAsync(
            "() => { localStorage.setItem('tm-demo-color-theme','indigo'); document.documentElement.setAttribute('data-tm-theme','indigo'); }");
    }

    private async Task SaveAsync(IPage page, string name, bool fullPage = true)
    {
        var directory = Path.Combine(
            FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "theme-matrix");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = fullPage,
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
