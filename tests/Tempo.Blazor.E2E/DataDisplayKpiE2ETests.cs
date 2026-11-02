using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan ff043b2e phase 1 — the 2.10.0 data-display surface in a real browser. The <c>/data-display</c>
/// page renders the KPI stat cards (delta, hidden direction word, sparkline, loading skeleton), the
/// whole-card link/button targets and the animated numbers. Assertions read behaviour, not markup:
/// the ticker is caught MID-FLIGHT after a bump (so the JavaScript module really ran), the target is
/// landed on, reduced motion lands immediately, the delta carries its direction as text as well as
/// colour, and a scoped axe scan of the two new sections finds no critical or serious violation.
/// Reference screenshots land in <c>TestResults/data-display-kpi</c>.
/// </summary>
public abstract class DataDisplayKpiE2EBase : PlaywrightTestBase
{
    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "data-display-kpi");

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

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1000, bool touch = false, bool reduceMotion = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            Locale = "en-US",
            ReducedMotion = reduceMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
            IgnoreHTTPSErrors = true,
        });
        await context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/data-display");
        await page.WaitForSelectorAsync("[data-testid='animated-numbers']", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private static ILocator KpiRow(IPage page) => page.GetByTestId("stat-cards-kpi");

    private static ILocator BumpButton(IPage page) => page.GetByTestId("animated-bump");

    /// <summary>The first animated number of the showcase (the one the bump button drives).</summary>
    private static ILocator ShowcaseValue(IPage page)
        => page.Locator("[data-testid='animated-numbers'] .tm-animated-number__value").First;

    private static async Task<long> ParseValueAsync(ILocator value)
    {
        var raw = await value.TextContentAsync() ?? string.Empty;
        // en-US: "12,450" / "$12,450" — keep the digits only.
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var parsed) ? parsed : -1;
    }

    /// <summary>
    /// Samples the showcase value for a moment and returns every distinct number it showed. Used to
    /// prove the count-up actually animated in the browser rather than snapping to its target.
    /// </summary>
    private static async Task<List<long>> SampleAsync(ILocator value, int samples = 8, int everyMs = 60)
    {
        var seen = new List<long>();
        for (var i = 0; i < samples; i++)
        {
            var current = await ParseValueAsync(value);
            if (current >= 0 && (seen.Count == 0 || seen[^1] != current))
            {
                seen.Add(current);
            }

            await Task.Delay(everyMs);
        }

        return seen;
    }

    [TestMethod]
    public async Task AnimatedNumber_CountsUpToItsTarget_AndRetargetsOnBump()
    {
        var page = await OpenAsync();
        var value = ShowcaseValue(page);

        Assert.AreEqual(12450, await ParseValueAsync(value), "the first paint already shows the target, never a flash of zero");

        // A bump retargets the running ticker: the intermediate frames have to be observable, and the
        // last one has to be the new target.
        await BumpButton(page).ClickAsync();
        var seen = await SampleAsync(value);

        Assert.IsTrue(seen.Any(v => v > 12450 && v < 13450),
            "the count-up must be visible between the old and the new target; sampled: " + string.Join(", ", seen));
        await Assertions.Expect(value).ToHaveTextAsync("13,450", new LocatorAssertionsToHaveTextOptions { Timeout = 5000 });
    }

    [TestMethod]
    public async Task WithoutReducedMotion_TheTickerRuns_AndWithIt_TheValueLandsImmediately()
    {
        var animatedPage = await OpenAsync();
        await BumpButton(animatedPage).ClickAsync();
        var animatedSamples = await SampleAsync(ShowcaseValue(animatedPage));
        Assert.IsTrue(animatedSamples.Any(v => v > 12450 && v < 13450),
            "a motion-friendly context animates; sampled: " + string.Join(", ", animatedSamples));

        var reducedPage = await OpenAsync(reduceMotion: true);
        await BumpButton(reducedPage).ClickAsync();
        await reducedPage.WaitForTimeoutAsync(50);
        Assert.AreEqual(13450, await ParseValueAsync(ShowcaseValue(reducedPage)),
            "prefers-reduced-motion: reduce means the target appears at once — no ticker at all");
    }

    [TestMethod]
    public async Task StatCard_DeltaCarriesDirectionAsTextNotOnlyColour()
    {
        var page = await OpenAsync();
        var card = KpiRow(page).Locator(".tm-stat-card").First;

        var trend = card.Locator(".tm-stat-card__trend");
        Assert.AreEqual("up", await trend.GetAttributeAsync("data-trend"));

        var delta = await card.Locator(".tm-stat-card__delta").TextContentAsync();
        StringAssert.Matches(delta ?? string.Empty, new System.Text.RegularExpressions.Regex(@"^\+12[.,]5\s?%$"),
            "the delta is the culture-formatted signed percentage");

        Assert.AreEqual("Increase", (await card.Locator(".tm-stat-card__trend .tm-sr-only").TextContentAsync())?.Trim(),
            "colour is never the only carrier — the direction travels as a visually hidden word");
        Assert.AreEqual("vs. last month", (await card.Locator(".tm-stat-card__comparison").TextContentAsync())?.Trim());

        Assert.AreEqual(1, await card.Locator(".tm-stat-card__sparkline .tm-sparkline svg").CountAsync(),
            "the sparkline renders as a real SVG chart");
        Assert.AreEqual(1, await card.Locator(".tm-stat-card__icon svg[aria-hidden='true']").CountAsync(),
            "the decorative icon stays out of the accessibility tree");

        // The tone is the OUTCOME, not the direction: a falling churn rate (PositiveIsGood=false) is
        // painted exactly like a rise, while a flat delta takes the neutral tone.
        string DeltaColour(int index) => $"getComputedStyle(document.querySelectorAll(\"[data-testid='stat-cards-kpi'] .tm-stat-card\")[{index}].querySelector('.tm-stat-card__delta')).color";
        var riseColour = await page.EvaluateAsync<string>(DeltaColour(0));
        var fallIsGoodColour = await page.EvaluateAsync<string>(DeltaColour(1));
        var flatColour = await page.EvaluateAsync<string>(DeltaColour(2));

        Assert.AreEqual(riseColour, fallIsGoodColour,
            $"a fall is good news for this metric, so it paints like a rise ({riseColour})");
        Assert.AreNotEqual(riseColour, flatColour, "a flat delta takes the neutral tone, not the good one");
    }

    [TestMethod]
    public async Task StatCard_LoadingCard_IsBusyAndSkeletonised()
    {
        var page = await OpenAsync();
        var card = KpiRow(page).Locator(".tm-stat-card").Nth(3);

        Assert.AreEqual("true", await card.GetAttributeAsync("aria-busy"));
        Assert.IsTrue(await card.Locator(".tm-skeleton").CountAsync() >= 3, "three skeletons replace the value");
        Assert.AreEqual(0, await card.Locator(".tm-stat-value").CountAsync(), "no stale value stays on screen");
        StringAssert.Contains(await card.TextContentAsync() ?? string.Empty, "Loading",
            "the state is named for a reader, not only painted");
    }

    [TestMethod]
    public async Task StatCard_WholeCardTargets_AreOneNativeElementEach()
    {
        var page = await OpenAsync();
        var row = page.GetByTestId("stat-cards-interactive");

        var link = row.Locator("a.tm-stat-card").First;
        Assert.AreEqual("data-display", await link.GetAttributeAsync("href"));
        Assert.IsTrue((await link.GetAttributeAsync("class") ?? string.Empty).Contains("tm-stat-card--interactive"));

        var button = row.Locator("button.tm-stat-card").First;
        Assert.AreEqual("button", await button.EvaluateAsync<string>("el => el.tagName.toLowerCase()"));
        Assert.AreEqual("button", await button.GetAttributeAsync("type"));
        Assert.AreEqual(0, await row.Locator("a.tm-stat-card button, button.tm-stat-card a").CountAsync(),
            "one native target per card — never a nested control");

        // A native button is keyboard-activatable without any key emulation from the component.
        await button.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual("button", await page.EvaluateAsync<string>(
            "() => document.activeElement && document.activeElement.tagName.toLowerCase()"),
            "Enter fires the native click without moving the focus away");
    }

    // ── axe and the visual reference ───────────────────────────────────────────

    private static async Task AssertNoAxeViolationsAsync(IPage page, string selector, string state)
    {
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
            """, selector);
        Assert.AreEqual(0, violations.Length, $"axe [{state}]: " + string.Join(" | ", violations));
    }

    [TestMethod]
    public async Task Axe_Scoped_To_TheNewSections_FindsNoCriticalOrSeriousViolation()
    {
        var page = await OpenAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });

        await AssertNoAxeViolationsAsync(page, "[data-testid='stat-cards-kpi']", "KPI cards");
        await AssertNoAxeViolationsAsync(page, "[data-testid='stat-cards-interactive']", "whole-card targets");
        await AssertNoAxeViolationsAsync(page, "[data-testid='animated-numbers']", "animated numbers");
    }

    [TestMethod]
    public async Task Visual_Reference_LightDark_At_1440_And_375()
    {
        var desktop = await OpenAsync(1440, 1000);
        await desktop.GetByTestId("stat-cards-kpi").ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(desktop, "[data-testid='stat-cards-kpi']");
        await ShootAsync(desktop, "data-display-kpi-cards-light-1440");

        await desktop.GetByTestId("animated-numbers").ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(desktop, "[data-testid='animated-numbers']");
        await ShootAsync(desktop, "data-display-kpi-numbers-light-1440");

        await ToggleDarkModeAsync(desktop);
        await desktop.GetByTestId("stat-cards-kpi").ScrollIntoViewIfNeededAsync();
        await ShootAsync(desktop, "data-display-kpi-cards-dark-1440");
        await desktop.GetByTestId("animated-numbers").ScrollIntoViewIfNeededAsync();
        await ShootAsync(desktop, "data-display-kpi-numbers-dark-1440");

        var phone = await OpenAsync(375, 812, touch: true);
        await phone.GetByTestId("stat-cards-kpi").ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(phone, "[data-testid='stat-cards-kpi']");
        await ShootAsync(phone, "data-display-kpi-cards-light-375");
        await phone.GetByTestId("animated-numbers").ScrollIntoViewIfNeededAsync();
        await ShootAsync(phone, "data-display-kpi-numbers-light-375");
    }

    private async Task ShootAsync(IPage page, string name)
    {
        await page.WaitForTimeoutAsync(300);
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
public class DataDisplayKpiE2ETests : DataDisplayKpiE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}
