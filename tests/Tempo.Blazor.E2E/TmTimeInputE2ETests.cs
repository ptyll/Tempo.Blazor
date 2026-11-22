using System.IO;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 1c95309c Fáze 3 — E2E of the TmTimeInput defect fixes on /pickers: 12-hour clock,
/// out-of-range stays visible, ReadOnly, TmFormField cascade, and a 44 px coarse-pointer
/// target. Screenshots land in __screenshots__/time-input/ at 1440 / 390×844 / 399×306
/// in light and dark.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmTimeInputE2ETests : WasmTestBase
{
    private static readonly string ShotDir = Path.Combine(
        TestContextHelper.RepositoryRoot, "tests", "Tempo.Blazor.E2E", "__screenshots__", "time-input");

    private static Task PinCultureAsync(IBrowserContext context) =>
        context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");

    private async Task<IPage> OpenPickersAsync(int width, int height, bool touch = false)
    {
        var page = await OpenAppPageAsync(
            $"{BaseUrl}/pickers",
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
                HasTouch = touch,
                IsMobile = touch,
            },
            PinCultureAsync);

        await page.WaitForSelectorAsync("[data-testid='time-input-12h-card']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60_000,
        });
        return page;
    }

    private async Task ShotAsync(ILocator target, string name)
    {
        Directory.CreateDirectory(ShotDir);
        await target.ScrollIntoViewIfNeededAsync();
        await target.Page.Mouse.MoveAsync(0, 0);
        await target.Page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await target.Page.WaitForTimeoutAsync(250);
        var path = Path.Combine(ShotDir, $"{name}.png");
        await target.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
        Assert.IsTrue(new FileInfo(path).Length > 2 * 1024, $"screenshot {name}.png must be a real capture");
        TestContext.AddResultFile(path);
    }

    [TestMethod]
    public async Task TimeInput_12Hour_ShowsPaddedHourAndPeriod()
    {
        var page = await OpenPickersAsync(1440, 900);
        var card = page.Locator("[data-testid='time-input-12h-card']");
        await Assertions.Expect(card.Locator(".tm-time-seg--hours")).ToHaveValueAsync("02");
        await Assertions.Expect(card.Locator(".tm-time-input__period")).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator("[data-testid='segmented-option'][data-value='True']"))
            .ToHaveAttributeAsync("aria-checked", "true");
    }

    [TestMethod]
    public async Task TimeInput_OutOfRange_StaysVisibleAndInvalid()
    {
        var page = await OpenPickersAsync(1440, 900);
        var card = page.Locator("[data-testid='time-input-out-of-range-card']");
        var hours = card.Locator(".tm-time-seg--hours");
        await hours.FillAsync("8");
        await hours.PressAsync("Tab");

        await Assertions.Expect(hours).ToHaveValueAsync("08");
        await Assertions.Expect(hours).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(card.Locator(".tm-time-input__error")).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator("p span.font-mono").First).ToHaveTextAsync("08:00");
    }

    [TestMethod]
    public async Task TimeInput_ReadOnly_BlocksEdit()
    {
        var page = await OpenPickersAsync(1440, 900);
        var card = page.Locator("[data-testid='time-input-readonly-card']");
        var hours = card.Locator(".tm-time-seg--hours");
        await Assertions.Expect(hours).ToHaveAttributeAsync("readonly", "");
        await Assertions.Expect(hours).ToHaveAttributeAsync("aria-readonly", "true");
        await Assertions.Expect(card.Locator(".tm-time-input")).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("tm-time-input--readonly"));
    }

    [TestMethod]
    public async Task TimeInput_FormField_HelpCascadeReachesSegments()
    {
        var page = await OpenPickersAsync(1440, 900);
        var hours = page.Locator("[data-testid='time-input-form-field-card'] .tm-time-seg--hours");
        var described = await hours.GetAttributeAsync("aria-describedby");
        Assert.IsFalse(string.IsNullOrWhiteSpace(described), "TmFormField help must land on the hours segment");
        StringAssert.Contains(described, "time-input-form-field-field-help");
    }

    [TestMethod]
    public async Task TimeInput_CoarsePointer_SegmentsMeetTouchTarget()
    {
        var page = await OpenPickersAsync(390, 844, touch: true);
        var box = await page.Locator("[data-testid='time-input-12h'] .tm-time-seg--hours").BoundingBoxAsync();
        Assert.IsNotNull(box, "hours segment must be measurable");
        Assert.IsTrue(box.Height >= 44, $"coarse hours height {box.Height} must be ≥ 44 px");
        Assert.IsTrue(box.Width >= 44, $"coarse hours width {box.Width} must be ≥ 44 px");
    }

    [TestMethod]
    public async Task TimeInput_Screenshots_ProposalViewports_LightAndDark()
    {
        var shots = new (int Width, int Height, bool Touch, string Suffix)[]
        {
            (1440, 900, false, "1440"),
            (390, 844, true, "390"),
            (399, 306, true, "flex"),
            (399, 353, true, "flex-353"),
        };

        foreach (var shot in shots)
        {
            var page = await OpenPickersAsync(shot.Width, shot.Height, shot.Touch);
            foreach (var theme in new[] { "light", "dark" })
            {
                await EnsureDemoThemeAsync(page, theme);
                await ShotAsync(page.Locator("[data-testid='time-input-12h-card']"), $"time-input-12h-{shot.Suffix}-{theme}");
                await ShotAsync(page.Locator("[data-testid='time-input-out-of-range-card']"), $"time-input-range-{shot.Suffix}-{theme}");
                await ShotAsync(page.Locator("[data-testid='time-input-readonly-card']"), $"time-input-readonly-{shot.Suffix}-{theme}");
                await ShotAsync(page.Locator("[data-testid='time-input-form-field-card']"), $"time-input-form-{shot.Suffix}-{theme}");
            }
        }
    }
}
