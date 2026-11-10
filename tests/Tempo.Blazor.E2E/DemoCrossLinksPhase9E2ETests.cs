using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 7ba95ed6 phase 9 — the demo pages cross-link each other and the modeling editor's full-bleed
/// override stops using the viewport width unit. The assertions read behaviour, not markup: a scenario
/// page's "Component reference" row really navigates to the component-level reference pages, a reference
/// section's "Full scenario" row really navigates back to the assembled scenario, and the full-bleed
/// desktop/tablet workspace of <c>/modeling-editor</c> never grows a horizontal scrollbar (WCAG 1.4.10
/// reflow — <c>100vw</c> includes the vertical scrollbar's width).
/// <para>
/// Reference screenshots of the modeling workspace (desktop 1440×760, tablet 1024×768 with a coarse
/// pointer) land in <c>TestResults/demo-cross-links-phase9</c>.
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class DemoCrossLinksPhase9E2ETests : WasmTestBase
{
    private const string AnalyticsKitUrl = "/analytics-kit";
    private const string DataDisplayUrl = "/data-display";
    private const string FormsUrl = "/forms";
    private const string ModelingEditorUrl = "/modeling-editor";
    private const string TraceWaterfallUrl = "/trace-waterfall";

    private static string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "demo-cross-links-phase9");

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

    // ── The scenario pages name their references ────────────────────────────────

    [TestMethod]
    [Description("The /analytics-kit 'Component reference' row navigates to /data-display, /charts and /forms, and /data-display links back")]
    public async Task AnalyticsKit_ComponentReferenceRow_NavigatesToEveryReferencePage()
    {
        var page = await OpenAsync(AnalyticsKitUrl);

        var row = page.Locator("[data-testid='analytics-kit-component-reference']");
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        Assert.AreEqual("Component reference", (await row.Locator(".demo-cross-links__label").TextContentAsync())?.Trim(),
            "the row carries the one localized label");
        CollectionAssert.AreEqual(
            new[] { "data-display", "charts", "forms" },
            await row.Locator("a").EvaluateAllAsync<string[]>("anchors => anchors.map(a => a.getAttribute('href'))"),
            "the reporting screen names the three component-level references");
        CollectionAssert.AreEqual(
            new[] { "Data Display", "Charts", "Form Inputs" },
            await row.Locator("a").EvaluateAllAsync<string[]>("anchors => anchors.map(a => a.textContent.trim())"),
            "the anchor text is the demo navigation title of the target page");

        // The links are relative, so the demo's <base href> resolves them — a root-absolute href would 404.
        var resolved = await row.Locator("a").EvaluateAllAsync<string[]>("anchors => anchors.map(a => a.href)");
        CollectionAssert.AreEqual(
            new[]
            {
                $"{BaseUrl.TrimEnd('/')}/data-display",
                $"{BaseUrl.TrimEnd('/')}/charts",
                $"{BaseUrl.TrimEnd('/')}/forms",
            },
            resolved,
            "each relative href resolves under the app's own base href");

        await row.Locator("a", new LocatorLocatorOptions { HasTextString = "Data Display" }).ClickAsync();
        await page.WaitForURLAsync("**/data-display", new PageWaitForURLOptions { Timeout = 20000 });
        await page.Locator("[data-testid='stat-cards-kpi']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });

        // And the reference section links back to the scenario it is assembled in.
        var back = page.Locator("[data-testid='data-display-kpi-scenario']");
        await back.ScrollIntoViewIfNeededAsync();
        await back.Locator("a", new LocatorLocatorOptions { HasTextString = "Analytics Kit" }).ClickAsync();
        await page.WaitForURLAsync("**/analytics-kit", new PageWaitForURLOptions { Timeout = 20000 });
        await page.Locator("[data-testid='analytics-kit-kpis']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });
    }

    [TestMethod]
    [Description("The /trace-waterfall 'Component reference' row navigates to /data-display and its waterfall section links back")]
    public async Task TraceWaterfall_ComponentReferenceRow_NavigatesToTheDataDisplayReference()
    {
        var page = await OpenAsync(TraceWaterfallUrl);

        var row = page.Locator("[data-testid='trace-page-component-reference']");
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        Assert.AreEqual(1, await row.Locator("a").CountAsync(), "the trace page names the one reference it has");
        Assert.AreEqual("Data Display", (await row.Locator("a").First.TextContentAsync())?.Trim());

        await row.Locator("a").First.ClickAsync();
        await page.WaitForURLAsync("**/data-display", new PageWaitForURLOptions { Timeout = 20000 });

        var back = page.Locator("[data-testid='data-display-trace-scenario']");
        await back.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });
        await back.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(200);
        await back.Locator("a", new LocatorLocatorOptions { HasTextString = "Trace Waterfall" }).ClickAsync();
        await page.WaitForURLAsync("**/trace-waterfall", new PageWaitForURLOptions { Timeout = 20000 });
        await page.Locator("[data-testid='trace-page-waterfall']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });
    }

    [TestMethod]
    [Description("Every reference section on /data-display links to the assembled scenario it is used in")]
    public async Task DataDisplay_EverySectionLinksToItsScenario()
    {
        var page = await OpenAsync(DataDisplayUrl);

        var expected = new (string TestId, string Href, string Text)[]
        {
            ("data-display-kpi-scenario", "analytics-kit", "Analytics Kit"),
            ("data-display-trend-scenario", "analytics-kit", "Analytics Kit"),
            ("data-display-breakdown-scenario", "analytics-kit", "Analytics Kit"),
            ("data-display-facet-scenario", "analytics-kit", "Analytics Kit"),
            ("data-display-usage-scenario", "analytics-kit", "Analytics Kit"),
            ("data-display-segmented-scenario", "analytics-kit", "Analytics Kit"),
            ("data-display-trace-scenario", "trace-waterfall", "Trace Waterfall"),
        };

        foreach (var (testId, href, text) in expected)
        {
            var row = page.Locator($"[data-testid='{testId}']");
            await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 20000 });
            Assert.AreEqual(href, await row.Locator("a").First.GetAttributeAsync("href"), testId);
            Assert.AreEqual(text, (await row.Locator("a").First.TextContentAsync())?.Trim(), testId);
        }

        // One row is driven end-to-end, so the relative href really resolves in the running app.
        var usage = page.Locator("[data-testid='data-display-usage-scenario']");
        await usage.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(200);
        await usage.Locator("a").First.ClickAsync();
        await page.WaitForURLAsync("**/analytics-kit", new PageWaitForURLOptions { Timeout = 20000 });
        await page.Locator("[data-testid='analytics-kit-usage']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });
    }

    [TestMethod]
    [Description("The /forms match-highlight section links to the scenario that assembles the input vocabulary")]
    public async Task Forms_MatchHighlightSection_LinksToTheAnalyticsScenario()
    {
        var page = await OpenAsync(FormsUrl);

        var row = page.Locator("[data-testid='forms-match-highlight-scenario']");
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 20000 });
        await row.ScrollIntoViewIfNeededAsync();
        Assert.AreEqual("analytics-kit", await row.Locator("a").First.GetAttributeAsync("href"));

        await page.WaitForTimeoutAsync(200);
        await row.Locator("a").First.ClickAsync();
        await page.WaitForURLAsync("**/analytics-kit", new PageWaitForURLOptions { Timeout = 20000 });
        await page.Locator("[data-testid='analytics-kit-component-reference']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });
    }

    [TestMethod]
    [Description("Visual sweep of the cross-link rows: light and dark at 1440, plus the phone band where the row must wrap without scrolling sideways")]
    public async Task VisualSweep_CrossLinkRows_LightDarkAndPhone()
    {
        var page = await OpenAsync(AnalyticsKitUrl);

        var reference = page.Locator("[data-testid='analytics-kit-component-reference']");
        await reference.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await ShootElementAsync(reference, "cross-links-analytics-kit-light-1440");

        await ToggleThemeAsync(page);
        await ShootElementAsync(reference, "cross-links-analytics-kit-dark-1440");
        await ToggleThemeAsync(page);

        var back = page.Locator("[data-testid='data-display-usage-scenario']");
        await page.GotoAsync($"{BaseUrl}{DataDisplayUrl}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await back.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 20000 });
        await back.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(200);
        await ShootElementAsync(back, "cross-links-data-display-usage-light-1440");

        // The phone band: the label and the anchors wrap instead of pushing the page sideways.
        var phone = await OpenAsync(AnalyticsKitUrl, width: 375, height: 812, touch: true);
        var phoneRow = phone.Locator("[data-testid='analytics-kit-component-reference']");
        await phoneRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await phoneRow.ScrollIntoViewIfNeededAsync();
        await phone.WaitForTimeoutAsync(200);

        var geometry = await ReadOverflowAsync(phone);
        Assert.IsTrue(geometry.NoHorizontalOverflow, $"the cross-link row must not make a 375px page scroll sideways: {geometry}");
        await ShootElementAsync(phoneRow, "cross-links-analytics-kit-phone-375");
    }

    // ── The modeling editor's full-bleed workspace never scrolls sideways ───────

    [TestMethod]
    [Description("1440×760 full-bleed modeling workspace: the main column fits the document width, so no horizontal scrollbar appears")]
    public async Task ModelingEditor_FullBleed_DoesNotGrowAHorizontalScrollbar_At1440()
    {
        var page = await OpenModelingEditorAsync(1440, 760, touch: false);

        var geometry = await ReadOverflowAsync(page);
        Assert.IsTrue(geometry.HasVerticalScrollbar,
            $"the 1440×760 shot must have a vertical scrollbar, otherwise the 100vw bug cannot show ({geometry})");
        Assert.IsTrue(geometry.NoHorizontalOverflow,
            $"the full-bleed page must not scroll sideways: {geometry}");

        await ShootAsync(page, "modeling-desktop-1440");
    }

    [TestMethod]
    [Description("1024×768 tablet band of the full-bleed modeling workspace: still no horizontal scrollbar, and the tablet dock is reachable")]
    public async Task ModelingEditor_FullBleed_DoesNotGrowAHorizontalScrollbar_At1024Tablet()
    {
        var page = await OpenModelingEditorAsync(1024, 768, touch: true);

        var geometry = await ReadOverflowAsync(page);
        Assert.IsTrue(geometry.NoHorizontalOverflow,
            $"the full-bleed page must not scroll sideways on the tablet band: {geometry}");

        await ShootAsync(page, "modeling-tablet-1024");
    }

    // ── Harness ────────────────────────────────────────────────────────────────

    private async Task<IPage> OpenAsync(string path, int width = 1440, int height = 1000, bool touch = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        await context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); localStorage.removeItem('tm-demo-theme');");

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}{path}");
        return page;
    }

    /// <summary>Switches the demo's own theme (the header toggle), so the whole frame repaints.</summary>
    private static async Task ToggleThemeAsync(IPage page)
    {
        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        await page.WaitForTimeoutAsync(600);
    }

    private static async Task ShootElementAsync(ILocator element, string name)
    {
        var bytes = await element.ScreenshotAsync(new LocatorScreenshotOptions { Timeout = 60_000 });
        Assert.IsTrue(bytes.Length > 2_000, $"{name}: the shot must contain the rendered cross-link row");
        await File.WriteAllBytesAsync(Path.Combine(ShotDir, $"{name}.png"), bytes);
    }

    private async Task<IPage> OpenModelingEditorAsync(int width, int height, bool touch)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        await context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); localStorage.removeItem('tm-demo-theme');");

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}{ModelingEditorUrl}?notation=bpmn&viewpoint=overview");
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await WaitForLayoutToSettleAsync(page, "[data-testid='modeling-preview-panel']");
        return page;
    }

    /// <summary>Measures the document scroller against its CLIENT width (which excludes the scrollbar).</summary>
    private static Task<OverflowGeometry> ReadOverflowAsync(IPage page) =>
        page.EvaluateAsync<OverflowGeometry>(
            """
            () => {
                const doc = document.documentElement;
                const main = document.querySelector("[data-demo-shell='main']");
                const scrollWidth = Math.max(doc.scrollWidth, document.body.scrollWidth);
                const clientWidth = doc.clientWidth;
                return {
                    scrollWidth,
                    clientWidth,
                    innerWidth: window.innerWidth,
                    mainWidth: Math.round(main ? main.getBoundingClientRect().width : -1),
                    hasVerticalScrollbar: window.innerHeight < Math.max(doc.scrollHeight, document.body.scrollHeight),
                    noHorizontalOverflow: scrollWidth <= clientWidth + 1
                };
            }
            """);

    private static async Task ShootAsync(IPage page, string name)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000,
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{name}: the shot must contain the rendered workspace");
        await File.WriteAllBytesAsync(Path.Combine(ShotDir, $"{name}.png"), bytes);
    }

    /// <summary>The document scroller's width readings, so a failure names the measured numbers.</summary>
    public sealed class OverflowGeometry
    {
        /// <summary>The scroll width of the document (the widest painted content).</summary>
        public long ScrollWidth { get; set; }

        /// <summary>The client width of the document element (the viewport minus its scrollbar).</summary>
        public long ClientWidth { get; set; }

        /// <summary>The raw viewport width, which includes the scrollbar.</summary>
        public long InnerWidth { get; set; }

        /// <summary>The width of the demo shell's main column.</summary>
        public long MainWidth { get; set; }

        /// <summary>Whether the page is taller than the viewport (a vertical scrollbar exists).</summary>
        public bool HasVerticalScrollbar { get; set; }

        /// <summary>Whether the document scrolls sideways.</summary>
        public bool NoHorizontalOverflow { get; set; }

        /// <inheritdoc />
        public override string ToString() =>
            $"scrollWidth={ScrollWidth}, clientWidth={ClientWidth}, innerWidth={InnerWidth}, mainWidth={MainWidth}, "
            + $"verticalScrollbar={HasVerticalScrollbar}";
    }
}
