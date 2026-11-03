using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan ff043b2e phase 7 — <c>TmUsageMeter</c> in a real browser. The assertions read behaviour: the
/// demo's monthly credit quota renders as a <c>role="meter"</c> track whose ARIA value matches the
/// visible readout, the warning state shows the near-limit call to action, two clicks walk the meter
/// over the limit (the over-limit line appears, the "Other" category row grows, the percentage keeps
/// counting above 100 %), the reset button restores the month, the compact copy drops the chrome, the
/// track keeps a visible edge under forced colors, the fill's width transition is clamped by
/// reduced motion, the meter stays inside a 390 px viewport, the dark theme re-points the surface, and
/// a scoped axe scan of the section finds no critical or serious violation. Reference screenshots
/// (light, dark, phone) land in <c>TestResults/usage-meter</c>.
/// </summary>
public abstract class UsageMeterE2EBase : PlaywrightTestBase
{
    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "usage-meter");

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static ILocator Section(IPage page) => page.Locator("[data-testid='usage-meter-section']");

    private static ILocator FullMeter(IPage page)
        => Section(page).Locator("[data-testid='usage-meter']").First;

    private static ILocator CompactMeter(IPage page)
        => Section(page).Locator("[data-testid='usage-meter']").Nth(1);

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1000, bool reduceMotion = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            ReducedMotion = reduceMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
            IgnoreHTTPSErrors = true,
        });
        await context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/data-display");
        await page.WaitForSelectorAsync(
            "[data-testid='usage-meter-section'] [role='meter']",
            new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    [TestMethod]
    public async Task Meter_ExposesItsValueContract()
    {
        var page = await OpenAsync();

        Assert.AreEqual(2, await Section(page).Locator("[role='meter']").CountAsync(),
            "the section renders the full meter and one compact copy");

        var track = FullMeter(page).Locator("[role='meter']");
        Assert.AreEqual("0", await track.GetAttributeAsync("aria-valuemin"));
        Assert.AreEqual("10000", await track.GetAttributeAsync("aria-valuemax"));
        Assert.AreEqual("8200", await track.GetAttributeAsync("aria-valuenow"));

        var valueText = await track.GetAttributeAsync("aria-valuetext");
        Assert.AreEqual("8,200 of 10,000 credits, 82%", valueText?.Replace("\u00A0", " "));

        var labelledBy = await track.GetAttributeAsync("aria-labelledby");
        Assert.IsFalse(string.IsNullOrEmpty(labelledBy), "the visible heading names the meter");
        Assert.AreEqual("Monthly credits", (await page.Locator($"#{labelledBy}").TextContentAsync())?.Trim());

        var describedBy = await track.GetAttributeAsync("aria-describedby");
        StringAssert.Contains(
            (await page.Locator($"#{describedBy}").TextContentAsync()) ?? string.Empty,
            "Resets on",
            "the reset line describes the meter, so the reader hears when the quota renews");

        await ShootAsync(page, "usage-meter-light");
    }

    [TestMethod]
    public async Task WarningState_ShowsTheNearLimitCallToAction()
    {
        var page = await OpenAsync();

        var meter = FullMeter(page);
        Assert.AreEqual("warning", await meter.GetAttributeAsync("data-state"));
        StringAssert.Contains(await meter.GetAttributeAsync("class") ?? string.Empty, "tm-usage-meter--warning");

        var state = meter.Locator(".tm-usage-meter__state");
        StringAssert.Contains((await state.TextContentAsync()) ?? string.Empty, "Near limit");
        Assert.AreEqual(1, await state.Locator("svg").CountAsync(), "the state carries an icon, not only a colour");

        await Assertions.Expect(meter.Locator("[data-testid='usage-meter-upgrade']")).ToBeVisibleAsync();

        // The category segments can cover the whole fill, so the track carries a state ring as well.
        var outline = await meter.Locator("[role='meter']")
            .EvaluateAsync<string>("el => `${getComputedStyle(el).outlineStyle} ${getComputedStyle(el).outlineWidth}`");
        Assert.AreEqual("solid 2px", outline, "the warning state also rings the track, not only the fill");
    }

    [TestMethod]
    public async Task CategoryTable_IsTheAccessibleAlternativeToTheStrip()
    {
        var page = await OpenAsync();

        var table = FullMeter(page).Locator(".tm-usage-meter__table");
        await Assertions.Expect(table).ToBeVisibleAsync();

        Assert.AreEqual("Usage by category", (await table.Locator("caption").TextContentAsync())?.Trim());

        var headers = await table.Locator("thead th").AllTextContentsAsync();
        CollectionAssert.AreEqual(
            new[] { "Category", "Used", "Share" },
            headers.Select(text => text.Trim()).ToArray());

        var rows = table.Locator("tbody tr");
        Assert.AreEqual(3, await rows.CountAsync());
        Assert.AreEqual("row", await rows.First.Locator("th").GetAttributeAsync("scope"));
        Assert.AreEqual(0, await FullMeter(page).Locator(".tm-usage-meter__row--other").CountAsync(),
            "the demo categories explain the whole usage at load");
    }

    [TestMethod]
    public async Task TwoClicksWalkTheMeterOverTheLimit()
    {
        var page = await OpenAsync();

        await Section(page).Locator("[data-testid='usage-meter-add']").ClickAsync();
        await page.WaitForTimeoutAsync(200);
        Assert.AreEqual("warning", await FullMeter(page).GetAttributeAsync("data-state"),
            "8 200 + 1 500 is 9 700 — still under the limit");

        await Section(page).Locator("[data-testid='usage-meter-add']").ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var meter = FullMeter(page);
        Assert.AreEqual("critical", await meter.GetAttributeAsync("data-state"));
        Assert.AreEqual("true", await meter.GetAttributeAsync("data-over-limit"));

        var track = meter.Locator("[role='meter']");
        Assert.AreEqual("11200", await track.GetAttributeAsync("aria-valuenow"),
            "the reader hears the true amount, not the capped bar");
        StringAssert.EndsWith(await track.GetAttributeAsync("aria-valuetext") ?? string.Empty, "112%",
            "the percentage keeps counting above 100 %");

        var over = meter.Locator(".tm-usage-meter__over");
        await Assertions.Expect(over).ToBeVisibleAsync();
        Assert.AreEqual("Over limit by 1,200 credits", (await over.TextContentAsync())?.Trim());

        Assert.AreEqual(1, await meter.Locator(".tm-usage-meter__row--other").CountAsync(),
            "usage the categories do not explain becomes an explicit Other row");

        await ShootAsync(page, "usage-meter-over-limit");
    }

    [TestMethod]
    public async Task ResetRestoresTheInitialMonth()
    {
        var page = await OpenAsync();

        await Section(page).Locator("[data-testid='usage-meter-add']").ClickAsync();
        await Section(page).Locator("[data-testid='usage-meter-add']").ClickAsync();
        await page.WaitForTimeoutAsync(200);
        await Section(page).Locator("[data-testid='usage-meter-reset']").ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var meter = FullMeter(page);
        Assert.AreEqual("warning", await meter.GetAttributeAsync("data-state"));
        Assert.AreEqual("8200", await meter.Locator("[role='meter']").GetAttributeAsync("aria-valuenow"));
        Assert.AreEqual(0, await meter.Locator(".tm-usage-meter__row--other").CountAsync());
        Assert.IsNull(await meter.GetAttributeAsync("data-over-limit"));
    }

    [TestMethod]
    public async Task CompactCopy_KeepsItsVisibleTitleAndDropsTheChrome()
    {
        var page = await OpenAsync();

        var compact = CompactMeter(page);
        StringAssert.Contains(await compact.GetAttributeAsync("class") ?? string.Empty, "tm-usage-meter--compact");
        Assert.AreEqual(0, await compact.Locator(".tm-usage-meter__table").CountAsync());
        Assert.AreEqual(0, await compact.Locator(".tm-usage-meter__daily").CountAsync());

        var title = compact.Locator(".tm-usage-meter__title");
        Assert.AreEqual(1, await title.CountAsync(),
            "a compact meter keeps a VISIBLE label, so a sighted reader is not left with a nameless bar");
        Assert.AreEqual("Monthly credits", (await title.TextContentAsync())?.Trim());
        Assert.AreEqual("SPAN", await title.EvaluateAsync<string>("el => el.tagName"),
            "the compact label is not a new heading in the page outline");

        var labelledBy = await compact.Locator("[role='meter']").GetAttributeAsync("aria-labelledby");
        Assert.IsFalse(string.IsNullOrEmpty(labelledBy), "the visible label also names the meter");
        Assert.AreEqual(
            "Monthly credits",
            (await page.Locator($"#{labelledBy}").TextContentAsync())?.Trim());
        Assert.IsNull(await compact.Locator("[role='meter']").GetAttributeAsync("aria-label"),
            "the meter carries exactly one name");
    }

    [TestMethod]
    public async Task ForcedColors_KeepTheTrackBordered()
    {
        var page = await OpenAsync();
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ForcedColors = ForcedColors.Active });
        await page.WaitForTimeoutAsync(200);

        var border = await page.EvaluateAsync<string>(
            """
            () => {
                const track = document.querySelector("[data-testid='usage-meter-section'] [role='meter']");
                if (!track) return "missing";
                const style = getComputedStyle(track);
                return `${style.borderTopWidth} ${style.borderTopStyle}`;
            }
            """);

        Assert.AreNotEqual("missing", border);
        StringAssert.Contains(border, "solid", "the track keeps an edge when the UA replaces the fill");
    }

    [TestMethod]
    public async Task ReducedMotion_ClampsTheFillTransition()
    {
        var page = await OpenAsync(reduceMotion: true);

        var duration = await page.EvaluateAsync<string>(
            """
            () => {
                const fill = document.querySelector("[data-testid='usage-meter-section'] .tm-usage-meter__fill");
                if (!fill) return "missing";
                return getComputedStyle(fill).transitionDuration;
            }
            """);

        Assert.AreNotEqual("missing", duration);

        // Chromium normalizes a 0.01 ms clamp to "1e-05s", so accept every spelling of "effectively 0".
        var seconds = duration.EndsWith("ms", StringComparison.Ordinal)
            ? double.Parse(duration[..^2], System.Globalization.CultureInfo.InvariantCulture) / 1000
            : double.Parse(duration.TrimEnd('s'), System.Globalization.CultureInfo.InvariantCulture);

        Assert.IsTrue(seconds <= 0.001,
            $"prefers-reduced-motion must clamp the fill's width transition (got {duration})");
    }

    [TestMethod]
    public async Task PhoneWidth_KeepsTheMeterInsideTheViewport()
    {
        var page = await OpenAsync(width: 390, height: 844);

        var overflow = await page.EvaluateAsync<double>(
            "() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.IsTrue(overflow <= 1, $"a 390 px phone must not scroll the page sideways (overflow {overflow}px)");

        var box = await FullMeter(page).BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box!.X >= -1 && box.X + box.Width <= 391,
            $"the meter stays inside the 390 px viewport (x {box.X}, width {box.Width})");

        var table = await FullMeter(page).Locator(".tm-usage-meter__table").BoundingBoxAsync();
        Assert.IsNotNull(table);
        Assert.IsTrue(table!.X + table.Width <= 391, "the category table wraps inside the card instead of overflowing");

        await ShootAsync(page, "usage-meter-phone");
    }

    [TestMethod]
    public async Task DarkTheme_RepointsTheSurfaceAndTheTrack()
    {
        var page = await OpenAsync();

        var light = await FullMeter(page).EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");

        await page.EvaluateAsync(
            """
            () => {
                document.documentElement.setAttribute('data-theme', 'dark');
                document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', 'dark'));
            }
            """);
        await page.WaitForTimeoutAsync(300);

        var dark = await FullMeter(page).EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.AreEqual("rgb(30, 41, 59)", dark, "the dark theme re-points --tm-bg-surface (#1e293b)");
        Assert.AreNotEqual(light, dark, "the card really re-painted for the dark theme");

        var track = await FullMeter(page).Locator("[role='meter']")
            .EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.AreEqual("rgb(51, 65, 85)", track, "the track uses the dark --tm-bg-muted (#334155)");

        await ShootAsync(page, "usage-meter-dark");
    }

    [TestMethod]
    public async Task ScopedAxeScan_HasNoCriticalOrSeriousViolations()
    {
        var page = await OpenAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });

        var violations = await page.EvaluateAsync<string[]>(
            """
            async (selector) => {
                const host = document.querySelector(selector);
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).slice(0, 4).join('; ')})`);
            }
            """, "[data-testid='usage-meter-section']");

        Assert.AreEqual(0, violations.Length, "axe: " + string.Join(" | ", violations));
    }

    private async Task ShootAsync(IPage page, string name)
    {
        // The data-display page is long, so a viewport screenshot would frame the badges at the top
        // instead of the meter — scroll the section into view BEFORE the shot.
        await Section(page).ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(400);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, $"{name}.png"),
            FullPage = false,
        });
    }
}

/// <summary>The WASM lane (the demo default).</summary>
[TestClass]
public class UsageMeterE2ETests : UsageMeterE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}
