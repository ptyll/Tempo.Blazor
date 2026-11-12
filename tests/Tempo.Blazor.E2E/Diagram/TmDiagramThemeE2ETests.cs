using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 92e96b25, phase 4 (TG-004) — the diagram canvas is theme-relative on the real
/// <c>/modeling-editor?scenario=bpmn2-generated</c> demo: the page and every paper-painted BPMN
/// shape carry the <c>--tm-canvas-*</c> / <c>--stencil-*</c> tokens, so light keeps the historical
/// white sheet while dark mode flips the whole canvas to the <c>DiagramSvgPalette.Dark</c> steps —
/// a BPMN diagram in dark mode shows NO white area. Locator screenshots of the canvas in
/// light/dark (plus the phone and reduced-motion edge cases) go to gitignored TestResults for the
/// UX review.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmDiagramThemeE2ETests : WasmTestBase
{
    private const string Route = "/modeling-editor?scenario=bpmn2-generated";
    private const string Canvas = ".tm-diagram-canvas";
    private const string PageRect = "rect.tm-diagram-page";

    private static string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "diagram-themes");

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

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1000, bool reducedMotion = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        await page.GotoAsync($"{BaseUrl}{Route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator($"{Canvas} {PageRect}").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await page.Locator($"{Canvas} g.tm-diagram-node").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(500);
        return page;
    }

    private static async Task ToggleDarkAsync(IPage page)
    {
        await page.Locator("[data-testid='theme-toggle']").First.DispatchEventAsync("click");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-theme=\"dark\"]') !== null", null, new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(400);
    }

    private async Task ShotCanvasAsync(IPage page, string name)
    {
        Assert.IsTrue(await page.EvaluateAsync<bool>(
            "() => !document.querySelector('#blazor-error-ui') || document.querySelector('#blazor-error-ui').offsetParent === null"),
            "the Blazor error overlay must stay hidden");
        var locator = page.Locator(Canvas).First;
        await locator.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(150);
        var path = Path.Combine(ShotDir, $"bpmn-canvas-{name}.png");
        await locator.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
        Assert.IsTrue(new FileInfo(path).Length > 5 * 1024, $"screenshot bpmn-canvas-{name} looks empty");
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

    private static string ParseHex(string rgb)
    {
        var match = Regex.Match(rgb, @"rgba?\((\d+),\s*(\d+),\s*(\d+)");
        Assert.IsTrue(match.Success, $"cannot parse '{rgb}'");
        return "#" + string.Concat(match.Groups.Cast<Group>().Skip(1)
            .Select(g => int.Parse(g.Value, CultureInfo.InvariantCulture).ToString("x2", CultureInfo.InvariantCulture)));
    }

    [TestMethod]
    public async Task BpmnCanvas_DarkMode_FlipsTheSheet_AndShowsNoWhiteArea()
    {
        var page = await OpenAsync();

        // Light: the historical white sheet with dark ink (byte-identical to the pre-TG-004 look).
        var lightPage = await page.Locator(PageRect).First.EvaluateAsync<string>("e => getComputedStyle(e).fill");
        Assert.AreEqual("#ffffff", ParseHex(lightPage), "light keeps the white paper sheet");
        var lightStroke = await page.Locator($"{Canvas} g.tm-diagram-node path.tm-diagram-node__shape-bg, {Canvas} g.tm-diagram-node .tm-diagram-node__svg-bg *")
            .First.EvaluateAsync<string>("e => getComputedStyle(e).stroke");
        Assert.AreEqual("#1f2937", ParseHex(lightStroke), "light keeps the paper-ink outline");
        await ShotCanvasAsync(page, "light");

        await ToggleDarkAsync(page);

        // Dark: the whole canvas flips to the DiagramSvgPalette.Dark steps — no white area.
        var darkPage = await page.Locator(PageRect).First.EvaluateAsync<string>("e => getComputedStyle(e).fill");
        Assert.AreEqual("#27272a", ParseHex(darkPage),
            "the dark page is the DiagramSvgPalette.Dark DefaultNodeFill — the white sheet is gone");

        var darkStroke = await page.Locator($"{Canvas} g.tm-diagram-node path.tm-diagram-node__shape-bg, {Canvas} g.tm-diagram-node .tm-diagram-node__svg-bg *")
            .First.EvaluateAsync<string>("e => getComputedStyle(e).stroke");
        Assert.AreEqual("#e5e7eb", ParseHex(darkStroke),
            "the dark outline lifts to the DiagramSvgPalette.Dark DefaultNodeStroke");
        Assert.IsTrue(Contrast(darkStroke, darkPage) >= 3,
            $"the dark outline ({darkStroke}) must hold 3:1 on the dark page ({darkPage})");

        // No white paint survives anywhere on the canvas: every painted shape/line resolves a
        // token whose dark value is a dark step.
        var whiteOffenders = await page.Locator(Canvas).EvaluateAsync<string[]>(
            @"root => {
                const out = [];
                root.querySelectorAll('*').forEach(el => {
                    const fill = getComputedStyle(el).fill;
                    const stroke = getComputedStyle(el).stroke;
                    if (fill && /^(white|#fff(fff)?)$/i.test(fill.trim())) out.push(el.className + ' fill=' + fill);
                    if (stroke && /^(white|#fff(fff)?)$/i.test(stroke.trim())) out.push(el.className + ' stroke=' + stroke);
                });
                return out;
            }");
        Assert.AreEqual(0, whiteOffenders.Length,
            "no element on the dark canvas may paint white: " + string.Join(" | ", whiteOffenders));

        await ShotCanvasAsync(page, "dark");
    }

    [TestMethod]
    public async Task BpmnCanvas_DarkMode_PhoneEdgeCase_ShowsNoWhiteSheet()
    {
        var page = await OpenAsync(390, 844);
        await ToggleDarkAsync(page);

        var darkPage = await page.Locator(PageRect).First.EvaluateAsync<string>("e => getComputedStyle(e).fill");
        Assert.AreEqual("#27272a", ParseHex(darkPage), "the phone dark canvas flips too");

        var metrics = await page.EvaluateAsync<double[]>(
            "() => [document.documentElement.scrollWidth, window.innerWidth]");
        Assert.IsTrue(metrics[0] <= metrics[1] + 1,
            $"the document overflows horizontally: {metrics[0]} > {metrics[1]}");

        await ShotCanvasAsync(page, "dark-phone");
    }

    [TestMethod]
    public async Task BpmnCanvas_DarkMode_ReducedMotionEdgeCase_ShowsNoWhiteSheet()
    {
        var page = await OpenAsync(1440, 1000, reducedMotion: true);
        await ToggleDarkAsync(page);

        var darkPage = await page.Locator(PageRect).First.EvaluateAsync<string>("e => getComputedStyle(e).fill");
        Assert.AreEqual("#27272a", ParseHex(darkPage), "the reduced-motion dark canvas flips too");

        await ShotCanvasAsync(page, "dark-reduced-motion");
    }
}

/// <summary>The dark-canvas contract on the Blazor Server demo (:7107) — computed styles only.</summary>
[TestClass]
[TestCategory("Server")]
public sealed class TmDiagramThemeServerE2ETests : ServerTestBase
{
    [TestMethod]
    public async Task BpmnCanvas_DarkMode_Flips_On_The_Server_Host()
        => await TmDiagramThemeSmoke.RunAsync(BaseUrl, () => CreateContextAsync(), (page, url) => GotoAppReadyAsync(page, url));
}

/// <summary>The dark-canvas contract on the InteractiveAuto demo (:7108).</summary>
[TestClass]
[TestCategory("InteractiveAuto")]
public sealed class TmDiagramThemeInteractiveAutoE2ETests : InteractiveAutoTestBase
{
    [TestMethod]
    public async Task BpmnCanvas_DarkMode_Flips_On_The_InteractiveAuto_Host()
        => await TmDiagramThemeSmoke.RunAsync(BaseUrl, () => CreateContextAsync(), (page, url) => GotoAppReadyAsync(page, url));
}

/// <summary>The host-independent smoke assertions both non-WASM lanes run.</summary>
internal static class TmDiagramThemeSmoke
{
    public static async Task RunAsync(
        string baseUrl,
        Func<Task<IBrowserContext>> createContext,
        Func<IPage, string, Task> gotoReady)
    {
        var context = await createContext();
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);

        await gotoReady(page, $"{baseUrl}/modeling-editor?scenario=bpmn2-generated");
        var pageRect = page.Locator("rect.tm-diagram-page").First;
        await pageRect.WaitForAsync(new LocatorWaitForOptions { Timeout = 90000 });
        await page.WaitForTimeoutAsync(400);

        var lightFill = await pageRect.EvaluateAsync<string>("e => getComputedStyle(e).fill");
        StringAssert.StartsWith(lightFill, "rgb(255, 255, 255)",
            "light keeps the white paper sheet on this host too");

        await page.Locator("[data-testid='theme-toggle']").First.DispatchEventAsync("click");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-theme=\"dark\"]') !== null", null, new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(400);

        var darkFill = await pageRect.EvaluateAsync<string>("e => getComputedStyle(e).fill");
        Assert.AreEqual("rgb(39, 39, 42)", darkFill,
            "the dark canvas flips to the DiagramSvgPalette.Dark fill on this host too");

        Assert.AreEqual(0, errors.Count, "The page raised: " + string.Join(" | ", errors));
    }
}
