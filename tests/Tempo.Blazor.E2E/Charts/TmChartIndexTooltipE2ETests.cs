using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan ff043b2e phase 3 — the <c>TmChart</c> index tooltip and the <c>TmSparkline</c> extras in a
/// real browser. The assertions read behaviour, not markup: hovering anywhere in a category draws the
/// crosshair and lists every series at that index, the chart is ONE tab stop whose arrow keys move the
/// index and whose Escape hides the card, exactly one polite live region announces the index, the
/// sparkline tooltip follows hover and keyboard while its box never overflows, the min/max caption
/// stays decorative, and a scoped axe scan of the two new sections finds no critical or serious
/// violation. Reference screenshots land in <c>TestResults/charts-index-tooltip</c>.
/// </summary>
public abstract class TmChartIndexTooltipE2EBase : PlaywrightTestBase
{
    private const string IndexSection = "[data-testid='charts-demo-index-tooltip']";
    private const string SparkSection = "[data-testid='charts-demo-sparkline-extras']";

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "charts-index-tooltip");

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

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1000, bool touch = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        await context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/charts");
        await page.WaitForSelectorAsync(
            $"{IndexSection} .tm-chart__index-zone",
            new PageWaitForSelectorOptions { Timeout = 60000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private static ILocator DefaultChart(IPage page)
        => page.Locator($"{IndexSection} [data-testid='charts-demo-index-default'] .tm-chart");

    private static ILocator CustomChart(IPage page)
        => page.Locator($"{IndexSection} [data-testid='charts-demo-index-custom'] .tm-chart");

    /// <summary>
    /// Moves the real mouse onto a zone and waits until the chart reports that index. A single
    /// teleporting <c>Mouse.MoveAsync</c> is not enough in this harness — measured, Chromium delivers
    /// the leaving zone's <c>mouseout</c> but never the entering zone's <c>mouseover</c> for a
    /// one-step jump, so the pointer is nudged a couple of pixels within the zone like a real one
    /// until the state follows. The assertion still demands the final state, so a chart that ignored
    /// a legitimate hover fails here.
    /// </summary>
    private static async Task HoverZoneAsync(IPage page, ILocator chart, int ordinal)
    {
        var zone = chart.Locator(".tm-chart__index-zone").Nth(ordinal);
        await zone.ScrollIntoViewIfNeededAsync();
        var expected = ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var box = await zone.BoundingBoxAsync();
            Assert.IsNotNull(box, $"zone {ordinal} has no box");
            await page.Mouse.MoveAsync(
                box!.X + box.Width / 2 + attempt % 3,
                box.Y + box.Height / 2 + attempt / 3);
            await page.WaitForTimeoutAsync(120);
            if (await chart.GetAttributeAsync("data-index-ordinal") == expected)
            {
                return;
            }
        }

        var hit = await page.EvaluateAsync<string>(
            """
            () => {
                const zones = document.querySelectorAll("[data-testid='charts-demo-index-default'] .tm-chart__index-zone");
                const el = zones[%ORDINAL%];
                if (!el) return 'no zone';
                const b = el.getBoundingClientRect();
                const at = document.elementFromPoint(b.x + b.width / 2, b.y + b.height / 2);
                return (at ? at.tagName + '|' + (at.getAttribute('class') ?? '') : 'null');
            }
            """.Replace("%ORDINAL%", expected, StringComparison.Ordinal));
        Assert.Fail($"zone {ordinal} never became the active index (reported "
                    + $"'{await chart.GetAttributeAsync("data-index-ordinal")}', hit under the pointer: {hit})");
    }

    private static async Task<string?> LiveTextAsync(ILocator chart)
        => (await chart.Locator("[aria-live]").TextContentAsync())?.Trim();

    [TestMethod]
    public async Task IndexTooltip_HoveringACategory_DrawsTheCrosshairAndListsEverySeries()
    {
        var page = await OpenAsync();
        var chart = DefaultChart(page);

        Assert.AreEqual(0, await chart.Locator(".tm-chart__crosshair").CountAsync(),
            "nothing is drawn before the plot is explored");
        Assert.AreEqual(0, await chart.Locator(".tm-chart__tooltip--index").CountAsync());
        Assert.AreEqual(string.Empty, await LiveTextAsync(chart),
            "a chart that has not been explored announces nothing");

        await HoverZoneAsync(page, chart, 2);

        await Assertions.Expect(chart.Locator(".tm-chart__crosshair")).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });

        var card = chart.Locator(".tm-chart__tooltip--index");
        Assert.AreEqual(2, await card.Locator(".tm-chart__tooltip-series").CountAsync(),
            "both series are read at the same category");

        var text = await card.TextContentAsync();
        StringAssert.Contains(text, "Mar");
        StringAssert.Contains(text, "Filings");
        StringAssert.Contains(text, "Rulings");

        var dots = await card.Locator(".tm-chart__tooltip-series .tm-chart__tooltip-dot").CountAsync();
        Assert.AreEqual(2, dots, "each row keeps its series swatch");

        StringAssert.Contains(await LiveTextAsync(chart) ?? string.Empty, "Mar",
            "the live region announces the whole index");
    }

    [TestMethod]
    public async Task IndexTooltip_HoveringAnotherCategory_ReplacesTheCard()
    {
        var page = await OpenAsync();
        var chart = DefaultChart(page);

        await HoverZoneAsync(page, chart, 0);
        await Assertions.Expect(chart.Locator(".tm-chart__tooltip--index")).ToContainTextAsync(
            "Jan", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });

        // The state must survive a quiet moment: a demo that hands the chart a NEW data instance on
        // every access would wipe the crosshair on any parent re-render.
        await page.WaitForTimeoutAsync(900);
        Assert.AreEqual("0", await chart.GetAttributeAsync("data-index-ordinal"),
            "the first category stays active until the pointer moves");

        await HoverZoneAsync(page, chart, 4);
        Assert.AreEqual("4", await chart.GetAttributeAsync("data-index-ordinal"),
            $"the chart must report zone 4 as the active index (live='{await LiveTextAsync(chart)}')");
        await Assertions.Expect(chart.Locator(".tm-chart__tooltip--index")).ToContainTextAsync(
            "May", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });

        Assert.AreEqual(1, await chart.Locator(".tm-chart__tooltip--index").CountAsync(),
            "one card at a time — a new index replaces the old one instead of stacking a second");
    }

    [TestMethod]
    public async Task IndexTooltip_LeavingThePlot_ClearsTheCard()
    {
        var page = await OpenAsync();
        var chart = DefaultChart(page);

        await HoverZoneAsync(page, chart, 3);
        await Assertions.Expect(chart.Locator(".tm-chart__tooltip--index")).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });

        // Move the pointer out of the plot entirely (the legend row under the chart).
        await page.Mouse.MoveAsync(5, 5);

        await Assertions.Expect(chart.Locator(".tm-chart__tooltip--index")).ToHaveCountAsync(
            0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
    }

    [TestMethod]
    public async Task IndexTooltip_KeyboardWalksTheIndices_AndEscapeHidesTheCard()
    {
        var page = await OpenAsync();
        var chart = DefaultChart(page);
        var svg = chart.Locator("svg");

        Assert.AreEqual("0", await svg.GetAttributeAsync("tabindex"),
            "the chart is one tab stop, not one per point");

        await svg.FocusAsync();
        Assert.AreEqual(string.Empty, await LiveTextAsync(chart),
            "focus alone shows nothing — the reader is not talked at on tab-in");

        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(chart.Locator(".tm-chart__tooltip--index")).ToContainTextAsync(
            "Jan", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });
        var firstLive = await LiveTextAsync(chart);
        StringAssert.Contains(firstLive ?? string.Empty, "Jan");

        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(chart.Locator(".tm-chart__tooltip--index")).ToContainTextAsync(
            "Feb", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });
        Assert.AreNotEqual(firstLive, await LiveTextAsync(chart),
            "the live region follows the index");

        await page.Keyboard.PressAsync("End");
        await Assertions.Expect(chart.Locator(".tm-chart__tooltip--index")).ToContainTextAsync(
            "Jun", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });

        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(chart.Locator(".tm-chart__tooltip--index")).ToContainTextAsync(
            "Jan", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(chart.Locator(".tm-chart__tooltip--index")).ToHaveCountAsync(
            0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        Assert.AreEqual(string.Empty, await LiveTextAsync(chart));
        Assert.AreEqual("svg", await page.EvaluateAsync<string>(
            "() => document.activeElement && document.activeElement.tagName.toLowerCase()"),
            "Escape hides the card without throwing the reader out of the chart");

        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(chart.Locator(".tm-chart__tooltip--index")).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
    }

    [TestMethod]
    public async Task IndexTooltip_IsOneLiveRegionOnly()
    {
        var page = await OpenAsync();
        var chart = DefaultChart(page);

        Assert.AreEqual(1, await chart.Locator("[aria-live]").CountAsync(),
            "one region per chart, or the index is announced twice");
        Assert.AreEqual("polite", await chart.Locator("[aria-live]").GetAttributeAsync("aria-live"));
        Assert.AreEqual("status", await chart.Locator("[aria-live]").GetAttributeAsync("role"));

        // The card itself lives inside the role="img" SVG, so it is not a second announcement channel.
        await HoverZoneAsync(page, chart, 1);
        await Assertions.Expect(chart.Locator(".tm-chart__tooltip--index")).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        Assert.AreEqual(0, await chart.Locator(".tm-chart__tooltip[aria-live]").CountAsync());
        Assert.AreEqual(0, await chart.Locator(".tm-chart__tooltip[role='status']").CountAsync());
    }

    [TestMethod]
    public async Task IndexTooltip_CustomTemplatePrintsTheCategoryTotal()
    {
        var page = await OpenAsync();
        var chart = CustomChart(page);

        await HoverZoneAsync(page, chart, 1);

        var total = page.Locator($"{IndexSection} [data-testid='charts-demo-index-total']");
        await Assertions.Expect(total).ToContainTextAsync(
            "650", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });
        StringAssert.Contains(await total.TextContentAsync() ?? string.Empty, "Total");
    }

    [TestMethod]
    public async Task Sparkline_TooltipFollowsHoverAndKeyboard_InsideItsOwnBox()
    {
        var page = await OpenAsync();
        var spark = page.Locator($"{SparkSection} [data-testid='charts-demo-sparkline-tooltip'] .tm-sparkline");

        Assert.AreEqual("0", await spark.Locator("svg").GetAttributeAsync("tabindex"));
        Assert.AreEqual(0, await spark.Locator(".tm-sparkline__tooltip").CountAsync());

        // The demo sparkline carries BOTH the min/max caption and the tooltip: the caption sits at the
        // top-start, the card at the top-end, so the caption steps aside while the card is up and
        // comes back as soon as it closes. This is the narrow-cell overlap the CSS rule prevents.
        await Assertions.Expect(spark.Locator(".tm-sparkline__minmax")).ToBeVisibleAsync();

        await spark.Locator(".tm-sparkline__point").Nth(3).HoverAsync();
        await Assertions.Expect(spark.Locator(".tm-sparkline__tooltip")).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        await Assertions.Expect(spark.Locator(".tm-sparkline__minmax")).ToBeHiddenAsync();

        var bubble = await spark.Locator(".tm-sparkline__tooltip").BoundingBoxAsync();
        var plot = await spark.BoundingBoxAsync();
        Assert.IsNotNull(bubble);
        Assert.IsNotNull(plot);
        Assert.IsTrue(bubble!.Y >= plot!.Y - 1 && bubble.Y + bubble.Height <= plot.Y + plot.Height + 1,
            "the value card stays inside the sparkline box — a bubble above it would be clipped");
        Assert.IsTrue(bubble.X >= plot.X - 1 && bubble.X + bubble.Width <= plot.X + plot.Width + 1);

        await spark.Locator("svg").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(spark.Locator(".tm-sparkline__tooltip")).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });

        StringAssert.Matches(
            (await spark.Locator("[aria-live]").TextContentAsync() ?? string.Empty).Trim(),
            new System.Text.RegularExpressions.Regex(@"^Point \d+ of 10: \d+$"));

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(spark.Locator(".tm-sparkline__tooltip")).ToHaveCountAsync(
            0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
    }

    [TestMethod]
    public async Task Sparkline_MinMaxAndLastPointAreDecorative_AndTheDefaultIsUnchanged()
    {
        var page = await OpenAsync();

        var plain = page.Locator($"{SparkSection} [data-testid='charts-demo-sparkline-plain'] .tm-sparkline");
        Assert.AreEqual(0, await plain.Locator("[tabindex]").CountAsync(),
            "the default sparkline is not a tab stop");
        Assert.AreEqual(0, await plain.Locator("[aria-live]").CountAsync());
        Assert.AreEqual(0, await plain.Locator(".tm-sparkline__last-point").CountAsync());

        var minMax = page.Locator($"{SparkSection} [data-testid='charts-demo-sparkline-minmax'] .tm-sparkline");
        Assert.AreEqual("true", await minMax.Locator(".tm-sparkline__minmax").GetAttributeAsync("aria-hidden"),
            "the numbers already live in the aria-label summary");

        var label = await minMax.Locator("svg").GetAttributeAsync("aria-label");
        StringAssert.Contains(label ?? string.Empty, "minimum");
        StringAssert.Contains(label ?? string.Empty, "maximum");

        var ring = page.Locator($"{SparkSection} [data-testid='charts-demo-sparkline-last-point'] .tm-sparkline");
        Assert.AreEqual(1, await ring.Locator("circle.tm-sparkline__last-point").CountAsync());
        Assert.AreEqual("true", await ring.Locator("circle.tm-sparkline__last-point").GetAttributeAsync("aria-hidden"));
        Assert.AreEqual("var(--tm-color-success)", await ring.Locator("path.tm-sparkline__area").GetAttributeAsync("fill"),
            "the demo passes a token through Color");
    }

    [TestMethod]
    public async Task IndexTooltip_ZoneIsTappableOnACoarsePointer()
    {
        var page = await OpenAsync(390, 844, touch: true);
        var chart = DefaultChart(page);
        await chart.Locator(".tm-chart__index-zone").Nth(1).ScrollIntoViewIfNeededAsync();

        await chart.Locator(".tm-chart__index-zone").Nth(1).TapAsync();

        await Assertions.Expect(chart.Locator(".tm-chart__tooltip--index")).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        StringAssert.Contains(await chart.Locator(".tm-chart__tooltip--index").TextContentAsync() ?? string.Empty, "Feb");
    }

    [TestMethod]
    public async Task Axe_Scoped_To_TheNewSections_FindsNoCriticalOrSeriousViolation()
    {
        var page = await OpenAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });

        await AssertNoAxeViolationsAsync(page, IndexSection, "index tooltip");

        // The card and the crosshair are transient; scan them too, they are what the mode adds.
        await HoverZoneAsync(page, DefaultChart(page), 2);
        await Assertions.Expect(DefaultChart(page).Locator(".tm-chart__tooltip--index")).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        await AssertNoAxeViolationsAsync(page, IndexSection, "index tooltip with the card open");

        await page.Mouse.MoveAsync(5, 5);
        await AssertNoAxeViolationsAsync(page, SparkSection, "sparkline extras");
    }

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
    public async Task Visual_Reference_LightDark_At_1440_And_375()
    {
        var desktop = await OpenAsync(1440);
        var indexSection = desktop.Locator(IndexSection);

        await CenterAsync(desktop, IndexSection);
        await WaitForLayoutToSettleAsync(desktop, IndexSection);
        await ShootAsync(desktop, "charts-index-tooltip-light-1440");

        // The card is part of the reference: hover the third month and shoot again.
        await HoverZoneAsync(desktop, DefaultChart(desktop), 2);
        await Assertions.Expect(DefaultChart(desktop).Locator(".tm-chart__tooltip--index")).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        await ShootAsync(desktop, "charts-index-tooltip-hover-light-1440", keepPointer: true);

        await CenterAsync(desktop, SparkSection);
        await WaitForLayoutToSettleAsync(desktop, SparkSection);
        await ShootAsync(desktop, "charts-sparkline-extras-light-1440");

        await ToggleDarkModeAsync(desktop);
        await CenterAsync(desktop, IndexSection);
        await HoverZoneAsync(desktop, DefaultChart(desktop), 4);
        await Assertions.Expect(DefaultChart(desktop).Locator(".tm-chart__tooltip--index")).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        await ShootAsync(desktop, "charts-index-tooltip-hover-dark-1440", keepPointer: true);

        await CenterAsync(desktop, SparkSection);
        await ShootAsync(desktop, "charts-sparkline-extras-dark-1440");

        var phone = await OpenAsync(390, 844, touch: true);
        await CenterAsync(phone, IndexSection);
        await WaitForLayoutToSettleAsync(phone, IndexSection);
        await ShootAsync(phone, "charts-index-tooltip-light-390");

        await CenterAsync(phone, SparkSection);
        await ShootAsync(phone, "charts-sparkline-extras-light-390");
    }

    /// <summary>Centers a section in the viewport so each reference shows its own subject.</summary>
    private static Task CenterAsync(IPage page, string selector)
        => page.Locator(selector).EvaluateAsync("el => el.scrollIntoView({ block: 'center' })");

    private async Task ShootAsync(IPage page, string name, bool keepPointer = false)
    {
        await page.WaitForTimeoutAsync(300);
        if (!keepPointer)
        {
            // Park the pointer and drop focus, so a reference shot is not a hover state by accident.
            await page.Mouse.MoveAsync(2, 2);
            await page.EvaluateAsync(
                "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
            await page.WaitForTimeoutAsync(200);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, $"{name}.png"),
            FullPage = false,
        });
    }
}

/// <summary>The WASM lane (the demo default).</summary>
[TestClass]
public class TmChartIndexTooltipE2ETests : TmChartIndexTooltipE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}
