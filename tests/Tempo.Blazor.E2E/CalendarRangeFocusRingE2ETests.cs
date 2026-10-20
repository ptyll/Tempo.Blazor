using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 16 follow-up proven in a real browser: the dark in-range fill of a date-range picker is
/// <c>primary-900</c> (the light <c>primary-100</c> put dark text on a light fill at 1.1:1), and
/// the migrated focus rules paint the two-layer <c>--tm-focus-ring</c> (4px outer layer), not the
/// soft 3px glow. The token-graph tests pin the declared values; these assertions read computed
/// styles, which is the only way to prove the chain resolves in a real theme switch.
/// </summary>
[TestClass]
public class CalendarRangeFocusRingE2ETests : WasmTestBase
{
    private const string PickersPage = "/pickers";

    /// <summary>Dark <c>--tm-cal-range-bg</c> = <c>var(--tm-color-primary-900)</c> = #1e3a8a.</summary>
    private const string DarkRangeFill = "rgb(30, 58, 138)";

    private async Task<IPage> OpenPickersPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}{PickersPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static async Task SelectFiveDayRangeAsync(IPage page)
    {
        await page.Locator(".tm-date-range-trigger").First.ClickAsync();
        await page.WaitForSelectorAsync(".tm-overlay-panel .tm-cal-day:visible",
            new PageWaitForSelectorOptions { Timeout = 15000 });
        var day = (int n) => page.Locator(".tm-overlay-panel .tm-cal-day:visible",
            new PageLocatorOptions { HasTextRegex = new Regex($"^{n}$") }).First;
        await day(10).ClickAsync();
        await page.WaitForTimeoutAsync(250);
        await day(15).ClickAsync();
        await page.WaitForTimeoutAsync(400);
    }

    [TestMethod]
    [Description("Dark in-range days fill with primary-900 (#1e3a8a); the old light primary-100 " +
        "fill measured 1.1:1 against dark day text")]
    public async Task DarkInRangeFill_IsPrimary900_NotTheLightWash()
    {
        var page = await OpenPickersPageAsync();
        await SelectFiveDayRangeAsync(page);

        // Selecting the end day closes the popup — switch the theme while no overlay is open
        // (an open overlay's layer intercepts the header toggle's pointer events), then
        // reopen the popup in dark mode to read the in-range fill.
        await page.Locator("button[title*='dark' i]:visible").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-theme='dark']",
            new PageWaitForSelectorOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);

        await page.Locator(".tm-date-range-trigger").First.ClickAsync();
        await page.WaitForSelectorAsync(".tm-cal-day--in-range",
            new PageWaitForSelectorOptions { Timeout = 15000 });

        var fill = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.tm-cal-day--in-range')).backgroundColor");
        StringAssert.Contains(fill, DarkRangeFill,
            "the dark in-range fill must be primary-900, not the light primary-100 (1.1:1)");
    }

    [TestMethod]
    [Description("The date-range trigger paints the two-layer --tm-focus-ring (4px outer layer), " +
        "not the 3px glow it replaced")]
    public async Task MigratedTriggerFocus_PaintsTheTwoLayerRing()
    {
        var page = await OpenPickersPageAsync();
        var trigger = page.Locator(".tm-date-range-trigger").First;
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.FocusAsync();
        var shadow = await trigger.EvaluateAsync<string>("el => getComputedStyle(el).boxShadow");
        StringAssert.Contains(shadow, "0px 0px 0px 4px",
            "the two-layer ring's outer layer is 4px; the old glow was a single 3px layer");
    }
}
