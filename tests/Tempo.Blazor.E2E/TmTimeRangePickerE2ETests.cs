using System.IO;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 1c95309c Fáze 7 — E2E of TmTimeRangePicker overnight range and Popup/Auto adoption
/// on /time-input. Screenshots land in __screenshots__/time-input/.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmTimeRangePickerE2ETests : WasmTestBase
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
        float? deviceScaleFactor = null)
    {
        var options = new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            HasTouch = touch,
            IsMobile = touch,
        };
        if (deviceScaleFactor is { } dpr)
        {
            options.DeviceScaleFactor = dpr;
        }

        var page = await OpenAppPageAsync($"{BaseUrl}/time-input", options, PinCultureAsync);
        await page.WaitForSelectorAsync("[data-testid='time-range-overnight-card']", new PageWaitForSelectorOptions
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

    private static async Task AssertNoHorizontalOverflowAsync(ILocator card)
    {
        var overflow = await card.EvaluateAsync<string>(@"el => {
            if (el.scrollWidth > el.clientWidth + 1) {
                return `scrollWidth ${el.scrollWidth} > clientWidth ${el.clientWidth}`;
            }
            return '';
        }");
        Assert.AreEqual(string.Empty, overflow, "the overnight card must not overflow horizontally");
    }

    [TestMethod]
    public async Task TimeRange_Overnight_ShowsDurationWithoutError()
    {
        var page = await OpenTimeInputAsync(1280, 800);
        var card = page.Locator("[data-testid='time-range-overnight-card']");
        await Assertions.Expect(card.Locator(".tm-field-error")).ToHaveCountAsync(0);
        await Assertions.Expect(card.Locator(".tm-time-range-duration")).ToContainTextAsync("8h 00m");
        await Assertions.Expect(card.Locator(".tm-time-range-overnight")).ToContainTextAsync("(+1 day)");
        await Assertions.Expect(card.Locator(".tm-time-range-picker")).ToHaveAttributeAsync("data-tm-overnight", "true");
        await Assertions.Expect(page.Locator("[data-testid='time-range-overnight-echo']"))
            .ToContainTextAsync("22:00 – 06:00");
    }

    [TestMethod]
    public async Task TimeRange_Popup_DesktopCommitsBothEnds()
    {
        var page = await OpenTimeInputAsync(1280, 800);
        var card = page.Locator("[data-testid='time-range-overnight-card']");
        var startTrigger = card.Locator(".tm-time-range-from .tm-time-picker__trigger");
        await Assertions.Expect(startTrigger).ToHaveAttributeAsync("aria-haspopup", "dialog");

        await startTrigger.ClickAsync();
        await Assertions.Expect(page.Locator(".tm-time-picker__popup [data-testid='time-selector']")).ToBeVisibleAsync();
        await page.Locator(".tm-time-picker__popup [data-testid='time-selector-increase-hours']").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='time-range-overnight-echo']"))
            .ToContainTextAsync("23:00 – 06:00");
    }

    [TestMethod]
    public async Task TimeRange_Phone_SheetDoneCommits()
    {
        var page = await OpenTimeInputAsync(390, 844, touch: true);
        var card = page.Locator("[data-testid='time-range-overnight-card']");
        await Assertions.Expect(card.Locator(".tm-time-seg")).ToHaveCountAsync(0);

        await card.Locator(".tm-time-range-from .tm-time-picker__trigger").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-drawer")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".tm-overlay-panel-sheet__done")).ToBeVisibleAsync();
        await page.Locator(".tm-drawer [data-testid='time-selector-increase-hours']").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='time-range-overnight-echo']"))
            .ToContainTextAsync("22:00 – 06:00");

        await page.Locator(".tm-overlay-panel-sheet__done").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='time-range-overnight-echo']"))
            .ToContainTextAsync("23:00 – 06:00");
    }

    [TestMethod]
    public async Task TimeRange_Cover374_NoHorizontalOverflow()
    {
        var page = await OpenTimeInputAsync(374, 374, touch: true, deviceScaleFactor: 2);
        var card = page.Locator("[data-testid='time-range-overnight-card']");
        await AssertNoHorizontalOverflowAsync(card);

        var trigger = card.Locator(".tm-time-range-from .tm-time-picker__trigger");
        var box = await trigger.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box.Height >= 44, $"cover trigger height {box.Height} must be ≥ 44 px");
        Assert.IsTrue(box.Width >= 44, $"cover trigger width {box.Width} must be ≥ 44 px");

        await trigger.ClickAsync();
        await Assertions.Expect(page.Locator(".tm-drawer [data-testid='time-selector']"))
            .ToHaveAttributeAsync("data-tm-time-fit", "square");
    }

    [TestMethod]
    public async Task TimeRange_Flex399x306_FitsSquare()
    {
        var page = await OpenTimeInputAsync(399, 306, touch: true);
        var card = page.Locator("[data-testid='time-range-overnight-card']");
        await AssertNoHorizontalOverflowAsync(card);
        await card.Locator(".tm-time-range-from .tm-time-picker__trigger").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-drawer [data-testid='time-selector']"))
            .ToHaveAttributeAsync("data-tm-time-fit", "square");
    }

    [TestMethod]
    public async Task TimeRange_Axe_LightAndDark_NoCriticalOrSerious()
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
                    const host = document.querySelector("[data-testid='time-range-overnight-card']");
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
    public async Task TimeRange_Screenshots_ProposalViewports_LightAndDark()
    {
        var shots = new (int Width, int Height, bool Touch, string Suffix, float? Dpr)[]
        {
            (1440, 900, false, "1440", null),
            (390, 844, true, "390", null),
            (374, 374, true, "cover", 2f),
            (399, 306, true, "flex", null),
        };

        foreach (var shot in shots)
        {
            var page = await OpenTimeInputAsync(shot.Width, shot.Height, shot.Touch, shot.Dpr);
            foreach (var theme in new[] { "light", "dark" })
            {
                await EnsureDemoThemeAsync(page, theme);
                await ShotAsync(page.Locator("[data-testid='time-range-overnight-card']"),
                    $"time-range-overnight-{shot.Suffix}-{theme}");
            }
        }
    }
}
