using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class SigningLocalizationE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("PDF template designer switches localized field labels without moving fields")]
    public async Task PdfTemplateDesigner_SwitchesPreviewLanguageWithoutMovingField()
    {
        var page = await OpenSigningComponentsAsync();
        var designer = GetDesigner(page);
        await designer.ScrollIntoViewIfNeededAsync();

        var field = designer.Locator("[data-field-uuid='designer-name']").First;
        await Assertions.Expect(field).ToContainTextAsync("Celé jméno");
        var before = await field.BoundingBoxAsync();
        Assert.IsNotNull(before);

        await designer.Locator(".tm-pdf-template-designer__culture-preview").SelectOptionAsync("en-US");

        await Assertions.Expect(field).ToContainTextAsync("Full name");
        var after = await field.BoundingBoxAsync();
        Assert.IsNotNull(after);
        Assert.AreEqual(before.X, after.X, 1.0, "Changing preview language must not move the field horizontally.");
        Assert.AreEqual(before.Y, after.Y, 1.0, "Changing preview language must not move the field vertically.");
    }

    [TestMethod]
    [Description("Field editor updates localized labels while keeping option values stable")]
    public async Task FieldEditor_UpdatesLocalizedLabelAndKeepsOptionValue()
    {
        var page = await OpenSigningComponentsAsync();
        var designer = GetDesigner(page);
        await designer.ScrollIntoViewIfNeededAsync();

        await designer.Locator("[data-field-uuid='designer-name']").First.ClickAsync();
        await designer.Locator(".tm-pdf-template-designer__culture-preview").SelectOptionAsync("en-US");
        await designer.Locator(".tm-signing-field-editor-panel__localization-culture").First.SelectOptionAsync("en-US");
        var labelInput = designer.Locator(".tm-signing-field-editor-panel__localized-label").First;
        await ReplaceInputValueAsync(page, labelInput, "Signer legal name");

        await Assertions.Expect(designer.Locator("[data-field-uuid='designer-name']").First).ToContainTextAsync("Signer legal name");

        await designer.Locator("[data-field-uuid='designer-delivery']").First.ClickAsync();
        var optionInput = designer.Locator("[data-localized-option-uuid='designer-delivery-email'] .tm-signing-field-editor-panel__localized-option-label").First;
        await ReplaceInputValueAsync(page, optionInput, "Electronic delivery");

        await Assertions.Expect(designer.Locator("[data-field-uuid='designer-delivery']").First).ToContainTextAsync("Electronic delivery");
        var optionValue = await designer.Locator("[data-option-uuid='designer-delivery-email'] .tm-signing-field-editor-panel__option-value").InputValueAsync();
        Assert.AreEqual("email", optionValue);
    }

    [TestMethod]
    [Description("Field editor saves Czech labels and the designer preview uses them immediately")]
    public async Task FieldEditor_SavesCzechLabel()
    {
        var page = await OpenSigningComponentsAsync();
        var designer = GetDesigner(page);
        await designer.ScrollIntoViewIfNeededAsync();

        await designer.Locator("[data-field-uuid='designer-name']").First.ClickAsync();
        await designer.Locator(".tm-pdf-template-designer__culture-preview").SelectOptionAsync("cs-CZ");
        await designer.Locator(".tm-signing-field-editor-panel__localization-culture").First.SelectOptionAsync("cs-CZ");
        await ReplaceInputValueAsync(page, designer.Locator(".tm-signing-field-editor-panel__localized-label").First, "Podepisující osoba");

        await Assertions.Expect(designer.Locator("[data-field-uuid='designer-name']").First).ToContainTextAsync("Podepisující osoba");
    }

    [TestMethod]
    [Description("Signing runner switches language without losing entered value or current step")]
    public async Task SigningFormRunner_SwitchesLanguageWithoutLosingValueOrStep()
    {
        var page = await OpenSigningComponentsAsync();
        var runner = GetRunner(page);
        await runner.ScrollIntoViewIfNeededAsync();
        var panel = runner.Locator(".tm-signing-form-runner__step-panel").First;

        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Full name");
        var input = panel.Locator("input.tm-signing-text-step__input").First;
        await input.FillAsync("Alex Johnson");

        await panel.Locator(".tm-signing-form-runner__language-select").SelectOptionAsync("cs-CZ");

        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Celé jméno");
        await Assertions.Expect(input).ToHaveValueAsync("Alex Johnson");
        await Assertions.Expect(panel.Locator(".tm-signing-form-runner__progress")).ToContainTextAsync("1");
        await Assertions.Expect(await GetByTestIdAsync(page, "signing-runner-status")).ToContainTextAsync("Language snapshot: cs-CZ");
    }

    [TestMethod]
    [Description("Signing runner switches language without losing selected options and autosaves stable option values")]
    public async Task SigningFormRunner_SwitchesLanguageWithoutLosingSelectedOption()
    {
        var page = await OpenSigningComponentsAsync();
        var runner = GetRunner(page);
        await runner.ScrollIntoViewIfNeededAsync();
        var panel = runner.Locator(".tm-signing-form-runner__step-panel").First;

        await panel.Locator("input.tm-signing-text-step__input").First.FillAsync("Alex Johnson");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        var deliverySelect = panel.Locator("select.tm-signing-choice-step__select").First;
        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Delivery method");
        await deliverySelect.SelectOptionAsync("paper");
        await Assertions.Expect(await GetByTestIdAsync(page, "signing-runner-status")).ToContainTextAsync("delivery: paper", new() { Timeout = 4_000 });

        await panel.Locator(".tm-signing-form-runner__language-select").SelectOptionAsync("cs-CZ");

        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Způsob doručení");
        await Assertions.Expect(deliverySelect).ToHaveValueAsync("paper");
    }

    [TestMethod]
    [Description("Keyboard can operate the signing runner language selector")]
    public async Task SigningFormRunner_LanguageSelectorSupportsKeyboard()
    {
        var page = await OpenSigningComponentsAsync();
        var runner = GetRunner(page);
        await runner.ScrollIntoViewIfNeededAsync();
        var panel = runner.Locator(".tm-signing-form-runner__step-panel").First;
        var language = panel.Locator(".tm-signing-form-runner__language-select").First;

        await language.FocusAsync();
        await page.Keyboard.PressAsync("ArrowUp");

        await Assertions.Expect(language).ToHaveValueAsync("cs-CZ");
        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Celé jméno");
    }

    [TestMethod]
    [Description("Condition builder keeps selected field and option UUIDs when the preview language changes")]
    public async Task ConditionBuilder_KeepsSelectedFieldUuidWhenLanguageChanges()
    {
        var page = await OpenSigningComponentsAsync();
        var condition = await GetByTestIdAsync(page, "condition-builder");

        await condition.Locator(".tm-condition-builder__field").First.SelectOptionAsync("condition-country");
        await condition.Locator(".tm-condition-builder__value-select").First.SelectOptionAsync("country-us");
        await Assertions.Expect(await GetByTestIdAsync(page, "condition-builder-status")).ToContainTextAsync("1 condition");

        await page.Locator(".condition-builder-culture").First.SelectOptionAsync("cs-CZ");

        await Assertions.Expect(condition.Locator(".tm-condition-builder__field").First).ToHaveValueAsync("condition-country");
        await Assertions.Expect(condition.Locator(".tm-condition-builder__value-select").First).ToHaveValueAsync("country-us");
        await Assertions.Expect(condition).ToContainTextAsync("Země");
        await Assertions.Expect(condition).ToContainTextAsync("Spojené státy");
    }

    [TestMethod]
    [Description("Localized signing UI avoids empty fallback strings and keeps editor panels contained")]
    public async Task LocalizedSigningUi_KeepsFallbackTextAndPanelLayoutStable()
    {
        var page = await OpenSigningComponentsAsync();
        await page.SetViewportSizeAsync(1366, 768);
        var designer = GetDesigner(page);
        await designer.ScrollIntoViewIfNeededAsync();
        await designer.Locator("[data-field-uuid='designer-name']").First.ClickAsync();
        await designer.Locator(".tm-pdf-template-designer__culture-preview").SelectOptionAsync("cs-CZ");
        await designer.Locator(".tm-signing-field-editor-panel__localization-culture").First.SelectOptionAsync("cs-CZ");
        await ReplaceInputValueAsync(page, designer.Locator(".tm-signing-field-editor-panel__localized-label").First, "NejdelšíPodepisujícíOsobaBezMezer");

        await AssertElementDoesNotOverflowAsync(designer.Locator(".tm-pdf-template-designer__panel").First);
        await page.SetViewportSizeAsync(390, 844);
        await designer.ScrollIntoViewIfNeededAsync();
        await AssertElementDoesNotOverflowAsync(designer.Locator(".tm-pdf-template-designer__panel").First);

        var text = await designer.Locator(".tm-pdf-template-designer__panel").First.InnerTextAsync();
        Assert.IsFalse(text.Contains("[]", StringComparison.Ordinal), "Localized UI should not render empty fallback text.");
        Assert.IsFalse(text.Contains("[Tm", StringComparison.Ordinal), "Localized UI should not render missing resource keys.");
    }

    [TestMethod]
    [Description("Signing runner shows localized required validation message")]
    public async Task SigningFormRunner_ShowsLocalizedRequiredValidationMessage()
    {
        var page = await OpenSigningComponentsAsync();
        var runner = GetRunner(page);
        await runner.ScrollIntoViewIfNeededAsync();
        var panel = runner.Locator(".tm-signing-form-runner__step-panel").First;

        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Full name");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__validation")).ToContainTextAsync("Full name is required.");
    }

    [TestMethod]
    [Description("Audit trail viewer shows the signing localization snapshot culture")]
    public async Task AuditTrailViewer_ShowsSigningLocalizationSnapshotCulture()
    {
        var page = await OpenSigningComponentsAsync();
        var runner = GetRunner(page);
        await runner.ScrollIntoViewIfNeededAsync();
        var panel = runner.Locator(".tm-signing-form-runner__step-panel").First;

        await panel.Locator(".tm-signing-form-runner__language-select").SelectOptionAsync("cs-CZ");

        var audit = await GetByTestIdAsync(page, "audit-trail-viewer");
        await Assertions.Expect(audit).ToContainTextAsync("Signing culture");
        await Assertions.Expect(audit).ToContainTextAsync("cs-CZ");
        await Assertions.Expect(audit).ToContainTextAsync("Celé jméno");
        await Assertions.Expect(audit).ToContainTextAsync("Original PDF language");
    }

    private async Task<IPage> OpenSigningComponentsAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static ILocator GetDesigner(IPage page)
    {
        return page.Locator("[data-testid='pdf-template-designer']").First;
    }

    private static ILocator GetRunner(IPage page)
    {
        return page.Locator("[data-testid='signing-runner-demo']").First;
    }

    private static async Task<ILocator> GetByTestIdAsync(IPage page, string testId)
    {
        var locator = page.Locator($"[data-testid='{testId}']").First;
        await locator.ScrollIntoViewIfNeededAsync();
        return locator;
    }

    private static async Task ReplaceInputValueAsync(IPage page, ILocator input, string value)
    {
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.InsertTextAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    private static async Task AssertElementDoesNotOverflowAsync(ILocator locator)
    {
        var overflows = await locator.EvaluateAsync<bool>("element => element.scrollWidth > element.clientWidth + 2");
        Assert.IsFalse(overflows, "Element content should fit inside its available width.");
    }
}
