using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.E2E;

[TestClass]
public class ConditionBuilderE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("Condition builder creates two conditions and keeps the configured status in sync")]
    public async Task ConditionBuilder_CreatesTwoConditions()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var builder = page.Locator("[data-testid='condition-builder']").First;
        await builder.ScrollIntoViewIfNeededAsync();
        await builder.Locator(".tm-condition-builder__field select").Nth(0).SelectOptionAsync("condition-country");
        await builder.Locator(".tm-condition-builder__value-select select").Nth(0).SelectOptionAsync("country-cz");

        await Expect(page.Locator("[data-testid='condition-builder-status']")).ToContainTextAsync("1 condition configured");

        await builder.Locator(".tm-condition-builder__add").ClickAsync();
        await builder.Locator(".tm-condition-builder__field select").Nth(1).SelectOptionAsync("condition-consent");

        await Expect(builder.Locator(".tm-condition-builder__row")).ToHaveCountAsync(2);
        await Expect(page.Locator("[data-testid='condition-builder-status']")).ToContainTextAsync("2 conditions configured");
    }

    [TestMethod]
    [Description("Condition builder switches the second condition operation from AND to OR")]
    public async Task ConditionBuilder_SwitchesOperationToOr()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var builder = page.Locator("[data-testid='condition-builder']").First;
        await builder.ScrollIntoViewIfNeededAsync();
        await builder.Locator(".tm-condition-builder__field select").Nth(0).SelectOptionAsync("condition-country");
        await builder.Locator(".tm-condition-builder__value-select select").Nth(0).SelectOptionAsync("country-cz");
        await builder.Locator(".tm-condition-builder__add").ClickAsync();
        await builder.Locator(".tm-condition-builder__field select").Nth(1).SelectOptionAsync("condition-consent");

        var operation = builder.Locator(".tm-condition-builder__operation select").First;
        await operation.SelectOptionAsync("Or");

        await Expect(operation).ToHaveValueAsync("Or");
    }

    [TestMethod]
    [Description("Condition builder validates a missing value for value-based conditions")]
    public async Task ConditionBuilder_MissingValue_ShowsValidation()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var builder = page.Locator("[data-testid='condition-builder']").First;
        await builder.ScrollIntoViewIfNeededAsync();
        await builder.Locator(".tm-condition-builder__field select").Nth(0).SelectOptionAsync("condition-country");

        await Expect(builder).ToHaveClassAsync(new Regex("tm-condition-builder--invalid"));
        await Expect(builder.Locator(".tm-condition-builder__validation")).ToContainTextAsync("Choose a value");
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
