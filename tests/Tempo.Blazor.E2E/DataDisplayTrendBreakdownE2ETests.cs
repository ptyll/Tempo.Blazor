using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan ff043b2e phase 2 — <c>TmTrendList</c> and <c>TmBreakdownCard</c> in a real browser. The
/// assertions read behaviour, not markup: the delta is the culture-formatted signed percentage with
/// its direction also in text, the trend row links are real anchors (and the refused href renders no
/// target), the breakdown shares add up to 100 % with the strip hidden from assistive technology, the
/// period switch re-sorts the rows, reduced motion still reorders (just without the FLIP), a coarse
/// pointer gets the 44 px row target, and a scoped axe scan of the two new sections finds no critical
/// or serious violation. Reference screenshots land in <c>TestResults/data-display-trend-breakdown</c>.
/// </summary>
public abstract class DataDisplayTrendBreakdownE2EBase : PlaywrightTestBase
{
    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "data-display-trend-breakdown");

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

    private async Task<IPage> OpenAsync(
        int width = 1440,
        int height = 1000,
        bool touch = false,
        bool reduceMotion = false)
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
        await page.WaitForSelectorAsync(
            "[data-testid='breakdown-card-section'] .tm-breakdown-card",
            new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private static ILocator TrendSection(IPage page) => page.GetByTestId("trend-list-section");

    private static ILocator BreakdownSection(IPage page) => page.GetByTestId("breakdown-card-section");

    private static ILocator PeriodCard(IPage page) => BreakdownSection(page).Locator(".tm-breakdown-card").First;

    private static async Task<List<string>> ShareTextsAsync(IPage page)
    {
        var shares = await PeriodCard(page).Locator(".tm-breakdown-card__share").AllTextContentsAsync();
        return [.. shares.Select(share => share.Trim())];
    }

    private static int PercentOf(string share)
        => int.Parse(new string(share.Where(char.IsDigit).ToArray()), System.Globalization.CultureInfo.InvariantCulture);

    [TestMethod]
    public async Task TrendList_RendersTheListAndTheTable_LightAndBusy()
    {
        var page = await OpenAsync();
        var section = TrendSection(page);

        // Three lists: the compact list, the table layout and the loading state.
        Assert.AreEqual(3, await section.Locator(".tm-trend-list").CountAsync());
        Assert.IsTrue(await section.Locator("ul.tm-trend-list__items > li.tm-trend-list__item").CountAsync() >= 4,
            "the compact list renders one semantic row per metric");
        Assert.AreEqual(4, await section.Locator("table.tm-trend-list__table tbody tr").CountAsync(),
            "the table layout renders the same metrics with real rows");

        var headers = await section.Locator("table.tm-trend-list__table thead th").AllTextContentsAsync();
        CollectionAssert.AreEqual(new[] { "Item", "Value", "Change" }, headers.Select(h => h.Trim()).ToArray());

        Assert.AreEqual(4, await section.Locator("li.tm-trend-list__item .tm-sr-only").CountAsync(),
            "every row names its direction for a reader, not only in colour");
        Assert.AreEqual(1, await section.Locator(".tm-trend-list__item .tm-sparkline svg").CountAsync());
        Assert.AreEqual("true", await section.Locator(".tm-trend-list[aria-busy='true']").First.GetAttributeAsync("aria-busy"));
        Assert.AreEqual(0, await section.Locator("[aria-live]").CountAsync(),
            "the demo opts into no live region — a list that re-renders must not become a chorus");
    }

    [TestMethod]
    public async Task TrendList_DeltaIsCultureFormatted_AndTheDirectionIsAlsoText()
    {
        var page = await OpenAsync();
        var row = TrendSection(page).Locator("li.tm-trend-list__item").First;

        var trend = row.Locator(".tm-trend-list__trend");
        Assert.AreEqual("up", await trend.GetAttributeAsync("data-trend"));

        var delta = (await row.Locator(".tm-trend-list__delta").TextContentAsync())?.Trim();
        StringAssert.Matches(delta ?? string.Empty,
            new System.Text.RegularExpressions.Regex(@"^\+12[.,]5\s?%$"),
            "the delta is the culture-formatted signed percentage, not a hardcoded '%' literal");
        Assert.AreEqual("Increase", (await row.Locator(".tm-trend-list__trend .tm-sr-only").TextContentAsync())?.Trim());

        // The tone is the OUTCOME, not the direction: the falling churn row (PositiveIsGood=false)
        // paints like a rise and still reads "Decrease".
        var churn = TrendSection(page).Locator("li.tm-trend-list__item").Nth(1);
        Assert.AreEqual("down", await churn.Locator(".tm-trend-list__trend").GetAttributeAsync("data-trend"));
        Assert.AreEqual("Decrease", (await churn.Locator(".tm-trend-list__trend .tm-sr-only").TextContentAsync())?.Trim());

        var rise = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelectorAll(\"[data-testid='trend-list-section'] li.tm-trend-list__item\")[0].querySelector('.tm-trend-list__trend')).color");
        var fallIsGood = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelectorAll(\"[data-testid='trend-list-section'] li.tm-trend-list__item\")[1].querySelector('.tm-trend-list__trend')).color");
        Assert.AreEqual(rise, fallIsGood, "a fall that is good news is painted exactly like a rise");
    }

    [TestMethod]
    public async Task TrendList_LinkIsOneNativeTarget_AndKeepsTheFocusRing()
    {
        var page = await OpenAsync();
        var link = TrendSection(page).Locator("li.tm-trend-list__item a.tm-trend-list__link").First;

        Assert.AreEqual(1, await link.CountAsync(), "the NPS row carries the demo's link");
        Assert.AreEqual("data-display", await link.GetAttributeAsync("href"));
        Assert.AreEqual(0, await TrendSection(page).Locator("li.tm-trend-list__item a button, li.tm-trend-list__item button a").CountAsync(),
            "one native target per row — never a nested control");

        await link.FocusAsync();
        Assert.AreEqual("a", await page.EvaluateAsync<string>(
            "() => document.activeElement && document.activeElement.tagName.toLowerCase()"));

        // The shared focus ring is the two-layer box-shadow plus a transparent outline, so
        // forced-colors still paints the system ring.
        var boxShadow = await link.EvaluateAsync<string>("el => getComputedStyle(el).boxShadow");
        Assert.AreNotEqual("none", boxShadow, "the link paints the library's focus ring");
        var outlineStyle = await link.EvaluateAsync<string>("el => getComputedStyle(el).outlineStyle");
        Assert.AreEqual("solid", outlineStyle, "the transparent outline is the forced-colors fallback");
    }

    [TestMethod]
    public async Task Breakdown_StripIsDecorative_AndTheSharesAddUpToOneHundred()
    {
        var page = await OpenAsync();
        var card = PeriodCard(page);

        Assert.AreEqual("true", await card.Locator(".tm-breakdown-card__bar").GetAttributeAsync("aria-hidden"),
            "the table carries the data; the painted strip must not be announced a second time");

        var shares = await ShareTextsAsync(page);
        Assert.AreEqual(4, shares.Count);
        Assert.AreEqual(100, shares.Sum(PercentOf), "the rounded shares add up to exactly 100 %, never 99 or 101");

        Assert.AreEqual(4, await card.Locator(".tm-breakdown-card__bar .tm-breakdown-card__segment").CountAsync());

        var headers = await card.Locator("table.tm-breakdown-card__table thead th").AllTextContentsAsync();
        CollectionAssert.AreEqual(new[] { "Item", "Value", "Share" }, headers.Select(h => h.Trim()).ToArray());
        Assert.AreEqual(4, await card.Locator("table.tm-breakdown-card__table tbody th[scope='row']").CountAsync(),
            "every part is a row header, so a reader hears which value belongs to which part");

        // The row header must keep its table-cell box: a display:flex on the th makes the browser wrap
        // it in an anonymous cell, and WebKit/VoiceOver then loses the row-header role — which is the
        // whole point of rendering the data as a table. The swatch + label layout rides the wrapper.
        var rowHeader = card.Locator("table.tm-breakdown-card__table tbody th[scope='row']").First;
        Assert.AreEqual("table-cell", await rowHeader.EvaluateAsync<string>("el => getComputedStyle(el).display"),
            "the row header stays a real table cell");
        Assert.AreEqual("flex",
            await rowHeader.Locator(".tm-breakdown-card__label-inner")
                .EvaluateAsync<string>("el => getComputedStyle(el).display"),
            "the swatch + label flex layout rides the inner wrapper");
        Assert.AreEqual("true", await rowHeader.Locator(".tm-breakdown-card__swatch").GetAttributeAsync("aria-hidden"),
            "the decorative swatch stays out of the row header's accessible name");
    }

    [TestMethod]
    public async Task Breakdown_PeriodSwitch_ReSortsTheRows()
    {
        var page = await OpenAsync();
        var card = PeriodCard(page);
        var firstLabel = await card.Locator("tbody tr .tm-breakdown-card__label").First.TextContentAsync();
        StringAssert.Contains(firstLabel ?? string.Empty, "Direct");

        // The demo binds Period; selecting "Last week" swaps the values, so another channel leads.
        await card.Locator(".tm-breakdown-card__period label.tm-radio-segment").Nth(1).ClickAsync();

        await Assertions.Expect(card.Locator("tbody tr .tm-breakdown-card__label").First)
            .ToContainTextAsync("Search", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });

        var shares = await ShareTextsAsync(page);
        Assert.AreEqual(100, shares.Sum(PercentOf), "the new order still adds up to 100 %");

        // The declared-total card keeps its explicit remainder row all the way through.
        var totalCard = BreakdownSection(page).Locator(".tm-breakdown-card").Nth(1);
        Assert.AreEqual(5, await totalCard.Locator("tbody tr").CountAsync(),
            "four parts plus the explicit 'Other' row a declared total leaves over");
        Assert.AreEqual("Other", (await totalCard.Locator(".tm-breakdown-card__row--remainder .tm-breakdown-card__label").TextContentAsync())?.Trim());
    }

    [TestMethod]
    public async Task Breakdown_Reorder_RunsTheFlipAnimation()
    {
        var page = await OpenAsync();
        var card = PeriodCard(page);

        // Register the probe BEFORE the switch so the transient animation cannot be missed by a
        // sampling race: the listener is on the card (capture), so it also catches the recreated row
        // nodes the versioned Blazor key produces.
        await card.EvaluateAsync(
            """
            el => {
                window.__tmFlipSeen = [];
                el.addEventListener(
                    'animationstart',
                    event => window.__tmFlipSeen.push(event.animationName),
                    true);
                el.addEventListener(
                    'animationend',
                    event => window.__tmFlipSeen.push('end:' + event.animationName),
                    true);
            }
            """);

        Assert.AreEqual(2, await card.Locator(".tm-breakdown-card__period label.tm-radio-segment").CountAsync(),
            "the demo offers two periods");
        await card.Locator(".tm-breakdown-card__period label.tm-radio-segment").Nth(1).ClickAsync();

        await Assertions.Expect(card.Locator("tbody tr .tm-breakdown-card__label").First)
            .ToContainTextAsync("Search", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });
        await page.WaitForTimeoutAsync(600);

        var seen = await page.EvaluateAsync<string[]>("() => window.__tmFlipSeen || []");
        CollectionAssert.Contains(seen, "tm-breakdown-row-flip",
            "a reorder has to run the FLIP keyframe animation, not just re-order the DOM; seen: "
            + string.Join(", ", seen));

        // The moved rows carry the start offset the animation's first keyframe reads.
        var shifted = await card.Locator("tbody tr[style]").CountAsync();
        Assert.IsTrue(shifted >= 1, "at least one row carries its --tm-breakdown-card-row-shift offset");
        StringAssert.Contains(
            await card.Locator("tbody tr[style]").First.GetAttributeAsync("style") ?? string.Empty,
            "--tm-breakdown-card-row-shift");
    }

    [TestMethod]
    public async Task Breakdown_ReducedMotion_StillReorders_JustWithoutTheFlip()
    {
        var page = await OpenAsync(reduceMotion: true);
        var card = PeriodCard(page);

        await card.Locator(".tm-breakdown-card__period label.tm-radio-segment").Nth(1).ClickAsync();

        await Assertions.Expect(card.Locator("tbody tr .tm-breakdown-card__label").First)
            .ToContainTextAsync("Search", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });

        var transform = await card.Locator("tbody tr").First.EvaluateAsync<string>(
            "el => getComputedStyle(el).transform");
        Assert.IsTrue(transform is "none" || transform == "matrix(1, 0, 0, 1, 0, 0)",
            $"reduced motion must not offset a row (transform={transform})");
    }

    [TestMethod]
    public async Task CoarsePointer_GivesTheTrendRowAFortyFourPixelTarget()
    {
        var page = await OpenAsync(390, 844, touch: true);
        var row = TrendSection(page).Locator("li.tm-trend-list__item").First;
        await row.ScrollIntoViewIfNeededAsync();

        var box = await row.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box!.Height >= 44, $"a coarse pointer gets the 44 px row target (height={box.Height})");
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

        await AssertNoAxeViolationsAsync(page, "[data-testid='trend-list-section']", "trend list");
        await AssertNoAxeViolationsAsync(page, "[data-testid='breakdown-card-section']", "breakdown card");
    }

    [TestMethod]
    public async Task Visual_Reference_LightDark_At_1440_And_375()
    {
        var desktop = await OpenAsync(1440);
        await TrendSection(desktop).ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(desktop, "[data-testid='trend-list-section']");
        await ShootAsync(desktop, "data-trend-list-light-1440");

        await BreakdownSection(desktop).ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(desktop, "[data-testid='breakdown-card-section']");
        await ShootAsync(desktop, "data-breakdown-card-light-1440");

        await ToggleDarkModeAsync(desktop);
        await TrendSection(desktop).ScrollIntoViewIfNeededAsync();
        await ShootAsync(desktop, "data-trend-list-dark-1440");
        await BreakdownSection(desktop).ScrollIntoViewIfNeededAsync();
        await ShootAsync(desktop, "data-breakdown-card-dark-1440");

        var phone = await OpenAsync(375, 812, touch: true);
        await TrendSection(phone).ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(phone, "[data-testid='trend-list-section']");
        await ShootAsync(phone, "data-trend-list-light-375");
        await BreakdownSection(phone).ScrollIntoViewIfNeededAsync();
        await ShootAsync(phone, "data-breakdown-card-light-375");
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
public class DataDisplayTrendBreakdownE2ETests : DataDisplayTrendBreakdownE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}
