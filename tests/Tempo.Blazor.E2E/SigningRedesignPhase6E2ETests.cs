using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Shared page plumbing for the phase-6 active-steps suites: the fake-host runner lives on the
/// <c>/signing-components</c> demo (<c>[data-testid='signing-active-steps-demo']</c>) — phone OTP
/// (demo code 123456), upload (fake store) and external verification (simulated provider) are
/// confirmed by the HOST, the SI2 opt-in contract of Signing redesign phase 6.
/// </summary>
internal static class SigningRedesignPhase6Specs
{
    public const string SigningComponentsUrl = "/signing-components";

    /// <summary>The demo OTP the fake host accepts.</summary>
    public const string DemoOtpCode = "123456";

    /// <summary>A valid 1×1 PNG, uploaded through the real browser file pipeline.</summary>
    public const string PngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    /// <summary>A >50 KB payload for the standalone image step's MaxFileSize rejection capture.</summary>
    public static byte[] OversizeBytes { get; } = new byte[60_000];

    /// <summary>
    /// Sends the phone code for the runner and waits for the OTP row with the running countdown
    /// (the send is Secondary and disabled while it runs — phase 6 review item 9).
    /// </summary>
    public static async Task SendPhoneForCountdownAsync(ILocator runner)
    {
        var phone = runner.Locator("input.tm-signing-phone-step__input").First;
        await phone.FillAsync("777 123 456");
        await phone.BlurAsync();
        await runner.Locator(".tm-signing-phone-step__send").First.ClickAsync();
        await Assertions.Expect(runner.Locator("input.tm-signing-phone-step__otp").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        await Assertions.Expect(runner.Locator(".tm-signing-phone-step__resend").First)
            .ToContainTextAsync("30", new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
    }

    public static string Phase6Dir
    {
        get
        {
            var dir = Path.Combine(FindRepoRoot().FullName,
                "tests", "Tempo.Blazor.E2E", "__screenshots__", "signing-redesign", "phase-6");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static async Task<IPage> OpenActiveStepsRunnerAsync(
        IBrowser browser,
        string baseUrl,
        int width,
        int height,
        bool mobile,
        Action<IBrowserContext> registerContext,
        Func<IPage, Task> waitForReady)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            HasTouch = mobile,
            IsMobile = mobile
        });
        registerContext(context);
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
        await page.GotoAsync($"{baseUrl}{SigningComponentsUrl}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });
        await waitForReady(page);
        var runner = GetActiveRunner(page);
        await runner.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(200);
        return page;
    }

    public static ILocator GetActiveRunner(IPage page) =>
        page.Locator("[data-testid='signing-active-steps-demo']").First;

    public static ILocator GetStepsRail(ILocator runner) => runner.Locator(".tm-signing-form-runner__steps").First;

    /// <summary>
    /// Completes the phone flow against the fake host: send (skipped when the OTP row is already
    /// up — the throttled send stays disabled while the countdown runs) → OTP row → (optional
    /// wrong code shows the localized validation) → correct code → verified state.
    /// </summary>
    public static async Task CompletePhoneFlowAsync(ILocator runner, string? wrongCode = null)
    {
        var otp = runner.Locator("input.tm-signing-phone-step__otp").First;
        if (!await otp.IsVisibleAsync())
        {
            var phone = runner.Locator("input.tm-signing-phone-step__input").First;
            await phone.FillAsync("777 123 456");
            await phone.BlurAsync();

            await runner.Locator(".tm-signing-phone-step__send").First.ClickAsync();
            await Assertions.Expect(otp).ToBeVisibleAsync();
            await Assertions.Expect(runner.Locator(".tm-signing-phone-step__resend").First)
                .ToContainTextAsync("30", new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
        }

        if (wrongCode is not null)
        {
            await otp.FillAsync(wrongCode);
            await runner.Locator(".tm-signing-phone-step__verify").First.ClickAsync();
            await Assertions.Expect(runner.Locator(".tm-signing-step-shell__validation").First)
                .ToContainTextAsync("not valid", new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
        }

        await otp.FillAsync(DemoOtpCode);
        await runner.Locator(".tm-signing-phone-step__verify").First.ClickAsync();
        await Assertions.Expect(runner.Locator(".tm-signing-phone-step__verified").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
    }

    /// <summary>
    /// Completes the upload flow: the picked PNG goes through the real browser file pipeline to
    /// the fake host, whose returned url lands on the attachment card. The attachment must be the
    /// CURRENT step first — only the current step renders (select it through the field list).
    /// </summary>
    public static async Task CompleteAttachmentFlowAsync(IPage page, ILocator runner, bool touch)
    {
        var steps = GetStepsRail(runner);
        await SigningRedesignPhase5Specs.SelectFieldAsync(page, steps, "active-attachment", touch);

        var input = runner.Locator("input[type='file']").First;
        await input.SetInputFilesAsync(new[]
        {
            new FilePayload { Name = "signature-photo.png", MimeType = "image/png", Buffer = Convert.FromBase64String(PngBase64) }
        });

        await Assertions.Expect(runner.Locator(".tm-signing-attachment-step__item").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var preview = runner.Locator("img.tm-signing-attachment-step__preview").First;
        await Assertions.Expect(preview).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var src = await preview.GetAttributeAsync("src");
        Assert.IsTrue(
            src is not null && src.StartsWith("data:image/png;base64,", StringComparison.Ordinal),
            $"the card previews the host-returned upload url (src starts with 'data:…', got '{src?[..Math.Min(40, src?.Length ?? 0)]}…')");
    }

    /// <summary>Completes the external flow: the click only notifies the host — the verified
    /// state appears after the simulated provider call (≈400 ms), never on the click itself.</summary>
    public static async Task CompleteExternalFlowAsync(ILocator runner)
    {
        var link = runner.Locator(".tm-signing-external-step__link").First;
        await Assertions.Expect(link).ToBeVisibleAsync();
        Assert.AreEqual("https://verify.example.test/session", await link.GetAttributeAsync("href"));
        var rel = await link.GetAttributeAsync("rel") ?? string.Empty;
        Assert.IsTrue(rel.Contains("noopener") && rel.Contains("noreferrer"), "the provider link must carry rel=noopener noreferrer");
        await Assertions.Expect(runner.Locator(".tm-signing-external-step__provider").First)
            .ToContainTextAsync("Demo Verify");

        await runner.Locator(".tm-signing-external-step__start-button").First.ClickAsync();

        // The click must NOT complete the step: first the host's loading context, then the
        // provider result (the value the host stores) flips the verified state.
        await Assertions.Expect(runner.Locator(".tm-signing-external-step__verified").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    public static async Task CaptureRunnerAsync(IPage page, string fileName, List<string> written, string? revealSelector = null)
    {
        await page.EvaluateAsync("() => window.scrollTo(0, 0)");
        // Hide the demo shell's app bars (the phase-3/4/5 convention) — element captures stitch
        // scrolled frames and the demo chrome would bleed in.
        await page.EvaluateAsync(
            "() => document.querySelectorAll('.demo-shell .sticky.top-0, .demo-shell .fixed.top-0').forEach(el => el.style.display = 'none')");
        var runner = GetActiveRunner(page);
        await runner.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(150);
        if (revealSelector is not null)
        {
            // The desktop panel caps at 100dvh and scrolls INTERNALLY, and the sticky action bar
            // rides on its bottom edge — the alert can sit entirely BEHIND that opaque bar (a
            // bare scrollIntoView block:nearest then no-ops, the element is "in view"). Reveal it
            // the way the package's signing-runner.js does: scroll the panel so the element's
            // bottom ends ABOVE the bar.
            await runner.Locator(revealSelector).First.EvaluateAsync(
                @"el => {
                    const panel = el.closest('.tm-signing-form-runner__step-panel');
                    if (!panel) { el.scrollIntoView({ block: 'nearest' }); return; }
                    const panelRect = panel.getBoundingClientRect();
                    const elRect = el.getBoundingClientRect();
                    const padding = parseFloat(getComputedStyle(panel).paddingBottom) || 0;
                    const contentBottom = panelRect.bottom - padding;
                    const bar = panel.querySelector('.tm-signing-form-runner__actions');
                    const barRect = bar ? bar.getBoundingClientRect() : null;
                    const usableBottom = barRect && barRect.bottom >= contentBottom - 2
                        ? barRect.top
                        : contentBottom;
                    const delta = elRect.bottom - usableBottom;
                    if (delta > 0) { panel.scrollTop += delta; }
                }");
            await page.WaitForTimeoutAsync(80);
        }

        var path = Path.Combine(Phase6Dir, fileName);
        await runner.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
        Console.WriteLine($"[signing-phase-6] wrote {path}");
        written.Add(path);
    }

    /// <summary>The REAL local preview (no host resolver): the standalone image step of the
    /// demo's Signing Steps gallery renders the RequestImageFileAsync data-url thumbnail.</summary>
    public static async Task CaptureLocalPreviewAsync(IPage page, List<string> written)
    {
        var step = page.Locator("[data-testid='signing-step-image']").First;
        await step.ScrollIntoViewIfNeededAsync();
        await step.Locator("input[type='file']").First.SetInputFilesAsync(new[]
        {
            new FilePayload { Name = "photo.png", MimeType = "image/png", Buffer = Convert.FromBase64String(PngBase64) }
        });

        var preview = step.Locator("img.tm-signing-attachment-step__preview").First;
        await Assertions.Expect(preview).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var src = await preview.GetAttributeAsync("src");
        Assert.IsTrue(
            src is not null && src.StartsWith("data:image/", StringComparison.Ordinal),
            "the standalone step's local preview is the real RequestImageFileAsync data url");

        await page.EvaluateAsync(
            "() => document.querySelectorAll('.demo-shell .sticky.top-0, .demo-shell .fixed.top-0').forEach(el => el.style.display = 'none')");
        await step.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(150);
        var path = Path.Combine(Phase6Dir, "signing-attachment-local-preview--standalone.png");
        await step.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
        Console.WriteLine($"[signing-phase-6] wrote {path}");
        written.Add(path);
    }

    /// <summary>The MaxFileSize rejection on the standalone image step (phase 6 review): a pick
    /// beyond the limit shows the localized "larger than" message and never joins the cards.</summary>
    public static async Task CaptureAttachmentTooLargeAsync(IPage page, List<string> written, string fileName)
    {
        var step = page.Locator("[data-testid='signing-step-image']").First;
        await step.ScrollIntoViewIfNeededAsync();
        await step.Locator("input[type='file']").First.SetInputFilesAsync(new[]
        {
            new FilePayload { Name = "too-big.png", MimeType = "image/png", Buffer = OversizeBytes }
        });

        await Assertions.Expect(step.Locator(".tm-signing-step-shell__validation").First)
            .ToContainTextAsync("larger than", new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
        // The step carries ONE seeded demo card — the rejected pick adds nothing.
        await Assertions.Expect(step.Locator(".tm-signing-attachment-step__item"))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        await page.EvaluateAsync(
            "() => document.querySelectorAll('.demo-shell .sticky.top-0, .demo-shell .fixed.top-0').forEach(el => el.style.display = 'none')");
        await step.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(150);
        var path = Path.Combine(Phase6Dir, fileName);
        await step.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
        Console.WriteLine($"[signing-phase-6] wrote {path}");
        written.Add(path);
    }

    /// <summary>The upload BUSY state (phase 6 review): the demo host takes ~1.2 s, so the
    /// "Uploading …" row, the disabled zone and the polite announcement are capturable.</summary>
    public static async Task CaptureAttachmentUploadingAsync(IPage page, ILocator runner, List<string> written, string fileName)
    {
        var input = runner.Locator("input[type='file']").First;
        await input.SetInputFilesAsync(new[]
        {
            new FilePayload { Name = "signature-photo.png", MimeType = "image/png", Buffer = Convert.FromBase64String(PngBase64) }
        });

        var uploading = runner.Locator(".tm-signing-attachment-step__uploading").First;
        await Assertions.Expect(uploading)
            .ToContainTextAsync("Uploading", new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
        await Assertions.Expect(runner.Locator("input[type='file']").First)
            .ToBeDisabledAsync(new LocatorAssertionsToBeDisabledOptions { Timeout = 5_000 });

        await page.EvaluateAsync(
            "() => document.querySelectorAll('.demo-shell .sticky.top-0, .demo-shell .fixed.top-0').forEach(el => el.style.display = 'none')");
        await runner.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(150);
        var path = Path.Combine(Phase6Dir, fileName);
        await runner.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
        Console.WriteLine($"[signing-phase-6] wrote {path}");
        written.Add(path);

        // The upload lands (the host returns the url) and the busy state clears.
        await Assertions.Expect(runner.Locator(".tm-signing-attachment-step__item").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Assertions.Expect(uploading).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5_000 });
    }

    /// <summary>The provider LOADING state (phase 6 review): the Start action stays in place,
    /// disabled and aria-busy, next to the indicator row — the demo provider takes ~800 ms.</summary>
    public static async Task CaptureExternalLoadingAsync(IPage page, ILocator runner, List<string> written, string fileName)
    {
        await runner.Locator(".tm-signing-external-step__start-button").First.ClickAsync();

        var loading = runner.Locator(".tm-signing-external-step__loading").First;
        await Assertions.Expect(loading).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        var start = runner.Locator(".tm-signing-external-step__start-button").First;
        await Assertions.Expect(start).ToBeDisabledAsync(new LocatorAssertionsToBeDisabledOptions { Timeout = 5_000 });
        Assert.AreEqual("true", await start.GetAttributeAsync("aria-busy"), "the in-flight Start action is aria-busy");

        await page.EvaluateAsync(
            "() => document.querySelectorAll('.demo-shell .sticky.top-0, .demo-shell .fixed.top-0').forEach(el => el.style.display = 'none')");
        await runner.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(150);
        var path = Path.Combine(Phase6Dir, fileName);
        await runner.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
        Console.WriteLine($"[signing-phase-6] wrote {path}");
        written.Add(path);

        // The provider confirms: the verified state replaces the call to action.
        await Assertions.Expect(runner.Locator(".tm-signing-external-step__verified").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    /// <summary>The provider ERROR state (phase 6 review): the demo failure toggle is flipped,
    /// Start reports the localized provider error through the step alert, and the toggle is
    /// restored afterwards.</summary>
    public static async Task CaptureExternalErrorAsync(IPage page, ILocator runner, List<string> written, string fileName)
    {
        var toggle = page.Locator("[data-testid='signing-active-steps-fail-toggle']").First;
        await toggle.CheckAsync();

        await runner.Locator(".tm-signing-external-step__start-button").First.ClickAsync();
        await Assertions.Expect(runner.Locator(".tm-signing-step-shell__validation").First)
            .ToContainTextAsync("failure", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await page.EvaluateAsync(
            "() => document.querySelectorAll('.demo-shell .sticky.top-0, .demo-shell .fixed.top-0').forEach(el => el.style.display = 'none')");
        await runner.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(150);
        var path = Path.Combine(Phase6Dir, fileName);
        await runner.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
        Console.WriteLine($"[signing-phase-6] wrote {path}");
        written.Add(path);

        await toggle.UncheckAsync();
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

/// <summary>
/// Signing redesign phase 6 — the active-steps contract captures on the <c>/signing-components</c>
/// demo: the phone countdown / verified states, the upload card with the host-returned url, the
/// external verified state, the standalone local preview and the all-confirmed completion, at
/// 1440 and 390 (touch), light/dark × default/indigo.
/// <para>
/// Like the other generators the CAPTURE test only runs when <c>TM_WRITE_BASELINES</c> is set:
/// <code>
/// TM_WRITE_BASELINES=1 dotnet test tests/Tempo.Blazor.E2E --filter "SigningRedesignPhase6E2ETests"
/// </code>
/// The behaviour assertions live in <see cref="SigningRedesignPhase6BehaviorE2ETests"/> (always on).
/// </para>
/// </summary>
[TestClass]
public class SigningRedesignPhase6E2ETests : BaselineGeneratorTestBase
{
    [TestMethod]
    [Description("Writes the phase-6 active-steps captures (1440/390, light/dark x default/indigo) for the UX review")]
    public async Task CaptureSigningRedesignPhase6Screens()
    {
        var written = new List<string>();

        // ── 1440 desktop: the three flows' states, light/default. ──
        var page = await SigningRedesignPhase6Specs.OpenActiveStepsRunnerAsync(
            Browser, BaseUrl, 1440, 900, mobile: false, RegisterContext, WaitForAppReadyAsync);
        var runner = SigningRedesignPhase6Specs.GetActiveRunner(page);
        var steps = SigningRedesignPhase6Specs.GetStepsRail(runner);

        // The runner opens on the CURRENT step = the phone step: send for the countdown state
        // (the send turns Secondary and throttles while the countdown runs).
        await SigningRedesignPhase6Specs.SendPhoneForCountdownAsync(runner);
        await SigningRedesignPhase6Specs.CaptureRunnerAsync(page, "signing-active-phone-countdown--1440--light--default.png", written);

        // Rejected code: the localized validation lands with the error owned by the OTP input.
        var otpInput = runner.Locator("input.tm-signing-phone-step__otp").First;
        await otpInput.FillAsync("000000");
        await runner.Locator(".tm-signing-phone-step__verify").First.ClickAsync();
        await Assertions.Expect(runner.Locator(".tm-signing-step-shell__validation").First)
            .ToContainTextAsync("not valid", new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
        // The alert renders at the step's end INSIDE the capped panel — reveal it so the capture
        // shows the error text, not just the red OTP input.
        await SigningRedesignPhase6Specs.CaptureRunnerAsync(
            page, "signing-active-phone-invalid--1440--light--default.png", written,
            revealSelector: ".tm-signing-step-shell__validation");

        await SigningRedesignPhase6Specs.CompletePhoneFlowAsync(runner);
        await Assertions.Expect(steps.Locator(".tm-signing-phone-step__verified").First).ToBeVisibleAsync();
        await SigningRedesignPhase6Specs.CaptureRunnerAsync(page, "signing-active-phone-verified--1440--light--default.png", written);

        // Upload: the BUSY state first ("Uploading …", disabled zone), then the uploaded card.
        await SigningRedesignPhase5Specs.SelectFieldAsync(page, steps, "active-attachment", touch: false);
        await SigningRedesignPhase6Specs.CaptureAttachmentUploadingAsync(
            page, runner, written, "signing-active-attachment-uploading--1440--light--default.png");
        await SigningRedesignPhase6Specs.CaptureRunnerAsync(page, "signing-active-attachment-uploaded--1440--light--default.png", written);

        // External: the provider's ERROR state (simulated), then LOADING, then the verified state.
        await steps.Locator(".tm-signing-form-runner__field-button[data-field-uuid='active-verification']").ClickAsync();
        await SigningRedesignPhase6Specs.CaptureExternalErrorAsync(
            page, runner, written, "signing-active-external-error--1440--light--default.png");
        await SigningRedesignPhase6Specs.CaptureExternalLoadingAsync(
            page, runner, written, "signing-active-external-loading--1440--light--default.png");
        await SigningRedesignPhase6Specs.CaptureRunnerAsync(page, "signing-active-external-verified--1440--light--default.png", written);
        await page.Context.CloseAsync();

        // ── The standalone LOCAL preview (real RequestImageFileAsync pipeline, no host resolver). ──
        page = await SigningRedesignPhase6Specs.OpenActiveStepsRunnerAsync(
            Browser, BaseUrl, 1440, 900, mobile: false, RegisterContext, WaitForAppReadyAsync);
        await SigningRedesignPhase6Specs.CaptureLocalPreviewAsync(page, written);
        await page.Context.CloseAsync();

        // ── 1440 dark/indigo: the countdown state, then the all-confirmed completion state. ──
        foreach (var (dark, indigo, theme, brand) in new[]
                 {
                     (true, false, "dark", "default"),
                     (false, true, "light", "indigo"),
                     (true, true, "dark", "indigo")
                 })
        {
            page = await SigningRedesignPhase6Specs.OpenActiveStepsRunnerAsync(
                Browser, BaseUrl, 1440, 900, mobile: false, RegisterContext, WaitForAppReadyAsync);
            await SigningRedesignPhase5Specs.ApplyThemeAsync(page, dark, indigo);
            var themedRunner = SigningRedesignPhase6Specs.GetActiveRunner(page);

            await SigningRedesignPhase6Specs.SendPhoneForCountdownAsync(themedRunner);
            await SigningRedesignPhase6Specs.CaptureRunnerAsync(page, $"signing-active-phone-countdown--1440--{theme}--{brand}.png", written);

            await SigningRedesignPhase6Specs.CompletePhoneFlowAsync(themedRunner);
            await SigningRedesignPhase6Specs.CompleteAttachmentFlowAsync(page, themedRunner, touch: false);
            var rail = SigningRedesignPhase6Specs.GetStepsRail(themedRunner);
            await rail.Locator(".tm-signing-form-runner__field-button[data-field-uuid='active-verification']").ClickAsync();
            await SigningRedesignPhase6Specs.CompleteExternalFlowAsync(themedRunner);
            await page.WaitForTimeoutAsync(150);
            await SigningRedesignPhase6Specs.CaptureRunnerAsync(page, $"signing-active-all-confirmed--1440--{theme}--{brand}.png", written);
            await page.Context.CloseAsync();
        }

        // ── 390 touch: the three flows inside the mobile sheet. ──
        page = await SigningRedesignPhase6Specs.OpenActiveStepsRunnerAsync(
            Browser, BaseUrl, 390, 844, mobile: true, RegisterContext, WaitForAppReadyAsync);
        runner = SigningRedesignPhase6Specs.GetActiveRunner(page);
        steps = SigningRedesignPhase6Specs.GetStepsRail(runner);

        await SigningRedesignPhase5Specs.ExpandSheetAsync(steps, touch: true);
        await SigningRedesignPhase6Specs.SendPhoneForCountdownAsync(runner);
        await SigningRedesignPhase6Specs.CaptureRunnerAsync(page, "signing-active-phone-countdown--390--light--default.png", written);

        await SigningRedesignPhase6Specs.CompletePhoneFlowAsync(runner);
        await SigningRedesignPhase6Specs.CaptureRunnerAsync(page, "signing-active-phone-verified--390--light--default.png", written);

        await SigningRedesignPhase5Specs.SelectFieldAsync(page, steps, "active-attachment", touch: true);
        await SigningRedesignPhase6Specs.CaptureAttachmentUploadingAsync(
            page, runner, written, "signing-active-attachment-uploading--390--light--default.png");
        await SigningRedesignPhase6Specs.CaptureRunnerAsync(page, "signing-active-attachment-uploaded--390--light--default.png", written);

        await SigningRedesignPhase5Specs.SelectFieldAsync(page, steps, "active-verification", touch: true);
        await SigningRedesignPhase6Specs.CaptureExternalLoadingAsync(
            page, runner, written, "signing-active-external-loading--390--light--default.png");
        await SigningRedesignPhase6Specs.CaptureRunnerAsync(page, "signing-active-external-verified--390--light--default.png", written);

        // The MaxFileSize rejection on the standalone image step at the touch viewport (the
        // review's attachment-too-large capture).
        await SigningRedesignPhase6Specs.CaptureAttachmentTooLargeAsync(
            page, written, "signing-attachment-too-large--390--light--default.png");
        await page.Context.CloseAsync();

        foreach (var path in written)
        {
            Assert.IsTrue(File.Exists(path), $"expected the phase-6 capture {Path.GetFileName(path)} to be written");
        }

        var files = Directory.GetFiles(SigningRedesignPhase6Specs.Phase6Dir, "*.png");
        Assert.IsTrue(
            files.Length >= written.Count,
            string.Create(CultureInfo.InvariantCulture,
                $"expected at least {written.Count} phase-6 PNGs with the --{{width}}--{{theme}}--{{brand}} naming, found {files.Length}"));
    }
}

/// <summary>
/// The phase-6 BEHAVIOUR suite (always on — not gated by <c>TM_WRITE_BASELINES</c>): at 1440 the
/// rejected OTP keeps the step open and the accepted code completes it, the upload lands with the
/// host url, the external click only notifies the host, and the ceremony completes; at 390 (touch)
/// the whole contract works inside the mobile sheet.
/// </summary>
[TestClass]
public class SigningRedesignPhase6BehaviorE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("At 1440: rejected OTP shows validation, host-confirmed steps complete the ceremony")]
    public async Task SigningActiveSteps_Desktop1440_HostContract_CompletesCeremony()
    {
        var page = await SigningRedesignPhase6Specs.OpenActiveStepsRunnerAsync(
            Browser, BaseUrl, 1440, 900, mobile: false, RegisterContext, WaitForAppReadyAsync);
        var runner = SigningRedesignPhase6Specs.GetActiveRunner(page);
        var steps = SigningRedesignPhase6Specs.GetStepsRail(runner);

        // The host contract: the rejected code shows the localized validation and the step stays
        // open; the accepted code completes it.
        await SigningRedesignPhase6Specs.CompletePhoneFlowAsync(runner, wrongCode: "000000");

        // The upload flow (the step navigates through the field list first): host-returned url on
        // the card.
        await SigningRedesignPhase6Specs.CompleteAttachmentFlowAsync(page, runner, touch: false);

        // The external flow: the click only notifies the host; the verified state comes from the
        // stored provider result.
        var rail = SigningRedesignPhase6Specs.GetStepsRail(runner);
        await rail.Locator(".tm-signing-form-runner__field-button[data-field-uuid='active-verification']").ClickAsync();
        await SigningRedesignPhase6Specs.CompleteExternalFlowAsync(runner);

        // All three required steps are host-confirmed: Complete is enabled and finishes.
        var complete = steps.Locator(".tm-signing-form-runner__complete").First;
        await Assertions.Expect(complete).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 5_000 });
        await complete.ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='signing-active-steps-status']"))
            .ToContainTextAsync("Contract completed", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    }

    [TestMethod]
    [Description("At 390 touch: the host-confirmed flows work inside the mobile sheet")]
    public async Task SigningActiveSteps_Touch390_HostContract_WorksInTheSheet()
    {
        var page = await SigningRedesignPhase6Specs.OpenActiveStepsRunnerAsync(
            Browser, BaseUrl, 390, 844, mobile: true, RegisterContext, WaitForAppReadyAsync);
        var runner = SigningRedesignPhase6Specs.GetActiveRunner(page);
        var steps = SigningRedesignPhase6Specs.GetStepsRail(runner);

        await SigningRedesignPhase5Specs.ExpandSheetAsync(steps, touch: true);

        // Phone: rejected code first (the alert lands in the step shell), then the demo code.
        await SigningRedesignPhase6Specs.CompletePhoneFlowAsync(runner, wrongCode: "000000");
        await Assertions.Expect(steps.Locator(".tm-signing-phone-step__verified").First).ToBeVisibleAsync();

        // Upload through the field list.
        await SigningRedesignPhase6Specs.CompleteAttachmentFlowAsync(page, runner, touch: true);

        // External: the verified state replaces the call to action.
        await SigningRedesignPhase5Specs.SelectFieldAsync(page, steps, "active-verification", touch: true);
        await SigningRedesignPhase6Specs.CompleteExternalFlowAsync(runner);

        var complete = steps.Locator(".tm-signing-form-runner__complete").First;
        await Assertions.Expect(complete).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 5_000 });
        await complete.TapAsync();
        await Assertions.Expect(page.Locator("[data-testid='signing-active-steps-status']"))
            .ToContainTextAsync("Contract completed", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    }
}
