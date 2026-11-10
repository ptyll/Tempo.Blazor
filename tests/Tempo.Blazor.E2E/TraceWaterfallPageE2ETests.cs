using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan ff043b2e phase 9 — the dedicated <c>/trace-waterfall</c> page in a real browser. The assertions read
/// behaviour, not markup: the generated run renders as one treegrid of roughly 150 spans, a disclosure
/// collapses and expands a subtree (by pointer and by keyboard), the roving tabindex walks the rows and
/// Enter selects one (the detail panel follows), the zoom group narrows the shared axis and resets it, the
/// live toggle republishes the run with running spans and stops again, a 375 px viewport keeps the grid's
/// own horizontal scroll INSIDE the component, and a scoped axe scan finds no critical or serious
/// violation. Reference screenshots (light and dark, 1440 and 375 px) land in
/// <c>TestResults/trace-waterfall-page</c>.
/// </summary>
public abstract class TraceWaterfallPageE2EBase : PlaywrightTestBase
{
    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "trace-waterfall-page");

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
        await GotoAppReadyAsync(page, $"{BaseUrl}/trace-waterfall");
        await page.WaitForSelectorAsync(
            "[data-testid='trace-page-waterfall'] [role='treegrid']",
            new PageWaitForSelectorOptions { Timeout = 60000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private static ILocator Grid(IPage page)
        => page.Locator("[data-testid='trace-page-waterfall'] [role='treegrid']");

    /// <summary>
    /// One treegrid ROW. The <c>[role='row']</c> qualifier matters: the page's detail panel also carries
    /// <c>data-span-id</c> (that is how a test reads which span it shows), so a bare id selector would
    /// resolve to two elements once the span is selected.
    /// </summary>
    private static ILocator Row(IPage page, string spanId)
        => page.Locator($"[data-testid='trace-page-waterfall'] [role='row'][data-span-id='{spanId}']");

    private static ILocator Detail(IPage page) => page.GetByTestId("trace-page-detail");

    [TestMethod]
    public async Task GeneratedRun_RendersAsATreegridOfRoughlyOneHundredFiftySpans()
    {
        var page = await OpenAsync();

        var rows = await Grid(page).Locator("[role='row'][data-span-id]").CountAsync();
        Assert.IsTrue(rows is >= 140 and <= 200,
            $"the generated run renders about 150 spans in the DOM (was {rows}), under the 200-row virtualization threshold");

        Assert.AreEqual(1, await page.Locator("[data-testid='trace-page-waterfall'] [role='treegrid']").CountAsync());
        StringAssert.Contains((await page.GetByTestId("trace-page-summary").TextContentAsync())!, rows.ToString(),
            "the summary line reports the same span count");

        // The simulated timeout is painted as an error (an icon AND a word, never colour alone).
        Assert.AreEqual(1, await Row(page, "round-04-db").Locator(".tm-trace-waterfall__status--error").CountAsync());
        Assert.AreEqual(1, await Row(page, "notify").Locator(".tm-trace-waterfall__status--error").CountAsync());
    }

    [TestMethod]
    public async Task Disclosure_CollapsesAndExpandsASubtree()
    {
        var page = await OpenAsync();
        var before = await Grid(page).Locator("[role='row'][data-span-id]").CountAsync();

        await Row(page, "sub-agent").Locator(".tm-trace-waterfall__toggle").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        var collapsed = await Grid(page).Locator("[role='row'][data-span-id]").CountAsync();
        Assert.IsTrue(collapsed < before, $"collapsing the sub-agent hides its children ({collapsed} of {before})");
        Assert.AreEqual("false", await Row(page, "sub-agent").GetAttributeAsync("aria-expanded"));

        await Row(page, "sub-agent").Locator(".tm-trace-waterfall__toggle").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        Assert.AreEqual(before, await Grid(page).Locator("[role='row'][data-span-id]").CountAsync(),
            "expanding it brings the subtree back");
        Assert.AreEqual("true", await Row(page, "sub-agent").GetAttributeAsync("aria-expanded"));
    }

    [TestMethod]
    public async Task Keyboard_WalksTheRows_AndEnterSelectsOne()
    {
        var page = await OpenAsync();

        // The roving tabindex keeps exactly one row in the tab order.
        var tabbable = await Grid(page).Locator("[role='row'][tabindex='0']").CountAsync();
        Assert.AreEqual(1, tabbable, "exactly one row is in the tab order");

        await Grid(page).Locator("[role='row'][tabindex='0']").FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(300);

        var activeId = await page.EvaluateAsync<string>(
            "() => (document.activeElement && document.activeElement.getAttribute('data-span-id')) || ''");
        Assert.IsFalse(string.IsNullOrEmpty(activeId), "ArrowDown moved the focus onto another row");

        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(500);
        Assert.AreEqual(activeId, await Detail(page).GetAttributeAsync("data-span-id"),
            "Enter selects the row and the detail panel follows it");
        Assert.AreEqual("true", await Row(page, activeId).GetAttributeAsync("aria-selected"));

        // Collapsing and expanding from the keyboard keeps the row focused.
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual(activeId, await page.EvaluateAsync<string>(
            "() => (document.activeElement && document.activeElement.getAttribute('data-span-id')) || ''"));
    }

    [TestMethod]
    public async Task Zoom_NarrowsTheAxis_AndResetRestoresIt()
    {
        var page = await OpenAsync();
        var window = page.Locator("[data-testid='trace-page-waterfall'] .tm-trace-waterfall__window");
        var before = (await window.TextContentAsync())!.Trim();

        await page.GetByTestId("trace-zoom-in").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        var zoomed = (await window.TextContentAsync())!.Trim();
        Assert.AreNotEqual(before, zoomed, "zooming in narrows the reported window");
        Assert.IsFalse(await page.GetByTestId("trace-zoom-reset").IsDisabledAsync(),
            "the reset action is available once the axis is zoomed");

        await page.GetByTestId("trace-zoom-reset").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        Assert.AreEqual(before, (await window.TextContentAsync())!.Trim(), "reset restores the whole trace");
    }

    [TestMethod]
    public async Task LiveToggle_RepublishesTheRunWithRunningSpans()
    {
        var page = await OpenAsync();

        Assert.AreEqual("false", await page.GetByTestId("trace-page-live-state").GetAttributeAsync("data-live"));

        await page.GetByTestId("trace-page-live").ClickAsync();
        await page.WaitForTimeoutAsync(800);

        Assert.AreEqual("true", await page.GetByTestId("trace-page-live-state").GetAttributeAsync("data-live"));
        await page.WaitForSelectorAsync(
            "[data-testid='trace-page-waterfall'] [role='row'][data-span-id='answer'] .tm-trace-waterfall__status--running",
            new PageWaitForSelectorOptions { Timeout = 10000 });
        Assert.AreEqual("Stop live trace", (await page.GetByTestId("trace-page-live").TextContentAsync())!.Trim());

        await page.GetByTestId("trace-page-live").ClickAsync();
        await page.WaitForTimeoutAsync(600);
        Assert.AreEqual("false", await page.GetByTestId("trace-page-live-state").GetAttributeAsync("data-live"));
        Assert.AreEqual(0,
            await Row(page, "answer").Locator(".tm-trace-waterfall__status--running").CountAsync(),
            "stopping the run puts the finished trace back");
    }

    [TestMethod]
    public async Task PhoneBand_KeepsTheHorizontalScrollInsideTheGrid()
    {
        var page = await OpenAsync(375, 812, touch: true);

        var pageWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        Assert.IsTrue(pageWidth <= 376, $"the page itself does not scroll sideways (scrollWidth {pageWidth})");

        var overflow = await Grid(page).EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 1");
        Assert.IsTrue(overflow, "the treegrid keeps the full template and scrolls inside its own scrollport");

        var box = await page.GetByTestId("trace-page-waterfall").BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box!.Width <= 375, $"the waterfall stays inside the phone (width {box.Width})");
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
            """,
            "[data-testid='trace-page-run']");

        Assert.AreEqual(0, violations.Length, "axe: " + string.Join(" | ", violations));
    }

    /// <summary>
    /// Plan 7ba95ed6 phase 8 — a FULL-PAGE capture of the long trace has no blank tail.
    /// <para>
    /// With 152 rows inside the waterfall's 420 px scrollport, Chromium used to report the CLIPPED
    /// rowgroup in the root scroller as well: <c>documentElement.scrollHeight</c> came back at 5024 px
    /// against a 1016 px body, so a Playwright <c>FullPage</c> shot captured ~4000 px of empty page
    /// (and <c>window.scrollTo(0, 4000)</c> stayed at 0, i.e. no reader could ever reach it). The
    /// containment rule on the scroll owner (<c>contain: layout paint</c> in
    /// <c>_trace-waterfall.css</c>) makes the clipped content stop contributing to the viewport's
    /// scrollable overflow.
    /// </para>
    /// <para>
    /// The assertion is measured, not eyeballed: the two scroll heights must agree within a few pixels,
    /// and the committed PNG's own pixel height must match that document height — a screenshot taller
    /// than the document is exactly the blank tail this guards.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task FullPageScreenshot_HasNoBlankTail()
    {
        var page = await OpenAsync(1440, 1000);
        await ScrollToWaterfallAsync(page);

        var heights = await page.EvaluateAsync<int[]>(
            """
            () => {
                const grid = document.querySelector("[data-testid='trace-page-waterfall'] [role='treegrid']");
                return [
                    document.documentElement.scrollHeight,
                    document.body.scrollHeight,
                    grid ? grid.scrollHeight : 0,
                    grid ? grid.clientHeight : 0
                ];
            }
            """);

        var documentHeight = heights[0];
        var bodyHeight = heights[1];
        var gridScrollHeight = heights[2];
        var gridClientHeight = heights[3];

        Assert.IsTrue(gridScrollHeight > gridClientHeight + 200,
            $"the grid really does clip a tall rowgroup (scrollHeight {gridScrollHeight} vs clientHeight {gridClientHeight}), " +
            "otherwise this test proves nothing about containment");

        Assert.IsTrue(documentHeight <= bodyHeight + 16,
            $"the clipped rowgroup must not inflate the root scroller (documentElement {documentHeight} px vs body {bodyHeight} px)");

        var path = Path.Combine(ShotDir, "trace-waterfall-page-fullpage-1440.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
        Assert.IsTrue(File.Exists(path), "the full-page capture lands on disk");

        var (width, height) = ReadPngSize(path);
        Assert.IsTrue(height <= bodyHeight + 16,
            $"the capture is no taller than the document ({height} px vs {bodyHeight} px) — a taller PNG is the blank tail");
        Assert.IsTrue(height >= 600, $"the capture is a real page, not a stub ({width}x{height})");
    }

    /// <summary>Reads the pixel size out of a PNG's IHDR chunk (bytes 16–23), so the check needs no image library.</summary>
    private static (int Width, int Height) ReadPngSize(string path)
    {
        var header = new byte[24];
        using var stream = File.OpenRead(path);
        var read = stream.Read(header, 0, header.Length);
        Assert.AreEqual(24, read, "a PNG starts with its 8-byte signature and the 25-byte IHDR length/type/width/height");
        return (ReadBigEndian(header, 16), ReadBigEndian(header, 20));
    }

    private static int ReadBigEndian(byte[] buffer, int offset)
        => (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];

    [TestMethod]
    public async Task VisualSweep_LightAndDark_DesktopAndPhone()
    {
        var desktop = await OpenAsync(1440, 1000);
        await ScrollToWaterfallAsync(desktop);
        await ShootAsync(desktop, "trace-waterfall-page-light-1440");

        await ToggleThemeAsync(desktop);
        await ScrollToWaterfallAsync(desktop);
        await ShootAsync(desktop, "trace-waterfall-page-dark-1440");

        var phone = await OpenAsync(375, 812, touch: true);
        await ScrollToWaterfallAsync(phone);
        await ShootAsync(phone, "trace-waterfall-page-light-375");

        await ToggleThemeAsync(phone);
        await ScrollToWaterfallAsync(phone);
        await ShootAsync(phone, "trace-waterfall-page-dark-375");
    }

    private static async Task ScrollToWaterfallAsync(IPage page)
    {
        await page.GetByTestId("trace-page-waterfall").ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>Switches the demo's own theme (the header toggle), so the whole frame repaints, not just a section.</summary>
    private static async Task ToggleThemeAsync(IPage page)
    {
        // The demo renders TWO toggles (the phone header and the sidebar footer); only one is visible per band.
        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        await page.WaitForTimeoutAsync(600);
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
public class TraceWaterfallPageE2ETests : TraceWaterfallPageE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}
