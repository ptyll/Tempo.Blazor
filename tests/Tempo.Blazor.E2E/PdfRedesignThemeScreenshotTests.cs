using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// PdfViewer redesign phase 2 (tokenization): desktop-1440 captures of the three PDF screens
/// (<c>/pdf-viewer</c>, <c>/pdf-annotator</c>, <c>/redaction</c>) in every light-dark ×
/// default-indigo combination, reviewed against
/// <c>output/imagegen/tempo-reimagined/TmPdfViewer/pdf-viewer--desktop.png</c> and
/// <c>…/TmPdfAnnotator/pdf-annotator--desktop.png</c> (workspace, panels, primary color).
///
/// Each combination also ASSERTS token propagation in the DOM: after the phase the components
/// read the core tokens, so re-pointing the primary scale (indigo) and dark mode must reach
/// them — this is the phase's DoD, verified machine-readably here for every screen.
/// </summary>
[TestClass]
public sealed class PdfRedesignThemeScreenshotTests : WasmTestBase
{
    private const int DesktopWidth = 1440;

    private static string OutputDirectory(string screen) =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", screen);

    /// <summary>The expected computed colors per theme: (page → probe selector → rgb string).</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Expected = new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        // The workspace under the page is --tm-bg-workspace; it follows the indigo theme and dark.
        ["pdf-viewer"] = new Dictionary<string, string>
        {
            ["light-default"] = "rgb(255, 255, 255)",
            ["light-indigo"] = "rgb(247, 248, 252)",
            ["dark-default"] = "rgb(15, 23, 42)",
            ["dark-indigo"] = "rgb(11, 17, 32)",
        },
        // The thread badge and the redaction export button paint --tm-color-primary.
        ["pdf-annotator"] = new Dictionary<string, string>
        {
            ["light-default"] = "rgb(37, 99, 235)",
            ["light-indigo"] = "rgb(79, 70, 229)",
            ["dark-default"] = "rgb(96, 165, 250)",
            ["dark-indigo"] = "rgb(129, 140, 248)",
        },
        ["redaction"] = new Dictionary<string, string>
        {
            ["light-default"] = "rgb(37, 99, 235)",
            ["light-indigo"] = "rgb(79, 70, 229)",
            ["dark-default"] = "rgb(96, 165, 250)",
            ["dark-indigo"] = "rgb(129, 140, 248)",
        },
    };

    private static readonly IReadOnlyDictionary<string, string> ProbeSelectors =
        new Dictionary<string, string>
        {
            ["pdf-viewer"] = ".tm-pdf-viewer__canvas-wrap",
            // The active tool paints --tm-color-primary; thread badges carry per-author DATA
            // colors (PdfAnnotationColorHelper), which intentionally do NOT follow the theme.
            ["pdf-annotator"] = ".tm-pdf-annotator__mode--active",
            ["redaction"] = ".tm-redaction__btn--primary",
        };

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfScreens_CaptureEveryThemeCombination_AndPropagateTokens()
    {
        foreach (var (screen, expectedColors) in Expected)
        {
            foreach (var dark in new[] { false, true })
            {
                foreach (var indigo in new[] { false, true })
                {
                    var theme = (dark ? "dark" : "light") + "-" + (indigo ? "indigo" : "default");
                    var page = await OpenScreenAsync("/" + screen, indigo);

                    if (dark)
                    {
                        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
                        await page.WaitForFunctionAsync(
                            "() => document.querySelector('[data-theme=\"dark\"]') !== null",
                            null, new PageWaitForFunctionOptions { Timeout = 15000 });
                    }

                    // Token propagation: the probe element must resolve the theme's color —
                    // an undefined alias with a hex fallback would stay the fallback forever.
                    // Polled, not read once: the button carries a 150ms background transition,
                    // so a single read right after the theme flip catches it mid-flight.
                    var probe = page.Locator(ProbeSelectors[screen]).First;
                    await probe.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
                    var expected = expectedColors[theme];
                    var actual = string.Empty;
                    for (var attempt = 0; attempt < 20; attempt++)
                    {
                        actual = await probe.EvaluateAsync<string>(
                            "el => getComputedStyle(el).backgroundColor");
                        if (actual == expected)
                        {
                            break;
                        }

                        await page.WaitForTimeoutAsync(150);
                    }

                    Assert.AreEqual(
                        expected, actual,
                        $"{screen} must read the core tokens in {theme}.");

                    await SettleAsync(page);
                    await CaptureAsync(page, screen, $"{screen}--{theme}--desktop");
                    await page.CloseAsync();
                }
            }
        }
    }

    private async Task<IPage> OpenScreenAsync(string path, bool indigo)
    {
        var page = await CreatePageAsync();
        await page.SetViewportSizeAsync(DesktopWidth, 1000);
        await page.GotoAsync(BaseUrl.TrimEnd('/') + path,
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });

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

        return page;
    }

    /// <summary>Parks pointer and focus so the shot shows the resting page, then lets fonts settle.</summary>
    private static async Task SettleAsync(IPage page)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        await page.WaitForTimeoutAsync(400);
    }

    private async Task CaptureAsync(IPage page, string screen, string name)
    {
        var directory = OutputDirectory(screen);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".png");
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

            current = current.Parent!;
        }

        throw new InvalidOperationException("Could not locate TempoBlazor.slnx.");
    }
}
