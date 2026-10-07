using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// The responsive convention, exercised on a real TmDashboard: three hosts at the reference widths,
/// a forced layout, a coarse pointer, dark mode and the indigo theme. Every case asserts a computed
/// value — a screenshot alone cannot fail a layout.
/// </summary>
[TestClass]
public sealed class ResponsiveConventionsE2ETests : WasmTestBase
{
    private static readonly string OutputDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "TestResults", "responsive-conventions");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Dashboard_RestacksByContainer_NotByViewport()
    {
        var page = await OpenAsync(1440, 900);

        var wide = await GridAsync(page, "rc-host-wide");
        var medium = await GridAsync(page, "rc-host-medium");
        var compact = await GridAsync(page, "rc-host-compact");
        var narrow = await GridAsync(page, "rc-host-narrow");

        Assert.HasCount(12, wide.Tracks, $"the wide host is the desktop grid (container {wide.Width:0}px)");
        Assert.HasCount(6, medium.Tracks, $"the 58rem host is the six-column tablet grid (container {medium.Width:0}px)");
        Assert.HasCount(2, compact.Tracks, $"the 50rem host is the two-column step inside tablet (container {compact.Width:0}px)");
        Assert.HasCount(1, narrow.Tracks, "the 24rem host stacks even though the viewport is 1440px wide");
        Assert.IsTrue(narrow.MinWidget >= 120, $"a stacked widget must stay readable, got {narrow.MinWidget:0}px");

        await ShotAsync(page, "1440");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task ForcedMobile_Stacks_AndForcedTablet_UsesSixColumns()
    {
        var page = await OpenAsync(1440, 900);

        await page.GetByRole(AriaRole.Button, new() { Name = "Mobile", Exact = true }).ClickAsync();
        var mobile = await GridAsync(page, "rc-host-wide");
        Assert.HasCount(1, mobile.Tracks, "a forced mobile layout stacks the grid");
        await ShotAsync(page, "forced-mobile");

        await page.GetByRole(AriaRole.Button, new() { Name = "Tablet", Exact = true }).ClickAsync();
        var tablet = await GridAsync(page, "rc-host-wide");
        Assert.HasCount(6, tablet.Tracks, "a forced tablet layout uses six columns");
        await ShotAsync(page, "forced-tablet");

        await page.GetByRole(AriaRole.Button, new() { Name = "Auto", Exact = true }).ClickAsync();
        var restored = await GridAsync(page, "rc-host-wide");
        Assert.HasCount(12, restored.Tracks, "returning to Auto restores the measured desktop grid");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Touch_GrowsTheIconButton_AndHidesTheEdgeHandles()
    {
        var page = await OpenTouchAsync(1024, 768);
        await page.GetByRole(AriaRole.Button, new() { Name = "Edit" }).First.ClickAsync();
        await page.Locator(".tm-dashboard--edit").First.WaitForAsync();

        var icon = await page.GetByTestId("rc-reveal").BoundingBoxAsync();
        Assert.IsNotNull(icon);
        Assert.IsTrue(icon.Width >= 44 && icon.Height >= 44, $"the icon button must be at least 44px, got {icon.Width:0}x{icon.Height:0}");

        var reveal = await page.GetByTestId("rc-reveal").EvaluateAsync<string>("el => getComputedStyle(el).opacity");
        Assert.AreEqual("1", reveal, "a coarse pointer shows a hover-only action");

        var edge = await page.Locator(".tm-widget-resize-n").CountAsync();
        Assert.AreEqual(0, edge, "below desktop the edge handles are not rendered");
        var se = await page.Locator(".tm-widget-resize-se").CountAsync();
        Assert.AreEqual(0, se, "resize is desktop only, so a coarse pointer below desktop has no handle");

        await ShotAsync(page, "1024-touch");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task EditMode_ShowsTheGrid_AndACoarsePointerHidesTheEdgeHandles()
    {
        var desktop = await OpenAsync(1440, 900);
        await desktop.GetByRole(AriaRole.Button, new() { Name = "Edit" }).First.ClickAsync();
        await desktop.Locator(".tm-dashboard--edit").First.WaitForAsync();
        var se = await desktop.Locator(".tm-widget-resize-se").First.BoundingBoxAsync();
        Assert.IsNotNull(se, "desktop edit mode renders the corner handle");
        await ShotAsync(desktop, "edit-desktop");

        var touch = await OpenTouchAsync(1440, 900);
        await touch.GetByRole(AriaRole.Button, new() { Name = "Edit" }).First.ClickAsync();
        await touch.Locator(".tm-dashboard--edit").First.WaitForAsync();
        var corner = await touch.Locator(".tm-widget-resize-se").First.BoundingBoxAsync();
        Assert.IsNotNull(corner);
        Assert.IsTrue(corner.Width >= 44 && corner.Height >= 44, $"the corner handle must be at least 44px, got {corner.Width:0}x{corner.Height:0}");
        var edge = await touch.Locator(".tm-widget-resize-n").First.EvaluateAsync<string>("el => getComputedStyle(el).display");
        Assert.AreEqual("none", edge, "a coarse pointer hides the edge resize handles");
        await ShotAsync(touch, "edit-touch");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task CoarsePointer_ShowsTheReveal_WithoutEditMode()
    {
        var page = await OpenTouchAsync(390, 844);
        var reveal = await page.GetByTestId("rc-reveal").EvaluateAsync<string>("el => getComputedStyle(el).opacity");
        Assert.AreEqual("1", reveal, "a coarse pointer shows a hover-only action without entering edit mode");
        await ShotAsync(page, "coarse-pointer");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Headings_MatchTheMeasuredContainer()
    {
        foreach (var (width, height) in new[] { (1440, 900), (1024, 768), (390, 844) })
        {
            var page = await OpenAsync(width, height);
            foreach (var host in new[] { "rc-host-wide", "rc-host-medium", "rc-host-compact", "rc-host-narrow" })
            {
                var grid = await page.GetByTestId(host).Locator(".tm-dashboard-grid-container").EvaluateAsync<int>("el => Math.round(el.clientWidth)");
                var layout = await page.GetByTestId(host).Locator(".tm-dashboard").GetAttributeAsync("data-layout");
                var heading = await page.GetByTestId($"{host}-heading").InnerTextAsync();
                StringAssert.Contains(heading, $"{grid} px", $"{host} at {width}: the heading must name the measured width");
                StringAssert.Contains(heading, layout!, $"{host} at {width}: the heading must name the dashboard's own data-layout");
            }
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task FinePointer_HidesTheHoverAction()
    {
        var page = await OpenAsync(1440, 900);

        var reveal = await page.GetByTestId("rc-reveal").EvaluateAsync<string>("el => getComputedStyle(el).opacity");
        Assert.AreEqual("0", reveal, "a fine pointer hides the action until hover or focus");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task ReferenceWidths_RenderTheDocumentedGrids()
    {
        var narrow = await OpenAsync(390, 844);
        var phone = await GridAsync(narrow, "rc-host-wide");
        Assert.HasCount(1, phone.Tracks, "390px of viewport leaves the wide host below the mobile boundary");
        await ShotAsync(narrow, "390");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task DarkAndIndigo_KeepTheContainerRestack()
    {
        foreach (var (dark, indigo, name) in new (bool Dark, bool Indigo, string Name)[]
        {
            (true, false, "dark"),
            (false, true, "indigo"),
            (true, true, "dark-indigo"),
        })
        {
            var page = await OpenAsync(1440, 900);
            if (indigo)
                await page.EvaluateAsync("() => document.documentElement.setAttribute('data-tm-theme', 'indigo')");
            if (dark)
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Switch to dark mode" }).ClickAsync();
                await page.Locator(".demo-shell[data-theme='dark']").WaitForAsync();
                await page.Mouse.MoveAsync(0, 0);
                await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
            }

            var grid = await GridAsync(page, "rc-host-narrow");
            Assert.HasCount(1, grid.Tracks, $"{name}: the narrow host still stacks");
            await ShotAsync(page, name);
        }
    }

    private async Task<IPage> OpenAsync(int width, int height) => await OpenCoreAsync(width, height, touch: false);

    private async Task<IPage> OpenTouchAsync(int width, int height) => await OpenCoreAsync(width, height, touch: true);

    private async Task<IPage> OpenCoreAsync(int width, int height, bool touch)
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
        await page.GotoAsync(BaseUrl.TrimEnd('/') + "/responsive-conventions",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        await page.WaitForFunctionAsync(
            "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        await page.GetByTestId("rc-host-wide").Locator(".tm-dashboard-grid").WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await page.GetByTestId("rc-host-wide-heading").GetByText("px").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        return page;
    }

    private sealed class Grid
    {
        public double[] Tracks { get; set; } = [];
        public double Width { get; set; }
        public double MinWidget { get; set; }
    }

    private static async Task<Grid> GridAsync(IPage page, string host)
    {
        return await page.EvaluateAsync<Grid>(
            """
            host => {
              const grid = document.querySelector(`[data-testid="${host}"] .tm-dashboard-grid`);
              const tracks = getComputedStyle(grid).gridTemplateColumns.split(' ').filter(Boolean).map(parseFloat);
              const widgets = [...grid.querySelectorAll('.tm-widget')].map(el => el.getBoundingClientRect().width);
              return { tracks, width: grid.clientWidth, minWidget: Math.min(...widgets) };
            }
            """,
            host);
    }

    private static async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(OutputDir);
        var path = Path.GetFullPath(Path.Combine(OutputDir, $"{name}.png"));

        // A full-page or element screenshot resets Playwright's touch emulation, so the capture
        // would show a fine pointer. Grow the viewport to the document instead, then restore it.
        var viewport = page.ViewportSize ?? new ViewportSize { Width = 1440, Height = 900 };
        var height = await page.EvaluateAsync<int>("() => document.documentElement.scrollHeight");
        await page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = "*, *::before, *::after { transition: none !important; animation: none !important; }",
        });
        await page.SetViewportSizeAsync(viewport.Width, Math.Max(viewport.Height, height));
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false, Type = ScreenshotType.Png });
        await page.SetViewportSizeAsync(viewport.Width, viewport.Height);

        if (name.Contains("touch", StringComparison.Ordinal))
        {
            var coarse = await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches");
            Assert.IsTrue(coarse, $"{name}: the touch emulation must survive the screenshot");
        }
    }
}
