using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Regression E2E for TmNumberInput's invalid state in dark mode. The dark base rule
/// (<c>[data-theme="dark"] .tm-number-input</c>, specificity 0,2,0) is more specific than the plain
/// <c>.tm-number-input--error</c> modifier (0,1,0), so a missing dark error rule silently repaints
/// the invalid border neutral — a class-based unit test cannot see this, only a computed style can.
/// </summary>
[TestClass]
public class NumberInputDarkModeE2ETests : WasmTestBase
{
    private const string FeedbackPage = "/feedback";

    /// <summary>--tm-color-danger: #dc2626 in the light theme (one step darker than red-500 so the
    /// white ink Tempo paints on it keeps AA), lightened to #f87171 in the dark one.</summary>
    private const string LightDanger = "rgb(220, 38, 38)";
    private const string DarkDanger = "rgb(248, 113, 113)";

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private async Task<IPage> OpenFeedbackPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}{FeedbackPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var section = page.Locator("[data-testid='number-input-section']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();
        return page;
    }

    private static ILocator ErrorControl(IPage page) =>
        page.Locator("[data-testid='number-input-error'] .tm-number-input");

    private static Task<string> BorderColorAsync(ILocator control) =>
        control.EvaluateAsync<string>("el => getComputedStyle(el).borderTopColor");

    [TestMethod]
    [Description("TmNumberInput keeps its error border in both light and dark mode")]
    public async Task NumberInput_ErrorBorder_SurvivesDarkMode()
    {
        var page = await OpenFeedbackPageAsync();
        var control = ErrorControl(page);

        await Expect(control).ToBeVisibleAsync();
        Assert.AreEqual(LightDanger, await BorderColorAsync(control),
            "the invalid number input must be outlined in the danger colour in light mode");

        // Switch to dark: the demo marks the theme on the layout wrapper and renders two toggles
        // (mobile + desktop shell), so click the visible one and wait for data-theme.
        await page.Locator("button[title*='dark' i]:visible").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-theme='dark']", new PageWaitForSelectorOptions { Timeout = 15000 });
        await page.Locator("[data-testid='number-input-section']").ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(400);

        var darkBorder = await BorderColorAsync(control);
        Assert.AreEqual(DarkDanger, darkBorder,
            $"the invalid number input must keep its error border in dark mode, was '{darkBorder}'");
    }
}
