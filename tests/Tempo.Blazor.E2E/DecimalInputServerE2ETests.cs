using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Server-lane mirror of the TmDecimalInput fill() regression on /forms. The Server round-trip
/// always loses the race that WASM only loses on timing, so Decimal7 stays here as the
/// deterministic repro (Fáze 8e, DECIMAL-FOCUS-KEEPS-TEXT).
/// </summary>
[TestClass]
public class DecimalInputServerE2ETests : ServerTestBase
{
    private const string FormsPage = "/forms";

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private async Task<IPage> OpenFormsPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await GotoAppReadyAsync(page, $"{BaseUrl}{FormsPage}");

        var section = page.Locator("[data-testid='decimal-input-section']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();
        return page;
    }

    private static ILocator Field(IPage page, string testId) =>
        page.Locator($"[data-testid='{testId}'] .tm-decimal-input__input");

    [TestMethod]
    [Description("Playwright fill() over a grouped Czech value replaces it instead of appending")]
    public async Task Decimal7_FillOverFormattedValue_ReplacesIt()
    {
        var page = await OpenFormsPageAsync();
        var czech = Field(page, "decimal-input-czech");
        var bound = page.Locator("[data-testid='decimal-input-culture-value']");

        await SeedGroupedCzechValueAsync(czech);

        Assert.AreEqual("50 000,00", NormalizeSpaces(await czech.InputValueAsync()),
            "setup must leave the idle grouped Czech text so fill() races HandleFocus");
        await Expect(bound).ToHaveTextAsync("50000");

        await czech.FillAsync("12345,67");
        await czech.BlurAsync();

        await Expect(bound).ToHaveTextAsync("12345.67");
        Assert.AreEqual("12 345,67", NormalizeSpaces(await czech.InputValueAsync()),
            "fill() must replace the grouped value, not append to the ungrouped focus text");
    }

    [TestMethod]
    [Description("Tab into a grouped Czech value then type replaces it instead of appending")]
    public async Task Decimal7_TabThenType_ReplacesFormattedValue()
    {
        var page = await OpenFormsPageAsync();
        var czech = Field(page, "decimal-input-czech");
        var bound = page.Locator("[data-testid='decimal-input-culture-value']");

        await SeedGroupedCzechValueAsync(czech);
        Assert.AreEqual("50 000,00", NormalizeSpaces(await czech.InputValueAsync()));

        await page.Keyboard.PressAsync("Shift+Tab");
        await czech.PressSequentiallyAsync("7");
        await page.Keyboard.PressAsync("Tab");

        await Expect(bound).ToHaveTextAsync("7");
        Assert.AreEqual("7,00", NormalizeSpaces(await czech.InputValueAsync()),
            "typing over the native Tab selection must replace the grouped value, not append");
    }

    /// <summary>
    /// Sets the Czech field to 50 000 without <see cref="ILocator.FillAsync"/> so the setup
    /// itself cannot race HandleFocus.
    /// </summary>
    private static async Task SeedGroupedCzechValueAsync(ILocator input)
    {
        await input.ClickAsync();
        await input.PressAsync("Control+A");
        await input.PressSequentiallyAsync("50000");
        await input.PressAsync("Tab");
    }

    private static string NormalizeSpaces(string? value) =>
        (value ?? string.Empty).Replace('\u00A0', ' ').Replace('\u202F', ' ');
}
