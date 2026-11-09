using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase-9 coverage for the board, calendar and timeline (plan c8d4bcd2, DB-7 + DB-8).
/// Screenshots land in tests/Tempo.Blazor.E2E/__screenshots__/notion-database/phase-9/.
/// </summary>
[TestClass]
public sealed class NotionDatabasePhase9E2ETests : WasmTestBase
{
    private static readonly (int Width, int Height, string Band)[] Bands =
    [
        (1440, 900, "1440"),
        (1024, 768, "1024"),
        (768, 1024, "768"),
        (390, 844, "390"),
    ];

    [TestMethod]
    [Description("Phase 9: a board card moves through the Move-to menu, and a phone lane is about 85% wide")]
    public async Task Board_MoveTo_MovesCard_AndMobileLaneSnaps()
    {
        var page = await OpenAsync();
        var db = page.Locator(".tm-db").First;
        await SwitchAsync(page, db, "Board");
        await Assertions.Expect(db.Locator(".tm-dbb")).ToBeVisibleAsync();

        var card = db.GetByTestId("notion-db-board-card").First;
        var before = await card.GetAttributeAsync("data-from-group");
        var recordId = await card.GetAttributeAsync("data-record-id");
        await card.GetByTestId("notion-db-board-move").Locator(".tm-dropdown-trigger").ClickAsync();
        var target = page.GetByTestId("notion-db-board-move-target")
            .And(page.Locator($":not([data-group-value='{before}'])"))
            .First;
        var next = await target.GetAttributeAsync("data-group-value");
        await target.ClickAsync();
        await Assertions.Expect(db.Locator(
                $".tm-dbb__col[data-group-value='{next}'] [data-testid=notion-db-board-card][data-record-id='{recordId}']"))
            .ToBeVisibleAsync();
        await Assertions.Expect(db.Locator(
                $".tm-dbb__col[data-group-value='{before}'] [data-testid=notion-db-board-card][data-record-id='{recordId}']"))
            .ToHaveCountAsync(0);

        // Crossing into the mobile shell remounts the canvas, which drops back to the table view.
        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForTimeoutAsync(400);
        await SwitchAsync(page, db, "Board");
        var widths = await db.Locator(".tm-dbb").EvaluateAsync<double[]>(
            """
            root => {
                const lane = root.querySelector('.tm-dbb__col');
                const scroll = root.querySelector('.tm-dbb__scroll');
                return [lane.getBoundingClientRect().width, scroll.getBoundingClientRect().width];
            }
            """);
        var ratio = widths[0] / widths[1];
        Assert.IsTrue(ratio is > 0.80 and < 0.90, $"A phone lane should be about 85% of the scroller, was {ratio:P0}.");

        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Phase 9: dragging a timeline bar commits a new left offset")]
    public async Task Timeline_DragBar_Desktop()
    {
        var page = await OpenAsync(hasTouch: false);
        var db = page.Locator(".tm-db").First;
        await SwitchAsync(page, db, "Timeline");
        await Assertions.Expect(db.Locator(".tm-dbtl")).ToBeVisibleAsync();

        // Seed dates sit months before the window, so page back until a bar is on the grid.
        var prev = db.Locator(".tm-dbtl__nav-btn").First;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (await db.Locator(".tm-dbtl__gantt").EvaluateAsync<bool>(
                    """
                    gantt => [...gantt.querySelectorAll('[data-timeline-bar]')].some(bar => {
                        const view = gantt.getBoundingClientRect();
                        const box = bar.getBoundingClientRect();
                        return box.left > view.left + 8 && box.left < view.right - 80 && box.width > 8;
                    })
                    """))
                break;
            await prev.ClickAsync();
            await page.WaitForTimeoutAsync(200);
        }

        var recordId = await db.Locator(".tm-dbtl__gantt").EvaluateAsync<string>(
            """
            gantt => {
                const view = gantt.getBoundingClientRect();
                const bar = [...gantt.querySelectorAll('[data-timeline-bar]')]
                    .find(item => {
                        const box = item.getBoundingClientRect();
                        return box.left > view.left + 8 && box.left < view.right - 80;
                    });
                return bar ? bar.getAttribute('data-record-id') : '';
            }
            """);
        Assert.IsFalse(string.IsNullOrEmpty(recordId), "No timeline bar is inside the visible grid.");
        var bar = db.Locator($".tm-dbtl__gantt [data-record-id='{recordId}']");
        var before = await bar.EvaluateAsync<double>("el => parseFloat(el.style.left) || 0");
        // The grid can sit below the fold on a tall page; a press there lands on nothing.
        var point = await bar.EvaluateAsync<double[]>(
            """
            el => {
                el.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
                const box = el.getBoundingClientRect();
                return [box.left + 10, box.top + box.height / 2];
            }
            """);
        await page.Mouse.MoveAsync((float)point[0], (float)point[1]);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)point[0] + 60, (float)point[1], new MouseMoveOptions { Steps = 8 });
        await page.Mouse.UpAsync();

        await page.WaitForFunctionAsync(
            """
            ([id, before]) => {
                const bar = document.querySelector(`.tm-dbtl__gantt [data-record-id='${id}']`);
                return !!bar && Math.abs((parseFloat(bar.style.left) || 0) - before) > 1;
            }
            """,
            new object[] { recordId, before });
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Phase 9: Alt+ArrowRight moves the focused timeline bar one day")]
    public async Task Timeline_Keyboard_MovesBar()
    {
        var page = await OpenAsync(hasTouch: false);
        var db = page.Locator(".tm-db").First;
        await SwitchAsync(page, db, "Timeline");

        var bar = db.Locator(".tm-dbtl__gantt [data-testid=notion-db-timeline-bar]").First;
        await Assertions.Expect(bar).ToBeVisibleAsync();
        Assert.IsTrue((await bar.GetAttributeAsync("aria-keyshortcuts"))?.Contains("Alt+ArrowRight") == true,
            "The bar must offer Alt+Arrow as the non-drag alternative.");
        var before = await bar.EvaluateAsync<double>("el => parseFloat(el.style.left) || 0");
        var perDay = await db.Locator(".tm-dbtl").EvaluateAsync<double>("root => Number(root.dataset.pxPerDay) || 0");

        await bar.FocusAsync();
        await page.Keyboard.PressAsync("Alt+ArrowRight");

        await Assertions.Expect(bar).ToBeVisibleAsync();
        var after = await bar.EvaluateAsync<double>("el => parseFloat(el.style.left) || 0");
        Assert.AreEqual(perDay, after - before, 0.6, "Alt+ArrowRight must move the bar one day to the right.");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Phase 9: board, calendar and timeline screenshots across bands, themes and empty states")]
    public async Task Surfaces_Screenshots()
    {
        var page = await OpenAsync();
        var db = page.Locator(".tm-db").First;

        await SwitchAsync(page, db, "Board");
        await CaptureAsync(page, db, "board", ".tm-dbb", "Board");
        await page.SetViewportSizeAsync(1440, 900);
        await page.WaitForTimeoutAsync(400);
        await SwitchAsync(page, db, "Board");
        await db.GetByTestId("notion-db-board-card").First
            .GetByTestId("notion-db-board-move").Locator(".tm-dropdown-trigger").ClickAsync();
        await Assertions.Expect(page.GetByTestId("notion-db-board-move-target").First).ToBeVisibleAsync();
        // The menu is a top-layer popover; clipping the first item alone is a few hundred bytes.
        await ShotPageAsync(page, "board-move-to-menu", "[role='menu']:has([data-testid=notion-db-board-move-target])");
        await page.Keyboard.PressAsync("Escape");

        await SwitchAsync(page, db, "Calendar");
        await CaptureAsync(page, db, "calendar", ".tm-dbcal", "Calendar");
        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForTimeoutAsync(400);
        if (await db.Locator(".tm-dbcal").CountAsync() == 0)
            await SwitchAsync(page, db, "Calendar");
        var overflow = db.GetByTestId("notion-db-calendar-overflow").First;
        if (await overflow.CountAsync() > 0)
            await overflow.ClickAsync();
        else
            await db.GetByTestId("notion-db-calendar-cell").First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("notion-db-calendar-day-sheet")).ToBeVisibleAsync();
        await ShotPageAsync(page, "calendar-390-day-sheet", "[data-testid=notion-db-calendar-day-sheet]");
        await page.Keyboard.PressAsync("Escape");

        await page.SetViewportSizeAsync(1440, 900);
        await SwitchAsync(page, db, "Timeline");
        await CaptureAsync(page, db, "timeline", ".tm-dbtl", "Timeline");
        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForTimeoutAsync(400);
        if (await db.Locator(".tm-dbtl").CountAsync() == 0)
            await SwitchAsync(page, db, "Timeline");
        // The bar lives in a nested scroll container. Playwright's click scrolls it and then reports
        // it outside the viewport, so the click never lands. A DOM click opens the sheet directly.
        await db.GetByTestId("notion-db-timeline-bar").First.EvaluateAsync("el => el.click()");
        await Assertions.Expect(page.GetByTestId("notion-db-timeline-date-sheet")).ToBeVisibleAsync();
        await ShotPageAsync(page, "timeline-390-date-sheet", "[data-testid=notion-db-timeline-date-sheet]");
        await page.Keyboard.PressAsync("Escape");
        await page.Context.CloseAsync();

        await CaptureSeedAsync("board-empty-group", "board", "Board", ".tm-dbb", ".tm-dbb__empty");
        await CaptureSeedAsync("no-date-field", "calendar", "Calendar", ".tm-dbcal", ".tm-dbcal__no-field");
        await CaptureSeedAsync("no-date-field", "timeline", "Timeline", ".tm-dbtl", ".tm-dbtl__no-field");
        await CaptureSeedAsync("empty-timeline", "timeline", "Timeline", ".tm-dbtl", ".tm-dbtl__empty-add");
    }

    private async Task CaptureSeedAsync(string seed, string surface, string viewName, string root, string marker)
    {
        var page = await OpenAsync(seed);
        var db = page.Locator(".tm-db").First;
        await SwitchAsync(page, db, viewName);
        await Assertions.Expect(db.Locator(marker).First).ToBeVisibleAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.WaitForTimeoutAsync(400);
        await ShotAsync(page, $"{surface}-{seed}-light", db, root);
        await SetThemeAsync(page, "dark");
        await ShotAsync(page, $"{surface}-{seed}-dark", db, root);
        await page.Context.CloseAsync();
    }

    private async Task<IPage> OpenAsync(string seed = "default", bool hasTouch = true)
    {
        using var http = DemoApiTestHttp.CreateClient();
        (await http.PostAsync("/api/notion/reset", null)).EnsureSuccessStatusCode();
        (await http.PostAsync($"/api/notion/databases/e2e/seed/{seed}", null)).EnsureSuccessStatusCode();

        var context = await CreateContextAsync(hasTouch: hasTouch);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}/notion-editor");
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions { Timeout = 30000 });
        var db = page.Locator(".tm-db").First;
        await db.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await db.ScrollIntoViewIfNeededAsync();
        await page.WaitForSelectorAsync(".tm-db__skeleton", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 10000,
        });
        return page;
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

    private static async Task CaptureAsync(IPage page, ILocator db, string surface, string selector, string viewName)
    {
        foreach (var band in Bands)
        {
            await page.SetViewportSizeAsync(band.Width, band.Height);
            await page.WaitForTimeoutAsync(400);
            if (await db.Locator(selector).CountAsync() == 0)
                await SwitchAsync(page, db, viewName);
            await Assertions.Expect(db.Locator(selector).First).ToBeVisibleAsync();
            await ShotAsync(page, $"{surface}-{band.Band}-light", db, selector);
            await SetThemeAsync(page, "dark");
            await ShotAsync(page, $"{surface}-{band.Band}-dark", db, selector);
            await SetThemeAsync(page, "light");
        }
    }

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

    private static Task ShotPageAsync(IPage page, string name, string selector) =>
        ShotAsync(page, name, page.Locator("body"), selector);

    private static async Task ShotAsync(IPage page, string name, ILocator db, string selector)
    {
        var path = Path.Combine(ShotDir(), name + ".png");
        var frame = await db.EvaluateAsync<float[]>(
            """
            (root, selector) => {
                const start = root.querySelector(selector);
                if (!start) return [0, 0, 1, 1, window.innerWidth, window.innerHeight];
                const scrolling = document.scrollingElement;
                if (scrolling) scrolling.style.scrollBehavior = 'auto';
                start.scrollIntoView({ block: 'start', inline: 'nearest', behavior: 'instant' });
                const box = start.getBoundingClientRect();
                return [Math.max(0, box.x), Math.max(0, box.y), box.right, box.bottom, window.innerWidth, window.innerHeight];
            }
            """,
            selector);
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
        var info = new FileInfo(path);
        Assert.IsTrue(info.Exists && info.Length > 2048, $"{info.Name} is {info.Length} bytes; expected > 2 KB.");
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        Assert.AreEqual(24, stream.Read(header));
        var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        Assert.IsTrue(height > 40, $"{info.Name} is {height}px tall; expected > 40.");
    }

    private static string ShotDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        var shots = Path.Combine(dir!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "notion-database", "phase-9");
        Directory.CreateDirectory(shots);
        return shots;
    }
}
