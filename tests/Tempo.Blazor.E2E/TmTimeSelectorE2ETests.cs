using System.IO;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 1c95309c Fáze 4 — E2E of TmTimeSelector on /time-input: spinbutton readout, +/−,
/// 12-hour period, out-of-range, ReadOnly, TmFormField cascade, and proposal viewports
/// 1440 / 390×844 / 399×306 / 399×353 in light and dark. Screenshots land in
/// __screenshots__/time-input/.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmTimeSelectorE2ETests : WasmTestBase
{
    private static readonly string ShotDir = Path.Combine(
        TestContextHelper.RepositoryRoot, "tests", "Tempo.Blazor.E2E", "__screenshots__", "time-input");

    private static Task PinCultureAsync(IBrowserContext context) =>
        context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");

    private async Task<IPage> OpenTimeInputAsync(int width, int height, bool touch = false)
    {
        var page = await OpenAppPageAsync(
            $"{BaseUrl}/time-input",
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
                HasTouch = touch,
                IsMobile = touch,
            },
            PinCultureAsync);

        await page.WaitForSelectorAsync("[data-testid='time-selector-inline-card']", new PageWaitForSelectorOptions
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
        await AssertNoOverflowAsync(target);
        await target.Page.WaitForTimeoutAsync(250);
        var path = Path.Combine(ShotDir, $"{name}.png");
        await target.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
        Assert.IsTrue(new FileInfo(path).Length > 2 * 1024, $"screenshot {name}.png must be a real capture");
        TestContext.AddResultFile(path);
    }

    private static async Task AssertNoOverflowAsync(ILocator card)
    {
        var root = card.Locator("[data-testid='time-selector']");
        await Assertions.Expect(root).ToBeVisibleAsync();
        var overflow = await root.EvaluateAsync<string>(@"el => {
            if (el.scrollWidth > el.clientWidth + 1) {
                return `root scrollWidth ${el.scrollWidth} > clientWidth ${el.clientWidth}`;
            }
            const rootBox = el.getBoundingClientRect();
            const targets = el.querySelectorAll('.tm-time-selector__step, [role=""spinbutton""], .tm-time-selector__period [data-testid=""segmented-option""]');
            for (const node of targets) {
                const box = node.getBoundingClientRect();
                if (box.width === 0 && box.height === 0) continue;
                if (box.left < rootBox.left - 1 || box.right > rootBox.right + 1) {
                    return `${node.getAttribute('data-testid') || node.getAttribute('data-tm-time-segment') || node.className} box ${box.left.toFixed(1)}–${box.right.toFixed(1)} outside root ${rootBox.left.toFixed(1)}–${rootBox.right.toFixed(1)}`;
                }
            }
            return '';
        }");
        Assert.AreEqual(string.Empty, overflow, "every stepper, spinbutton and AM/PM option must stay inside the selector (WCAG 1.4.10 / 2.5.8)");
    }

    [TestMethod]
    public async Task TimeSelector_Inline_ShowsSpinbuttonsAndSteppers()
    {
        var page = await OpenTimeInputAsync(1440, 900);
        var card = page.Locator("[data-testid='time-selector-inline-card']");
        await Assertions.Expect(card.Locator("[data-testid='time-selector']")).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator("[data-tm-time-segment='hours']")).ToHaveTextAsync("14");
        await Assertions.Expect(card.Locator("[data-tm-time-segment='minutes']")).ToHaveTextAsync("30");
        await Assertions.Expect(card.Locator("[data-testid='time-selector-increase-hours']")).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator("[data-testid='time-selector-host-input']")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task TimeSelector_IncreaseHour_UpdatesValue()
    {
        var page = await OpenTimeInputAsync(1440, 900);
        var card = page.Locator("[data-testid='time-selector-inline-card']");
        await card.Locator("[data-testid='time-selector-increase-hours']").ClickAsync();
        await Assertions.Expect(card.Locator("[data-tm-time-segment='hours']")).ToHaveTextAsync("15");
        await Assertions.Expect(card.Locator("[data-testid='time-selector-inline-echo']")).ToContainTextAsync("15:30");
    }

    [TestMethod]
    public async Task TimeSelector_12Hour_ShowsPeriod()
    {
        var page = await OpenTimeInputAsync(1440, 900);
        var card = page.Locator("[data-testid='time-selector-12h-card']");
        await Assertions.Expect(card.Locator("[data-tm-time-segment='hours']")).ToHaveTextAsync("02");
        await Assertions.Expect(card.Locator(".tm-time-selector__period")).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator("[data-testid='segmented-option'][data-value='True']"))
            .ToHaveAttributeAsync("aria-checked", "true");
    }

    [TestMethod]
    public async Task TimeSelector_12Hour_DoesNotOverflowAtPhoneAndFlex()
    {
        foreach (var (width, height) in new[] { (390, 844), (399, 306) })
        {
            var page = await OpenTimeInputAsync(width, height, touch: true);
            await AssertNoOverflowAsync(page.Locator("[data-testid='time-selector-12h-card']"));
        }
    }

    [TestMethod]
    public async Task TimeSelector_OutOfRange_StaysVisibleAndInvalid()
    {
        var page = await OpenTimeInputAsync(1440, 900);
        var card = page.Locator("[data-testid='time-selector-range-card']");
        await Assertions.Expect(card.Locator("[data-tm-time-segment='hours']")).ToHaveTextAsync("08");
        await Assertions.Expect(card.Locator("[data-tm-time-segment='hours']")).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(card.Locator(".tm-time-selector__error")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task TimeSelector_ReadOnly_HidesSteppers()
    {
        var page = await OpenTimeInputAsync(1440, 900);
        var card = page.Locator("[data-testid='time-selector-readonly-card']");
        await Assertions.Expect(card.Locator("[data-tm-time-segment='hours']")).ToHaveAttributeAsync("aria-readonly", "true");
        await Assertions.Expect(card.Locator("[data-testid='time-selector-increase-hours']")).ToHaveCountAsync(0);
    }

    [TestMethod]
    public async Task TimeSelector_FormField_HelpCascadeReachesSpinbuttons()
    {
        var page = await OpenTimeInputAsync(1440, 900);
        var hours = page.Locator("[data-testid='time-selector-form-card'] [data-tm-time-segment='hours']");
        var described = await hours.GetAttributeAsync("aria-describedby");
        Assert.IsFalse(string.IsNullOrWhiteSpace(described), "TmFormField help must land on the hours spinbutton");
        StringAssert.Contains(described, "time-selector-form-field-help");
    }

    [TestMethod]
    public async Task TimeSelector_CoarsePointer_TargetsMeetTouchTarget()
    {
        var page = await OpenTimeInputAsync(390, 844, touch: true);
        var box = await page.Locator("[data-testid='time-selector-inline-card'] [data-tm-time-segment='hours']").BoundingBoxAsync();
        Assert.IsNotNull(box, "hours spinbutton must be measurable");
        Assert.IsTrue(box.Height >= 44, $"coarse hours height {box.Height} must be ≥ 44 px");
        Assert.IsTrue(box.Width >= 44, $"coarse hours width {box.Width} must be ≥ 44 px");
    }

    [TestMethod]
    public async Task TimeSelector_Screenshots_ProposalViewports_LightAndDark()
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
            var page = await OpenTimeInputAsync(shot.Width, shot.Height, shot.Touch);
            foreach (var theme in new[] { "light", "dark" })
            {
                await EnsureDemoThemeAsync(page, theme);
                await ShotAsync(page.Locator("[data-testid='time-selector-inline-card']"), $"time-selector-inline-{shot.Suffix}-{theme}");
                await ShotAsync(page.Locator("[data-testid='time-selector-12h-card']"), $"time-selector-12h-{shot.Suffix}-{theme}");
                await ShotAsync(page.Locator("[data-testid='time-selector-range-card']"), $"time-selector-range-{shot.Suffix}-{theme}");
                await ShotAsync(page.Locator("[data-testid='time-selector-readonly-card']"), $"time-selector-readonly-{shot.Suffix}-{theme}");
            }
        }
    }
}
