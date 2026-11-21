using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for TmDecimalInput on the Forms demo page (WASM demo at 7106, API at 5100).
/// Covers the happy paths, the parsing/clamping edge cases, keyboard stepping, percent mode and
/// EditForm validation, plus light/dark screenshots for UX review in
/// <c>__screenshots__/decimal-input/</c>.
/// </summary>
[TestClass]
public class DecimalInputE2ETests : WasmTestBase
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

    /// <summary>Types a value and commits it the way a user does — by leaving the field.</summary>
    private static async Task CommitAsync(ILocator input, string text)
    {
        await input.ClickAsync();
        await input.FillAsync(text);
        await input.BlurAsync();
    }

    /// <summary>
    /// Switches the demo to dark mode. The demo renders two theme toggles (mobile + desktop shell)
    /// and marks the theme on the layout wrapper, not on &lt;html&gt;, so the shared helper's selector
    /// picks the hidden one — click the visible toggle and wait for data-theme instead.
    /// </summary>
    private static async Task EnableDarkModeAsync(IPage page)
    {
        var toggle = page.Locator("button[title*='dark' i]:visible").First;
        await toggle.ClickAsync();
        await page.WaitForSelectorAsync("[data-theme='dark']", new PageWaitForSelectorOptions { Timeout = 15000 });
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "decimal-input");
        Directory.CreateDirectory(dir);
        await page.Locator("[data-testid='decimal-input-section']").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(dir, fileName),
            Type = ScreenshotType.Png
        });
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

    [TestMethod]
    [Description("Decimal input parses comma and dot alike and formats for the culture on blur")]
    public async Task Decimal1_ParsesCommaAndDot_AndFormatsOnBlur()
    {
        var page = await OpenFormsPageAsync();
        var basic = Field(page, "decimal-input-basic");

        await CommitAsync(basic, "1234,56");
        await Expect(page.Locator("[data-testid='decimal-input-basic-value']")).ToHaveTextAsync("1234.56");

        await CommitAsync(basic, "9876.5");
        await Expect(page.Locator("[data-testid='decimal-input-basic-value']")).ToHaveTextAsync("9876.5");

        // Formatted on blur with a group separator (en-US demo culture → "9,876.50").
        var shown = await basic.InputValueAsync();
        StringAssert.Contains(shown, "876", $"formatted text was '{shown}'");
    }

    [TestMethod]
    [Description("Decimal input cleans pasted currency text and rejects garbage")]
    public async Task Decimal2_EdgeCases_PastedCurrencyAndGarbage()
    {
        var page = await OpenFormsPageAsync();
        var czech = Field(page, "decimal-input-czech");

        // Pasted Czech money: NBSP grouping, comma decimal, currency suffix.
        await CommitAsync(czech, "1 234,50 Kč");
        await Expect(page.Locator("[data-testid='decimal-input-culture-value']")).ToHaveTextAsync("1234.50");

        // The English twin shows the very same value in its own culture.
        var english = Field(page, "decimal-input-english");
        var englishText = await english.InputValueAsync();
        StringAssert.Contains(englishText, "1,234.50", $"en-US text was '{englishText}'");

        // Garbage clears the value instead of keeping a half-parsed number.
        await CommitAsync(czech, "abc");
        await Expect(page.Locator("[data-testid='decimal-input-culture-value']")).ToHaveTextAsync("null");
        await Expect(czech).ToHaveValueAsync(string.Empty);
    }

    [TestMethod]
    [Description("Decimal input clamps out-of-range input and steps with buttons and arrow keys")]
    public async Task Decimal3_EdgeCases_ClampAndSteppers()
    {
        var page = await OpenFormsPageAsync();
        var range = Field(page, "decimal-input-range");
        var value = page.Locator("[data-testid='decimal-input-range-value']");

        // Above Max → clamped to the Max value itself (10).
        await CommitAsync(range, "150");
        await Expect(value).ToHaveTextAsync("10");

        // Below Min → clamped to the Min value itself (0).
        await CommitAsync(range, "-4");
        await Expect(value).ToHaveTextAsync("0");

        // Decrement at the minimum is disabled, increment still works (step 0.5).
        var decrement = page.Locator("[data-testid='decimal-input-range'] .tm-decimal-input__decrement");
        var increment = page.Locator("[data-testid='decimal-input-range'] .tm-decimal-input__increment");
        await Expect(decrement).ToBeDisabledAsync();

        await increment.ClickAsync();
        await Expect(value).ToHaveTextAsync("0.5");

        // ArrowUp steps from the text the user just typed, not from the stale bound value.
        await range.ClickAsync();
        await range.FillAsync("3");
        await range.PressAsync("ArrowUp");
        await Expect(value).ToHaveTextAsync("3.5");

        await range.PressAsync("ArrowDown");
        await Expect(value).ToHaveTextAsync("3.0");
    }

    [TestMethod]
    [Description("Percent mode shows percent while the model keeps the fraction")]
    public async Task Decimal4_PercentMode_StoresFraction()
    {
        var page = await OpenFormsPageAsync();
        var percent = Field(page, "decimal-input-percent");
        var stored = page.Locator("[data-testid='decimal-input-percent-value']");

        await Expect(stored).ToHaveTextAsync("0.21");
        StringAssert.Contains(await percent.InputValueAsync(), "21");
        await Expect(page.Locator("[data-testid='decimal-input-percent'] .tm-decimal-input__suffix")).ToHaveTextAsync("%");

        await CommitAsync(percent, "12,5");
        await Expect(stored).ToHaveTextAsync("0.125");

        // Percent scale clamping: 150 % → 100 % → fraction 1.
        await CommitAsync(percent, "150");
        await Expect(stored).ToHaveTextAsync("1");
    }

    [TestMethod]
    [Description("EditForm validation message appears and clears for the bound decimal field")]
    public async Task Decimal5_EditFormValidation()
    {
        var page = await OpenFormsPageAsync();
        var validated = Field(page, "decimal-input-validated");
        var wrapper = page.Locator("[data-testid='decimal-input-validated'] .tm-decimal-input");

        await CommitAsync(validated, "5");

        var message = page.Locator("[data-testid='decimal-input-validated'] .tm-input-error-message");
        await Expect(message).ToContainTextAsync("Unit price must be between 10 and 1000.");
        await Expect(wrapper).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-decimal-input--error"));
        await Expect(validated).ToHaveAttributeAsync("aria-invalid", "true");

        await CommitAsync(validated, "250");
        await Expect(message).ToHaveCountAsync(0);
    }

    [TestMethod]
    [Description("Screenshots of the decimal input section in light and dark theme")]
    public async Task Decimal6_Screenshots_LightAndDark()
    {
        var page = await OpenFormsPageAsync();

        // Put the section into a representative state: a validation error on the validated field.
        await CommitAsync(Field(page, "decimal-input-validated"), "5");
        await page.Locator("[data-testid='decimal-input-section']").ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(300);

        await SaveScreenshotAsync(page, "decimal-input-light.png");

        await EnableDarkModeAsync(page);
        await page.Locator("[data-testid='decimal-input-section']").ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(400);

        await Expect(page.Locator("[data-theme='dark']").First).ToBeVisibleAsync();

        // The input must be repainted by the dark tokens, not left on the light surface.
        var background = await Field(page, "decimal-input-basic").EvaluateAsync<string>(
            "el => getComputedStyle(el.closest('.tm-decimal-input__control')).backgroundColor");
        Assert.AreNotEqual("rgb(255, 255, 255)", background,
            "the decimal input must not keep a hardcoded white surface in dark mode");

        // The invalid border must survive dark mode: the dark base rule is more specific than the
        // --error modifier, so a missing dark error rule silently repaints the border neutral.
        // --tm-color-danger lightens to #f87171 in the dark theme.
        var errorBorder = await Field(page, "decimal-input-validated").EvaluateAsync<string>(
            "el => getComputedStyle(el.closest('.tm-decimal-input__control')).borderTopColor");
        Assert.AreEqual("rgb(248, 113, 113)", errorBorder,
            $"the invalid field must keep its error border in dark mode, was '{errorBorder}'");

        await SaveScreenshotAsync(page, "decimal-input-dark.png");
    }
}
