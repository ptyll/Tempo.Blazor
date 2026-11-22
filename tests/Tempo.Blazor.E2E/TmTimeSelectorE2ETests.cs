using System.IO;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 1c95309c Fáze 5 — E2E of TmTimeSelector on /time-input: spinbutton readout, hour/minute
/// grid, host-owned presets, square tap-to-open, TypeTime, proposal viewports (1440 / 1280×800 /
/// 768×1024 / 390×844 / 844×390 / 667×375 / 399×306 / 399×353 / 374×374), axe, forced-colors,
/// reduced motion and the four demo cultures. Screenshots land in __screenshots__/time-input/.
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

    private async Task<IPage> OpenTimeInputAsync(
        int width,
        int height,
        bool touch = false,
        bool pinCulture = true,
        Func<IBrowserContext, Task>? extraInit = null)
    {
        Func<IBrowserContext, Task>? beforeNavigate = null;
        if (pinCulture || extraInit is not null)
        {
            beforeNavigate = async context =>
            {
                if (pinCulture)
                {
                    await PinCultureAsync(context);
                }

                if (extraInit is not null)
                {
                    await extraInit(context);
                }
            };
        }

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
            beforeNavigate);

        await page.WaitForSelectorAsync("[data-testid='time-selector-inline-card']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60_000,
        });
        return page;
    }

    private static Task InstallFakeVisualViewportAsync(IBrowserContext context, int width, double height)
        => context.AddInitScriptAsync(
            $$"""
            (() => {
                const vv = new EventTarget();
                let vvHeight = {{height.ToString(System.Globalization.CultureInfo.InvariantCulture)}};
                let vvWidth = {{width}};
                Object.defineProperties(vv, {
                    height: { get() { return vvHeight; }, configurable: true },
                    width: { get() { return vvWidth; }, configurable: true },
                    offsetTop: { get() { return 0; } },
                    offsetLeft: { get() { return 0; } },
                    scale: { get() { return 1; } },
                });
                window.__tmSetVisualViewportHeight = (h) => {
                    vvHeight = h;
                    vv.dispatchEvent(new Event('resize'));
                };
                Object.defineProperty(window, 'visualViewport', { configurable: true, get: () => vv });
            })();
            """);

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
            (1280, 800, false, "1280"),
            (768, 1024, true, "tablet"),
            (390, 844, true, "390"),
            (844, 390, true, "landscape"),
            (667, 375, true, "se"),
            (399, 306, true, "flex"),
            (399, 353, true, "flex-353"),
            (374, 374, true, "cover"),
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

    [TestMethod]
    public async Task TimeSelector_Desktop_ShowsBothGridsAndPresets()
    {
        var page = await OpenTimeInputAsync(1280, 800);
        var card = page.Locator("[data-testid='time-selector-inline-card']");
        var root = card.Locator("[data-testid='time-selector']");
        await Assertions.Expect(root).ToHaveAttributeAsync("data-tm-time-layout", "desktop");
        await Assertions.Expect(card.Locator("[data-testid='time-selector-grid-hours'] [data-tm-time-grid-cell]")).ToHaveCountAsync(24);
        await Assertions.Expect(card.Locator("[data-testid='time-selector-grid-minutes'] [data-tm-time-grid-cell]")).ToHaveCountAsync(12);
        await Assertions.Expect(card.Locator("[data-testid='time-selector-grid-tabs']")).ToHaveCountAsync(0);
        await Assertions.Expect(card.Locator("[data-testid='time-selector-presets']")).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator("[data-testid='time-selector-preset-now']")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task TimeSelector_Phone_ShowsHoursTabThenSwitchesOnHourTap()
    {
        var page = await OpenTimeInputAsync(390, 844, touch: true);
        var card = page.Locator("[data-testid='time-selector-inline-card']");
        await Assertions.Expect(card.Locator("[data-testid='time-selector-grid-tabs'] [data-value='Hours']"))
            .ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(card.Locator("[data-testid='time-selector-grid-hours']")).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator("[data-testid='time-selector-type-time']")).ToBeVisibleAsync();

        await card.Locator("[data-testid='time-selector-grid-hours'] [data-tm-time-hour='9']").ClickAsync();
        await Assertions.Expect(card.Locator("[data-testid='time-selector-grid-tabs'] [data-value='Minutes']"))
            .ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(card.Locator("[data-testid='time-selector-inline-echo']")).ToContainTextAsync("09:30");
    }

    [TestMethod]
    public async Task TimeSelector_LandscapePhone_UsesTabletLayout()
    {
        var page = await OpenTimeInputAsync(844, 390, touch: true);
        var root = page.Locator("[data-testid='time-selector-inline-card'] [data-testid='time-selector']");
        await Assertions.Expect(root).ToHaveAttributeAsync("data-tm-time-layout", "tablet");
        await Assertions.Expect(root.Locator("[data-testid='time-selector-grid-hours']")).ToBeVisibleAsync();
        await Assertions.Expect(root.Locator("[data-testid='time-selector-grid-minutes']")).ToBeVisibleAsync();
        await Assertions.Expect(root.Locator("[data-testid='time-selector-grid-tabs']")).ToHaveCountAsync(0);
    }

    [TestMethod]
    public async Task TimeSelector_FlexWindow_HidesGridUntilHoursTap()
    {
        var page = await OpenTimeInputAsync(399, 306, touch: true);
        var card = page.Locator("[data-testid='time-selector-inline-card']");
        var root = card.Locator("[data-testid='time-selector']");
        await Assertions.Expect(root).ToHaveAttributeAsync("data-tm-time-fit", "square");
        await Assertions.Expect(card.Locator("[data-testid='time-selector-grid-hours']")).ToHaveCountAsync(0);
        await Assertions.Expect(card.Locator("[data-testid='time-selector-type-time']")).ToHaveCountAsync(0);

        await card.Locator("[data-tm-time-segment='hours']").ClickAsync();
        await Assertions.Expect(root).ToHaveAttributeAsync("data-tm-time-grid-open", "true");
        await Assertions.Expect(card.Locator("[data-testid='time-selector-grid-hours']")).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator("[data-testid='time-selector-presets']")).ToHaveCountAsync(0);
    }

    [TestMethod]
    public async Task TimeSelector_PresetChip_CommitsValue()
    {
        var page = await OpenTimeInputAsync(390, 844, touch: true);
        var card = page.Locator("[data-testid='time-selector-inline-card']");
        await card.Locator("[data-testid='time-selector-preset'][data-tm-time-preset='12:00']").ClickAsync();
        await Assertions.Expect(card.Locator("[data-testid='time-selector-inline-echo']")).ToContainTextAsync("12:00");
        await Assertions.Expect(card.Locator("[data-tm-time-segment='hours']")).ToHaveTextAsync("12");
    }

    [TestMethod]
    public async Task TimeSelector_TypeTime_CommitsNumericInput()
    {
        var page = await OpenTimeInputAsync(390, 844, touch: true);
        var card = page.Locator("[data-testid='time-selector-inline-card']");
        await card.Locator("[data-testid='time-selector-type-time']").ClickAsync();
        var input = card.Locator("[data-testid='time-selector-type-input']");
        await Assertions.Expect(input).ToHaveAttributeAsync("inputmode", "numeric");
        await input.FillAsync("09:15");
        await input.PressAsync("Enter");
        await Assertions.Expect(card.Locator("[data-testid='time-selector-inline-echo']")).ToContainTextAsync("09:15");
    }

    [TestMethod]
    public async Task TimeSelector_KeyboardOpen_OnSquare_HidesGridAndSteppers()
    {
        var page = await OpenTimeInputAsync(399, 844, touch: true);
        var card = page.Locator("[data-testid='time-selector-inline-card']");
        await page.SetViewportSizeAsync(399, 93);
        await card.Locator("[data-testid='time-selector-host-input']").FocusAsync();

        await Assertions.Expect(card.Locator("[data-testid='time-selector']"))
            .ToHaveAttributeAsync("data-tm-time-keyboard", "open");
        await Assertions.Expect(card.Locator("[data-testid='time-selector']"))
            .ToHaveAttributeAsync("data-tm-time-fit", "square");
        await Assertions.Expect(card.Locator("[data-testid='time-selector-grid-hours']")).ToHaveCountAsync(0);
        await Assertions.Expect(card.Locator("[data-testid='time-selector-presets']")).ToHaveCountAsync(0);
        await Assertions.Expect(card.Locator("[data-testid='time-selector-increase-hours']")).ToHaveCountAsync(0);
    }

    [TestMethod]
    public async Task TimeSelector_KeyboardOpen_OnSquare_ViaFakeVisualViewport()
    {
        var page = await OpenTimeInputAsync(
            399,
            306,
            touch: true,
            extraInit: context => InstallFakeVisualViewportAsync(context, 399, 306));
        var card = page.Locator("[data-testid='time-selector-inline-card']");
        await card.Locator("[data-tm-time-segment='hours']").ClickAsync();
        await Assertions.Expect(card.Locator("[data-testid='time-selector-grid-hours']")).ToBeVisibleAsync();

        await card.Locator("[data-testid='time-selector-host-input']").FocusAsync();
        await page.EvaluateAsync("() => window.__tmSetVisualViewportHeight(93.5)");

        await Assertions.Expect(card.Locator("[data-testid='time-selector']"))
            .ToHaveAttributeAsync("data-tm-time-keyboard", "open");
        await Assertions.Expect(card.Locator("[data-testid='time-selector-grid-hours']")).ToHaveCountAsync(0);
        await Assertions.Expect(card.Locator("[data-testid='time-selector-presets']")).ToHaveCountAsync(0);
        await Assertions.Expect(card.Locator("[data-testid='time-selector-increase-hours']")).ToHaveCountAsync(0);
    }

    [TestMethod]
    public async Task TimeSelector_KeyboardOpen_OnRegular_KeepsChromeAndTypeTime()
    {
        var page = await OpenTimeInputAsync(
            390,
            844,
            touch: true,
            extraInit: context => InstallFakeVisualViewportAsync(context, 390, 844));
        var card = page.Locator("[data-testid='time-selector-inline-card']");
        await card.Locator("[data-testid='time-selector-host-input']").FocusAsync();
        await page.EvaluateAsync("() => window.__tmSetVisualViewportHeight(450)");

        var root = card.Locator("[data-testid='time-selector']");
        await Assertions.Expect(root).ToHaveAttributeAsync("data-tm-time-keyboard", "open");
        await Assertions.Expect(root).ToHaveAttributeAsync("data-tm-time-fit", "regular");
        await Assertions.Expect(card.Locator("[data-testid='time-selector-type-time']")).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator("[data-testid='time-selector-grid-hours']")).ToBeVisibleAsync();
        var wrap = await card.Locator(".tm-time-selector__readout").EvaluateAsync<string>(
            "el => getComputedStyle(el).flexWrap");
        Assert.AreNotEqual("nowrap", wrap, "regular + keyboard must not collapse to the one-line square readout");
    }

    [TestMethod]
    public async Task TimeSelector_SquareHourCells_MeetFortyFourPxOrFallBackToSixByFour()
    {
        var page = await OpenTimeInputAsync(399, 306, touch: true);
        var card = page.Locator("[data-testid='time-selector-inline-card']");
        await card.Locator("[data-tm-time-segment='hours']").ClickAsync();
        var hours = card.Locator("[data-testid='time-selector-grid-hours']");
        await Assertions.Expect(hours).ToBeVisibleAsync();
        var columns = await hours.GetAttributeAsync("data-tm-time-grid-columns");
        var cell = hours.Locator("[data-tm-time-grid-cell]").First;
        var box = await cell.BoundingBoxAsync();
        Assert.IsNotNull(box, "hour cell must be measurable");
        if (box.Width < 44)
        {
            Assert.AreEqual("6", columns, "cells under 44 px must fall back to 6×4");
        }
        else
        {
            Assert.IsTrue(box.Width >= 44, $"hour cell width {box.Width} must be ≥ 44 px");
        }
    }

    [TestMethod]
    public async Task TimeSelector_SquareHoursAndMinutes_FitWithoutPageScroll()
    {
        var page = await OpenTimeInputAsync(399, 306, touch: true);
        var card = page.Locator("[data-testid='time-selector-inline-card']");
        await card.Locator("[data-tm-time-segment='hours']").ClickAsync();
        await Assertions.Expect(card.Locator("[data-testid='time-selector-grid-hours']")).ToBeVisibleAsync();
        var hoursFit = await card.Locator("[data-testid='time-selector']").EvaluateAsync<bool>(
            "el => el.scrollHeight <= el.clientHeight + 1");
        Assert.IsTrue(hoursFit, "open hours grid must not grow the selector past the square");

        await card.Locator("[data-tm-time-segment='minutes']").ClickAsync();
        await Assertions.Expect(card.Locator("[data-testid='time-selector-grid-minutes']")).ToBeVisibleAsync();
        var minutesFit = await card.Locator("[data-testid='time-selector']").EvaluateAsync<bool>(
            "el => el.scrollHeight <= el.clientHeight + 1");
        Assert.IsTrue(minutesFit, "open minutes grid must not grow the selector past the square");
        var activeTag = await page.EvaluateAsync<string>(
            "() => document.activeElement && document.activeElement.tagName");
        Assert.AreNotEqual("INPUT", activeTag, "tapping a spinbutton must not land focus on an editable field");
        Assert.AreNotEqual("TEXTAREA", activeTag);
    }

    [TestMethod]
    public async Task TimeSelector_ShowSeconds_CollapsesGridOnSquare()
    {
        var page = await OpenTimeInputAsync(399, 306, touch: true);
        var card = page.Locator("[data-testid='time-selector-seconds-card']");
        var root = card.Locator("[data-testid='time-selector']");
        await Assertions.Expect(root).ToHaveAttributeAsync("data-tm-time-fit", "square");
        await Assertions.Expect(card.Locator("[data-testid='time-selector-grid-hours']")).ToHaveCountAsync(0);
        await card.Locator("[data-tm-time-segment='hours']").ClickAsync();
        await Assertions.Expect(card.Locator("[data-testid='time-selector-grid-hours']")).ToHaveCountAsync(0);
        await Assertions.Expect(card.Locator("[data-tm-time-segment='seconds']")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task TimeSelector_Flex353_StaysSquareWithoutOverflow()
    {
        var page = await OpenTimeInputAsync(399, 353, touch: true);
        var root = page.Locator("[data-testid='time-selector-inline-card'] [data-testid='time-selector']");
        await Assertions.Expect(root).ToHaveAttributeAsync("data-tm-time-fit", "square");
        await AssertNoOverflowAsync(page.Locator("[data-testid='time-selector-inline-card']"));
    }

    [TestMethod]
    public async Task TimeSelector_CoverSquare_ReadoutStaysInside360()
    {
        var page = await OpenTimeInputAsync(374, 374, touch: true);
        var readout = page.Locator("[data-testid='time-selector-inline-card'] .tm-time-selector__readout");
        var box = await readout.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box.Width <= 360, $"readout width {box.Width} must stay ≤ 360 px on 374×374");
        await AssertNoOverflowAsync(page.Locator("[data-testid='time-selector-inline-card']"));
    }

    [TestMethod]
    public async Task TimeSelector_PaddingBottom_MeetsSpace3()
    {
        foreach (var (width, height, touch) in new[] { (390, 844, true), (768, 1024, true), (1280, 800, false) })
        {
            var page = await OpenTimeInputAsync(width, height, touch);
            var padding = await page.EvaluateAsync<double>(
                """
                () => {
                    const el = document.querySelector("[data-testid='time-selector-inline-card'] [data-testid='time-selector']");
                    return parseFloat(getComputedStyle(el).paddingBottom);
                }
                """);
            Assert.IsTrue(padding >= 12, $"{width}×{height} padding-bottom {padding} must be ≥ 12 px (--tm-space-3)");
        }
    }

    [TestMethod]
    public async Task TimeSelector_SeLandscape_UsesTabletLayout()
    {
        var page = await OpenTimeInputAsync(667, 375, touch: true);
        var root = page.Locator("[data-testid='time-selector-inline-card'] [data-testid='time-selector']");
        await Assertions.Expect(root).ToHaveAttributeAsync("data-tm-time-layout", "tablet");
        await Assertions.Expect(root.Locator("[data-testid='time-selector-grid-tabs']")).ToHaveCountAsync(0);
        var fit = await root.GetAttributeAsync("data-tm-time-fit");
        if (fit != "square")
        {
            await Assertions.Expect(root.Locator("[data-testid='time-selector-grid-hours']")).ToBeVisibleAsync();
            await Assertions.Expect(root.Locator("[data-testid='time-selector-grid-minutes']")).ToBeVisibleAsync();
        }
    }

    [TestMethod]
    public async Task TimeSelector_HourGrid_ArrowsMoveDomFocus()
    {
        var page = await OpenTimeInputAsync(390, 844, touch: true);
        var card = page.Locator("[data-testid='time-selector-inline-card']");
        var hours = card.Locator("[data-testid='time-selector-grid-hours']");
        var first = hours.Locator("[data-tm-time-hour='0']");
        await first.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        var rightHour = await page.EvaluateAsync<string?>(
            "() => document.activeElement?.getAttribute('data-tm-time-hour')");
        Assert.AreEqual("1", rightHour, "ArrowRight must move document.activeElement to the next hour cell");

        await page.Keyboard.PressAsync("ArrowDown");
        var downHour = await page.EvaluateAsync<string?>(
            "() => document.activeElement?.getAttribute('data-tm-time-hour')");
        Assert.AreEqual("7", downHour, "ArrowDown on a 6-column hour grid from 01 lands on 07");
    }

    [TestMethod]
    public async Task TimeSelector_Axe_LightAndDark_NoCriticalOrSerious()
    {
        var page = await OpenTimeInputAsync(1280, 800);
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });
        foreach (var theme in new[] { "light", "dark" })
        {
            await EnsureDemoThemeAsync(page, theme);
            var violations = await page.EvaluateAsync<string[]>(
                """
                async () => {
                    const host = document.querySelector("[data-testid='time-selector-inline-card']");
                    await Promise.all(document.getAnimations()
                        .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                        .map(a => a.finished.catch(() => { })));
                    await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                    const result = await axe.run(host, {
                        runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                        resultTypes: ['violations']
                    });
                    return result.violations
                        .filter(v => v.impact === 'critical' || v.impact === 'serious')
                        .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).slice(0, 4).join('; ')})`);
                }
                """);
            Assert.AreEqual(0, violations.Length, $"axe [{theme}]: " + string.Join(" | ", violations));
        }
    }

    [TestMethod]
    public async Task TimeSelector_ForcedColors_SelectedCellUsesHighlight()
    {
        var page = await OpenTimeInputAsync(1280, 800);
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ForcedColors = ForcedColors.Active });
        await page.WaitForTimeoutAsync(400);
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(forced-colors: active)').matches"));
        var cell = page.Locator("[data-testid='time-selector-inline-card'] [data-testid='time-selector-grid-hours'] [aria-checked='true']");
        await Assertions.Expect(cell).ToBeVisibleAsync();
        var decoration = await cell.EvaluateAsync<string>("el => getComputedStyle(el).textDecorationLine");
        StringAssert.Contains(decoration, "underline");
    }

    [TestMethod]
    public async Task TimeSelector_ReducedMotion_DoesNotAnimateGrid()
    {
        var page = await OpenTimeInputAsync(1280, 800);
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(prefers-reduced-motion: reduce)').matches"));
        var duration = await page.EvaluateAsync<string>(
            """
            () => {
                const cell = document.querySelector("[data-testid='time-selector-grid-hours'] [data-tm-time-grid-cell]");
                return cell ? getComputedStyle(cell).transitionDuration : '0s';
            }
            """);
        Assert.IsTrue(duration == "0s" || duration == "0s, 0s",
            $"grid cells must not animate under reduced motion (got {duration})");
    }

    [TestMethod]
    public async Task TimeSelector_FourCultures_LocalizeGroupLabel()
    {
        // PinCultureAsync is a context init script, so a language-switcher reload would
        // immediately re-apply English. Open without the pin and wait after each switch.
        var page = await OpenTimeInputAsync(1280, 800, pinCulture: false);
        var expected = new Dictionary<string, string>
        {
            ["en"] = "Time picker",
            ["cs"] = "Výběr času",
            ["sk"] = "Výber času",
            ["fr"] = "Sélecteur d'heure",
        };
        foreach (var (culture, label) in expected)
        {
            if (culture != "en")
            {
                await SwitchLanguageAsync(page, culture);
            }

            await page.WaitForSelectorAsync("[data-testid='time-selector-inline-card']", new PageWaitForSelectorOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 60_000,
            });
            await Assertions.Expect(page.Locator("[data-testid='time-selector-inline-card'] [data-testid='time-selector']"))
                .ToHaveAttributeAsync("aria-label", label);
        }
    }
}
