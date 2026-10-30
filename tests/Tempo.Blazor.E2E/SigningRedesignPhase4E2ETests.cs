using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Signing redesign phase 4 (review round) — the post-change captures of the SURFACES THE PHASE
/// TOUCHED: the eight runner steps on the Tm components (text, number, date, phone, choice,
/// attachment, external, signature) on the <c>/signing-components</c> demo. Written to
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-4/</c> with the review-round
/// naming <c>…--{width}--{light|dark}--{default|indigo}.png</c>:
/// <list type="bullet">
/// <item>every step at 1440 in every light/dark × default/indigo combination;</item>
/// <item>every step at 1024 (touch), 390 and 320 in light default.</item>
/// </list>
/// <para>
/// Dark is FORCED exactly like the phase-3 pass (the demo shell carries its own
/// <c>data-theme="light"</c>, so every already-rendered <c>[data-theme]</c> host flips with the
/// root; the runner's light-island document wrapper is the exception). Indigo rides
/// <c>data-tm-theme="indigo"</c> on the root. Like the other generators it only runs when
/// <c>TM_WRITE_BASELINES</c> is set:
/// <code>
/// TM_WRITE_BASELINES=1 dotnet test tests/Tempo.Blazor.E2E --filter "SigningRedesignPhase4E2ETests"
/// </code>
/// </para>
/// </summary>
[TestClass]
public class SigningRedesignPhase4E2ETests : BaselineGeneratorTestBase
{
    private const string SigningComponentsUrl = "/signing-components";

    private static readonly (string Screen, string TestId)[] Steps =
    [
        ("signing-text-step", "signing-step-text"),
        ("signing-number-step", "signing-step-number"),
        ("signing-date-step", "signing-step-date"),
        ("signing-choice-step", "signing-step-select"),
        ("signing-radio-step", "signing-step-radio"),
        ("signing-attachment-step", "signing-step-image"),
        ("signing-phone-step", "signing-step-phone"),
        ("signing-signature-step", "signing-step-signature"),
        ("signing-external-step", "signing-step-payment")
    ];

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

    private static string Phase4Dir
    {
        get
        {
            var dir = Path.Combine(FindRepoRoot().FullName,
                "tests", "Tempo.Blazor.E2E", "__screenshots__", "signing-redesign", "phase-4");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    [TestMethod]
    [Description("Writes the phase-4 Signing step captures (all eight steps, light/dark x default/indigo) for the UX review")]
    public async Task CaptureSigningRedesignPhase4Screens()
    {        var written = new List<string>();

        // 1440 pass: every step in every light/dark x default/indigo combination.
        foreach (var (dark, indigo, theme, brand) in Variants)
        {
            var page = await OpenStepsPageAsync(1440, 900, touch: false);
            await ApplyThemeAsync(page, dark, indigo);
            await page.WaitForTimeoutAsync(150);

            foreach (var (screen, testId) in Steps)
            {
                var path = Path.Combine(Phase4Dir, $"{screen}--1440--{theme}--{brand}.png");
                await CaptureElementAsync(page, testId, path);
                written.Add(path);
            }
        }

        // Light pass: every step at the remaining viewports (1024 is the touch pass).
        foreach (var (width, height, name, touch) in LightViewports)
        {
            var page = await OpenStepsPageAsync(width, height, touch);
            await page.WaitForTimeoutAsync(150);

            foreach (var (screen, testId) in Steps)
            {
                var path = Path.Combine(Phase4Dir, $"{screen}--{name}--light--default.png");
                await CaptureElementAsync(page, testId, path);
                written.Add(path);
            }
        }

        foreach (var path in written)
        {
            Assert.IsTrue(File.Exists(path), $"expected the phase-4 capture {Path.GetFileName(path)} to be written");
        }

        var expectedCount = Variants.Length * Steps.Length + LightViewports.Length * Steps.Length;
        var files = Directory.GetFiles(Phase4Dir, "*.png");
        Assert.IsTrue(
            files.Length >= expectedCount,
            string.Create(CultureInfo.InvariantCulture,
                $"expected at least {expectedCount} phase-4 PNGs with the --{{width}}--{{theme}}--{{brand}} naming, found {files.Length}"));
    }

    /// <summary>
    /// The Draw mode of the signature step at the two review widths (phase 4 review round): the
    /// capture tabs and the pad at 1440 and 390 — the mobile one is where the review asked for
    /// proof that the pad keeps a usable height.
    /// </summary>
    [TestMethod]
    [Description("Writes the phase-4 Draw-mode signature captures at 1440 and 390")]
    public async Task CaptureSigningRedesignPhase4DrawSignature()
    {
        foreach (var (width, height, name, touch) in new[]
                 {
                     (1440, 900, "1440", false),
                     (390, 844, "390", false)
                 })
        {
            var page = await OpenStepsPageAsync(width, height, touch);
            var step = page.Locator("[data-testid='signing-step-signature']").First;
            await step.ScrollIntoViewIfNeededAsync();

            await step.GetByRole(AriaRole.Tab, new() { Name = "Draw" }).ClickAsync();
            await page.WaitForTimeoutAsync(150);

            var path = Path.Combine(Phase4Dir, $"signing-signature-step-draw--{name}--light--default.png");
            await step.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
            Console.WriteLine($"[signing-phase-4] wrote {path}");

            // The capture stays in Draw mode with a canvas that actually received the mode class.
            await Assertions.Expect(
                    step.Locator(".tm-signature-capture").First)
                .ToHaveAttributeAsync("data-mode", "Draw");
        }
    }

    /// <summary>
    /// A Czech signer types the decimal COMMA (phase 4 review): <c>PressSequentially("125,5")</c>
    /// in the runner switched to cs-CZ must store the value (the field list flips the amount to
    /// "Done") without firing the step's validation alert — and the token survives the commit.
    /// </summary>
    [TestMethod]
    [Description("cs number step accepts the typed decimal comma (125,5) without an alert")]
    public async Task SigningRunner_CsCulture_CommaNumberCommitsWithoutAlert()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1280, 720);
        await page.GotoAsync($"{BaseUrl}{SigningComponentsUrl}");
        await WaitForAppReadyAsync(page);

        var runner = page.Locator("[data-testid='signing-runner-demo']").First;
        await runner.ScrollIntoViewIfNeededAsync();
        var panel = runner.Locator(".tm-signing-form-runner__steps").First;

        await panel.Locator(".tm-signing-form-runner__field-button[data-field-uuid='runner-amount']").ClickAsync();
        await panel.Locator(".tm-signing-form-runner__language-select").SelectOptionAsync("cs-CZ");

        var input = panel.Locator("input.tm-signing-number-step__input").First;
        await input.ClickAsync();
        await input.PressSequentiallyAsync("125,5");
        await input.PressAsync("Tab");

        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__validation")).ToHaveCountAsync(0);
        await Assertions.Expect(input).ToHaveValueAsync("125,5");
        await Assertions.Expect(panel.Locator(
                ".tm-signing-form-runner__field-button[data-field-uuid='runner-amount'] .tm-signing-form-runner__field-status--done"))
            .ToHaveCountAsync(1);
    }

    private async Task<IPage> OpenStepsPageAsync(int width, int height, bool touch)
    {        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
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
        await page.WaitForSelectorAsync("[data-testid='signing-step-text']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 20_000 });
        return page;
    }

    /// <summary>
    /// Forces the theme/brand the way the phase-3 pass does: the root element and every
    /// already-rendered <c>[data-theme]</c> host flip together, and the opt-in indigo brand rides
    /// <c>data-tm-theme</c> on the root. The runner-owned document wrapper (the light island) is
    /// the exception; the steps demo does not render one.
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
        // Start from a known scroll position: the previous element's scroll state must not bleed
        // into this capture.
        await page.EvaluateAsync("() => window.scrollTo(0, 0)");
        var element = page.Locator($"[data-testid='{testId}']").First;
        await element.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(120);
        await element.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
        Console.WriteLine($"[signing-phase-4] wrote {path}");
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
