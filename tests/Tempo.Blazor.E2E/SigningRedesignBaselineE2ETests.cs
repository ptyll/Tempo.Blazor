using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Signing redesign phase 1 — the pre-redesign VISUAL BASELINE.
/// <para>
/// Before the Signing redesign starts, this generator captures every Signing screen (the same 22
/// surfaces the `output/imagegen/tempo-reimagined/Tempo.Blazor.Signing/` reference mockups cover:
/// every runner step type, completion waiting/completed, the designer, the editors and the product
/// panels) at 1440 / 1024 / 390 / 320 into
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-1-baseline/</c>. Later phases
/// diff their screenshots against these PNGs to prove "no visual regression at 1440 beyond the
/// intended role colours and dark-mode button contrast".
/// </para>
/// <para>
/// Like the other generators it only runs when <c>TM_WRITE_BASELINES</c> is set:
/// <code>
/// TM_WRITE_BASELINES=1 dotnet test tests/Tempo.Blazor.E2E --filter "SigningRedesignBaselineE2ETests"
/// </code>
/// </para>
/// </summary>
[TestClass]
public class SigningRedesignBaselineE2ETests : BaselineGeneratorTestBase
{
    private const string SigningComponentsUrl = "/signing-components";
    private const string SigningFromEditorUrl = "/signing-from-editor";

    /// <summary>
    /// Where the captures go: the pre-redesign baseline (<c>phase-1-baseline</c>, the default — also
    /// what the <c>git worktree</c> pre-change run writes) or the current-tree capture
    /// (<c>phase-1</c>, when <c>TM_SIGNING_BASELINE_DIR</c> is set to that name). The UX review
    /// diffs the two directories.
    /// </summary>
    private static string BaselineDir
    {
        get
        {
            var target = Environment.GetEnvironmentVariable("TM_SIGNING_BASELINE_DIR");
            var dirName = string.IsNullOrWhiteSpace(target) ? "phase-1-baseline" : target.Trim();
            var dir = Path.Combine(FindRepoRoot().FullName,
                "tests", "Tempo.Blazor.E2E", "__screenshots__", "signing-redesign", dirName);
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

    /// <summary>The element screenshots of <c>/signing-components</c>, keyed by reference-mockup name.</summary>
    private static readonly (string Screen, string TestId)[] ComponentScreens =
    [
        ("document-page-viewer", "signing-document-viewer"),
        ("document-comment-thread-panel", "document-comments-viewer"),
        ("condition-builder", "condition-builder"),
        ("formula-builder", "formula-builder"),
        ("recipient-role-editor", "recipient-role-editor-template"),
        ("field-editor-panel", "field-editor-panel"),
        ("pdf-template-designer", "pdf-template-designer"),
        ("signing-text-step", "signing-step-text"),
        ("signing-number-step", "signing-step-number"),
        ("signing-date-step", "signing-step-date"),
        ("signing-choice-step", "signing-step-select"),
        ("signing-attachment-step", "signing-step-image"),
        ("signing-phone-step", "signing-step-phone"),
        ("signing-external-step", "signing-step-payment"),
        ("signing-signature-step", "signature-draw-capture"),
        ("signing-form-runner", "signing-runner-demo"),
        ("signing-completion-panel", "signing-completion-panel"),
        ("signing-completion-waiting", "signing-completion-waiting"),
        ("submission-status-timeline", "submission-status-timeline"),
        ("share-link-panel", "share-link-panel"),
        ("pdf-signature-verification", "pdf-signature-verification"),
        ("audit-trail-viewer", "audit-trail-viewer")
    ];

    /// <summary>
    /// The surfaces the dark pass captures at 1440: the three role-colour screens, whose
    /// <c>--tm-signing-role-*</c> palette has its own dark variants.
    /// </summary>
    private static readonly string[] DarkScreens =
    [
        "signing-form-runner",
        "pdf-template-designer",
        "submission-status-timeline"
    ];

    [TestMethod]
    [Description("Writes the pre-redesign Signing baseline screenshots for the UX review")]
    public async Task CaptureSigningRedesignBaselines()
    {
        foreach (var (width, height, name) in Viewports)
        {
            // HasTouch only matters for the tablet viewport: Signing's coarse-pointer behaviour
            // (touch targets, tap alternatives) is what the 1024 capture must show, and a mouse-only
            // context reports (hover: hover) / (pointer: fine) and would capture the desktop one.
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
            await page.WaitForSelectorAsync("[data-testid='audit-trail-viewer']",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 20_000 });

            // The demo shell's sticky page bar (and its fixed mobile counterpart) floats above the
            // content and bleeds into the element captures of tall components at every viewport,
            // and the runner's sticky steps rail slides over the content below it while Playwright
            // scrolls an element into view. The baseline documents the COMPONENT, not the chrome.
            await page.AddStyleTagAsync(new PageAddStyleTagOptions
            {
                Content = ".sticky.top-0, .fixed.top-0 { visibility: hidden !important; } " +
                          ".tm-signing-form-runner__steps { position: static !important; }"
            });

            foreach (var (screen, testId) in ComponentScreens)
            {
                // Start every element from a known scroll position: the previous element's scroll
                // state (and the sticky chrome reacting to it) must not bleed into this capture.
                await page.EvaluateAsync("() => window.scrollTo(0, 0)");
                var element = page.Locator($"[data-testid='{testId}']").First;
                await element.ScrollIntoViewIfNeededAsync();
                await page.WaitForTimeoutAsync(120);
                var path = Path.Combine(BaselineDir, $"{screen}--{name}.png");
                await element.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
                Console.WriteLine($"[signing-baseline] wrote {path}");
            }

            // The document-editor -> signing bridge page is a screen of its own (full page, not an element).
            await page.GotoAsync($"{BaseUrl}{SigningFromEditorUrl}", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000
            });
            await WaitForAppReadyAsync(page);
            await page.WaitForTimeoutAsync(600);
            var pagePath = Path.Combine(BaselineDir, $"signing-template-from-editor-page--{name}.png");
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = pagePath,
                FullPage = true,
                Type = ScreenshotType.Png
            });
            Console.WriteLine($"[signing-baseline] wrote {pagePath}");

            await context.CloseAsync();
        }

        var written = Directory.GetFiles(BaselineDir, "*.png");
        Assert.IsTrue(
            written.Length >= (ComponentScreens.Length + 1) * Viewports.Length,
            string.Create(CultureInfo.InvariantCulture,
                $"expected at least {(ComponentScreens.Length + 1) * Viewports.Length} baseline PNGs, found {written.Length}"));
    }

    /// <summary>
    /// The dark counterpart of the 1440 pass: the same three role-colour surfaces
    /// (<see cref="DarkScreens"/>) captured with the dark theme forced, so the UX review sees the
    /// dark <c>--tm-signing-role-*</c> variants next to the light captures.
    /// <para>
    /// The theme is forced, not clicked: the demo shell wrapper carries its own
    /// <c>data-theme="light"</c> and would re-assert the light palette over a document-level
    /// attribute alone, so every already-rendered <c>[data-theme]</c> host flips with the root.
    /// </para>
    /// </summary>
    [TestMethod]
    [Description("Writes the dark-mode 1440 Signing captures for the UX review")]
    public async Task CaptureSigningRedesignDarkBaselines()
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
        await page.WaitForSelectorAsync("[data-testid='audit-trail-viewer']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 20_000 });

        await page.EvaluateAsync(
            "() => { document.documentElement.dataset.theme = 'dark';" +
            " document.querySelectorAll('[data-theme]').forEach(el => { el.dataset.theme = 'dark'; }); }");
        // The runner's sticky steps rail slides over the content below it while Playwright scrolls
        // an element into view; for the captures it renders in flow, like the light pass above.
        await page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = ".sticky.top-0, .fixed.top-0 { visibility: hidden !important; } " +
                      ".tm-signing-form-runner__steps { position: static !important; }"
        });
        await page.WaitForTimeoutAsync(150);

        foreach (var screen in DarkScreens)
        {
            var testId = ComponentScreens.Single(entry => entry.Screen == screen).TestId;
            await page.EvaluateAsync("() => window.scrollTo(0, 0)");
            var element = page.Locator($"[data-testid='{testId}']").First;
            await element.ScrollIntoViewIfNeededAsync();
            await page.WaitForTimeoutAsync(120);
            var path = Path.Combine(BaselineDir, $"{screen}--1440-dark.png");
            await element.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
            Console.WriteLine($"[signing-baseline] wrote {path}");
        }

        foreach (var screen in DarkScreens)
        {
            var path = Path.Combine(BaselineDir, $"{screen}--1440-dark.png");
            Assert.IsTrue(File.Exists(path), $"expected the dark capture {Path.GetFileName(path)} to be written");
        }

        await context.CloseAsync();
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
