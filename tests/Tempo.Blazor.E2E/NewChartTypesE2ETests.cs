using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for the Fáze N8 chart types (Funnel, Heatmap, Treemap) on the /charts demo page
/// (WASM demo at 7106): rendering with realistic data, hover tooltips, click events
/// surfacing the data point, and legend toggles (edge cases). Screenshots land in
/// <c>__screenshots__/charts-new-types/</c>.
/// </summary>
[TestClass]
public class NewChartTypesE2ETests : WasmTestBase
{
    private const string DemoPage = "/charts";

    private sealed record DemoPageHandle(IPage Page, List<string> Errors);

    private async Task<DemoPageHandle> OpenPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);

        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error" && msg.Text.Contains("Unhandled exception"))
            {
                errors.Add(msg.Text);
            }
        };

        await GotoAppReadyAsync(page, $"{BaseUrl}{DemoPage}");

        await page.Locator("[data-testid='charts-demo-funnel'] .tm-chart__funnel-segment").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 90000 });
        return new DemoPageHandle(page, errors);
    }

    private static void AssertNoBlazorErrors(DemoPageHandle handle)
        => Assert.AreEqual(0, handle.Errors.Count,
            "The page raised unhandled exceptions: " + string.Join(" | ", handle.Errors));

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Funnel_RendersStages_TooltipAndClick()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var section = page.Locator("[data-testid='charts-demo-funnel']");
        await section.ScrollIntoViewIfNeededAsync();

        var segments = section.Locator(".tm-chart__funnel-segment");
        Assert.AreEqual(5, await segments.CountAsync());
        StringAssert.Contains(await section.InnerTextAsync(), "%");   // conversion percentages

        await segments.Nth(1).HoverAsync();
        var tooltip = section.Locator(".tm-chart__tooltip");
        await tooltip.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        StringAssert.Contains(await tooltip.InnerTextAsync(), "Konzultace");
        await SaveScreenshotAsync(page, "funnel");

        await segments.Nth(2).ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='charts-demo-clicked']"))
            .ToContainTextAsync("Nabídka", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        AssertNoBlazorErrors(handle);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Heatmap_RendersMatrix_TooltipAndRowToggleEdge()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var section = page.Locator("[data-testid='charts-demo-heatmap']");
        await section.ScrollIntoViewIfNeededAsync();

        var cells = section.Locator(".tm-chart__heatmap-cell");
        Assert.AreEqual(25, await cells.CountAsync());   // 5 days × 5 dayparts

        await cells.Nth(7).HoverAsync();
        var tooltip = section.Locator(".tm-chart__tooltip");
        await tooltip.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        StringAssert.Contains(await tooltip.InnerTextAsync(), "Úterý");
        await SaveScreenshotAsync(page, "heatmap");

        // Edge: toggling a day drops its whole row.
        await section.Locator("[data-testid='chart-legend-0']").ClickAsync();
        await Assertions.Expect(cells).ToHaveCountAsync(20, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "edge-heatmap-row-toggle");
        AssertNoBlazorErrors(handle);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Treemap_RendersHierarchy_ClickAndSubtreeToggleEdge()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var section = page.Locator("[data-testid='charts-demo-treemap']");
        await section.ScrollIntoViewIfNeededAsync();

        Assert.AreEqual(3, await section.Locator(".tm-chart__treemap-group").CountAsync());
        var tiles = section.Locator(".tm-chart__treemap-tile");
        Assert.AreEqual(8, await tiles.CountAsync());
        await SaveScreenshotAsync(page, "treemap");

        var novak = section.Locator(".tm-chart__treemap-tile[data-path='Sporná agenda / Novák a syn']");
        await novak.ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='charts-demo-clicked']"))
            .ToContainTextAsync("Novák a syn", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });

        // Edge: toggling a practice area drops its whole subtree.
        await section.Locator("[data-testid='chart-legend-2']").ClickAsync();
        await Assertions.Expect(tiles).ToHaveCountAsync(5, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "edge-treemap-subtree-toggle");
        AssertNoBlazorErrors(handle);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Plan 8fa7319c, phase 1: a funnel label is drawn in viewBox units and the SVG meets into its box,
    /// so the size a reader MEETS is the viewBox size times the box's scale — and that scale is
    /// height-limited on a wide desktop card exactly as it is on a phone. A 320 px chart card keeps
    /// ≈ 271 px of plot once its legend is laid out, so 672 px wide meets at ≈ 0.68 and a phone's 358 px
    /// at ≈ 0.60; a width-only container query never fired at 1440. The type scale therefore compensates
    /// the real scale (`_chart.css` solves `font-size × min(plotWidth / 600, plotHeight / 400)` from the
    /// plot's own axes), and this spec asserts the RENDERED size, not the computed one: a stage label at
    /// or above 12 px, its value line at or above 11 px, on every funnel the page renders — the 1440
    /// Auto card the finding measured included. Screenshots land in <c>__screenshots__/charts-new-types/</c>
    /// at 390 / 834 / 1440, light + dark.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Funnel_LabelsStayReadable_AtEveryContainerWidth_InLightAndDark()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error" && msg.Text.Contains("Unhandled exception"))
            {
                errors.Add(msg.Text);
            }
        };

        foreach (var (width, height, name) in new[] { (390, 844, "390"), (834, 1112, "834"), (1440, 1100, "1440") })
        {
            await page.SetViewportSizeAsync(width, height);
            await GotoAppReadyAsync(page, $"{BaseUrl}{DemoPage}");

            var section = page.Locator("[data-testid='charts-demo-funnel-labels']");
            await section.ScrollIntoViewIfNeededAsync();
            await section.Locator(".tm-chart__funnel-segment").First
                .WaitForAsync(new LocatorWaitForOptions { Timeout = 90000 });

            // Outside placement: every stage label starts at the gutter, in the gutter class.
            var outsideLabels = page.Locator("[data-testid='charts-demo-funnel-outside'] .tm-chart__funnel-label");
            Assert.IsTrue(await outsideLabels.CountAsync() >= 5, "every stage keeps its label");
            foreach (var label in await outsideLabels.AllAsync())
            {
                Assert.AreEqual("start", await TextAnchorAsync(label),
                    "an Outside label always sits beside the stage");
                StringAssert.Contains(await label.GetAttributeAsync("class") ?? "", "tm-chart__funnel-label--outside");
                Assert.IsNull(await label.GetAttributeAsync("fill"),
                    "the ink is a token class, never an inline colour");
            }

            // RENDERED readability — the whole point of the finding. A label is 12 px on screen, its
            // value line 11 px, measured as viewBox type × the scale the SVG actually meets at.
            foreach (var chart in new[] { "charts-demo-funnel", "charts-demo-funnel-outside", "charts-demo-funnel-zero" })
            {
                var label = page.Locator($"[data-testid='{chart}'] .tm-chart__funnel-label").First;
                var value = page.Locator($"[data-testid='{chart}'] .tm-chart__funnel-value").First;

                var labelPx = await RenderedFunnelFontPxAsync(label);
                var valuePx = await RenderedFunnelFontPxAsync(value);
                var labelBox = await RenderedTextHeightPxAsync(label);
                var valueBox = await RenderedTextHeightPxAsync(value);
                var plot = await PlotSizeAsync(label);
                var diagnostics = await TypeScaleDiagnosticsAsync(page, chart);

                Assert.IsTrue(labelPx >= 12,
                    $"{name}: the stage label of '{chart}' renders at {labelPx:0.00} px in a "
                    + $"{plot[0]:0} × {plot[1]:0} plot — a funnel label is never smaller than 12 px "
                    + $"({diagnostics})");
                Assert.IsTrue(valuePx >= 11,
                    $"{name}: the value line of '{chart}' renders at {valuePx:0.00} px in a "
                    + $"{plot[0]:0} × {plot[1]:0} plot — a funnel value is never smaller than 11 px "
                    + $"({diagnostics})");
                Assert.IsTrue(labelBox >= 11,
                    $"{name}: the stage label box of '{chart}' measures {labelBox:0.00} px on screen");
                Assert.IsTrue(valueBox >= 11,
                    $"{name}: the value box of '{chart}' measures {valueBox:0.00} px on screen");
            }

            // The narrow card must take a bigger viewBox step than the wide one, or the discrete
            // fallback (an engine without length division in calc()) would be a no-op.
            var narrowComputed = await ComputedFontSizeAsync(
                page.Locator("[data-testid='charts-demo-funnel-outside'] .tm-chart__funnel-label").First);
            var wideComputed = await ComputedFontSizeAsync(
                page.Locator("[data-testid='charts-demo-funnel'] .tm-chart__funnel-label").First);
            Assert.IsTrue(narrowComputed >= wideComputed,
                $"{name}: the narrower card takes the larger viewBox step ({narrowComputed:0.##} vs {wideComputed:0.##})");

            // No label or value may run past the viewBox edge: a clipped stage name is not "beside the stage".
            foreach (var text in await page.Locator(
                         "[data-testid='charts-demo-funnel-outside'] .tm-chart__funnel-label,"
                         + " [data-testid='charts-demo-funnel-outside'] .tm-chart__funnel-value").AllAsync())
            {
                var box = await text.EvaluateAsync<double[]>(
                    "el => { const b = el.getBBox(); return [b.x, b.x + b.width]; }");
                Assert.IsTrue(box[0] >= 0 && box[1] <= 600,
                    $"the text '{await text.TextContentAsync()}' spans {box[0]:0}–{box[1]:0} of a 600-unit viewBox");
            }

            // The stalled pipeline: both zero stages keep a visible band and their 0 (0 %) label.
            var zero = page.Locator("[data-testid='charts-demo-funnel-zero']");
            Assert.AreEqual(4, await zero.Locator(".tm-chart__funnel-segment").CountAsync(),
                "a stage with the value 0 must not disappear from the funnel");
            Assert.IsTrue(Regex.IsMatch(await zero.InnerTextAsync(), @"0\s*\(0[\s\u00A0]*%\)"),
                "a zero stage keeps its '0 (0 %)' label");
            Assert.AreEqual(4, await zero.Locator("[data-testid='chart-funnel-table'] tbody tr").CountAsync(),
                "the screen-reader table carries exactly the stages that are drawn, zero stages included");
            Assert.IsTrue(Regex.IsMatch(
                    await zero.Locator("[data-testid='chart-funnel-table']").InnerTextAsync(),
                    @"0\s*/\s*1[\s\u00A0,.]?240\s*\(0[\s\u00A0]*%\)"),
                "a reader hears the flow between the pipeline's first stage and a stalled one");

            await SaveScreenshotAsync(section, $"funnel-labels-{name}-light");
            await SaveScreenshotAsync(page.Locator("[data-testid='charts-demo-funnel']"), $"funnel-auto-{name}-light");

            await page.Locator(ThemeToggleSelector).First.ClickAsync();
            await Assertions.Expect(page.Locator(".demo-shell"))
                .ToHaveAttributeAsync("data-theme", "dark",
                    new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
            await section.ScrollIntoViewIfNeededAsync();
            await SaveScreenshotAsync(section, $"funnel-labels-{name}-dark");
            await SaveScreenshotAsync(page.Locator("[data-testid='charts-demo-funnel']"), $"funnel-auto-{name}-dark");

            await page.Locator(ThemeToggleSelector).First.ClickAsync();
            await Assertions.Expect(page.Locator(".demo-shell"))
                .ToHaveAttributeAsync("data-theme", "light",
                    new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
        }

        Assert.AreEqual(0, errors.Count, "The page raised unhandled exceptions: " + string.Join(" | ", errors));
    }

    private const string ThemeToggleSelector = "[data-testid='theme-toggle']:visible";

    /// <summary>The label's computed font-size in viewBox units (the CSS token step, before scaling).</summary>
    private static Task<double> ComputedFontSizeAsync(ILocator label) =>
        label.EvaluateAsync<double>("el => parseFloat(getComputedStyle(el).fontSize)");

    /// <summary>
    /// The font size a reader actually meets: viewBox units × the scale the SVG applies
    /// (<c>preserveAspectRatio="xMidYMid meet"</c>, so the smaller of the width and height ratios).
    /// The funnel label is a <c>&lt;tspan&gt;</c>, so the scale comes from its owning SVG.
    /// </summary>
    private static Task<double> RenderedFunnelFontPxAsync(ILocator label) =>
        label.EvaluateAsync<double>(
            "el => { const svg = el.ownerSVGElement; const r = svg.getBoundingClientRect();"
            + " const scale = Math.min(r.width / 600, r.height / 400);"
            + " return parseFloat(getComputedStyle(el).fontSize) * scale; }");

    /// <summary>
    /// The height of the glyph box a reader sees, in CSS pixels: the <c>getBBox()</c> height (viewBox
    /// units) × the scale the SVG applies. This is the "rendered height" the finding measured.
    /// </summary>
    private static Task<double> RenderedTextHeightPxAsync(ILocator text) =>
        text.EvaluateAsync<double>(
            "el => { const svg = el.ownerSVGElement; const r = svg.getBoundingClientRect();"
            + " const scale = Math.min(r.width / 600, r.height / 400);"
            + " return el.getBBox().height * scale; }");

    /// <summary>The size of the box the SVG meets into (the funnel's plot container).</summary>
    private static Task<double[]> PlotSizeAsync(ILocator text) =>
        text.EvaluateAsync<double[]>(
            "el => { const r = el.ownerSVGElement.getBoundingClientRect(); return [r.width, r.height]; }");

    /// <summary>
    /// What the type scale resolved for one funnel: the container widths the queries see, the computed
    /// font size and whether the engine supports the exact compensation. Only used in failure messages.
    /// </summary>
    private static Task<string> TypeScaleDiagnosticsAsync(IPage page, string chart) =>
        page.EvaluateAsync<string>($@"() => {{
            const root = document.querySelector(""[data-testid='{chart}'] .tm-chart--funnel"");
            const plot = document.querySelector(""[data-testid='{chart}'] .tm-chart__plot"");
            const label = document.querySelector(""[data-testid='{chart}'] .tm-chart__funnel-label"");
            const box = (el) => el ? el.getBoundingClientRect() : {{ width: 0, height: 0 }};
            return JSON.stringify({{
                root: [Math.round(box(root).width), Math.round(box(root).height)],
                plot: [Math.round(box(plot).width), Math.round(box(plot).height)],
                fontSize: label ? getComputedStyle(label).fontSize : null,
                division: CSS.supports('font-size', 'calc(1px * 600 / 100cqw * 1px)')
            }});
        }}");

    /// <summary>The <c>text-anchor</c> of the <c>&lt;text&gt;</c> a label line belongs to.</summary>
    private static Task<string?> TextAnchorAsync(ILocator tspan) =>
        tspan.EvaluateAsync<string?>("el => el.closest('text')?.getAttribute('text-anchor')");

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "charts-new-types");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, $"{fileName}.png"),
            FullPage = false
        });
    }

    private static async Task SaveScreenshotAsync(ILocator section, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "charts-new-types");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{fileName}.png");
        await section.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = path,
            Timeout = 60_000
        });
        Assert.IsTrue(File.Exists(path), $"the screenshot was not written: {path}");
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
