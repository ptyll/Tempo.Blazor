using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 92e96b25, phase 2 (TG-002) — the chart series palette on the real <c>/charts</c> demo: the
/// default donut resolves the <c>--tm-chart-series-*</c> tokens, dark mode LIFTS the series to tones
/// that hold ≥ 3:1 on the dark surface, and the indigo colour theme retints series 1 to the indigo
/// primary. Each run screenshots the donut, funnel, sparkline, gauge and heatmap SECTIONS (scrolled
/// into view, element-cropped) in light/dark/indigo for the UX review. Diagnostic screenshots go to
/// gitignored TestResults, never a committed baseline.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmChartThemeE2ETests : WasmTestBase
{
    private const string Route = "/charts";
    private const string Donut = "[data-testid='chart-donut-default']";
    private const string Funnel = "[data-testid='charts-demo-funnel']";
    private const string SparklineExtras = "[data-testid='charts-demo-sparkline-extras']";

    /// <summary>
    /// The chart sections a UX review needs on disk: the donut under test, the funnel, the
    /// sparkline extras, the gauges (no test id — the section is named by its "Gauges" heading)
    /// and the heatmap. Each shot scrolls the section into view first so the PNG shows the chart.
    /// </summary>
    private static readonly (string Name, string Selector)[] Sections =
    [
        ("donut", "[data-testid='chart-donut-default']"),
        ("funnel", "[data-testid='charts-demo-funnel']"),
        ("sparkline", "[data-testid='charts-demo-sparkline-extras']"),
        ("gauge", "section.demo-section:has(h2:text-is('Gauges'))"),
        ("heatmap", "[data-testid='charts-demo-heatmap']"),
    ];

    private static string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "chart-themes");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent!;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1100)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        await page.GotoAsync($"{BaseUrl}{Route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator($"{Donut} svg").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private static async Task<string[]> ComputedFillsAsync(IPage page, string selector)
        => await page.Locator(selector).EvaluateAllAsync<string[]>(
            "els => els.map(e => getComputedStyle(e).fill)");

    private static async Task ToggleDarkAsync(IPage page)
    {
        await page.Locator("[data-testid='theme-toggle']").First.DispatchEventAsync("click");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-theme=\"dark\"]') !== null", null, new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(400);
    }

    private static async Task SetColorThemeAsync(IPage page, string theme)
    {
        var label = char.ToUpperInvariant(theme[0]) + theme[1..];
        var button = page.Locator("button", new() { HasTextString = label }).Locator("visible=true").First;
        if (await button.CountAsync() > 0) await button.ClickAsync();
        else await page.Locator("button", new() { HasTextString = label }).First.DispatchEventAsync("click");
        await page.WaitForFunctionAsync(
            "t => document.documentElement.getAttribute('data-tm-theme') === t", theme, new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(300);
    }

    private async Task ShotSectionsAsync(IPage page, string mode)
    {
        Assert.IsTrue(await page.EvaluateAsync<bool>(
            "() => !document.querySelector('#blazor-error-ui') || document.querySelector('#blazor-error-ui').offsetParent === null"),
            "the Blazor error overlay must stay hidden");

        foreach (var (name, selector) in Sections)
        {
            var locator = page.Locator(selector).First;
            await locator.ScrollIntoViewIfNeededAsync();
            await page.WaitForTimeoutAsync(150);
            var path = Path.Combine(ShotDir, $"chart-section-{name}-{mode}.png");
            await locator.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
            Assert.IsTrue(new FileInfo(path).Length > 5 * 1024,
                $"screenshot chart-section-{name}-{mode} looks empty");
        }
    }

    /// <summary>WCAG contrast between two <c>rgb(r, g, b)</c> computed colours.</summary>
    private static double Contrast(string foreground, string background)
    {
        double Luminance(string rgb)
        {
            var channels = Regex.Match(rgb, @"rgb\((\d+),\s*(\d+),\s*(\d+)\)")
                .Groups.Cast<Group>().Skip(1)
                .Select(g => int.Parse(g.Value, CultureInfo.InvariantCulture) / 255.0)
                .Select(c => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4))
                .ToArray();
            Assert.AreEqual(3, channels.Length, $"cannot parse '{rgb}'");
            return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
        }

        var a = Luminance(foreground);
        var b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    [TestMethod]
    public async Task SeriesPalette_DarkMode_LiftsSeriesColours_ForContrast()
    {
        var page = await OpenAsync();

        // Light: pixel-identical to the legacy palette (backward compatibility through the tokens).
        var lightFills = await ComputedFillsAsync(page, $"{Donut} path.tm-chart__slice");
        CollectionAssert.AreEqual(
            new[] { "#3b82f6", "#ef4444", "#10b981", "#f59e0b", "#8b5cf6" },
            lightFills.Select(ParseHex).ToArray(),
            $"light computed fills: {string.Join(", ", lightFills)}");
        await ShotSectionsAsync(page, "light");

        await ToggleDarkAsync(page);

        // Dark: the fills LIFT (they are the 400-tones now)…
        var darkFills = await ComputedFillsAsync(page, $"{Donut} path.tm-chart__slice");
        CollectionAssert.AreNotEqual(lightFills, darkFills, "dark mode must lift the series palette");

        // …and every series holds ≥ 3:1 (WCAG 1.4.11 for graphics) against the page background.
        var background = await page.Locator(".demo-shell").EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor");
        for (var i = 0; i < darkFills.Length; i++)
        {
            Assert.IsTrue(Contrast(darkFills[i], background) >= 3,
                $"dark series {i + 1} ({darkFills[i]}) must hold 3:1 on {background}");
        }

        // Edge case: the funnel's paired series ink stays readable in dark mode too. The demo funnels
        // set their own stage colours (host colours), so no --series-n label exists on the page — probe
        // the real shipped rule with a temporary element carrying the series-1 label class, and the
        // resolved series-1 fill through a probe rect.
        var probe = await page.EvaluateAsync<string>(
            @"() => {
                const svg = document.querySelector(""[data-testid='charts-demo-funnel'] svg"");
                const inkEl = document.createElementNS('http://www.w3.org/2000/svg', 'text');
                inkEl.setAttribute('class', 'tm-chart__funnel-label tm-chart__funnel-label--series-1');
                svg.appendChild(inkEl);
                const ink = getComputedStyle(inkEl).fill;
                inkEl.remove();
                const fillEl = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
                fillEl.style.fill = 'var(--tm-chart-series-1)';
                svg.appendChild(fillEl);
                const fill = getComputedStyle(fillEl).fill;
                fillEl.remove();
                return ink + '|' + fill;
            }");
        var parts = probe.Split('|');
        Assert.IsTrue(Contrast(parts[0], parts[1]) >= 4.5,
            $"the paired series-1 ink ({parts[0]}) must clear WCAG AA on the series-1 fill ({parts[1]}) in dark mode");

        await ShotSectionsAsync(page, "dark");
    }

    [TestMethod]
    public async Task Series1_FollowsTheIndigoColourTheme_InLightAndDark()
    {
        var page = await OpenAsync();

        await SetColorThemeAsync(page, "indigo");
        var indigoFills = await ComputedFillsAsync(page, $"{Donut} path.tm-chart__slice");
        Assert.AreEqual("#6366f1", ParseHex(indigoFills[0]),
            "series 1 follows the indigo primary-500 in the indigo theme");
        Assert.AreEqual("#ef4444", ParseHex(indigoFills[1]),
            "the hue-fixed accents stay untouched by the indigo theme");
        await ShotSectionsAsync(page, "indigo-light");

        await ToggleDarkAsync(page);
        var indigoDarkFills = await ComputedFillsAsync(page, $"{Donut} path.tm-chart__slice");
        Assert.AreEqual("#818cf8", ParseHex(indigoDarkFills[0]),
            "series 1 follows the indigo primary-400 in indigo dark");
        await ShotSectionsAsync(page, "indigo-dark");
    }

    [TestMethod]
    public async Task Sparkline_ComputedStroke_ResolvesTheSeriesToken()
    {
        var page = await OpenAsync();

        var lightStroke = await page.Locator($"{SparklineExtras} polyline.tm-sparkline__line").First
            .EvaluateAsync<string>("e => getComputedStyle(e).stroke");
        Assert.AreEqual("#3b82f6", ParseHex(lightStroke),
            "the light sparkline stroke is the legacy series blue through the token");

        await ToggleDarkAsync(page);
        var darkStroke = await page.Locator($"{SparklineExtras} polyline.tm-sparkline__line").First
            .EvaluateAsync<string>("e => getComputedStyle(e).stroke");
        Assert.AreEqual("#60a5fa", ParseHex(darkStroke),
            "the dark sparkline stroke lifts to the 400-tone");
    }

    private static string ParseHex(string rgb)
    {
        var match = Regex.Match(rgb, @"rgba?\((\d+),\s*(\d+),\s*(\d+)");
        Assert.IsTrue(match.Success, $"cannot parse '{rgb}'");
        return "#" + string.Concat(match.Groups.Cast<Group>().Skip(1)
            .Select(g => int.Parse(g.Value, CultureInfo.InvariantCulture).ToString("x2", CultureInfo.InvariantCulture)));
    }
}
