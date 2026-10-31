using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Shared page plumbing for the phase-5 runner suites: the runner lives on the
/// <c>/signing-components</c> demo (<c>[data-testid='signing-runner-demo']</c>); the mobile suite
/// runs with <c>HasTouch</c>/<c>IsMobile</c> contexts and drives the walkthrough with
/// <c>TapAsync</c> (phase 5 review), the signature stroke goes through CDP touch events, and the
/// keyboard simulation resizes the viewport onto a focused input.
/// </summary>
internal static class SigningRedesignPhase5Specs
{
    public const string SigningComponentsUrl = "/signing-components";

    public static string Phase5Dir
    {
        get
        {
            var dir = Path.Combine(FindRepoRoot().FullName,
                "tests", "Tempo.Blazor.E2E", "__screenshots__", "signing-redesign", "phase-5");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static async Task<IPage> OpenRunnerAsync(
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
        var runner = GetRunner(page);
        await runner.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(200);
        return page;
    }

    public static ILocator GetRunner(IPage page) => page.Locator("[data-testid='signing-runner-demo']").First;

    public static ILocator GetStepsRail(ILocator runner) => runner.Locator(".tm-signing-form-runner__steps").First;

    /// <summary>
    /// Selects a field through the list and POSITIVELY waits for the step landing. The sheet lands
    /// in the FULL edit state after every navigation (phase 5 review), so the list may be folded:
    /// when it is hidden the helper first walks More → "Field list" to bring it back. The tap can
    /// land while the sheet is still settling from the previous landing's smooth auto-scroll (the
    /// pressed row shifts and the tap fires on the list container, not the row), so a swallowed tap
    /// retries before the assert gives up. The landing is asserted through the <c>aria-current</c>
    /// ATTRIBUTE — the row hides again with the full state, so visibility would be the wrong probe.
    /// </summary>
    public static async Task SelectFieldAsync(IPage page, ILocator steps, string fieldUuid, bool touch)
    {
        var row = steps.Locator($".tm-signing-form-runner__field-button[data-field-uuid='{fieldUuid}']");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!await row.IsVisibleAsync())
            {
                await OpenFieldListAsync(steps, touch);
            }

            if (touch)
            {
                await row.TapAsync();
            }
            else
            {
                await row.ClickAsync();
            }

            try
            {
                await Assertions.Expect(row)
                    .ToHaveAttributeAsync("aria-current", "step", new LocatorAssertionsToHaveAttributeOptions { Timeout = 3_000 });
                return;
            }
            catch (InvalidOperationException)
            {
                // The tap landed mid-settle; the next attempt re-stabilizes the list first.
            }
        }

        Assert.Fail($"the field list never landed on {fieldUuid}");
    }

    /// <summary>Brings the field list back through the More menu's "Field list" entry (full →
    /// expanded; the entry also scrolls the list into view and focuses the current row).</summary>
    public static async Task OpenFieldListAsync(ILocator steps, bool touch)
    {
        var more = steps.Locator(".tm-signing-form-runner__more > button");
        if (!await more.IsVisibleAsync())
        {
            Assert.Fail("the More trigger is not visible — the sticky action bar needs the sheet open");
        }

        if (touch)
        {
            await more.TapAsync();
        }
        else
        {
            await more.ClickAsync();
        }

        var entry = steps.Locator(".tm-signing-form-runner__more-fields");
        await Assertions.Expect(entry).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        if (touch)
        {
            await entry.TapAsync();
        }
        else
        {
            await entry.ClickAsync();
        }

        await Assertions.Expect(steps.Locator(".tm-signing-form-runner__field-list").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
    }

    /// <summary>Collapses the sheet onto the peek state through the HANDLE (the sheet's only
    /// aria-expanded toggle — the phase-2 Minimize row is gone).</summary>
    public static async Task CollapseSheetAsync(ILocator steps, bool touch)
    {
        var handle = steps.Locator(".tm-signing-form-runner__sheet-handle");
        if (await handle.GetAttributeAsync("aria-expanded") != "true")
        {
            return;
        }

        if (touch)
        {
            await handle.TapAsync();
        }
        else
        {
            await handle.ClickAsync();
        }

        await Assertions.Expect(steps.Locator(".tm-signing-form-runner__step-panel").First)
            .Not.ToBeVisibleAsync();
    }

    /// <summary>Expands the sheet: the peek state's "Sign now" CTA first, the handle as fallback.</summary>
    public static async Task ExpandSheetAsync(ILocator steps, bool touch)
    {
        if (await steps.Locator(".tm-signing-form-runner__step-panel").First.IsVisibleAsync())
        {
            return;
        }

        var cta = steps.Locator(".tm-signing-form-runner__mobile-expand").First;
        if (await cta.IsVisibleAsync())
        {
            if (touch)
            {
                await cta.TapAsync();
            }
            else
            {
                await cta.ClickAsync();
            }
        }
        else
        {
            var handle = steps.Locator(".tm-signing-form-runner__sheet-handle");
            if (touch)
            {
                await handle.TapAsync();
            }
            else
            {
                await handle.ClickAsync();
            }
        }

        await Assertions.Expect(steps.Locator(".tm-signing-form-runner__step-panel").First).ToBeVisibleAsync();
    }

    public static async Task CaptureRunnerAsync(IPage page, string fileName, List<string> written)
    {
        await page.EvaluateAsync("() => window.scrollTo(0, 0)");
        // Hide the demo shell's app bars (the desktop sticky bar and the mobile fixed header): an
        // element screenshot stitches scrolled frames, so the demo chrome would bleed into the
        // runner capture (the phase-3/4 convention, extended to the mobile header — the tall
        // full-sheet captures stitch at 390).
        await page.EvaluateAsync(
            "() => document.querySelectorAll('.demo-shell .sticky.top-0, .demo-shell .fixed.top-0').forEach(el => el.style.display = 'none')");
        var runner = GetRunner(page);
        await runner.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(150);
        var path = Path.Combine(Phase5Dir, fileName);
        await runner.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
        Console.WriteLine($"[signing-phase-5] wrote {path}");
        written.Add(path);
    }

    /// <summary>A VIEWPORT capture for the states that paint outside the runner's own bounds
    /// (the More menu's fixed bottom sheet).</summary>
    public static async Task CaptureViewportAsync(IPage page, string fileName, List<string> written)
    {
        await page.EvaluateAsync(
            "() => document.querySelectorAll('.demo-shell .sticky.top-0, .demo-shell .fixed.top-0').forEach(el => el.style.display = 'none')");
        await page.WaitForTimeoutAsync(150);
        var path = Path.Combine(Phase5Dir, fileName);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png });
        Console.WriteLine($"[signing-phase-5] wrote {path}");
        written.Add(path);
    }

    public static async Task ApplyThemeAsync(IPage page, bool dark, bool indigo)
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

    /// <summary>Draws a stroke on the signature canvas with CDP TOUCH events (phase 5 review): the
    /// pad receives real pointerdown/move/up of pointerType touch, exactly the finger path.</summary>
    public static async Task DrawSignatureByTouchAsync(IPage page, ILocator signatureCapture)
    {
        var canvas = signatureCapture.Locator(".tm-signature-capture__canvas").First;
        await canvas.ScrollIntoViewIfNeededAsync();
        var box = await canvas.BoundingBoxAsync();
        Assert.IsNotNull(box, "Signature canvas should have a bounding box.");

        var session = await page.Context.NewCDPSessionAsync(page);
        async Task TouchAsync(string type, params (double X, double Y)[] points)
        {
            var payload = new Dictionary<string, object>
            {
                ["type"] = type,
                ["touchPoints"] = points.Length == 0
                    ? Array.Empty<object>()
                    : points.Select(p => (object)new Dictionary<string, object>
                    {
                        ["x"] = p.X,
                        ["y"] = p.Y
                    }).ToArray()
            };
            await session.SendAsync("Input.dispatchTouchEvent", payload);
        }

        var y0 = box!.Y + box.Height * 0.35;
        await TouchAsync("touchStart", (box.X + box.Width * 0.2, y0));
        await TouchAsync("touchMove", (box.X + box.Width * 0.2, y0), (box.X + box.Width * 0.42, box.Y + box.Height * 0.25));
        await TouchAsync("touchMove", (box.X + box.Width * 0.42, box.Y + box.Height * 0.25), (box.X + box.Width * 0.7, box.Y + box.Height * 0.45));
        await TouchAsync("touchEnd");
    }

    /// <summary>The active field overlay must sit ABOVE the sheet top after a step landing.</summary>
    public static async Task AssertActiveFieldAboveSheetAsync(IPage page)
    {
        var bounds = await page.EvaluateAsync<double[]>(
            """
            () => {
                const field = document.querySelector('.tm-signing-field--selected');
                const sheet = document.querySelector('.tm-signing-form-runner__steps');
                if (!field || !sheet) return [-1, -1];
                return [field.getBoundingClientRect().bottom, sheet.getBoundingClientRect().top];
            }
            """);

        Assert.IsTrue(bounds[0] >= 0, "the runner renders a selected field overlay");
        Assert.IsTrue(
            bounds[0] <= bounds[1] + 2,
            $"the active field must sit above the sheet after a step landing (field bottom={bounds[0]}, sheet top={bounds[1]})");
    }

    /// <summary>The primary CTA (Next / Complete) must be inside the viewport.</summary>
    public static async Task AssertCtaInsideViewportAsync(IPage page, ILocator steps)
    {
        // The primary action is contextual: Next while steps remain, Complete on the last one.
        var next = steps.Locator(".tm-signing-form-runner__next, .tm-signing-form-runner__complete").First;
        await Assertions.Expect(next).ToBeVisibleAsync();
        var box = await next.BoundingBoxAsync();
        Assert.IsNotNull(box, "the CTA must have a bounding box");
        var viewport = page.ViewportSize;
        Assert.IsTrue(
            box.Y >= 0 && box.Y + box.Height <= viewport!.Height,
            $"the CTA stays inside the viewport (y={box.Y}, bottom={box.Y + box.Height}, viewport={viewport.Height})");
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
/// Signing redesign phase 5 — the runner's mobile bottom sheet on the <c>/signing-components</c>
/// demo: the peek / expanded / full sheet states, the touch walkthrough with the radio-card choice
/// fields (SI1) and the coarse-pointer Draw default (SI3), the auto-scroll that keeps the active
/// field above the sheet, and the no-horizontal-scroll guard at 320. Screenshot captures go to
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-5/</c> with the review-round
/// naming <c>…--{width}--{light|dark}--{default|indigo}.png</c>.
/// <para>
/// Like the other generators the CAPTURE test only runs when <c>TM_WRITE_BASELINES</c> is set:
/// <code>
/// TM_WRITE_BASELINES=1 dotnet test tests/Tempo.Blazor.E2E --filter "SigningRedesignPhase5E2ETests"
/// </code>
/// The behaviour assertions live in <see cref="SigningRedesignPhase5BehaviorE2ETests"/> (always on).
/// </para>
/// </summary>
[TestClass]
public class SigningRedesignPhase5E2ETests : BaselineGeneratorTestBase
{
    /// <summary>
    /// Writes the phase-5 sheet captures (phase 5 review evidence set): the peek / full / choice
    /// cards / signature draw / More menu at 390 (touch), the peek (plus dark + indigo variants)
    /// and the full + cards at 320, the tablet docked panel at 768×1024, the docked panel at
    /// 1280×800 (the real host content width — the old 1024 capture pinned the runner and cut the
    /// rail off), and the docked desktop at 1440 in light/dark × default/indigo.
    /// </summary>
    [TestMethod]
    [Description("Writes the phase-5 runner sheet captures (390/320/768/1280/1440, light/dark x default/indigo) for the UX review")]
    public async Task CaptureSigningRedesignPhase5Screens()
    {
        var written = new List<string>();

        // ── 390 touch pass: peek → full → choice cards → signature draw → More menu. ──
        var page = await SigningRedesignPhase5Specs.OpenRunnerAsync(Browser, BaseUrl, 390, 844, mobile: true, RegisterContext, WaitForAppReadyAsync);
        var runner = SigningRedesignPhase5Specs.GetRunner(page);
        var steps = SigningRedesignPhase5Specs.GetStepsRail(runner);

        await SigningRedesignPhase5Specs.CollapseSheetAsync(steps, touch: true);
        await SigningRedesignPhase5Specs.CaptureRunnerAsync(page, "signing-runner-sheet-peek--390--light--default.png", written);

        // EXPANDED (the "Sign now" CTA): field list + step.
        await SigningRedesignPhase5Specs.ExpandSheetAsync(steps, touch: true);
        await SigningRedesignPhase5Specs.CaptureRunnerAsync(page, "signing-runner-sheet-expanded--390--light--default.png", written);

        // FULL: a list tap lands the active edit — step + sticky action bar, list folded away.
        await SigningRedesignPhase5Specs.SelectFieldAsync(page, steps, "runner-name", touch: true);
        await Assertions.Expect(steps.Locator(".tm-signing-form-runner__field-list").First).Not.ToBeVisibleAsync();
        await SigningRedesignPhase5Specs.CaptureRunnerAsync(page, "signing-runner-sheet-full--390--light--default.png", written);

        // The delivery choice renders as radio CARDS in the full sheet (SI1).
        await SigningRedesignPhase5Specs.SelectFieldAsync(page, steps, "runner-delivery", touch: true);
        await Assertions.Expect(steps.Locator(".tm-signing-choice-step .tm-radio-option").First).ToBeVisibleAsync();
        await SigningRedesignPhase5Specs.CaptureRunnerAsync(page, "signing-runner-choice-cards--390--light--default.png", written);

        // The signature step starts on the DRAW pad (SI3 coarse-pointer default).
        await SigningRedesignPhase5Specs.SelectFieldAsync(page, steps, "runner-signature", touch: true);
        await Assertions.Expect(steps.Locator(".tm-signature-capture").First).ToHaveAttributeAsync("data-mode", "Draw");
        await SigningRedesignPhase5Specs.CaptureRunnerAsync(page, "signing-runner-signature-draw--390--light--default.png", written);

        // The More menu of the sticky action bar. On mobile the menu presents as a fixed bottom
        // sheet, which an element stitch would miss — this one state is a VIEWPORT capture.
        await steps.Locator(".tm-signing-form-runner__more > button").TapAsync();
        await Assertions.Expect(steps.Locator(".tm-signing-form-runner__more-fields").First).ToBeVisibleAsync();
        await SigningRedesignPhase5Specs.CaptureViewportAsync(page, "signing-runner-more-menu--390--light--default.png", written);
        await page.Keyboard.PressAsync("Escape");
        await page.Context.CloseAsync();

        // ── 390 dark + indigo PEEK variants. ──
        foreach (var (dark, indigo, theme, brand) in new[]
                 {
                     (true, false, "dark", "default"),
                     (false, true, "light", "indigo")
                 })
        {
            page = await SigningRedesignPhase5Specs.OpenRunnerAsync(Browser, BaseUrl, 390, 844, mobile: true, RegisterContext, WaitForAppReadyAsync);
            await SigningRedesignPhase5Specs.ApplyThemeAsync(page, dark, indigo);
            await page.WaitForTimeoutAsync(150);
            await SigningRedesignPhase5Specs.CollapseSheetAsync(SigningRedesignPhase5Specs.GetStepsRail(SigningRedesignPhase5Specs.GetRunner(page)), touch: true);
            await SigningRedesignPhase5Specs.CaptureRunnerAsync(page, $"signing-runner-sheet-peek--390--{theme}--{brand}.png", written);
            await page.Context.CloseAsync();
        }

        // ── 320: the narrowest supported width — peek, full and the choice cards. ──
        page = await SigningRedesignPhase5Specs.OpenRunnerAsync(Browser, BaseUrl, 320, 640, mobile: true, RegisterContext, WaitForAppReadyAsync);
        runner = SigningRedesignPhase5Specs.GetRunner(page);
        steps = SigningRedesignPhase5Specs.GetStepsRail(runner);
        await SigningRedesignPhase5Specs.CollapseSheetAsync(steps, touch: true);
        await SigningRedesignPhase5Specs.CaptureRunnerAsync(page, "signing-runner-sheet-peek--320--light--default.png", written);

        await SigningRedesignPhase5Specs.ExpandSheetAsync(steps, touch: true);
        await SigningRedesignPhase5Specs.CaptureRunnerAsync(page, "signing-runner-sheet-expanded--320--light--default.png", written);

        await SigningRedesignPhase5Specs.SelectFieldAsync(page, steps, "runner-delivery", touch: true);
        await Assertions.Expect(steps.Locator(".tm-signing-choice-step .tm-radio-option").First).ToBeVisibleAsync();
        await Assertions.Expect(steps.Locator(".tm-signing-form-runner__field-list").First).Not.ToBeVisibleAsync();
        await SigningRedesignPhase5Specs.CaptureRunnerAsync(page, "signing-runner-sheet-full--320--light--default.png", written);

        await SigningRedesignPhase5Specs.CaptureRunnerAsync(page, "signing-runner-choice-cards--320--light--default.png", written);
        await page.Context.CloseAsync();

        // ── 768×1024 (tablet portrait): the docked two-column panel (≥ 640 keeps docked). ──
        page = await SigningRedesignPhase5Specs.OpenRunnerAsync(Browser, BaseUrl, 768, 1024, mobile: true, RegisterContext, WaitForAppReadyAsync);
        await SigningRedesignPhase5Specs.CaptureRunnerAsync(page, "signing-runner-tablet--768--light--default.png", written);
        await page.Context.CloseAsync();

        // ── 1280×800: the DOCKED capture at the demo host's real content width (review: the old
        //    1024 capture pinned the runner to ~1000px, and the demo column cut the right rail
        //    off — no pin here, the runner classifies docked on its own container). ──
        page = await SigningRedesignPhase5Specs.OpenRunnerAsync(Browser, BaseUrl, 1280, 800, mobile: false, RegisterContext, WaitForAppReadyAsync);
        var dockedWidth = await page.EvaluateAsync<double>(
            "() => document.querySelector(\"[data-testid='signing-runner-demo']\").getBoundingClientRect().width");
        Assert.IsTrue(dockedWidth >= 640, $"the desktop runner must classify docked (width={dockedWidth})");
        await SigningRedesignPhase5Specs.CaptureRunnerAsync(page, "signing-runner-docked--1280--light--default.png", written);
        await page.Context.CloseAsync();

        // ── 1440 desktop: light/dark × default/indigo docked. ──
        foreach (var (dark, indigo, theme, brand) in new[]
                 {
                     (false, false, "light", "default"),
                     (true, false, "dark", "default"),
                     (false, true, "light", "indigo"),
                     (true, true, "dark", "indigo")
                 })
        {
            page = await SigningRedesignPhase5Specs.OpenRunnerAsync(Browser, BaseUrl, 1440, 900, mobile: false, RegisterContext, WaitForAppReadyAsync);
            await SigningRedesignPhase5Specs.ApplyThemeAsync(page, dark, indigo);
            await page.WaitForTimeoutAsync(150);
            await SigningRedesignPhase5Specs.CaptureRunnerAsync(page, $"signing-runner-docked--1440--{theme}--{brand}.png", written);
            await page.Context.CloseAsync();
        }

        foreach (var path in written)
        {
            Assert.IsTrue(File.Exists(path), $"expected the phase-5 capture {Path.GetFileName(path)} to be written");
        }

        var files = Directory.GetFiles(SigningRedesignPhase5Specs.Phase5Dir, "*.png");
        Assert.IsTrue(
            files.Length >= written.Count,
            string.Create(CultureInfo.InvariantCulture,
                $"expected at least {written.Count} phase-5 PNGs with the --{{width}}--{{theme}}--{{brand}} naming, found {files.Length}"));
    }
}

/// <summary>
/// The phase-5 BEHAVIOUR suite (always on — not gated by <c>TM_WRITE_BASELINES</c>): the whole
/// guide works by TOUCH at 390, the mobile action bar keeps the primary the widest at 320, the
/// Previous landing keeps the field above the expanded sheet, an overlay tap in peek lands in full
/// above the sheet, a CDP touch stroke fills the signature without scrolling the sheet, and the
/// keyboard simulation (viewport shrunk onto a focused input) keeps the CTA visible.
/// </summary>
[TestClass]
public class SigningRedesignPhase5BehaviorE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("Completes the runner by touch at 390: radio cards, Draw default, field above the sheet, CTA visible")]
    public async Task SigningRunner_TouchWalkthrough_RadioCards_DrawDefault_FieldAboveSheet()
    {
        var page = await SigningRedesignPhase5Specs.OpenRunnerAsync(Browser, BaseUrl, 390, 844, mobile: true, RegisterContext, WaitForAppReadyAsync);
        var runner = SigningRedesignPhase5Specs.GetRunner(page);
        var steps = SigningRedesignPhase5Specs.GetStepsRail(runner);

        // The mobile layout (the demo's viewport scope resolves it) renders the delivery choice
        // as RADIO CARDS instead of the system select. Taps go through TapAsync (HasTouch context).
        await SigningRedesignPhase5Specs.ExpandSheetAsync(steps, touch: true);
        await SigningRedesignPhase5Specs.SelectFieldAsync(page, steps, "runner-delivery", touch: true);
        var cards = steps.Locator(".tm-signing-choice-step .tm-radio-option");
        await Assertions.Expect(cards.First).ToBeVisibleAsync();
        Assert.IsTrue(
            await steps.Locator(".tm-signing-choice-step__select select").CountAsync() == 0,
            "the mobile radio cards replace the system select for the 2-option delivery field");

        // A list tap landed in the FULL edit state: the field list is folded away.
        Assert.IsFalse(
            await steps.Locator(".tm-signing-form-runner__field-list").First.IsVisibleAsync(),
            "the full edit state hides the field list (step + sticky action bar only)");

        // Tap the "Paper" card — the same stable value the select raised.
        await cards.First.TapAsync();
        await Assertions.Expect(steps.Locator(".tm-signing-choice-step .tm-radio-option").First)
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex(".*tm-radio-checked.*"));

        // SI3: the signature step starts on the DRAW pad (coarse pointer in the mobile context).
        await SigningRedesignPhase5Specs.SelectFieldAsync(page, steps, "runner-signature", touch: true);
        await Assertions.Expect(steps.Locator(".tm-signature-capture").First).ToHaveAttributeAsync("data-mode", "Draw");

        // A CDP TOUCH stroke fills the pad: the value becomes non-empty (the preview renders) and
        // the sheet's internal scroll does not move while drawing.
        var signatureCapture = steps.Locator(".tm-signature-capture").First;
        var scrollTopBefore = await page.EvaluateAsync<double>(
            "() => { const el = document.querySelector('.tm-signing-form-runner__step-panel'); return el ? el.scrollTop : -1; }");
        await SigningRedesignPhase5Specs.DrawSignatureByTouchAsync(page, signatureCapture);
        await Assertions.Expect(runner.Locator(".tm-signature-capture__preview").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var scrollTopAfter = await page.EvaluateAsync<double>(
            "() => { const el = document.querySelector('.tm-signing-form-runner__step-panel'); return el ? el.scrollTop : -1; }");
        Assert.AreEqual(scrollTopBefore, scrollTopAfter, "drawing must not scroll the sheet");

        // The active field stays visible ABOVE the sheet after a step landing, and the CTA is
        // inside the viewport.
        await SigningRedesignPhase5Specs.AssertActiveFieldAboveSheetAsync(page);
        await SigningRedesignPhase5Specs.AssertCtaInsideViewportAsync(page, steps);

        // Finish the ceremony by touch.
        await SigningRedesignPhase5Specs.SelectFieldAsync(page, steps, "runner-name", touch: true);
        var name = steps.Locator("input.tm-signing-text-step__input").First;
        await name.FillAsync("Alex Johnson");
        await name.BlurAsync();

        await SigningRedesignPhase5Specs.SelectFieldAsync(page, steps, "runner-amount", touch: true);
        var amount = steps.Locator("input.tm-signing-number-step__input").First;
        await amount.FillAsync("100");
        await amount.PressAsync("Tab");

        await SigningRedesignPhase5Specs.SelectFieldAsync(page, steps, "runner-extra-toggle", touch: true);
        await steps.Locator(".tm-signing-choice-step .tm-checkbox-label").First.TapAsync();

        await SigningRedesignPhase5Specs.SelectFieldAsync(page, steps, "runner-extra-note", touch: true);
        var note = steps.Locator("input.tm-signing-text-step__input").First;
        await note.FillAsync("Board approved");
        await note.BlurAsync();

        await SigningRedesignPhase5Specs.SelectFieldAsync(page, steps, "runner-date", touch: true);
        var date = steps.Locator("input.tm-signing-date-step__input").First;
        await date.FillAsync("2026-05-08");
        await date.BlurAsync();

        await Assertions.Expect(steps.Locator(".tm-signing-form-runner__complete")).ToBeEnabledAsync();
        await steps.Locator(".tm-signing-form-runner__complete").TapAsync();
        await Assertions.Expect(page.Locator("[data-testid='signing-runner-status']")).ToContainTextAsync("Completed");
    }

    /// <summary>The no-horizontal-scroll guard at the narrowest supported width (phase DoD), and
    /// the step input stays visible INSIDE the sheet after Next (the full edit state keeps the
    /// step panel + sticky action bar in view at 320×640).</summary>
    [TestMethod]
    [Description("At 320 no horizontal scroll, and after Next the step input is visible inside the sheet")]
    public async Task SigningRunner_At320_NoHorizontalScroll_AndStepInputVisibleAfterNext()
    {
        var page = await SigningRedesignPhase5Specs.OpenRunnerAsync(Browser, BaseUrl, 320, 640, mobile: true, RegisterContext, WaitForAppReadyAsync);
        var runner = SigningRedesignPhase5Specs.GetRunner(page);
        var steps = SigningRedesignPhase5Specs.GetStepsRail(runner);
        await SigningRedesignPhase5Specs.ExpandSheetAsync(steps, touch: true);
        await runner.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(150);

        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.IsTrue(
            overflow <= 1,
            $"no horizontal scroll at 320px (scrollWidth - clientWidth = {overflow}px)");

        // Next lands in the full edit state: the (required) first step refuses with its message —
        // fill it first, then advance and check the NEXT step's input is visible inside the sheet.
        var name = steps.Locator("input.tm-signing-text-step__input").First;
        await name.FillAsync("Alex Johnson");
        await name.BlurAsync();
        await steps.Locator(".tm-signing-form-runner__next").TapAsync();

        // The delivery step presents as radio CARDS on mobile (SI1) — its native radios are
        // visually-hidden (the card label is the hit target), so visibility is measured as "a
        // control of the current step intersects the sheet's own viewport", after the sheet's
        // internal scroller has brought the step into view.
        var currentStep = steps.Locator(".tm-signing-form-runner__current-step").First;
        await currentStep.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(150);

        var visibleInsideSheet = await page.EvaluateAsync<bool>(
            """
            () => {
                const sheet = document.querySelector('.tm-signing-form-runner__steps').getBoundingClientRect();
                const current = document.querySelector('.tm-signing-form-runner__current-step');
                if (!current) return false;
                const controls = current.querySelectorAll('input, select');
                for (const control of controls) {
                    const probe = control.closest('label') ?? control;
                    const rect = probe.getBoundingClientRect();
                    if (rect.width <= 0 || rect.height <= 0) continue;
                    const intersects = rect.top < sheet.bottom && rect.bottom > sheet.top
                        && rect.left < sheet.right && rect.right > sheet.left;
                    if (intersects) return true;
                }
                return false;
            }
            """);
        Assert.IsTrue(visibleInsideSheet, "after Next the current step's input must be visible inside the sheet at 320");
    }

    /// <summary>Previous with the sheet expanded keeps the returned field visible above the sheet
    /// (review: the field bottom must not sink under the sheet top).</summary>
    [TestMethod]
    [Description("Previous with the sheet expanded lands the returned field above the sheet top")]
    public async Task SigningRunner_Previous_WithSheetExpanded_ActiveFieldAboveSheet()
    {
        var page = await SigningRedesignPhase5Specs.OpenRunnerAsync(Browser, BaseUrl, 390, 844, mobile: true, RegisterContext, WaitForAppReadyAsync);
        var runner = SigningRedesignPhase5Specs.GetRunner(page);
        var steps = SigningRedesignPhase5Specs.GetStepsRail(runner);

        await SigningRedesignPhase5Specs.ExpandSheetAsync(steps, touch: true);
        var name = steps.Locator("input.tm-signing-text-step__input").First;
        await name.FillAsync("Alex Johnson");
        await name.BlurAsync();

        await steps.Locator(".tm-signing-form-runner__next").TapAsync();
        await Assertions.Expect(steps.Locator(".tm-signing-form-runner__field-button[aria-current='step'][data-field-uuid='runner-delivery']"))
            .ToHaveAttributeAsync("aria-current", "step");

        await steps.Locator(".tm-signing-form-runner__previous").TapAsync();

        var bounds = await page.EvaluateAsync<double[]>(
            """
            () => {
                const field = document.querySelector('.tm-signing-field--selected');
                const sheet = document.querySelector('.tm-signing-form-runner__steps');
                if (!field || !sheet) return [-1, -1];
                return [field.getBoundingClientRect().bottom, sheet.getBoundingClientRect().top];
            }
            """);
        Assert.IsTrue(bounds[0] >= 0, "the runner renders a selected field overlay");
        Assert.IsTrue(
            bounds[0] <= bounds[1] + 2,
            $"Previous must keep the field above the expanded sheet (field bottom={bounds[0]}, sheet top={bounds[1]})");
    }

    /// <summary>An overlay tap in the PEEK state opens the sheet on that field's step, landing in
    /// the full edit state with the field above the sheet top (review).</summary>
    [TestMethod]
    [Description("Overlay tap in peek opens the full sheet with the tapped field above the sheet top")]
    public async Task SigningRunner_OverlayTap_InPeek_LandsFull_AboveSheet()
    {
        var page = await SigningRedesignPhase5Specs.OpenRunnerAsync(Browser, BaseUrl, 390, 844, mobile: true, RegisterContext, WaitForAppReadyAsync);
        var runner = SigningRedesignPhase5Specs.GetRunner(page);
        var steps = SigningRedesignPhase5Specs.GetStepsRail(runner);

        await SigningRedesignPhase5Specs.CollapseSheetAsync(steps, touch: true);

        // Tap the second field's document overlay (delivery) in the peek state.
        var overlay = runner.Locator(".tm-signing-field[data-field-uuid='runner-delivery']").First;
        await overlay.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(150);
        await overlay.TapAsync();

        var classes = await steps.GetAttributeAsync("class") ?? string.Empty;
        StringAssert.Contains(classes, "tm-signing-form-runner__steps--sheet-expanded", "the overlay tap opens the sheet");
        StringAssert.Contains(classes, "tm-signing-form-runner__steps--sheet-full", "the overlay tap lands in the full edit state");

        // The pending auto-scroll fires after the render and animates (smooth): poll until the
        // sheet's growth has settled before measuring, instead of racing a single read.
        var bounds = new double[] { -1, -1 };
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await page.WaitForTimeoutAsync(150);
            bounds = await page.EvaluateAsync<double[]>(
                """
                () => {
                    const field = document.querySelector('.tm-signing-field--selected');
                    const sheet = document.querySelector('.tm-signing-form-runner__steps');
                    if (!field || !sheet) return [-1, -1];
                    return [field.getBoundingClientRect().bottom, sheet.getBoundingClientRect().top];
                }
                """);
            if (bounds[0] >= 0 && bounds[0] <= bounds[1] + 2)
            {
                break;
            }
        }

        Assert.IsTrue(bounds[0] >= 0, "the tapped field is the selected overlay");
        Assert.IsTrue(
            bounds[0] <= bounds[1] + 2,
            $"the tapped field must sit above the expanded sheet top (field bottom={bounds[0]}, sheet top={bounds[1]})");
    }

    /// <summary>Keyboard simulation (phase 5 review): with a text input focused, shrinking the
    /// viewport to ~400px (the on-screen keyboard) keeps the Next CTA visible inside the sheet.</summary>
    [TestMethod]
    [Description("Shrinking the viewport onto a focused input keeps the Next CTA visible")]
    public async Task SigningRunner_KeyboardSimulation_CtaStaysVisible()
    {
        var page = await SigningRedesignPhase5Specs.OpenRunnerAsync(Browser, BaseUrl, 390, 844, mobile: true, RegisterContext, WaitForAppReadyAsync);
        var runner = SigningRedesignPhase5Specs.GetRunner(page);
        var steps = SigningRedesignPhase5Specs.GetStepsRail(runner);

        await SigningRedesignPhase5Specs.ExpandSheetAsync(steps, touch: true);

        var input = steps.Locator("input.tm-signing-text-step__input").First;
        await input.TapAsync();
        await Assertions.Expect(input).ToBeFocusedAsync();

        // The simulated keyboard: the visual viewport collapses to ~400px.
        await page.SetViewportSizeAsync(390, 400);

        // The browser re-scrolls the focused input into view after the resize; under suite load
        // that settle can outlast a single fixed wait and leave the measurement a few px off, so
        // poll until the CTA is inside the 400px viewport (same pattern as the overlay-tap test).
        var next = steps.Locator(".tm-signing-form-runner__next, .tm-signing-form-runner__complete").First;
        await Assertions.Expect(next).ToBeVisibleAsync();
        var box = await next.BoundingBoxAsync();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (box is not null && box.Y >= 0 && box.Y + box.Height <= 400)
            {
                break;
            }

            await page.WaitForTimeoutAsync(150);
            box = await next.BoundingBoxAsync();
        }

        Assert.IsNotNull(box, "the CTA must have a bounding box under the simulated keyboard");
        Assert.IsTrue(
            box.Y >= 0 && box.Y + box.Height <= 400,
            $"the CTA stays visible with the simulated keyboard (y={box.Y}, bottom={box.Y + box.Height})");
    }

    /// <summary>
    /// The mobile action bar grid (phase 5 review): at 320 the primary (Next) is the WIDEST
    /// action (1fr | 2fr | auto) and neither label overflows its button.
    /// </summary>
    [TestMethod]
    [Description("At 320 the Next action is wider than Previous and no label overflows")]
    public async Task SigningRunner_ActionBar320_NextWidest_NoLabelOverflow()
    {
        var page = await SigningRedesignPhase5Specs.OpenRunnerAsync(Browser, BaseUrl, 320, 640, mobile: true, RegisterContext, WaitForAppReadyAsync);
        var runner = SigningRedesignPhase5Specs.GetRunner(page);
        var steps = SigningRedesignPhase5Specs.GetStepsRail(runner);

        await SigningRedesignPhase5Specs.ExpandSheetAsync(steps, touch: true);
        await runner.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(150);

        var previous = steps.Locator(".tm-signing-form-runner__previous").First;
        var next = steps.Locator(".tm-signing-form-runner__next").First;
        var previousBox = await previous.BoundingBoxAsync();
        var nextBox = await next.BoundingBoxAsync();
        Assert.IsNotNull(previousBox);
        Assert.IsNotNull(nextBox);
        Assert.IsTrue(
            nextBox.Width > previousBox.Width,
            $"Next must be the widest action at 320 (next={nextBox.Width}, previous={previousBox.Width})");

        var overflowing = await page.EvaluateAsync<string>(
            """
            () => {
                const overflows = [];
                document.querySelectorAll('.tm-signing-form-runner__actions .tm-btn').forEach(btn => {
                    if (btn.scrollWidth > btn.clientWidth + 1) {
                        overflows.push(btn.className.split(' ').pop() + ' scroll=' + btn.scrollWidth + ' client=' + btn.clientWidth);
                    }
                });
                return overflows.join(' | ');
            }
            """);
        Assert.AreEqual(string.Empty, overflowing, $"no action label may overflow its button at 320 (overflow: {overflowing})");
    }

    /// <summary>Desktop (1440) keeps the phase-3 docked presentation: no sheet affordances in the
    /// layout, the choice step stays the system select, and the sticky footer keeps Next reachable.</summary>
    [TestMethod]
    [Description("At 1440 the runner keeps the docked panel (no sheet, select stays)")]
    public async Task SigningRunner_Desktop1440_KeepsDockedPanel()
    {
        var page = await SigningRedesignPhase5Specs.OpenRunnerAsync(Browser, BaseUrl, 1440, 900, mobile: false, RegisterContext, WaitForAppReadyAsync);
        var runner = SigningRedesignPhase5Specs.GetRunner(page);
        var steps = SigningRedesignPhase5Specs.GetStepsRail(runner);

        // The delivery select renders when its step is CURRENT — jump there through the list.
        await steps.Locator(".tm-signing-form-runner__field-button[data-field-uuid='runner-delivery']").ClickAsync();
        Assert.IsTrue(
            await steps.Locator(".tm-signing-choice-step .tm-radio-option").CountAsync() == 0,
            "the desktop delivery step stays the system select");
        Assert.IsTrue(
            await steps.Locator(".tm-signing-choice-step__select select").CountAsync() == 1,
            "the desktop delivery step renders the core TmSelect");

        // The primary action is contextual: Next while steps remain, Complete on the last one.
        var next = steps.Locator(".tm-signing-form-runner__next, .tm-signing-form-runner__complete").First;
        await Assertions.Expect(next).ToBeVisibleAsync();
        var box = await next.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box.Y + box.Height <= 900, $"the Next button stays inside the viewport (bottom={box.Y + box.Height})");
    }

    /// <summary>Desktop (1440, phase 5 review): after Next the active field's overlay is CENTRED
    /// in the viewport (±25% of the viewport height) — the docked rail sits BESIDE the document
    /// column, so <c>signing-runner.js</c> must not cut the usable space by the rail's top edge.</summary>
    [TestMethod]
    [Description("At 1440 after Next the active field centre sits within 25% of the viewport centre")]
    public async Task SigningRunner_Desktop1440_AfterNext_FieldCentredInViewport()
    {
        var page = await SigningRedesignPhase5Specs.OpenRunnerAsync(Browser, BaseUrl, 1440, 900, mobile: false, RegisterContext, WaitForAppReadyAsync);
        var runner = SigningRedesignPhase5Specs.GetRunner(page);
        var steps = SigningRedesignPhase5Specs.GetStepsRail(runner);

        // Fill the required first field, then advance with Next — the landing asks
        // signing-runner.js to scroll the delivery overlay into view.
        var name = steps.Locator("input.tm-signing-text-step__input").First;
        await name.FillAsync("Alex Johnson");
        await name.BlurAsync();
        await steps.Locator(".tm-signing-form-runner__next").ClickAsync();
        await Assertions.Expect(
            steps.Locator(".tm-signing-form-runner__field-button[data-field-uuid='runner-delivery']"))
            .ToHaveAttributeAsync("aria-current", "step", new LocatorAssertionsToHaveAttributeOptions { Timeout = 5_000 });

        // The smooth scroll animates after the render: poll until the field centre settles
        // inside the ±25% band instead of racing a single read.
        var reached = false;
        var metrics = new double[] { -1, -1 };
        for (var attempt = 0; attempt < 20 && !reached; attempt++)
        {
            await page.WaitForTimeoutAsync(150);
            metrics = await page.EvaluateAsync<double[]>(
                """
                () => {
                    const field = document.querySelector('.tm-signing-field--selected');
                    if (!field) return [-1, -1];
                    const rect = field.getBoundingClientRect();
                    const centre = (rect.top + rect.bottom) / 2;
                    const middle = window.innerHeight / 2;
                    return [centre, middle, Math.abs(centre - middle) <= window.innerHeight * 0.25 ? 1 : 0];
                }
                """);
            reached = metrics.Length >= 3 && metrics[2] == 1;
        }

        Assert.IsTrue(metrics[0] >= 0, "the runner renders a selected field overlay");
        Assert.IsTrue(
            reached,
            $"after Next the active field centre must sit within ±25% of the viewport centre at 1440 (field centre={metrics[0]}, viewport centre={metrics[1]})");
    }

}
