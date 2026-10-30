using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Signing redesign phase 2 — the post-change captures of the surfaces the phase touched: the form
/// runner (single panel, B25) and every step component (accessible names, fieldset/legend groups,
/// the signature step's new title / required badge / instruction). Written to
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-2/</c> at 1440 / 1024 (touch) /
/// 390 / 320; the UX review diffs them against the <c>phase-1-baseline</c> captures and expects NO
/// visual change except the signature step gaining its title / requirement / instruction.
/// <para>
/// Like the other generators it only runs when <c>TM_WRITE_BASELINES</c> is set:
/// <code>
/// TM_WRITE_BASELINES=1 dotnet test tests/Tempo.Blazor.E2E --filter "SigningRedesignPhase2E2ETests"
/// </code>
/// </para>
/// </summary>
[TestClass]
public class SigningRedesignPhase2E2ETests : BaselineGeneratorTestBase
{
    private const string SigningComponentsUrl = "/signing-components";

    private static string Phase2Dir
    {
        get
        {
            var dir = Path.Combine(FindRepoRoot().FullName,
                "tests", "Tempo.Blazor.E2E", "__screenshots__", "signing-redesign", "phase-2");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static readonly (int Width, int Height, string Name)[] Viewports =
    [
        (1440, 900, "1440"),
        (1024, 768, "1024"),
        (390, 844, "390"),
        (320, 700, "320")
    ];

    /// <summary>The touched element screenshots of <c>/signing-components</c>, keyed like the baseline.</summary>
    private static readonly (string Screen, string TestId)[] TouchedScreens =
    [
        ("signing-text-step", "signing-step-text"),
        ("signing-number-step", "signing-step-number"),
        ("signing-date-step", "signing-step-date"),
        ("signing-choice-step", "signing-step-select"),
        ("signing-choice-radio-step", "signing-step-radio"),
        ("signing-attachment-step", "signing-step-image"),
        ("signing-phone-step", "signing-step-phone"),
        ("signing-signature-step", "signing-step-signature"),
        ("signing-external-step", "signing-step-payment"),
        ("signing-form-runner", "signing-runner-demo")
    ];

    [TestMethod]
    [Description("Writes the phase-2 Signing captures (runner + step components) for the UX review")]
    public async Task CaptureSigningRedesignPhase2Screens()
    {
        foreach (var (width, height, name) in Viewports)
        {
            // HasTouch only matters for the tablet viewport: Signing's coarse-pointer behaviour is
            // what the 1024 capture must show.
            var context = await Browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
                HasTouch = width == 1024
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
            await page.WaitForSelectorAsync("[data-testid='signing-runner-demo']",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 20_000 });

            // The demo shell's sticky page bar floats above the content and bleeds into element
            // captures of tall components, and the runner's sticky steps rail slides over the
            // content below it while Playwright scrolls an element into view. The capture
            // documents the COMPONENT, not the chrome.
            await page.AddStyleTagAsync(new PageAddStyleTagOptions
            {
                Content = ".sticky.top-0, .fixed.top-0 { visibility: hidden !important; } " +
                          ".tm-signing-form-runner__steps { position: static !important; }"
            });

            foreach (var (screen, testId) in TouchedScreens)
            {
                // Start every element from a known scroll position: the previous element's scroll
                // state (and the sticky chrome reacting to it) must not bleed into this capture.
                await page.EvaluateAsync("() => window.scrollTo(0, 0)");
                var element = page.Locator($"[data-testid='{testId}']").First;
                await element.ScrollIntoViewIfNeededAsync();
                await page.WaitForTimeoutAsync(120);
                var path = Path.Combine(Phase2Dir, $"{screen}--{name}.png");
                await element.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
                Console.WriteLine($"[signing-phase-2] wrote {path}");
            }

            await context.CloseAsync();
        }

        var written = Directory.GetFiles(Phase2Dir, "*.png");
        Assert.IsTrue(
            written.Length >= TouchedScreens.Length * Viewports.Length,
            string.Create(CultureInfo.InvariantCulture,
                $"expected at least {TouchedScreens.Length * Viewports.Length} phase-2 PNGs, found {written.Length}"));
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
