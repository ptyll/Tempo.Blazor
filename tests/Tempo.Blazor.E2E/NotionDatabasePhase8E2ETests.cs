using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase-8 screenshot coverage for the database toolbar, table, list and gallery
/// (plan c8d4bcd2, DB-2 + DB-5 + DB-6). One test per surface. Screenshots land in
/// tests/Tempo.Blazor.E2E/__screenshots__/notion-database/phase-8/.
/// </summary>
[TestClass]
public sealed class NotionDatabasePhase8E2ETests : WasmTestBase
{
    private static readonly (int Width, int Height, bool Touch, string Band)[] Bands =
    [
        (1440, 900, false, "1440"),
        (1024, 768, true, "1024"),
        (390, 844, true, "390"),
        (320, 700, true, "320"),
    ];

    [TestMethod]
    [Description("Phase 8: the toolbar never overflows, search filters loaded rows, and a phone reaches every action")]
    public async Task Toolbar_DoesNotOverflow_AndSearchFiltersRows()
    {
        var page = await OpenAsync(1440, 900, false);
        var db = page.Locator(".tm-db").First;
        await db.WaitForAsync(Visible(15000));
        await page.WaitForSelectorAsync(".tm-db__skeleton", Hidden());

        var search = db.GetByTestId("notion-db-search");
        await search.FillAsync("authentication");
        await page.WaitForTimeoutAsync(400);
        Assert.AreEqual(1, await db.Locator(".tm-dbt__row").CountAsync(), "Search should keep only the matching row.");
        await search.FillAsync("");
        await page.WaitForTimeoutAsync(300);

        foreach (var band in Bands)
        {
            await page.SetViewportSizeAsync(band.Width, band.Height);
            await page.WaitForTimeoutAsync(400);
            await Assertions.Expect(db.Locator(".tm-toolbar-more, [data-tool='filter']").First).ToBeVisibleAsync();
            if (band.Width <= 390)
                await Assertions.Expect(db.GetByTestId("notion-db-view-switch")).ToBeVisibleAsync();
            await AssertNoOverflowAsync(db, ".tm-db__header");
            await AssertNoOverflowAsync(db, ".tm-db__toolbar");
            await ShotToolbarAsync(page, $"toolbar-{band.Band}-light", db);
            await SetThemeAsync(page, "dark");
            await ShotToolbarAsync(page, $"toolbar-{band.Band}-dark", db);
            await SetThemeAsync(page, "light");
        }

        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Phase 8: the table stays readable, and the first column survives a horizontal scroll on a phone")]
    public async Task Table_StaysReadable_AndFirstColumnSticks()
    {
        var page = await OpenAsync(1440, 900, false);
        var db = page.Locator(".tm-db").First;
        await db.WaitForAsync(Visible(15000));
        await page.WaitForSelectorAsync(".tm-db__skeleton", Hidden());
        await CaptureViewAsync(page, db, "table", ".tm-dbt");

        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForTimeoutAsync(400);
        var scroller = db.Locator(".tm-dbt-scroll");
        var header = db.Locator(".tm-dbt__th").Nth(1);
        var before = await header.BoundingBoxAsync();
        await scroller.EvaluateAsync("el => { el.scrollLeft = el.scrollWidth; }");
        await page.WaitForTimeoutAsync(200);
        var after = await header.BoundingBoxAsync();
        var wrap = await scroller.BoundingBoxAsync();
        Assert.IsNotNull(before);
        Assert.IsNotNull(after);
        Assert.IsNotNull(wrap);
        Assert.IsTrue(after!.X + 0.5 >= wrap!.X, $"The first column ({after.X}) scrolled left of the wrap ({wrap.X}).");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Phase 8: the list view reads as chips at every band")]
    public async Task List_RendersChips_AcrossBands()
    {
        var page = await OpenAsync(1440, 900, false);
        var db = page.Locator(".tm-db").First;
        await db.WaitForAsync(Visible(15000));
        await page.WaitForSelectorAsync(".tm-db__skeleton", Hidden());
        await SwitchAsync(page, db, "List");
        await Assertions.Expect(db.Locator(".tm-dblv")).ToBeVisibleAsync();
        await CaptureViewAsync(page, db, "list", ".tm-dblv");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Phase 8: the gallery reads as cards, one column on a phone")]
    public async Task Gallery_RendersCards_AcrossBands()
    {
        var page = await OpenAsync(1440, 900, false);
        var db = page.Locator(".tm-db").First;
        await db.WaitForAsync(Visible(15000));
        await page.WaitForSelectorAsync(".tm-db__skeleton", Hidden());
        await SwitchAsync(page, db, "Gallery");
        await Assertions.Expect(db.Locator(".tm-dbg")).ToBeVisibleAsync();
        await CaptureViewAsync(page, db, "gallery", ".tm-dbg");
        await page.Context.CloseAsync();
    }

    private static async Task SwitchAsync(IPage page, ILocator db, string viewName)
    {
        var viewSwitch = db.GetByTestId("notion-db-view-switch");
        if (await viewSwitch.CountAsync() > 0 && await viewSwitch.IsVisibleAsync())
        {
            await viewSwitch.Locator(".tm-dropdown-trigger").ClickAsync();
            await page.Locator("[role='menuitem']").Filter(new LocatorFilterOptions { HasText = viewName }).First.ClickAsync();
            await page.WaitForTimeoutAsync(600);
            return;
        }
        var tab = db.Locator(".tm-db__view-tab").Filter(new LocatorFilterOptions { HasText = viewName });
        if (await tab.CountAsync() > 0 && await tab.First.IsVisibleAsync())
            await tab.First.ClickAsync();
        else
        {
            await db.GetByTestId("notion-db-more-views").Locator(".tm-dropdown-trigger").ClickAsync();
            await page.Locator("[role='menuitem']").Filter(new LocatorFilterOptions { HasText = viewName }).First.ClickAsync();
        }
        await page.WaitForTimeoutAsync(600);
    }

    private async Task<IPage> OpenAsync(int width, int height, bool touch)
    {
        using var http = DemoApiTestHttp.CreateClient();
        (await http.PostAsync("/api/notion/reset", null)).EnsureSuccessStatusCode();
        (await http.PostAsync("/api/notion/databases/e2e/seed/default", null)).EnsureSuccessStatusCode();

        var context = await CreateContextAsync(touch);
        await context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}/notion-editor");
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions { Timeout = 30000 });
        return page;
    }

    private static async Task CaptureViewAsync(IPage page, ILocator db, string surface, string selector)
    {
        foreach (var band in Bands)
        {
            await page.SetViewportSizeAsync(band.Width, band.Height);
            await page.WaitForTimeoutAsync(400);
            if (await db.Locator(selector).CountAsync() == 0)
                await SwitchAsync(page, db, surface == "list" ? "List" : "Gallery");
            await Assertions.Expect(db.Locator(selector)).ToBeVisibleAsync();
            await ShotSurfaceAsync(page, $"{surface}-{band.Band}-light", db, surface);
            await SetThemeAsync(page, "dark");
            await ShotSurfaceAsync(page, $"{surface}-{band.Band}-dark", db, surface);
            await SetThemeAsync(page, "light");
        }
    }

    private static PageWaitForSelectorOptions Hidden() => new()
    {
        State = WaitForSelectorState.Hidden,
        Timeout = 10000,
    };

    private static LocatorWaitForOptions Visible(int timeout) => new() { Timeout = timeout };

    private static async Task SetThemeAsync(IPage page, string theme)
    {
        await page.EvaluateAsync(
            """
            theme => {
                document.documentElement.setAttribute('data-theme', theme);
                const shell = document.querySelector('.demo-shell');
                if (shell) shell.setAttribute('data-theme', theme);
            }
            """,
            theme);
        await page.WaitForTimeoutAsync(150);
    }

    private static string ShotDir()
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
            "__screenshots__", "notion-database", "phase-8");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return dir ?? throw new DirectoryNotFoundException("TempoBlazor.slnx");
    }

    /// <summary>Toolbar evidence: the header (search + view tabs included), clipped from the header's own top.</summary>
    private static Task ShotToolbarAsync(IPage page, string name, ILocator db) =>
        ClipRegionAsync(page, name, db, ".tm-db__header", ".tm-db__view-tabs, .tm-db__search");

    /// <summary>View evidence. Table shots scroll Status/Priority into the 1440 and 390 frames.</summary>
    private static async Task ShotSurfaceAsync(IPage page, string name, ILocator db, string surface)
    {
        await page.WaitForTimeoutAsync(200);
        if (surface == "table")
        {
            var width = page.ViewportSize?.Width ?? 0;
            if (width is 1440 or 390)
                await RevealPillColumnsAsync(db);
            await ClipRegionAsync(page, name, db, ".tm-dbt-wrap", ".tm-dbt-wrap");
            return;
        }

        var selector = surface == "list" ? ".tm-dblv" : ".tm-dbg";
        await ClipRegionAsync(page, name, db, selector, selector);
    }

    private static async Task RevealPillColumnsAsync(ILocator db)
    {
        await db.Locator(".tm-dbt-scroll").EvaluateAsync(
            """
            el => {
                const headers = [...el.querySelectorAll('.tm-dbt__th-name')];
                const status = headers.find(n => n.textContent.trim() === 'Status');
                const priority = headers.find(n => n.textContent.trim() === 'Priority');
                const target = priority ?? status;
                if (!target) return;
                const cell = target.closest('th');
                const end = cell.offsetLeft + cell.offsetWidth;
                el.scrollLeft = Math.max(0, end - el.clientWidth + 8);
            }
            """);
    }

    /// <summary>
    /// Scrolls the start of the region to the viewport top, then clips from that box downward so a tall
    /// surface is never reduced to the sliver that happened to sit inside the previous viewport.
    /// </summary>
    private static async Task ClipRegionAsync(IPage page, string name, ILocator db, string startSelector, string endSelector)
    {
        var path = Path.Combine(ShotDir(), name + ".png");
        var frame = await db.EvaluateAsync<float[]>(
            """
            (root, selectors) => {
                const start = root.querySelector(selectors.start);
                if (!start) return [0, 0, 1, 1, window.innerWidth, window.innerHeight];
                // The demo page sets scroll-behavior:smooth, so a bare scrollIntoView is still
                // animating when the clip is taken and the shot is a blank sliver.
                const scrolling = document.scrollingElement;
                if (scrolling) scrolling.style.scrollBehavior = 'auto';
                start.scrollIntoView({ block: 'start', inline: 'nearest', behavior: 'instant' });
                const box = start.getBoundingClientRect();
                const ends = [...root.querySelectorAll(selectors.end)];
                const end = ends.length ? ends[ends.length - 1].getBoundingClientRect() : box;
                return [
                    Math.max(0, box.x), Math.max(0, box.y),
                    Math.max(box.right, end.right), Math.max(box.bottom, end.bottom),
                    window.innerWidth, window.innerHeight,
                ];
            }
            """,
            new { start = startSelector, end = endSelector });

        var x = Math.Clamp(frame[0], 0, frame[4] - 1);
        var y = Math.Clamp(frame[1], 0, frame[5] - 1);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Timeout = 8000,
            Clip = new Clip
            {
                X = x,
                Y = y,
                Width = Math.Clamp(frame[2] - x, 1, frame[4] - x),
                Height = (float)Math.Clamp(Math.Max(frame[3] - y, 41), 1, frame[5] - y),
            },
        });
        AssertShot(path);
    }

    private static async Task AssertNoOverflowAsync(ILocator db, string selector)
    {
        var size = await db.Locator(selector).EvaluateAsync<int[]>(
            "el => [el.scrollWidth, el.clientWidth]");
        Assert.IsTrue(size[0] <= size[1] + 1,
            $"{selector} overflows: scrollWidth {size[0]} > clientWidth {size[1]}.");
    }

    private static void AssertShot(string path)
    {
        var info = new FileInfo(path);
        Assert.IsTrue(info.Exists && info.Length > 2048, $"{info.Name} is {info.Length} bytes; expected > 2 KB.");
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        Assert.AreEqual(24, stream.Read(header), $"{info.Name} is shorter than a PNG header.");
        var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        Assert.IsTrue(height > 40, $"{info.Name} is {height}px tall; expected > 40.");
    }

}
