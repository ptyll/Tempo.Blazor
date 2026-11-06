using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase-5 functional coverage for the page, page comments and shell panels
/// (plan c8d4bcd2, N2 + P4–P7). Screenshots land in
/// tests/Tempo.Blazor.E2E/__screenshots__/notion-*/phase-5/.
/// </summary>
[TestClass]
public sealed class NotionPhase5PagePanelsE2ETests : WasmTestBase
{
    private static readonly (int Width, int Height, bool Touch, string Band)[] Bands =
    [
        (1440, 900, false, "1440"),
        (1024, 768, true, "1024"),
        (390, 844, true, "390"),
        (320, 700, true, "320"),
    ];

    private async Task<IPage> OpenAsync(int width, int height, bool touch, string query = "")
    {
        using var http = DemoApiTestHttp.CreateClient();
        try { await http.PostAsync("https://localhost:5100/api/notion/reset", null); }
        catch { /* The API may not be up yet; the waits below report that. */ }

        var context = await CreateContextAsync(hasTouch: touch);
        await context.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}/notion-editor{query}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000,
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-page", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000,
        });
        return page;
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

    private static string ShotDir(string area)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
            "__screenshots__", area, "phase-5");
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

    private static async Task ShotAsync(IPage page, string area, string name, ILocator? region = null)
    {
        await page.WaitForTimeoutAsync(200);
        var path = Path.Combine(ShotDir(area), name + ".png");
        if (region is null)
        {
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path });
            return;
        }

        if (await region.CountAsync() > 0 && await region.IsVisibleAsync())
            await region.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Timeout = 8000 });
        else
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path });
    }

    private async Task SeedAsync(IPage page, string methodName)
    {
        // The in-page helper reloads as soon as the POST starts, which aborts the seed.
        using var client = DemoApiTestHttp.CreateClient();
        using var response = await client.PostAsync($"/api/notion/e2e/seed/{methodName}", content: null);
        response.EnsureSuccessStatusCode();
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-notion-page").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    [TestMethod]
    [Description("N2: the opt-in meta row shows last edited and the author; without a comment provider the Comments action is absent.")]
    public async Task PageMeta_ShowsLastEditedAndAuthor()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var meta = page.GetByTestId("notion-page-meta");
        await Assertions.Expect(meta).ToBeVisibleAsync();
        await Assertions.Expect(meta).ToContainTextAsync("Last edited");

        var hidden = await OpenAsync(1440, 900, touch: false, "?disableCommentProvider=true");
        await Assertions.Expect(hidden.GetByTestId("notion-page-meta")).ToBeVisibleAsync();
        Assert.AreEqual(0, await hidden.Locator("[data-action-id='comments']").CountAsync());
    }

    [TestMethod]
    [Description("P4: Dialog presentation opens page comments in a core TmModal, not the inline panel.")]
    public async Task PageComments_DialogPresentation_OpensModal()
    {
        var page = await OpenAsync(1440, 900, touch: false, "?pageComments=dialog");
        await SeedAsync(page, "seedCommentsPage");
        await page.GetByTestId("notion-page-meta-comments").ClickAsync();
        var dialog = page.Locator(".tm-modal-overlay").First;
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(dialog.Locator(".tm-npcp--embedded")).ToBeVisibleAsync();
        Assert.AreEqual(0, await page.Locator(".tm-npcp__toggle").CountAsync(),
            "Embedded mode renders no collapsible toggle.");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(dialog).ToBeHiddenAsync();

        foreach (var (width, height, touch, band) in Bands)
        {
            var shot = await OpenAsync(width, height, touch, width >= 1024 ? "" : "?pageComments=dialog");
            await SeedAsync(shot, "seedCommentsPage");
            if (width >= 1024)
            {
                var inline = shot.Locator(".tm-npcp").First;
                await inline.ScrollIntoViewIfNeededAsync();
                await inline.Locator(".tm-npcp__toggle").ClickAsync();
                await ShotAsync(shot, "notion-page-comments", $"popover-{band}-light", inline);
                await SetThemeAsync(shot, "dark");
                await ShotAsync(shot, "notion-page-comments", $"popover-{band}-dark", inline);
            }
            else
            {
                await shot.GetByTestId("notion-page-meta-comments").ClickAsync();
                var surface = shot.Locator(".tm-modal, .tm-sheet").First;
                await surface.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
                await ShotAsync(shot, "notion-page-comments", $"dialog-{band}-light", surface);
                await AssertSheetFitsViewportAsync(shot, surface);
                await SetThemeAsync(shot, "dark");
                await ShotAsync(shot, "notion-page-comments", $"dialog-{band}-dark", surface);
            }

            await shot.CloseAsync();
        }
    }

    [TestMethod]
    [Description("P5–P7: seeded blog, analytics, audit, tasks and notifications at every band, light and dark.")]
    public async Task ShellPanels_OpenAtEveryBand_AndCapture()
    {
        foreach (var (width, height, touch, band) in Bands)
        {
            // OpenAsync posts /api/notion/reset. Seed after that reset, or the second open
            // wipes the blog, tasks and notifications the screenshots are meant to show.
            var page = await OpenAsync(width, height, touch);
            await SeedAsync(page, "seedPhase5Evidence");
            foreach (var theme in new[] { "light", "dark" })
            {
                await SetThemeAsync(page, theme);
                await page.Locator(".tm-notion-page").First.ScrollIntoViewIfNeededAsync();
                await ShotAsync(page, "notion-page", $"meta-{band}-{theme}",
                    page.Locator(".tm-notion-header-meta").First);

                await Assertions.Expect(page.Locator("[data-testid='notion-notification-toggle']").First)
                    .ToBeVisibleAsync();
                await page.WaitForTimeoutAsync(3000);
                await page.Locator("[data-testid='notion-notification-toggle']").First.ClickAsync();
                var note = width >= 1024
                    ? page.Locator(".tm-notion-notifications__panel").First
                    : page.Locator(".tm-overlay-panel-sheet, .tm-notion-notifications__panel").First;
                await note.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
                await Assertions.Expect(note.Locator(".tm-notion-notifications__body").First).ToBeVisibleAsync();
                if (width < 1024)
                {
                    await Assertions.Expect(note.Locator(".tm-notification-bell__title")).ToBeVisibleAsync();
                    await AssertSheetFitsViewportAsync(page, note);
                }
                await ShotAsync(page, "notion-notifications", $"center-{band}-{theme}", note);
                await page.Keyboard.PressAsync("Escape");
                await Assertions.Expect(note).ToBeHiddenAsync();

                await OpenCommandAsync(page, "notion-blog-open");
                var blog = page.GetByTestId("notion-blog-panel");
                await Assertions.Expect(blog).ToBeVisibleAsync();
                var post = blog.GetByTestId("notion-blog-post-item").First;
                // The perex lives on the list row. A compact panel (390/320) swaps the list for the
                // detail once a post is opened, so check it while the list is on screen.
                await Assertions.Expect(blog.Locator(".tm-notion-blog__post-excerpt").First).ToBeVisibleAsync();
                await post.ClickAsync();
                await Assertions.Expect(post).ToHaveAttributeAsync("aria-current", "true");
                await Assertions.Expect(blog.Locator(".tm-notion-blog-post__author").First).ToBeVisibleAsync();
                if (width == 390)
                {
                    await ShotAsync(page, "notion-blog", $"detail-{band}-{theme}",
                        page.Locator(".tm-notion-blog-panel, .tm-side-panel").Last);
                    await blog.GetByTestId("notion-blog-back").ClickAsync();
                    await ShotAsync(page, "notion-blog", $"list-{band}-{theme}",
                        page.Locator(".tm-notion-blog-panel, .tm-side-panel").Last);
                }
                await ShotAsync(page, "notion-blog", $"panel-{band}-{theme}",
                    page.Locator(".tm-notion-blog-panel, .tm-side-panel").Last);
                await page.Keyboard.PressAsync("Escape");
                await Assertions.Expect(blog).ToBeHiddenAsync();

                await OpenCommandAsync(page, "notion-analytics-open");
                var analytics = page.GetByTestId("notion-analytics-panel");
                await Assertions.Expect(analytics).ToBeVisibleAsync();
                await ShotAsync(page, "notion-analytics", $"panel-{band}-{theme}",
                    page.Locator(".tm-notion-analytics-panel, .tm-side-panel").Last);
                await page.Keyboard.PressAsync("Escape");
                await Assertions.Expect(analytics).ToBeHiddenAsync();

                await OpenCommandAsync(page, "notion-audit-open");
                var audit = page.GetByTestId("notion-audit-panel");
                await Assertions.Expect(audit).ToBeVisibleAsync();
                await ShotAsync(page, "notion-audit", $"panel-{band}-{theme}",
                    page.Locator(".tm-notion-audit-panel, .tm-side-panel").Last);
                await page.Keyboard.PressAsync("Escape");
                await Assertions.Expect(audit).ToBeHiddenAsync();

                await OpenCommandAsync(page, "notion-tasks");
                var tasks = page.Locator(".tm-notion-tasks-panel, .tm-overlay-panel-sheet, .tm-side-panel-sheet").Last;
                await tasks.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
                if (width < 640)
                {
                    var filters = page.GetByTestId("notion-tasks-filters-open");
                    if (await filters.IsVisibleAsync())
                    {
                        await filters.ClickAsync();
                        var sheet = page.Locator(".tm-overlay-panel-sheet, .tm-sheet").Last;
                        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
                        await AssertSheetFitsViewportAsync(page, sheet);
                        await ShotAsync(page, "notion-tasks", $"filters-{band}-{theme}", sheet);
                        await page.Keyboard.PressAsync("Escape");
                    }
                }
                await ShotAsync(page, "notion-tasks", $"panel-{band}-{theme}", tasks);
                await page.Keyboard.PressAsync("Escape");
                await Assertions.Expect(tasks).ToBeHiddenAsync();
            }

            await page.CloseAsync();
        }
    }

    [TestMethod]
    [Description("P7: the notification center lists a body line and has no footer without OnViewAll.")]
    public async Task Notifications_BodyLine_NoFooterWithoutViewAll()
    {
        // OpenAsync posts /api/notion/reset, which clears the notification store. Seed AFTER that
        // reset and through the page's own API base, so the panel and the seed hit the same host.
        var page = await OpenAsync(1440, 900, touch: false);
        var seeded = await page.EvaluateAsync<string>(
            """
            async () => {
                const base = window.tmNotionDemoApiBase;
                const seeded = await fetch(`${base}/api/notion/e2e/seed/seedPhase5Evidence`, { method: 'POST' });
                if (!seeded.ok) return `seed failed ${seeded.status}`;
                const response = await fetch(`${base}/api/notion/notifications/users/demo?take=5`);
                const items = await response.json();
                const hits = items.filter(item => item.body && item.body.includes('can you confirm the date')).length;
                return hits > 0 ? 'ok' : `seeded item missing (status ${response.status}, count ${items.length})`;
            }
            """);
        Assert.AreEqual("ok", seeded, "The phase-5 seed must land a notification with a body before the panel opens.");
        var toggle = page.Locator("[data-testid='notion-notification-toggle']").First;
        await toggle.ClickAsync();
        var panel = page.Locator(".tm-notion-notifications__panel").First;
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await Assertions.Expect(panel.Locator(".tm-notion-notifications__body").First)
            .ToContainTextAsync("can you confirm the date");
        Assert.AreEqual(0, await panel.Locator(".tm-notion-notifications__view-all").CountAsync(),
            "The demo wires no OnViewAll, so the footer stays absent.");
        await ShotAsync(page, "notion-notifications", "center-1440-light", panel);
    }

    [TestMethod]
    [Description("N2: an aggregate concurrency conflict renders the inline banner at 1440 and 390.")]
    public async Task AggregateConflict_Banner_CapturedAt1440And390()
    {
        foreach (var (width, height, touch, band) in new[] { (1440, 900, false, "1440"), (390, 844, true, "390") })
        {
            var page = await OpenAsync(width, height, touch);
            await page.EvaluateAsync(
                """
                async () => {
                    await fetch('https://localhost:5100/api/notion/aggregate/e2e/advance-token/11111111-1111-1111-1111-111111111111', { method: 'POST' });
                }
                """);
            var block = page.Locator("[data-page-blocks] [contenteditable='true']").First;
            await block.ClickAsync();
            await page.Keyboard.PressAsync("End");
            await page.Keyboard.TypeAsync("x");
            // Blur commits the paragraph; Escape alone leaves the aggregate save unsent.
            await page.EvaluateAsync("() => document.activeElement?.blur()");
            await page.WaitForTimeoutAsync(1500);
            var banner = page.GetByTestId("notion-page-conflict");
            await banner.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await ShotAsync(page, "notion-page", $"conflict-{band}-light", banner);
            await SetThemeAsync(page, "dark");
            await ShotAsync(page, "notion-page", $"conflict-{band}-dark", banner);
            await page.CloseAsync();
        }
    }

    /// <summary>A sheet at 320 must fit the viewport: panel height at most the viewport, no horizontal overflow.</summary>
    private static async Task AssertSheetFitsViewportAsync(IPage page, ILocator sheet)
    {
        var fits = await sheet.EvaluateAsync<bool>(
            """
            el => {
                const panel = el.closest('.tm-drawer__panel, .tm-sheet__panel') ?? el;
                const box = panel.getBoundingClientRect();
                return box.height <= window.innerHeight + 1
                    && box.right <= window.innerWidth + 1
                    && document.documentElement.scrollWidth <= window.innerWidth + 1;
            }
            """);
        Assert.IsTrue(fits, "The sheet panel must fit the viewport without horizontal overflow.");
    }

    private static async Task OpenCommandAsync(IPage page, string testId)
    {
        var onBar = page.Locator(
            $".tm-notion-topbar [data-testid='{testId}']:not(.tm-toolbar-item--collapsed)").First;
        if (await onBar.CountAsync() > 0 && await onBar.IsVisibleAsync())
        {
            await onBar.ClickAsync();
            return;
        }

        var bar = page.Locator("[data-action-id='more'], .tm-notion-topbar .tm-toolbar-more").First;
        await bar.ClickAsync(new LocatorClickOptions { Force = true });
        // Visible entries only: a folded toolbar button keeps its data-testid (hidden, inert) and
        // sits earlier in the DOM, so an unfiltered union's First resolved to it, not the menu row.
        var entry = page.Locator(
            $"[data-menu-testid='{testId}']:visible, .tm-toolbar-more__menu [data-testid='{testId}']:visible, [data-action-id] [data-testid='{testId}']:visible").First;
        await entry.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 8000,
        });
        await entry.ClickAsync();
    }
}
