using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class SigningFieldEditorPanelE2ETests : WasmTestBase
{
    [DataTestMethod]
    [Description("Signing field editor renders a stable live preview for every supported field type")]
    [DataRow("Heading", "type", false)]
    [DataRow("Strikethrough", "minus", false)]
    [DataRow("Text", "file-text", false)]
    [DataRow("Signature", "edit", true)]
    [DataRow("Initials", "edit", true)]
    [DataRow("Date", "calendar", false)]
    [DataRow("DateNow", "calendar", false)]
    [DataRow("Number", "hash", false)]
    [DataRow("Image", "image", true)]
    [DataRow("File", "file", false)]
    [DataRow("Select", "list", false)]
    [DataRow("Checkbox", "check-square", false)]
    [DataRow("Multiple", "list", false)]
    [DataRow("Radio", "circle-dot", false)]
    [DataRow("Cells", "grid", false)]
    [DataRow("Stamp", "shield", false)]
    [DataRow("Payment", "tag", false)]
    [DataRow("Phone", "phone", false)]
    [DataRow("Verification", "lock", false)]
    [DataRow("Kba", "lock", false)]
    public async Task SigningFieldEditor_FieldTypePreview_IsStableForEveryType(string fieldType, string iconName, bool expectsThumbnail)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var panel = await GetPanelAsync(page);
        await panel.Locator(".tm-signing-field-editor-panel__type").SelectOptionAsync(fieldType);

        var preview = page.Locator("[data-testid='field-editor-preview']").First;
        await Expect(page.Locator("[data-testid='field-editor-status']")).ToContainTextAsync($": {fieldType}");
        await Expect(preview.Locator($".tm-signing-field__icon[data-icon='{iconName}']")).ToHaveCountAsync(1);

        if (expectsThumbnail)
        {
            await Expect(preview.Locator("img.tm-signing-field__thumbnail")).ToHaveCountAsync(1);
        }
        else
        {
            await Expect(preview.Locator("img.tm-signing-field__thumbnail")).ToHaveCountAsync(0);
        }

        var brokenImages = await preview.Locator("img").EvaluateAllAsync<string[]>(
            """
            images => images
                .filter(image => !image.complete || image.naturalWidth === 0)
                .map(image => image.getAttribute('src') || '')
            """);
        Assert.AreEqual(0, brokenImages.Length, $"Preview contains broken images for {fieldType}: {string.Join(", ", brokenImages)}");
    }

    [DataTestMethod]
    [Description("Signing field editor shows the expected configuration controls for every field type")]
    [DataRow("Heading", false, false, false, false, false, false)]
    [DataRow("Strikethrough", false, false, false, false, false, false)]
    [DataRow("Text", false, false, false, false, false, true)]
    [DataRow("Signature", false, true, false, false, false, false)]
    [DataRow("Initials", false, true, false, false, false, false)]
    [DataRow("Date", false, false, false, false, true, false)]
    [DataRow("DateNow", false, false, false, false, true, false)]
    [DataRow("Number", false, false, false, true, false, false)]
    [DataRow("Image", false, false, false, false, false, false)]
    [DataRow("File", false, false, false, false, false, false)]
    [DataRow("Select", true, false, false, false, false, false)]
    [DataRow("Checkbox", false, false, false, false, false, false)]
    [DataRow("Multiple", true, false, false, false, false, false)]
    [DataRow("Radio", true, false, false, false, false, false)]
    [DataRow("Cells", false, false, false, false, false, true)]
    [DataRow("Stamp", false, false, true, false, false, false)]
    [DataRow("Payment", false, false, false, true, false, false)]
    [DataRow("Phone", false, false, false, false, false, false)]
    [DataRow("Verification", false, false, false, false, false, false)]
    [DataRow("Kba", false, false, false, false, false, false)]
    public async Task SigningFieldEditor_FieldTypeControls_AreTypeSpecific(
        string fieldType,
        bool hasOptions,
        bool hasSignaturePreferences,
        bool hasStampPreferences,
        bool hasFormula,
        bool hasDateFormat,
        bool hasTextValidation)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var panel = await GetPanelAsync(page);
        await panel.Locator(".tm-signing-field-editor-panel__type").SelectOptionAsync(fieldType);

        await Expect(panel.Locator(".tm-signing-field-editor-panel__option-row")).ToHaveCountAsync(hasOptions ? 2 : 0);
        await Expect(panel.Locator(".tm-signing-field-editor-panel__signature-format")).ToHaveCountAsync(hasSignaturePreferences ? 1 : 0);
        await Expect(panel.Locator(".tm-signing-field-editor-panel__stamp-logo")).ToHaveCountAsync(hasStampPreferences ? 1 : 0);
        await Expect(panel.Locator(".tm-signing-field-editor-panel__open-formula")).ToHaveCountAsync(hasFormula ? 1 : 0);
        await Expect(panel.Locator(".tm-signing-field-editor-panel__date-format")).ToHaveCountAsync(hasDateFormat ? 1 : 0);
        await Expect(panel.Locator(".tm-signing-field-editor-panel__validation-pattern")).ToHaveCountAsync(hasTextValidation ? 1 : 0);
    }

    [TestMethod]
    [Description("Signing field editor select preview keeps full option labels visible")]
    public async Task SigningFieldEditor_SelectPreview_DoesNotClipOptionLabels()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var panel = await GetPanelAsync(page);
        await panel.Locator(".tm-signing-field-editor-panel__type").SelectOptionAsync("Select");

        var preview = page.Locator("[data-testid='field-editor-preview']").First;
        await Expect(preview).ToContainTextAsync("Czech Republic");
        await Expect(preview).ToContainTextAsync("United States");

        var clippedOptions = await preview.Locator(".tm-signing-field__option").EvaluateAllAsync<string[]>(
            """
            options => options
                .filter(option => option.scrollWidth > option.clientWidth + 1 || option.scrollHeight > option.clientHeight + 1)
                .map(option => option.textContent.trim())
            """);
        Assert.AreEqual(0, clippedOptions.Length, $"Preview clips option labels: {string.Join(", ", clippedOptions)}");
    }

    [TestMethod]
    [Description("Signing field editor renames a field and updates the live overlay preview")]
    public async Task SigningFieldEditor_RenamesFieldAndUpdatesPreview()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var panel = await GetPanelAsync(page);
        var name = panel.Locator(".tm-signing-field-editor-panel__name").First;

        await name.FillAsync("Residence country");
        await name.PressAsync("Tab");

        await Expect(page.Locator("[data-testid='field-editor-preview']")).ToContainTextAsync("Residence country");
        await Expect(page.Locator("[data-testid='field-editor-status']")).ToContainTextAsync("Residence country");
    }

    [TestMethod]
    [Description("Signing field editor adds a choice option and keeps the demo status in sync")]
    public async Task SigningFieldEditor_AddsChoiceOption()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var panel = await GetPanelAsync(page);
        await panel.Locator(".tm-signing-field-editor-panel__add-option").ClickAsync();

        await Expect(panel.Locator(".tm-signing-field-editor-panel__option-row")).ToHaveCountAsync(3);
        await Expect(page.Locator("[data-testid='field-editor-status']")).ToContainTextAsync("options: 3");
    }

    [TestMethod]
    [Description("Signing field editor toggles required and edits validation constraints")]
    public async Task SigningFieldEditor_SetsRequiredAndValidation()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var panel = await GetPanelAsync(page);
        await panel.Locator(".tm-signing-field-editor-panel__type").SelectOptionAsync("Text");
        await panel.Locator(".tm-signing-field-editor-panel__required").CheckAsync();
        await panel.Locator(".tm-signing-field-editor-panel__validation-min").FillAsync("2");
        await panel.Locator(".tm-signing-field-editor-panel__validation-min").PressAsync("Tab");
        await panel.Locator(".tm-signing-field-editor-panel__validation-message").FillAsync("Choose at least two characters");
        await panel.Locator(".tm-signing-field-editor-panel__validation-message").PressAsync("Tab");

        await Expect(page.Locator("[data-testid='field-editor-status']")).ToContainTextAsync("required: True");
        await Expect(panel.Locator(".tm-signing-field-editor-panel__validation-min")).ToHaveValueAsync("2");
        await Expect(panel.Locator(".tm-signing-field-editor-panel__validation-message")).ToHaveValueAsync("Choose at least two characters");
    }

    [TestMethod]
    [Description("Signing field editor saves a condition through the embedded condition builder")]
    public async Task SigningFieldEditor_SavesCondition()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var panel = await GetPanelAsync(page);
        await panel.Locator(".tm-signing-field-editor-panel__open-conditions").ClickAsync();
        var builder = panel.Locator(".tm-signing-field-editor-panel__condition-builder").First;
        await Expect(builder.Locator(".tm-condition-builder")).ToBeVisibleAsync();
        await builder.Locator(".tm-condition-builder__field").First.SelectOptionAsync("editor-consent");

        await Expect(page.Locator("[data-testid='field-editor-status']")).ToContainTextAsync("conditions: 1");
    }

    [TestMethod]
    [Description("Signing field editor keeps the embedded condition builder open while configuring comboboxes")]
    public async Task SigningFieldEditor_ConditionBuilder_StaysOpenAcrossSelections()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var panel = await GetPanelAsync(page);
        await panel.Locator(".tm-signing-field-editor-panel__open-conditions").ClickAsync();
        var builder = panel.Locator(".tm-signing-field-editor-panel__condition-builder").First;

        await builder.Locator(".tm-condition-builder__field").First.SelectOptionAsync("editor-plan");
        await Expect(builder.Locator(".tm-condition-builder")).ToBeVisibleAsync();
        await Expect(builder.Locator(".tm-condition-builder__row")).ToHaveCountAsync(1);

        await builder.Locator(".tm-condition-builder__value-select").First.SelectOptionAsync("editor-plan-pro");
        await Expect(builder.Locator(".tm-condition-builder")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='field-editor-status']")).ToContainTextAsync("conditions: 1");

        await builder.Locator(".tm-condition-builder__add").ClickAsync();
        await builder.Locator(".tm-condition-builder__operation").First.SelectOptionAsync("Or");
        await Expect(builder.Locator(".tm-condition-builder__row")).ToHaveCountAsync(2);

        await builder.Locator(".tm-condition-builder__field").Nth(1).SelectOptionAsync("editor-consent");
        await Expect(builder.Locator(".tm-condition-builder")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='field-editor-status']")).ToContainTextAsync("conditions: 2");
    }

    private static async Task<ILocator> GetPanelAsync(IPage page)
    {
        var panel = page.Locator("[data-testid='field-editor-panel']").First;
        await panel.ScrollIntoViewIfNeededAsync();
        return panel;
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
