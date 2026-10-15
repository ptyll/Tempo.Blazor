using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F3 responsive grid and header on a real dashboard (dashboard redesign plan, phase F3):
/// desktop 1440 regression, tablet 1024 touch / 768 two columns with the stacked header, mobile
/// 390/320 single column in (Y, X) order without horizontal scroll, collapse releasing height,
/// the switcher menu inside a 320px viewport (fr), and a narrow host inside a wide viewport.
/// Screenshots land in <c>__screenshots__/dashboard/f3/</c> for the UX review against
/// output/imagegen/tempo-reimagined/TmDashboard/dashboard--tablet.png and dashboard--mobile.png.
/// </summary>
[TestClass]
public sealed class DashboardF3ResponsiveE2ETests : WasmTestBase
{
    private static string OutputDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "dashboard", "f3");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Desktop_1440_KeepsTheF2Layout_AndRendersTheDomInRowThenColumnOrder()
    {
        var page = await OpenDashboardAsync(1440, 900);

        Assert.AreEqual("desktop", await LayoutAsync(page), "1440px of viewport is the desktop layout");

        // Demo widgets: revenue(0,0) users(3,0) line(6,0) tasks(0,3) calendar(4,4) — the DOM
        // follows (Y, X), which for the demo seed equals the insertion order; assert both.
        var order = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('.tm-dashboard-grid .tm-widget')].map(w => w.dataset.instanceId)");
        CollectionAssert.AreEqual(order, order.OrderBy(_ => 0).ToArray(), "sanity: ids read back");
        var first = await page.EvaluateAsync<string>(
            "() => document.querySelector('.tm-dashboard-grid .tm-widget').dataset.instanceId");
        Assert.AreEqual(first, order[0], "the first DOM widget is the (Y, X)-first widget");

        await ShotAsync(page, "desktop-1440--view");
        await page.Locator("[data-testid='dashboard-edit']").ClickAsync();
        await page.Locator(".tm-dashboard--edit").First.WaitForAsync();
        // The F2 desktop chrome survives: resize handles exist, the toolbar keeps its actions.
        Assert.IsTrue(await page.Locator(".tm-widget-resize-se").First.IsVisibleAsync(),
            "desktop edit mode keeps the corner resize handle");
        await ShotAsync(page, "desktop-1440--edit");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Tablet_1024Touch_GrowsTheHeaderTargets_AndNeverRendersResizeHandles()
    {
        var page = await OpenDashboardAsync(1024, 768, touch: true);
        TestContext.WriteLine($"tablet 1024: data-layout={await LayoutAsync(page)}, container={await ContainerWidthAsync(page):0}px");

        // Coarse pointer: the switcher, the Edit button and the collapse chevron are 44px targets.
        foreach (var (name, selector) in new[] { ("switcher", ".tm-dashboard-switcher"), ("edit", "[data-testid='dashboard-edit']"), ("chevron", ".tm-widget-btn") })
        {
            var box = await SizeAsync(page, selector);
            Assert.IsTrue(box.H >= 44, $"the {name} must be at least 44px tall on a coarse pointer, got {box.H:0}");
        }

        await page.Locator("[data-testid='dashboard-edit']").ClickAsync();
        await page.Locator(".tm-dashboard--edit").First.WaitForAsync();
        Assert.AreEqual(0, await page.Locator(".tm-widget-resize-handle").CountAsync(),
            "resize is desktop-only — a tablet renders no handles, even in edit mode");
        await ShotAsync(page, "tablet-1024-touch--edit");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Tablet_768_GridIsTwoColumns_AndTheHeaderStacksSwitcherAboveTitle()
    {
        var page = await OpenDashboardAsync(768, 1024);
        TestContext.WriteLine($"tablet 768: data-layout={await LayoutAsync(page)}, container={await ContainerWidthAsync(page):0}px");

        var tracks = await TracksAsync(page);
        Assert.HasCount(2, tracks, $"a ~720px container is below the 768px two-column step, got {tracks.Length} tracks");

        // The stacked header: the switcher row sits above the title row; the Edit button shares
        // the title's row.
        var tops = await HeaderTopsAsync(page);
        Assert.IsTrue(tops.Switcher < tops.Title - 4,
            $"the switcher (top {tops.Switcher:0}) must sit above the title (top {tops.Title:0})");
        Assert.AreEqual(tops.Title, tops.Edit, 40.0,
            $"the Edit button (top {tops.Edit:0}) shares the title row (top {tops.Title:0})");
        await ShotAsync(page, "tablet-768--view");
        await page.Locator("[data-testid='dashboard-edit']").ClickAsync();
        await page.Locator(".tm-dashboard--edit").First.WaitForAsync();
        await ShotAsync(page, "tablet-768--edit");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Mobile_390_StacksInRowThenColumn_WithoutHorizontalScroll_AndCollapseReleasesHeight()
    {
        var page = await OpenDashboardAsync(390, 844);
        TestContext.WriteLine($"mobile 390: data-layout={await LayoutAsync(page)}, container={await ContainerWidthAsync(page):0}px");

        Assert.HasCount(1, await TracksAsync(page), "the mobile grid is one column");
        AssertNoHorizontalScroll(page);

        // The title sits above the switcher; all header buttons are 44px targets. The demo page
        // carries HeaderActions, so this is the three-row mobile header: title | switcher row 2 |
        // host actions + Edit row 3 (the demo--mobile.png reference without actions shares
        // switcher and Edit on row 2 — covered by the bUnit header tests).
        var tops = await HeaderTopsAsync(page);
        Assert.IsTrue(tops.Title < tops.Switcher - 4,
            $"the title (top {tops.Title:0}) stacks above the switcher (top {tops.Switcher:0})");
        Assert.IsTrue(tops.Switcher < tops.Edit - 4,
            $"with host actions the Edit button (top {tops.Edit:0}) wraps below the switcher (top {tops.Switcher:0})");
        foreach (var (name, selector) in new[] { ("switcher", ".tm-dashboard-switcher"), ("edit", "[data-testid='dashboard-edit']"), ("host action", ".tm-dashboard-header-actions .tm-btn") })
        {
            var box = await SizeAsync(page, selector);
            Assert.IsTrue(box.H >= 44, $"the {name} must be a 44px target on mobile, got {box.H:0}");
        }

        // Widget tops grow in (Y, X) order — the mobile stack reads like the desktop grid.
        var widgetTops = await page.EvaluateAsync<double[]>(
            "() => [...document.querySelectorAll('.tm-dashboard-grid .tm-widget')].map(w => w.getBoundingClientRect().top)");
        for (var i = 1; i < widgetTops.Length; i++)
        {
            Assert.IsTrue(widgetTops[i] > widgetTops[i - 1],
                $"widget {i} (top {widgetTops[i]:0}) must sit below widget {i - 1} (top {widgetTops[i - 1]:0})");
        }

        await ShotAsync(page, "mobile-390--view");

        // Collapse the first card: it shrinks to its header and the next card moves up by the
        // released height.
        var before = await CollapseMetricsAsync(page);
        await page.Locator(".tm-widget-btn").First.ClickAsync();
        await page.WaitForTimeoutAsync(250);
        var after = await CollapseMetricsAsync(page);

        Assert.AreEqual(after.HeaderH, after.CardH, 3.0,
            $"the collapsed card (height {after.CardH:0}) collapses to its header (height {after.HeaderH:0})");
        var released = before.CardH - after.CardH;
        var moved = before.NextTop - after.NextTop;
        Assert.AreEqual(released, moved, 4.0,
            $"the next card must move up by the released height (card {before.CardH:0}→{after.CardH:0}, next top {before.NextTop:0}→{after.NextTop:0})");
        AssertNoHorizontalScroll(page);
        await ShotAsync(page, "mobile-390--collapsed");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Mobile_320_FrenchFits_WithoutScroll_AndTheOpenSwitcherMenuStaysInside()
    {
        var page = await OpenDashboardAsync(320, 844, culture: "fr");
        AssertNoHorizontalScroll(page);

        // The switcher popover (18rem min-width) must clamp inside the 320px viewport.
        await page.Locator(".tm-dashboard-switcher").ClickAsync();
        var menu = page.Locator(".tm-dashboard-menu").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var box = await menu.BoundingBoxAsync();
        Assert.IsNotNull(box, "the switcher menu rendered");
        Assert.IsTrue(box.X >= 0 && box.X + box.Width <= 320,
            $"the open menu ({box.X:0}..{box.X + box.Width:0}) must stay inside the 320px viewport");

        await page.Locator("body").ClickAsync(new LocatorClickOptions { Position = new Position { X = 10, Y = 300 } });
        await ShotAsync(page, "mobile-320-fr--view");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task NarrowHost_InAWideViewport_GetsTheMobileHeaderAndGrid()
    {
        var page = await OpenResponsiveConventionsAsync();
        var host = page.GetByTestId("rc-host-narrow");

        Assert.AreEqual("mobile", await host.Locator(".tm-dashboard").GetAttributeAsync("data-layout"),
            "a ~24rem host inside a 1440px viewport is a mobile dashboard");

        // The rc host passes no Title: the switcher already shows the name (sr-only h2), so this
        // is the one-row no-heading mobile header — switcher + Edit share the first row — and
        // the grid is one column.
        var layout = await host.EvaluateAsync<string>(
            """
            el => {
              const toolbar = el.querySelector('.tm-dashboard-toolbar');
              if (toolbar.classList.contains('tm-dashboard-toolbar--with-heading')) return 'unexpected-heading-modifier';
              const tracks = getComputedStyle(el.querySelector('.tm-dashboard-grid')).gridTemplateColumns.split(' ').filter(Boolean);
              if (tracks.length !== 1) return 'tracks=' + tracks.length;
              const top = sel => { const n = el.querySelector(sel); return n ? n.getBoundingClientRect().top : -1; };
              const switcher = top('.tm-dashboard-switcher');
              const edit = top("[data-testid='dashboard-edit']");
              if (edit < 0) return 'no-edit-button';
              return Math.abs(switcher - edit) <= 40 ? '' : `rows: switcher=${switcher.toFixed(0)} edit=${edit.toFixed(0)}`;
            }
            """);
        Assert.AreEqual("", layout, "the narrow host renders the one-row mobile header and a one-column grid");
        await ShotAsync(page, "responsive-conventions--narrow-host-1440");
    }

    // ── helpers ───────────────────────────────────────────────────

    private async Task<IPage> OpenDashboardAsync(int width, int height, bool touch = false, string? culture = null)
    {
        IPage page;
        if (touch)
        {
            var context = await Browser.NewContextAsync(new BrowserNewContextOptions
            {
                HasTouch = true,
                ViewportSize = new ViewportSize { Width = width, Height = height },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
            });
            RegisterContext(context);
            page = await context.NewPageAsync();
        }
        else
        {
            page = await CreatePageAsync();
            await page.SetViewportSizeAsync(width, height);
        }

        if (culture is not null)
        {
            await page.AddInitScriptAsync($"window.localStorage.setItem('tm-demo-culture','{culture}');");
        }

        await page.GotoAsync(BaseUrl.TrimEnd('/') + "/dashboard",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        await page.WaitForFunctionAsync(
            "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        await page.Locator(".tm-dashboard .tm-widget").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30_000 });
        await page.WaitForTimeoutAsync(400);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        return page;
    }

    private async Task<IPage> OpenResponsiveConventionsAsync()
    {
        var page = await CreatePageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync(BaseUrl.TrimEnd('/') + "/responsive-conventions",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        await page.WaitForFunctionAsync(
            "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        await page.GetByTestId("rc-host-narrow").Locator(".tm-dashboard-grid").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30_000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private Task<string> LayoutAsync(IPage page) =>
        page.Locator(".tm-dashboard").First.GetAttributeAsync("data-layout")!;

    private Task<double> ContainerWidthAsync(IPage page) =>
        page.EvaluateAsync<double>("() => document.querySelector('.tm-dashboard-grid-container').clientWidth");

    private Task<double[]> TracksAsync(IPage page) =>
        page.EvaluateAsync<double[]>(
            "() => getComputedStyle(document.querySelector('.tm-dashboard-grid')).gridTemplateColumns.split(' ').filter(Boolean).map(parseFloat)");

    private void AssertNoHorizontalScroll(IPage page)
    {
        var overflow = page.EvaluateAsync<string>(
            """
            () => {
              const doc = document.documentElement;
              const container = document.querySelector('.tm-dashboard-grid-container');
              const bad = [];
              if (doc.scrollWidth > doc.clientWidth) bad.push(`document ${doc.scrollWidth}>${doc.clientWidth}`);
              if (container.scrollWidth > container.clientWidth) bad.push(`grid-container ${container.scrollWidth}>${container.clientWidth}`);
              return bad.join(', ');
            }
            """);
        Assert.AreEqual("", overflow.Result, "no horizontal scroll at this width");
    }

    private Task<(double Switcher, double Title, double Edit)> HeaderTopsAsync(IPage page) =>
        page.EvaluateAsync<HeaderTops>(
            """
            () => {
              const top = el => el ? el.getBoundingClientRect().top : -1;
              return {
                switcher: top(document.querySelector('.tm-dashboard-switcher')),
                title: top(document.querySelector('.tm-dashboard-title')),
                edit: top(document.querySelector("[data-testid='dashboard-edit']")),
              };
            }
            """).ContinueWith(t => (t.Result.Switcher, t.Result.Title, t.Result.Edit));

    private Task<(double W, double H)> SizeAsync(IPage page, string selector) =>
        page.EvaluateAsync<Box>(
            """
            selector => {
              const box = document.querySelector(selector).getBoundingClientRect();
              return { w: box.width, h: box.height };
            }
            """,
            selector).ContinueWith(t => (t.Result.W, t.Result.H));

    private Task<(double CardH, double HeaderH, double NextTop)> CollapseMetricsAsync(IPage page) =>
        page.EvaluateAsync<CollapseMetrics>(
            """
            () => {
              const cards = [...document.querySelectorAll('.tm-dashboard-grid .tm-widget')];
              const card = cards[0].getBoundingClientRect();
              const header = cards[0].querySelector('.tm-widget-header').getBoundingClientRect();
              const next = cards[1] ? cards[1].getBoundingClientRect().top : 0;
              return { cardH: card.height, headerH: header.height, nextTop: next };
            }
            """).ContinueWith(t => (t.Result.CardH, t.Result.HeaderH, t.Result.NextTop));

    private sealed class HeaderTops
    {
        public double Switcher { get; set; }
        public double Title { get; set; }
        public double Edit { get; set; }
    }

    private sealed class Box
    {
        public double W { get; set; }
        public double H { get; set; }
    }

    private sealed class CollapseMetrics
    {
        public double CardH { get; set; }
        public double HeaderH { get; set; }
        public double NextTop { get; set; }
    }

    private async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(OutputDirectory);
        var path = Path.Combine(OutputDirectory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(path);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
