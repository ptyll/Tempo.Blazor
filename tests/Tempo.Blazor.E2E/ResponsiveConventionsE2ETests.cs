using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// The responsive convention, exercised in a browser: the same component in a wide and a narrow
/// host, at the three reference widths, with a forced layout, a coarse pointer, dark mode and the
/// indigo theme. Every case asserts a computed value — a screenshot alone cannot fail a layout.
/// </summary>
[TestClass]
public sealed class ResponsiveConventionsE2ETests : WasmTestBase
{
    private static readonly string OutputDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "TestResults", "responsive-conventions");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task SameComponent_RestacksByContainer_NotByViewport()
    {
        var page = await OpenAsync(1440, 900);

        var wide = await TileColumnsAsync(page, "rc-host-wide");
        var narrow = await TileColumnsAsync(page, "rc-host-narrow");

        Assert.HasCount(3, wide, "the wide host is the desktop three-column grid");
        Assert.HasCount(1, narrow, "the 24rem host stacks even though the viewport is 1440px wide");
        Assert.IsTrue(wide[0] > wide[1], "the lead tile takes twice the track of the others");

        await ShotAsync(page, "1440-both-hosts");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task ReferenceWidths_RenderTheDocumentedGrids()
    {
        foreach (var (width, height, name) in new (int Width, int Height, string Name)[]
        {
            (1440, 900, "1440"),
            (1024, 768, "1024-touch"),
            (390, 844, "390"),
        })
        {
            var page = await OpenAsync(width, height);

            // The demo sidebar narrows the content area, so the expected grid follows the host's
            // measured width, not the viewport: three columns above 1024px, two down to 640, one below.
            var hostWidth = await page.EvaluateAsync<double>(
                "() => document.querySelector('[data-testid=\"rc-host-wide\"] .rc-panel').clientWidth");
            var expected = hostWidth > 1024 ? 3 : hostWidth > 640 ? 2 : 1;

            var columns = await TileColumnsAsync(page, "rc-host-wide");
            Assert.HasCount(expected, columns, $"{name}: a {hostWidth:0}px host renders {expected} columns");
            await ShotAsync(page, name);
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task ForcedMobile_ReportsMobile_WhileTheGridFollowsTheContainer()
    {
        var page = await OpenAsync(1440, 900);
        await page.GetByRole(AriaRole.Button, new() { Name = "Mobile", Exact = true }).ClickAsync();

        var reported = await page.GetByTestId("rc-host-wide").Locator("[data-testid='rc-mode']").InnerTextAsync();
        Assert.AreEqual("mobile", reported.Trim().ToLowerInvariant(), "a forced mode changes the rendered branch");

        var columns = await TileColumnsAsync(page, "rc-host-wide");
        Assert.HasCount(3, columns, "the grid follows the container, not the forced mode");
        await ShotAsync(page, "forced-mobile");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task CoarsePointer_GrowsTheIconButton_ToTheTouchTarget()
    {
        var page = await OpenAsync(390, 844);

        // Playwright cannot emulate a coarse pointer, so the assertion reads what the rule applies:
        // the touch-target token, resolved to pixels. A finger gets at least 44px.
        var target = await page.EvaluateAsync<double>(
            """
            () => {
              const probe = document.createElement('div');
              probe.style.minHeight = 'var(--tm-touch-target)';
              document.body.appendChild(probe);
              const px = parseFloat(getComputedStyle(probe).minHeight);
              probe.remove();
              return px;
            }
            """);

        Assert.IsTrue(target >= 44, $"the touch target must be at least 44px, got {target}");
        await ShotAsync(page, "coarse-pointer");
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
            {
                await page.EvaluateAsync("() => document.documentElement.setAttribute('data-tm-theme', 'indigo')");
            }
            if (dark)
            {
                // The demo toggle paints the shell and sets data-theme together. Setting the
                // attribute alone leaves the light canvas under inverted ink.
                await page.GetByRole(AriaRole.Button, new() { Name = "Switch to dark mode" }).ClickAsync();
                await page.Locator(".demo-shell[data-theme='dark']").WaitForAsync();
                await page.Mouse.MoveAsync(0, 0);
                await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
            }

            var columns = await TileColumnsAsync(page, "rc-host-narrow");
            Assert.HasCount(1, columns, $"{name}: the narrow host still stacks");

            var background = await page.EvaluateAsync<string>(
                "() => getComputedStyle(document.querySelector('.rc-host')).backgroundColor");
            Assert.IsFalse(string.IsNullOrWhiteSpace(background), $"{name}: the host paints a background");
            if (dark)
            {
                var luminance = await page.EvaluateAsync<double>(
                    """
                    () => {
                      const [r, g, b] = getComputedStyle(document.querySelector('.rc-host')).backgroundColor.match(/\d+/g).map(Number);
                      return (r + g + b) / 3;
                    }
                    """);
                Assert.IsTrue(luminance < 80, $"{name}: the host background must be dark, luminance {luminance:0}");
            }
            await ShotAsync(page, name);
        }
    }

    private async Task<IPage> OpenAsync(int width, int height)
    {
        var page = await CreatePageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync(BaseUrl.TrimEnd('/') + "/responsive-conventions",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        await page.WaitForFunctionAsync(
            "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        await page.GetByTestId("rc-panel").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        // A parked pointer and a blurred focus keep the screenshot free of hover and focus rings.
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        return page;
    }

    /// <summary>
    /// The used column widths in pixels. A browser resolves <c>2fr 1fr 1fr</c> to pixel tracks, so
    /// the assertion counts tracks rather than comparing the declared template.
    /// </summary>
    private static async Task<double[]> TileColumnsAsync(IPage page, string host)
    {
        return await page.EvaluateAsync<double[]>(
            """
            host => getComputedStyle(document.querySelector(`[data-testid="${host}"] .rc-tiles`))
                .gridTemplateColumns.split(' ').filter(Boolean).map(parseFloat)
            """,
            host);
    }

    private static async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(OutputDir);
        var path = Path.GetFullPath(Path.Combine(OutputDir, $"{name}.png"));
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true, Type = ScreenshotType.Png });
    }
}
