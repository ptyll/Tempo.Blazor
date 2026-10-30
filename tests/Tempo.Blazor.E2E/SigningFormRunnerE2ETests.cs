using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class SigningFormRunnerE2ETests : WasmTestBase
{
    [TestMethod]
[Description("Signing form runner completes the required text, delivery, number, conditional note, and signature flow")]
    public async Task SigningFormRunner_CompletesRequiredFlow()
    {
        var page = await OpenRunnerAsync();
        var runner = GetRunner(page);
        var panel = GetDesktopPanel(runner);

        await panel.Locator("input.tm-signing-text-step__input").FillAsync("Alex Johnson");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Delivery method");
        await panel.Locator("select.tm-signing-choice-step__select").SelectOptionAsync("paper");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        await panel.Locator("input.tm-signing-number-step__input").FillAsync("100");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        await panel.Locator("input.tm-signing-choice-step__checkbox").CheckAsync();
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Internal note");
        await panel.Locator("input.tm-signing-text-step__input").FillAsync("Board approved");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Signing date");
        await panel.Locator("input.tm-signing-date-step__input").FillAsync("2026-05-08");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        var signatureInput = panel.Locator("input.tm-signature-capture__typed-input");
        await signatureInput.FillAsync("Alex Johnson");
        await signatureInput.PressAsync("Tab");
        await Assertions.Expect(panel.Locator(".tm-signing-form-runner__complete")).ToBeEnabledAsync();
        await panel.Locator(".tm-signing-form-runner__complete").ClickAsync();

        await Assertions.Expect(page.Locator("[data-testid='signing-runner-status']")).ToContainTextAsync("Completed");
    }

    [TestMethod]
    [Description("Signing form runner keeps the signer on the first invalid required step")]
    public async Task SigningFormRunner_BlocksEmptyRequiredStep()
    {
        var page = await OpenRunnerAsync();
        var panel = GetDesktopPanel(GetRunner(page));

        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        // Phase 2: the step validation surfaces in the step shell (one alert per step).
        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__validation")).ToContainTextAsync("Full name is required.");
        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Full name");
    }

    [TestMethod]
    [Description("Signing form runner updates formula overlays as dependent numeric values change")]
    public async Task SigningFormRunner_UpdatesFormulaOverlay()
    {
        var page = await OpenRunnerAsync();
        var runner = GetRunner(page);
        var panel = GetDesktopPanel(runner);

        await panel.Locator("input.tm-signing-text-step__input").FillAsync("Alex Johnson");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();
        await panel.Locator("select.tm-signing-choice-step__select").SelectOptionAsync("paper");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();
        await panel.Locator("input.tm-signing-number-step__input").FillAsync("100");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        await Assertions.Expect(runner.Locator(".tm-signing-field:has-text('Total with tax')")).ToContainTextAsync("121");
        await Assertions.Expect(page.Locator("[data-testid='signing-runner-status']")).ToContainTextAsync("total: 121");
    }

    [TestMethod]
    [Description("Signing form runner accessibility list can jump directly to a selected step")]
    public async Task SigningFormRunner_AccessibilityListJumpsToStep()
    {
        var page = await OpenRunnerAsync();
        var panel = GetDesktopPanel(GetRunner(page));

        await panel.Locator(".tm-signing-form-runner__accessibility-entry").ClickAsync();
        await panel.Locator(".tm-signing-form-runner__accessibility-field:has-text('Signature')").ClickAsync();

        await Assertions.Expect(panel.Locator("input.tm-signature-capture__typed-input")).ToBeVisibleAsync();
    }

    [TestMethod]
    [Description("Signing form runner keeps draw signature mode selected after completing a stroke")]
    public async Task SigningFormRunner_DrawSignatureModePersistsAfterMouseUp()
    {
        var page = await OpenRunnerAsync();
        var panel = GetDesktopPanel(GetRunner(page));

        await panel.Locator(".tm-signing-form-runner__accessibility-entry").ClickAsync();
        await panel.Locator(".tm-signing-form-runner__accessibility-field:has-text('Signature')").ClickAsync();
        await panel.GetByRole(AriaRole.Tab, new() { Name = "Draw" }).ClickAsync();

        var signatureCapture = panel.Locator(".tm-signature-capture").First;
        await Assertions.Expect(signatureCapture).ToHaveAttributeAsync("data-mode", "Draw");
        await DrawSignatureAsync(page, signatureCapture);

        await Assertions.Expect(signatureCapture).ToHaveAttributeAsync("data-mode", "Draw");
        await Assertions.Expect(panel.GetByRole(AriaRole.Tab, new() { Name = "Draw" })).ToHaveAttributeAsync("aria-selected", "true");
    }

    [TestMethod]
    [Description("Signing form runner exposes a collapsible mobile signing panel")]
    public async Task SigningFormRunner_MobilePanelCollapsesAndExpands()
    {
        var page = await OpenRunnerAsync(390, 820);
        var runner = GetRunner(page);
        var steps = GetStepsRail(runner);

        // B25 (phase 2): the sheet is a presentation of the ONE steps rail — collapsing hides it.
        await steps.Locator(".tm-signing-form-runner__mobile-minimize").ClickAsync();
        await Assertions.Expect(steps.Locator(".tm-signing-form-runner__mobile-expand")).ToBeVisibleAsync();
        await Assertions.Expect(steps.Locator(".tm-signing-form-runner__progress")).Not.ToBeVisibleAsync();

        await steps.Locator(".tm-signing-form-runner__mobile-expand").ClickAsync();
        await Assertions.Expect(steps.Locator(".tm-signing-form-runner__progress")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Expanding the mobile sheet with the keyboard moves focus onto the affordance that became
    /// visible (the minimize button), so the next Enter / Tab continues from the sheet.
    /// </summary>
    [TestMethod]
    [Description("After expanding the mobile sheet with Enter on 'Sign now', focus lands on the minimize button")]
    public async Task SigningFormRunner_ExpandingSheetKeepsFocusOnTheVisibleAffordance()
    {
        var page = await OpenRunnerAsync(390, 820);
        var runner = GetRunner(page);
        var steps = GetStepsRail(runner);

        // Collapse first: the sheet starts expanded at 390, so "Sign now" becomes visible.
        await steps.Locator(".tm-signing-form-runner__mobile-minimize").ClickAsync();
        await Assertions.Expect(steps.Locator(".tm-signing-form-runner__mobile-expand")).ToBeVisibleAsync();

        var expand = steps.Locator(".tm-signing-form-runner__mobile-expand").First;
        await expand.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        var activeClass = await page.EvaluateAsync<string>("() => document.activeElement?.className?.toString() ?? ''");
        StringAssert.Contains(activeClass, "tm-signing-form-runner__mobile-minimize",
            $"expanding the sheet must move focus onto the now-visible minimize button, got '{activeClass}'");
    }

    /// <summary>
    /// B25 (Signing redesign phase 2): the step panel exists EXACTLY ONCE in the DOM at every
    /// width — the column vs. mobile-sheet presentation is CSS-only. Before the fix the mobile
    /// panel was a second rendered copy, so every input and alert existed twice for assistive
    /// technology.
    /// </summary>
    [TestMethod]
    [Description("The step panel is rendered once in the DOM at 390px and at 1440px")]
    public async Task SigningFormRunner_SinglePanelInstanceInDom_AtMobileAndDesktop()
    {
        foreach (var (width, height) in new[] { (390, 820), (1440, 900) })
        {
            var page = await OpenRunnerAsync(width, height);
            var runner = GetRunner(page);
            var steps = GetStepsRail(runner);

            await Assertions.Expect(steps.Locator(".tm-signing-step-shell__title").First).ToBeVisibleAsync();

            var panelCount = await runner.Locator(".tm-signing-form-runner__step-panel").CountAsync();
            Assert.AreEqual(1, panelCount, $"at {width}px the step panel must exist once (found {panelCount})");
            var progressCount = await runner.Locator(".tm-signing-form-runner__progress").CountAsync();
            Assert.AreEqual(1, progressCount, $"at {width}px the progress region must exist once (found {progressCount})");
            var nextCount = await runner.Locator(".tm-signing-form-runner__next").CountAsync();
            Assert.AreEqual(1, nextCount, $"at {width}px the action row must exist once (found {nextCount})");
            var shellCount = await runner.Locator(".tm-signing-step-shell").CountAsync();
            Assert.AreEqual(1, shellCount, $"at {width}px the step shell must exist once (found {shellCount})");

            await page.Context.CloseAsync();
        }
    }

    /// <summary>Every runner input is reachable through its accessible name (getByLabel).</summary>
    [TestMethod]
    [Description("Runner inputs resolve their accessible names (getByLabel) while the signer walks the steps")]
    public async Task SigningFormRunner_InputsAreReachableByLabel()
    {
        var page = await OpenRunnerAsync();
        var runner = GetRunner(page);
        var steps = GetStepsRail(runner);

        // Text step: the field label names the input.
        await steps.GetByLabel("Full name").FillAsync("Alex Johnson");
        await steps.Locator(".tm-signing-form-runner__next").ClickAsync();

        // Select step.
        await steps.GetByLabel("Delivery method").SelectOptionAsync("paper");
        await steps.Locator(".tm-signing-form-runner__next").ClickAsync();

        // Number step.
        await steps.GetByLabel("Amount without tax").FillAsync("100");
        await steps.Locator(".tm-signing-form-runner__next").ClickAsync();

        await Assertions.Expect(steps.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Add internal note");
    }

    private async Task<IPage> OpenRunnerAsync(int width = 1280, int height = 720)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);
        var runner = GetRunner(page);
        await runner.ScrollIntoViewIfNeededAsync();
        return page;
    }

    private static ILocator GetRunner(IPage page)
    {
        return page.Locator("[data-testid='signing-runner-demo']").First;
    }

    private static ILocator GetDesktopPanel(ILocator runner)
    {
        return runner.Locator(".tm-signing-form-runner__steps").First;
    }

    private static ILocator GetStepsRail(ILocator runner)
    {
        return runner.Locator(".tm-signing-form-runner__steps").First;
    }

    private static async Task DrawSignatureAsync(IPage page, ILocator signatureCapture)
    {
        var canvas = signatureCapture.Locator(".tm-signature-capture__canvas").First;
        await canvas.ScrollIntoViewIfNeededAsync();
        var box = await canvas.BoundingBoxAsync();
        Assert.IsNotNull(box, "Signature canvas should have a bounding box.");

        await page.Mouse.MoveAsync((float)(box!.X + box.Width * 0.2), (float)(box.Y + box.Height * 0.35));
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(box.X + box.Width * 0.42), (float)(box.Y + box.Height * 0.25), new MouseMoveOptions { Steps = 4 });
        await page.Mouse.MoveAsync((float)(box.X + box.Width * 0.7), (float)(box.Y + box.Height * 0.45), new MouseMoveOptions { Steps = 4 });
        await page.Mouse.UpAsync();
    }
}
