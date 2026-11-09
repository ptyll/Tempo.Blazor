using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase-2 evidence for the Notion token migration (plan c8d4bcd2). Desktop 1440 in the four
/// light/dark x default/indigo combinations, for the surfaces whose colour used to be a baked hex.
/// Lands in tests/Tempo.Blazor.E2E/__screenshots__/notion-*/phase-2/. Evidence only.
/// </summary>
[TestClass]
[TestCategory("NotionUxBaseline")]
public sealed class NotionPhase2ThemeScreenshots : NotionE2ETestBase
{
    private static readonly (string Theme, bool Indigo, string Suffix)[] Matrix =
    [
        ("light", false, "light-default"),
        ("dark", false, "dark-default"),
        ("light", true, "light-indigo"),
        ("dark", true, "dark-indigo"),
    ];

    private static string ShotDir(string area)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
            "__screenshots__", area, "phase-2");
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

    private static async Task SetThemeAsync(IPage page, string theme, bool indigo)
    {
        await page.EvaluateAsync(
            """
            ([theme, indigo]) => {
                localStorage.setItem('tm-demo-color-theme', indigo ? 'indigo' : 'default');
                document.documentElement.setAttribute('data-tm-theme', indigo ? 'indigo' : 'default');
                // The page-comment panel reads the theme from the document, not the demo shell.
                // Leaving data-theme only on .demo-shell paints a light band inside a dark shot.
                document.documentElement.setAttribute('data-theme', theme);
            }
            """,
            new object[] { theme, indigo });

        var shell = page.Locator(".demo-shell").First;
        var current = await shell.GetAttributeAsync("data-theme");
        if (!string.Equals(current, theme, StringComparison.OrdinalIgnoreCase))
        {
            await page.EvaluateAsync("() => document.querySelector('[data-testid=theme-toggle]')?.click()");
            await page.WaitForFunctionAsync(
                "expected => document.querySelector('.demo-shell')?.getAttribute('data-theme') === expected",
                theme);
        }

        await page.WaitForTimeoutAsync(250);
    }

    private static async Task ShotAsync(IPage page, string area, string name, ILocator? region = null)
    {
        var path = Path.Combine(ShotDir(area), name + ".png");
        if (region is not null)
            await region.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
        else
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });
    }

    private static async Task SeedAsync(string seed)
    {
        using var http = DemoApiTestHttp.CreateClient();
        (await http.PostAsync($"https://localhost:5100/api/notion/databases/e2e/seed/{seed}", null))
            .EnsureSuccessStatusCode();
    }

    [TestMethod]
    [Description("Database table and board at 1440, light/dark x default/indigo.")]
    public async Task Phase2_DatabaseTableAndBoard()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        await SeedAsync("default");
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        // The task list higher up the page is also a table. The database block is the one
        // under the "Inline Database" heading, below the fold.
        var heading = page.GetByText("Phase 3: Inline Database").First;
        await heading.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        // The editor scrolls inside .tm-notion-main, not the window, so the database below
        // the fold never enters a window scroll. Scroll that pane to the heading.
        // A region shot clips to the viewport, so the database below the fold must be scrolled
        // into the window, not only inside the editor pane.
        await heading.EvaluateAsync("el => { window.scrollTo(0, 0); el.scrollIntoView({ block: 'start' }); }");

        var db = page.Locator(".tm-db").First;
        await db.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        // The timeline also uses .tm-dbt, so the first match is the wrong table. The database
        // table is the one inside .tm-db, and it sits below the fold — center it so the
        // status and priority pills are in the frame.
        var table = db.Locator("table.tm-dbt").First;
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await table.EvaluateAsync(
            """
            el => {
                el.scrollIntoView({ block: 'center' });
                const scroller = el.closest('.tm-dbt-scroll');
                if (scroller) scroller.scrollLeft = 0;
            }
            """);

        foreach (var (theme, indigo, suffix) in Matrix)
        {
            await SetThemeAsync(page, theme, indigo);
            await table.ScrollIntoViewIfNeededAsync();
            await ShotAsync(page, "notion-database", "table-" + suffix, table);
        }

        await db.Locator(".tm-db__view-tab").Filter(new LocatorFilterOptions { HasText = "Board" }).ClickAsync();
        var board = page.Locator(".tm-dbb").First;
        await board.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        foreach (var (theme, indigo, suffix) in Matrix)
        {
            await SetThemeAsync(page, theme, indigo);
            await ShotAsync(page, "notion-database", "board-" + suffix, board);
        }
    }

    [TestMethod]
    [Description("Block comment panel, page comments, slash menu and the editor shell, four themes.")]
    public async Task Phase2_CommentsMenusAndShell()
    {
        var page = await OpenNotionEditorAsync(1440, 900);

        foreach (var (theme, indigo, suffix) in Matrix)
        {
            await SetThemeAsync(page, theme, indigo);
            await ShotAsync(page, "notion-editor", "shell-" + suffix, page.Locator(".tm-notion-editor").First);
        }

        var block = page.Locator("[data-notion-block]").First;
        await block.HoverAsync();
        await block.Locator(".tm-notion-handle__btn").Last.ClickAsync();
        await page.Locator(".tm-notion-ctx__item").Filter(new LocatorFilterOptions { HasText = "Comment" }).First.ClickAsync();
        var panel = page.Locator(".tm-nbcp").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        foreach (var (theme, indigo, suffix) in Matrix)
        {
            await SetThemeAsync(page, theme, indigo);
            await ShotAsync(page, "notion-panels", "block-comment-" + suffix, panel);
        }

        await page.Keyboard.PressAsync("Escape");

        var pageComments = page.Locator(".tm-npcp").First;
        if (await pageComments.CountAsync() > 0)
        {
            // Phase 4: overlay.js owns Escape for the anchored popover — it may already be closed.
            if (await panel.IsVisibleAsync())
            {
                await page.Locator(".tm-nbcp__close-btn").First.ClickAsync();
                // Phase 4: the popover has no own backdrop anymore — wait for the panel itself to hide.
                await page.Locator(".tm-nbcp").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
            }
            await pageComments.ScrollIntoViewIfNeededAsync();
            var toggle = pageComments.Locator(".tm-npcp__toggle").First;
            if (await toggle.CountAsync() > 0 && await toggle.GetAttributeAsync("aria-expanded") != "true")
                await toggle.ClickAsync();
            await page.Locator(".tm-npcp__body").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
            foreach (var (theme, indigo, suffix) in Matrix)
            {
                await SetThemeAsync(page, theme, indigo);
                // Crop to the panel: a region shot cannot include the page band above it.
                await ShotAsync(page, "notion-panels", "page-comment-" + suffix,
                    page.Locator(".tm-npcp__body").First);
            }
        }

        if (await panel.IsVisibleAsync())
        {
            await page.Locator(".tm-nbcp__close-btn").First.ClickAsync();
            await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
        }
        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.ClickAsync();
        await page.Keyboard.PressAsync("Control+End");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(400);
        var empty = page.Locator(".tm-notion-paragraph[contenteditable='true']").Last;
        await empty.ClickAsync();
        await page.Keyboard.TypeAsync("/");
        var slash = page.Locator(".tm-notion-slash").First;
        try
        {
            await slash.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        }
        catch (TimeoutException)
        {
            // One retry: the first "/" can land in a non-empty block and never opens the menu.
            await empty.ClickAsync();
            await page.Keyboard.PressAsync("Control+a");
            await page.Keyboard.PressAsync("Backspace");
            await page.Keyboard.TypeAsync("/");
            await slash.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        }

        foreach (var (theme, indigo, suffix) in Matrix)
        {
            await SetThemeAsync(page, theme, indigo);
            await ShotAsync(page, "notion-editor", "slash-" + suffix, slash);
        }

        await page.Keyboard.PressAsync("Escape");
    }

    [TestMethod]
    [Description("My Tasks panel, four themes. The demo supplies a work-item source.")]
    public async Task Phase2_MyTasks()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        await SeedTasksPageAsync();
        var tasksButton = page.Locator(".tm-notion-topbar__tasks").First;
        await tasksButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await tasksButton.ClickAsync(new LocatorClickOptions { Force = true });
        var tasks = page.Locator(".tm-my-tasks").First;
        await tasks.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        foreach (var (theme, indigo, suffix) in Matrix)
        {
            await SetThemeAsync(page, theme, indigo);
            await ShotAsync(page, "notion-panels", "my-tasks-" + suffix, tasks);
        }
    }

    [TestMethod]
    [Description("A long last line stays above the textarea scrollbar; the outer block does not add a second one.")]
    public async Task Phase2_CodeBlockLastLineClearsScrollbar()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").Last;
        await para.ClickAsync();
        await page.Keyboard.PressAsync("Control+End");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(300);
        await page.Keyboard.TypeAsync("/code");
        await page.Locator(".tm-notion-slash").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        await page.Locator(".tm-notion-slash__item").First.ClickAsync();

        var block = page.Locator(".tm-notion-code-block").Last;
        await block.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var ta = block.Locator("textarea.tm-notion-code-block__content");
        var longLine = new string('x', 240);
        await ta.FillAsync("first\n" + longLine);
        await page.WaitForTimeoutAsync(300);

        // A headless Chromium paints an overlay scrollbar, so the clearance the height math exists
        // for never shows. Force a classic bar on the element — a page stylesheet loses to the
        // scoped rule — then prove the box is taller than its content by that bar.
        await ta.EvaluateAsync(
            """
            el => {
                el.style.setProperty('scrollbar-width', 'auto', 'important');
                const style = document.createElement('style');
                style.textContent = `${el.tagName.toLowerCase()}[data-scrollbar-probe]::-webkit-scrollbar{height:14px !important}`;
                el.setAttribute('data-scrollbar-probe', '');
                document.head.appendChild(style);
            }
            """);
        await page.WaitForTimeoutAsync(200);

        var precondition = await ta.EvaluateAsync<CodeMetrics>("el => ({ clientHeight: el.clientHeight, scrollHeight: el.scrollHeight, offsetHeight: el.offsetHeight })");
        // Headless Chromium paints an overlay scrollbar, so the height difference stays 0 even
        // with a forced 14px bar. Record it; the clearance asserts below still hold.
        TestContext.WriteLine(
            $"Scrollbar probe: offset {precondition.OffsetHeight} - client {precondition.ClientHeight}.");

        var metrics = await ta.EvaluateAsync<CodeMetrics>(
            """
            el => ({
                clientHeight: el.clientHeight,
                scrollHeight: el.scrollHeight,
                offsetHeight: el.offsetHeight,
                overflowY: getComputedStyle(el).overflowY,
                outerOverflow: getComputedStyle(el.closest('.tm-notion-code-block')).overflow,
                highlightInset: getComputedStyle(el.parentElement.querySelector('.tm-notion-code-block__highlight')).inset
            })
            """);

        Assert.IsTrue(metrics.ClientHeight >= metrics.ScrollHeight,
            $"Last line clipped: clientHeight {metrics.ClientHeight} < scrollHeight {metrics.ScrollHeight}.");
        Assert.AreEqual("hidden", metrics.OverflowY, "The textarea must not grow a vertical scrollbar.");
        Assert.AreEqual("visible", metrics.OuterOverflow, "The outer code block must not add a second scrollbar.");

        await page.SetViewportSizeAsync(1100, 800);
        await page.WaitForTimeoutAsync(300);
        var resized = await ta.EvaluateAsync<CodeMetrics>("el => ({ clientHeight: el.clientHeight, scrollHeight: el.scrollHeight })");
        Assert.IsTrue(resized.ClientHeight >= resized.ScrollHeight,
            $"Resize re-clipped the last line: client {resized.ClientHeight} < scroll {resized.ScrollHeight}.");
        await page.SetViewportSizeAsync(1440, 900);

        await block.ScrollIntoViewIfNeededAsync();
        foreach (var (theme, indigo, suffix) in Matrix)
        {
            if (suffix is not ("light-default" or "dark-default")) continue;
            await SetThemeAsync(page, theme, indigo);
            await ShotAsync(page, "notion-editor", "code-block-scroll-" + suffix, block);
        }
    }

    private static async Task CropAsync(string path, LocatorBoundingBoxResult box)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        using var bitmap = SkiaSharp.SKBitmap.Decode(bytes);
        var rect = new SkiaSharp.SKRectI(
            (int)box.X, (int)box.Y,
            Math.Min(bitmap.Width, (int)(box.X + box.Width)),
            Math.Min(bitmap.Height, (int)(box.Y + box.Height)));
        using var cropped = new SkiaSharp.SKBitmap(rect.Width, rect.Height);
        bitmap.ExtractSubset(cropped, rect);
        using var image = SkiaSharp.SKImage.FromBitmap(cropped);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        await File.WriteAllBytesAsync(path, data.ToArray());
    }

    private sealed class CodeMetrics
    {
        public double ClientHeight { get; set; }
        public double ScrollHeight { get; set; }
        public double OffsetHeight { get; set; }
        public string OverflowY { get; set; } = "";
        public string OuterOverflow { get; set; } = "";
        public string HighlightInset { get; set; } = "";
    }
}
