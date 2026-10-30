using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Signing redesign phase 3 (review round) — the post-change captures of the surfaces the phase
/// touched: the form runner wizard panel (title, progress bar, permanent field list, sticky
/// contextual footer) and the completion panel (hero circle, ChildContent slot). Written to
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-3/</c> with the review-round
/// naming <c>…--{width}--{light|dark}--{default|indigo}.png</c>:
/// <list type="bullet">
/// <item>the runner at 1440 in every light/dark × default/indigo combination, and at 1024 (touch)
/// inside a ~1000px pinned-width host so the tablet presentation shows;</item>
/// <item>the completion completed/waiting pair at 1440 in every combination and at 1024 (touch) /
/// 390 / 320 in light default.</item>
/// </list>
/// <para>
/// Dark is FORCED, not clicked, exactly like the phase-1 dark pass: the demo shell wrapper carries
/// its own <c>data-theme="light"</c> and would re-assert the light palette over a document-level
/// attribute alone, so every already-rendered <c>[data-theme]</c> host flips with the root. Indigo
/// rides the demo's own brand mechanism — <c>data-tm-theme="indigo"</c> on the root element, the
/// attribute the demo boot script derives from <c>tm-demo-color-theme</c> and
/// <c>theme-indigo.css</c> keys off.
/// </para>
/// <para>
/// Like the other generators it only runs when <c>TM_WRITE_BASELINES</c> is set:
/// <code>
/// TM_WRITE_BASELINES=1 dotnet test tests/Tempo.Blazor.E2E --filter "SigningRedesignPhase3E2ETests"
/// </code>
/// </para>
/// </summary>
[TestClass]
public class SigningRedesignPhase3E2ETests : BaselineGeneratorTestBase
{
    private const string SigningComponentsUrl = "/signing-components";

    /// <summary>The demo shell's sticky chrome and the runner's sticky rail must not bleed into element captures.</summary>
    private const string CaptureChromeStyle =
        ".sticky.top-0, .fixed.top-0 { visibility: hidden !important; } " +
        ".tm-signing-form-runner__steps { position: static !important; }";

    /// <summary>The review-round theme/brand matrix for the 1440 pass.</summary>
    private static readonly (bool Dark, bool Indigo, string Theme, string Brand)[] Variants =
    [
        (false, false, "light", "default"),
        (true, false, "dark", "default"),
        (false, true, "light", "indigo"),
        (true, true, "dark", "indigo")
    ];

    /// <summary>The remaining viewports run in light default; 1024 is the touch pass.</summary>
    private static readonly (int Width, int Height, string Name, bool Touch)[] LightViewports =
    [
        (1024, 768, "1024", true),
        (390, 844, "390", false),
        (320, 700, "320", false)
    ];

    private static string Phase3Dir
    {
        get
        {
            var dir = Path.Combine(FindRepoRoot().FullName,
                "tests", "Tempo.Blazor.E2E", "__screenshots__", "signing-redesign", "phase-3");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    [TestMethod]
    [Description("Writes the phase-3 Signing captures (runner + completion panel, light/dark x default/indigo) for the UX review")]
    public async Task CaptureSigningRedesignPhase3Screens()
    {
        var written = new List<string>();

        // 1440 pass: runner + completion pair in every light/dark x default/indigo combination.
        foreach (var (dark, indigo, theme, brand) in Variants)
        {
            var context = await Browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
                Locale = "en-US",
                IgnoreHTTPSErrors = true
            });
            RegisterContext(context);
            var page = await context.NewPageAsync();

            await page.AddInitScriptAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
            await page.GotoAsync($"{BaseUrl}{SigningComponentsUrl}", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000
            });
            await WaitForAppReadyAsync(page);
            await page.WaitForSelectorAsync("[data-testid='signing-completion-panel']",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 20_000 });

            await ApplyThemeAsync(page, dark, indigo);
            await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = CaptureChromeStyle });
            await page.WaitForTimeoutAsync(150);

            foreach (var (screen, testId) in new[]
                     {
                         ("signing-form-runner", "signing-runner-demo"),
                         ("signing-completion-panel", "signing-completion-panel"),
                         ("signing-completion-waiting", "signing-completion-waiting")
                     })
            {
                var path = Path.Combine(Phase3Dir, $"{screen}--1440--{theme}--{brand}.png");
                await CaptureElementAsync(page, testId, path);
                written.Add(path);
            }

            await context.CloseAsync();
        }

        // Light pass: the completion pair at the remaining viewports, plus the TABLET runner —
        // 1024 touch, with the runner pinned to a ~1000px host so the capture shows the tablet
        // presentation instead of the full-bleed demo column.
        foreach (var (width, height, name, touch) in LightViewports)
        {
            var context = await Browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
                HasTouch = touch
            });
            RegisterContext(context);
            var page = await context.NewPageAsync();

            await page.AddInitScriptAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
            await page.GotoAsync($"{BaseUrl}{SigningComponentsUrl}", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000
            });
            await WaitForAppReadyAsync(page);
            await page.WaitForSelectorAsync("[data-testid='signing-completion-panel']",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 20_000 });

            var style = CaptureChromeStyle;
            if (width == 1024)
            {
                // The demo column at 1024 is ~614px (the demo shell's sidebar offset eats the
                // rest), which classifies the runner's own container as mobile (sheet). Drop the
                // demo shell's desktop sidebar (fixed w-72 aside) and its offset, and pin the
                // runner to a ~1000px HOST width, so the tablet presentation shows AND the whole
                // runner stays inside the 1024px viewport (an element capture cannot reach a box
                // that overflows it horizontally). The runner's own steps rail is an <aside> too,
                // so the shell sidebar is targeted through its layout-stability class, not the tag.
                style += " aside.w-72 { display: none !important; }" +
                         " main { margin-left: 0 !important; max-width: none !important; width: auto !important; }" +
                         " .max-w-7xl { max-width: none !important; }" +
                         " [data-testid='signing-runner-demo'] { width: min(1000px, calc(100vw - 6rem)); max-width: none; }";
            }

            await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = style });
            await page.WaitForTimeoutAsync(150);

            foreach (var (screen, testId) in new[]
                     {
                         ("signing-completion-panel", "signing-completion-panel"),
                         ("signing-completion-waiting", "signing-completion-waiting")
                     })
            {
                var path = Path.Combine(Phase3Dir, $"{screen}--{name}--light--default.png");
                await CaptureElementAsync(page, testId, path);
                written.Add(path);
            }

            if (width == 1024)
            {
                var runnerPath = Path.Combine(Phase3Dir, $"signing-form-runner--{name}--light--default.png");
                await CaptureElementAsync(page, "signing-runner-demo", runnerPath);
                written.Add(runnerPath);
            }

            await context.CloseAsync();
        }

        foreach (var path in written)
        {
            Assert.IsTrue(File.Exists(path), $"expected the phase-3 capture {Path.GetFileName(path)} to be written");
        }

        // The review-round naming replaced the bare-width files; assert the directory holds the
        // full new set so a half-written regeneration cannot pass unnoticed.
        var expectedCount = (Variants.Length * 3) + 2 * (LightViewports.Length - 1) + 1;
        var files = Directory.GetFiles(Phase3Dir, "*.png");
        Assert.IsTrue(
            files.Length >= expectedCount,
            string.Create(CultureInfo.InvariantCulture,
                $"expected at least {expectedCount} phase-3 PNGs with the --{{width}}--{{theme}}--{{brand}} naming, found {files.Length}"));
    }

    /// <summary>
    /// Forces the theme/brand the way the phase-1 dark pass does: the root element and every
    /// already-rendered <c>[data-theme]</c> host flip together, and the opt-in indigo brand rides
    /// <c>data-tm-theme</c> on the root. The runner-owned <c>.tm-signing-form-runner__document</c>
    /// wrapper is the exception (phase 3 review): it is a deliberate LIGHT ISLAND (the white paper
    /// sheet keeps its light tokens inside a dark host), so it never flips with the page.
    /// </summary>
    private static async Task ApplyThemeAsync(IPage page, bool dark, bool indigo)
    {
        var theme = dark ? "dark" : "light";
        var brand = indigo
            ? "document.documentElement.setAttribute('data-tm-theme', 'indigo');"
            : "document.documentElement.removeAttribute('data-tm-theme');";
        await page.EvaluateAsync(
            "() => { document.documentElement.dataset.theme = '" + theme + "';" +
            " document.querySelectorAll('[data-theme]').forEach(el => { if (!el.classList.contains('tm-signing-form-runner__document')) { el.dataset.theme = '" + theme + "'; } });" +
            brand + " }");
    }

    private static async Task CaptureElementAsync(IPage page, string testId, string path)
    {
        // Start from a known scroll position: the previous element's scroll state (and the sticky
        // chrome reacting to it) must not bleed into this capture.
        await page.EvaluateAsync("() => window.scrollTo(0, 0)");
        var element = page.Locator($"[data-testid='{testId}']").First;
        await element.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(120);
        await element.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
        Console.WriteLine($"[signing-phase-3] wrote {path}");
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }

            current = current.Parent!;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
