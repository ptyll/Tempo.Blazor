using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class SigningStepsE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("Signing steps accept text, number, and date values")]
    public async Task SigningSteps_FillsTextNumberAndDate()
    {
        var page = await OpenStepsAsync();
        var steps = GetSteps(page);

        await steps.Locator("[data-testid='signing-step-text'] input").FillAsync("Alex Johnson");
        await steps.Locator("[data-testid='signing-step-number'] input").FillAsync("125.5");
        await steps.Locator("[data-testid='signing-step-date'] input").FillAsync("2026-05-08");

        await Assertions.Expect(steps.Locator("[data-testid='signing-step-text'] input")).ToHaveValueAsync("Alex Johnson");
        await Assertions.Expect(steps.Locator("[data-testid='signing-step-number'] input")).ToHaveValueAsync("125.5");
        await Assertions.Expect(steps.Locator("[data-testid='signing-step-date'] input")).ToHaveValueAsync("2026-05-08");
    }

    [TestMethod]
    [Description("Signing steps select single, radio, and multiple choices")]
    public async Task SigningSteps_SelectsChoices()
    {
        var page = await OpenStepsAsync();
        var steps = GetSteps(page);

        await steps.Locator("[data-testid='signing-step-select'] select").SelectOptionAsync("option-2");
        // Phase 4: the radio / checkbox rows are the core Tm components — the native input is the
        // visually hidden hit target inside the wrapping label, so a user clicks the ROW.
        await steps.Locator("[data-testid='signing-step-radio'] .tm-radio-option", new() { HasText = "One" }).ClickAsync();
        await steps.Locator("[data-testid='signing-step-multiple'] .tm-checkbox-label", new() { HasText = "One" }).ClickAsync();
        await steps.Locator("[data-testid='signing-step-multiple'] .tm-checkbox-label", new() { HasText = "Three" }).ClickAsync();

        await Assertions.Expect(steps.Locator("[data-testid='signing-step-select'] select")).ToHaveValueAsync("option-2");
        await Assertions.Expect(steps.GetByRole(AriaRole.Radio, new() { Name = "One" })).ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("[data-testid='signing-steps-status']")).ToContainTextAsync("Multiple: 2");
    }

    [TestMethod]
    [Description("Signing image step exposes a file upload and remove flow")]
    public async Task SigningSteps_UploadsAndRemovesImage()
    {
        var page = await OpenStepsAsync();
        var steps = GetSteps(page);
        var image = steps.Locator("[data-testid='signing-step-image']");

        await Assertions.Expect(image.Locator("input[type='file']")).ToHaveCountAsync(1);
        await image.Locator(".tm-signing-attachment-step__remove").ClickAsync();

        await Assertions.Expect(page.Locator("[data-testid='signing-steps-status']")).ToContainTextAsync("Attachments: 0");
    }

    [TestMethod]
    [Description("Signing phone step sends code and shows OTP state")]
    public async Task SigningSteps_PhoneShowsOtpAfterSendCode()
    {
        var page = await OpenStepsAsync();
        var steps = GetSteps(page);
        var phone = steps.Locator("[data-testid='signing-step-phone']");

        await phone.Locator("input[type='tel']").ClickAsync();
        await page.Keyboard.TypeAsync("777 123 456");
        await page.WaitForTimeoutAsync(300);
        await phone.Locator(".tm-signing-phone-step__send").ClickAsync();

        await Assertions.Expect(phone.Locator(".tm-signing-phone-step__otp")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid='signing-steps-status']")).ToContainTextAsync("+420777123456");
    }

    [TestMethod]
    [Description("Signing payment placeholder invokes checkout callback")]
    public async Task SigningSteps_PaymentCheckoutUpdatesStatus()
    {
        var page = await OpenStepsAsync();
        var steps = GetSteps(page);

        await steps.Locator("[data-testid='signing-step-payment'] .tm-signing-external-step__checkout").ClickAsync();

        await Assertions.Expect(page.Locator("[data-testid='signing-steps-status']")).ToContainTextAsync("Checkout: Payment");
    }

    /// <summary>
    /// Signing redesign phase 2: every step input carries an accessible name — the step shell's
    /// title id (aria-labelledby) for bare inputs, explicit aria-labels for the phone controls and
    /// wrapping labels for checkbox / radio / upload controls — so getByLabel resolves each of
    /// them (B25 follow-up: the runner is an end-user surface).
    /// </summary>
    [TestMethod]
    [Description("Every signing step input resolves its accessible name through getByLabel")]
    public async Task SigningSteps_EveryStepInputIsReachableByLabel()
    {
        var page = await OpenStepsAsync();
        var steps = GetSteps(page);

        await steps.GetByLabel("Full name").FillAsync("Alex Johnson");
        await steps.GetByLabel("Amount").FillAsync("42.5");
        await steps.GetByLabel("Signing date").FillAsync("2026-05-08");
        // Exact, scoped to the select step: the phone country select is labelled "Country" too.
        await steps.Locator("[data-testid='signing-step-select']").GetByLabel("Country", new() { Exact = true }).SelectOptionAsync("option-1");

        await Assertions.Expect(steps.Locator("[data-testid='signing-step-text'] input")).ToHaveValueAsync("Alex Johnson");
        await Assertions.Expect(steps.Locator("[data-testid='signing-step-number'] input")).ToHaveValueAsync("42.5");
        await Assertions.Expect(steps.Locator("[data-testid='signing-step-date'] input")).ToHaveValueAsync("2026-05-08");
        await Assertions.Expect(steps.Locator("[data-testid='signing-step-select'] select")).ToHaveValueAsync("option-1");

        // The radio rows are named by their option labels (the core TmRadio label wraps the
        // visually hidden native input): the user clicks the row, the state is read by role+name.
        await steps.Locator("[data-testid='signing-step-radio'] .tm-radio-option", new() { HasText = "One" }).ClickAsync();
        await Assertions.Expect(steps.GetByRole(AriaRole.Radio, new() { Name = "One" })).ToBeCheckedAsync();

        // Phone controls: country calling code, phone number, and (after send) the OTP input.
        var phone = steps.Locator("[data-testid='signing-step-phone']");
        await phone.GetByLabel("Phone number").FillAsync("777 123 456");
        await phone.Locator(".tm-signing-phone-step__send").ClickAsync();
        await Assertions.Expect(phone.GetByLabel("Verification code")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid='signing-steps-status']")).ToContainTextAsync("+420777123456");

        // The image upload input is named by its wrapping label.
        await Assertions.Expect(steps.Locator("[data-testid='signing-step-image']").GetByLabel("Upload image").First).ToBeAttachedAsync();
    }

    /// <summary>
    /// „Nové komponenty 1/7" fáze 5: in Segmented mode the phone step swaps the single code input
    /// for the core TmOtpInput; the demo verifier confirms the code "123456" (anything else is
    /// rejected), so completing the segmented cells must flip the input to its verified state.
    /// </summary>
    [TestMethod]
    [Description("Signing phone step segmented mode verifies the demo code 123456")]
    public async Task SigningSteps_PhoneSegmentedMode_VerifiesDemoCode()
    {
        var page = await OpenStepsAsync();
        var steps = GetSteps(page);
        var phone = steps.Locator("[data-testid='signing-step-phone']");
        var otp = phone.Locator(".tm-signing-phone-step__otp");

        // Switch the demo phone step to the segmented TmOtpInput (the switch is a core TmSelect —
        // a native <select id="signing-phone-code-mode">).
        await page.Locator("#signing-phone-code-mode").SelectOptionAsync(
            new[] { new SelectOptionValue { Label = "Segmented (TmOtpInput)" } });

        await phone.GetByLabel("Phone number").FillAsync("777 123 456");
        await phone.Locator(".tm-signing-phone-step__send").ClickAsync();

        // The otp row renders only after the send; in Segmented mode it is the core TmOtpInput.
        await Assertions.Expect(otp.Locator(".tm-otp-input__cell")).ToHaveCountAsync(6);
        // Focus (not click): the runner reveal-scrolls the otp row (smooth scroll), so the cell
        // can keep moving under a pointer click for longer than the actionability window.
        await otp.Locator(".tm-otp-input__cell").First.FocusAsync();
        await page.Keyboard.TypeAsync("123456");

        // The demo verifier confirms 123456 — the input settles in the verified state.
        await Assertions.Expect(otp).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-otp-input--verified"));
        await Assertions.Expect(phone.Locator(".tm-otp-input__status")).ToBeVisibleAsync();
    }

    private async Task<IPage> OpenStepsAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);
        var steps = GetSteps(page);
        await steps.ScrollIntoViewIfNeededAsync();
        return page;
    }

    private static ILocator GetSteps(IPage page)
    {
        return page.Locator("[data-testid='signing-steps-demo']").First;
    }
}
