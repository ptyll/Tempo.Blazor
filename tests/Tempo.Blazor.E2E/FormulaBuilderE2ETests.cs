using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.E2E;

[TestClass]
public class FormulaBuilderE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("Formula builder inserts field tokens and operators into the formula textarea")]
    public async Task FormulaBuilder_InsertsTokenAndOperator()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var builder = page.Locator("[data-testid='formula-builder']").First;
        await builder.ScrollIntoViewIfNeededAsync();
        await builder.Locator("[data-field-uuid='formula-subtotal']").ClickAsync();
        await builder.Locator("[data-operator='+']").ClickAsync();
        await builder.Locator("[data-field-uuid='formula-tax']").ClickAsync();

        await Expect(builder.Locator(".tm-formula-builder__textarea textarea"))
            .ToHaveValueAsync(new Regex(@"\{\{Subtotal\}\}\s\+\s\{\{Tax\}\}"));
    }

    [TestMethod]
    [Description("Formula builder saves a valid formula as normalized UUID tokens")]
    public async Task FormulaBuilder_SavesValidFormula()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var builder = page.Locator("[data-testid='formula-builder']").First;
        await builder.ScrollIntoViewIfNeededAsync();
        await builder.Locator("[data-field-uuid='formula-subtotal']").ClickAsync();
        await builder.Locator("[data-operator='+']").ClickAsync();
        await builder.Locator("[data-field-uuid='formula-tax']").ClickAsync();
        await builder.Locator(".tm-formula-builder__save").ClickAsync();

        await Expect(page.Locator("[data-testid='formula-builder-status']"))
            .ToContainTextAsync("Saved: {{formula-subtotal}} + {{formula-tax}}; readonly: True");
    }

    [TestMethod]
    [Description("Formula builder shows validation for unknown field tokens")]
    public async Task FormulaBuilder_UnknownToken_ShowsValidation()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var builder = page.Locator("[data-testid='formula-builder']").First;
        await builder.ScrollIntoViewIfNeededAsync();
        await builder.Locator(".tm-formula-builder__textarea textarea").FillAsync("{{Missing}} + 1");
        await builder.Locator(".tm-formula-builder__save").ClickAsync();

        await Expect(builder.Locator(".tm-formula-builder__textarea .tm-input-error-message")).ToContainTextAsync("Missing");
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
