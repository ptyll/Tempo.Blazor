using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase-3 review batch B — the responsive editor shell (plan c8d4bcd2, N0). The editor classifies
/// desktop / tablet / mobile from its OWN container: docked sidebar + full topbar on desktop, an
/// overlay drawer + "More" overflow on tablet, a drawer + mobile action bar on phone. Touch targets
/// reach 44px on a coarse pointer, Escape closes the drawer / overlay and returns focus to the
/// trigger, and a narrow host container drives the layout on a desktop viewport.
/// </summary>
[TestClass]
[TestCategory("Smoke")]
public sealed class NotionResponsiveShellE2ETests : WasmTestBase
{
    private async Task<IPage> OpenShellAsync(int width, int height, bool touch = false, string query = "")
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
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000,
        });
        return page;
    }

    private static async Task<bool> HasClassAsync(ILocator locator, string className) =>
        await locator.EvaluateAsync<bool>(
            "(el, name) => el.classList.contains(name)", className);

    /// <summary>The editor only carries band modifiers for tablet/mobile — desktop is their absence.</summary>
    private static async Task<bool> IsDesktopAsync(ILocator editor) =>
        !await HasClassAsync(editor, "tm-notion-editor--tablet") &&
        !await HasClassAsync(editor, "tm-notion-editor--mobile");

    // ── Desktop ─────────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Desktop: docked sidebar, full topbar, no drawer chrome.")]
    public async Task Shell_Desktop_DocksSidebar()
    {
        // The demo chrome consumes ~280px: 1600 leaves the editor container ≥ 1024 (desktop band).
        var page = await OpenShellAsync(1600, 900);

        var editor = page.Locator(".tm-notion-editor").First;
        Assert.IsTrue(await IsDesktopAsync(editor),
            "the editor classifies desktop from its own container at 1600");

        var sidebar = page.Locator(".tm-notion-sidebar").First;
        Assert.IsTrue(await sidebar.IsVisibleAsync(), "the sidebar is docked and visible on desktop");

        var topbar = page.Locator(".tm-notion-topbar").First;
        Assert.IsTrue(await topbar.IsVisibleAsync(), "the topbar renders");

        var more = page.Locator(".tm-notion-topbar .tm-toolbar-more").First;
        Assert.AreEqual(0, await more.CountAsync(), "a wide desktop bar leaves nothing to More");
    }

    [TestMethod]
    [Description("1440: still the desktop band — sidebar docked, topbar tools on the bar.")]
    public async Task Shell_1440_StaysDesktop()
    {
        var page = await OpenShellAsync(1440, 900);

        var editor = page.Locator(".tm-notion-editor").First;
        Assert.IsTrue(await IsDesktopAsync(editor),
            "1440 minus the demo chrome still leaves ≥1024px of editor container");
        Assert.IsTrue(await page.Locator(".tm-notion-sidebar").First.IsVisibleAsync(),
            "the sidebar stays docked at 1440");
    }

    // ── Tablet ──────────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("1024 touch: tablet band — drawer chrome, More overflow, 44px touch targets.")]
    public async Task Shell_1024Touch_TabletBandWithTouchTargets()
    {
        var page = await OpenShellAsync(1024, 768, touch: true);

        var editor = page.Locator(".tm-notion-editor").First;
        Assert.IsTrue(await HasClassAsync(editor, "tm-notion-editor--tablet"),
            "at 1024 the editor container is below the 1024 desktop breakpoint");

        var sidebar = page.Locator(".tm-notion-sidebar").First;
        Assert.IsFalse(await sidebar.IsVisibleAsync(), "the tablet drawer starts closed");

        var more = page.Locator(".tm-notion-topbar .tm-toolbar-more").First;
        Assert.IsTrue(await more.IsVisibleAsync(), "the tablet bar overflows into a More menu");

        // Coarse-pointer targets: the pinned sidebar toggle and the More trigger reach 44px.
        var toggleBox = await page.Locator(".tm-notion-sidebar-toggle").First.BoundingBoxAsync();
        Assert.IsNotNull(toggleBox);
        Assert.IsTrue(toggleBox!.Height >= 44, $"sidebar toggle touch target (got {toggleBox.Height:0.#})");
        var moreBox = await more.BoundingBoxAsync();
        Assert.IsNotNull(moreBox);
        Assert.IsTrue(moreBox!.Height >= 44, $"More trigger touch target (got {moreBox.Height:0.#})");
    }

    [TestMethod]
    [Description("1280: the demo chrome leaves <1024px of editor container — tablet (drawer + More).")]
    public async Task Shell_1280_Tablet_DrawerAndMore()
    {
        var page = await OpenShellAsync(1280, 900);

        var editor = page.Locator(".tm-notion-editor").First;
        Assert.IsTrue(await HasClassAsync(editor, "tm-notion-editor--tablet"),
            "1280 minus the ~296px demo chrome is the tablet band");
        Assert.IsFalse(await page.Locator(".tm-notion-sidebar").First.IsVisibleAsync(),
            "the page tree is a closed drawer");
        Assert.IsTrue(await page.Locator(".tm-notion-topbar .tm-toolbar-more").First.IsVisibleAsync(),
            "Secondary tools overflow into More");
    }

    // ── Mobile ──────────────────────────────────────────────────────────────────

    [DataTestMethod]
    [DataRow(390, 844)]
    [DataRow(320, 700)]
    [Description("390/320: mobile band, no horizontal document overflow.")]
    public async Task Shell_Mobile_NoHorizontalOverflow(int width, int height)
    {
        var page = await OpenShellAsync(width, height, touch: true);

        var editor = page.Locator(".tm-notion-editor").First;
        Assert.IsTrue(await HasClassAsync(editor, "tm-notion-editor--mobile"),
            $"{width}px is the mobile band");

        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.IsTrue(overflow <= 0,
            $"the document must not overflow horizontally at {width}px (scrollWidth-innerWidth={overflow})");
    }

    [TestMethod]
    [Description("Live resize 1440→800: the shell crosses into the tablet band without a reload.")]
    public async Task Shell_LiveResize_DesktopToTablet()
    {
        var page = await OpenShellAsync(1440, 900);
        var editor = page.Locator(".tm-notion-editor").First;
        Assert.IsTrue(await IsDesktopAsync(editor), "precondition: desktop");

        await page.SetViewportSizeAsync(800, 900);
        await page.WaitForTimeoutAsync(400);

        Assert.IsTrue(await HasClassAsync(editor, "tm-notion-editor--tablet"),
            "the shell follows the live resize into the tablet band");
        Assert.IsFalse(await page.Locator(".tm-notion-sidebar").First.IsVisibleAsync(),
            "the sidebar became a closed drawer");
        Assert.IsTrue(await page.Locator(".tm-notion-topbar").First.IsVisibleAsync(),
            "the topbar survives the resize");
    }

    [TestMethod]
    [Description("Narrow-container host: a 500px stage at a 1440 viewport classifies mobile.")]
    public async Task Shell_NarrowContainerHost_ClassifiesFromContainer()
    {
        var page = await OpenShellAsync(1440, 900, query: "?stage=500");

        var editor = page.Locator(".tm-notion-editor").First;
        Assert.IsTrue(await HasClassAsync(editor, "tm-notion-editor--mobile"),
            "the editor reads its own container, not the viewport: a 500px host is mobile at 1440");
        Assert.IsFalse(await page.Locator(".tm-notion-sidebar").First.IsVisibleAsync(),
            "the page tree is the mobile drawer (closed)");
    }

    [TestMethod]
    [Description("Public page at 320: the page H1 wraps inside its container (no clipped overflow).")]
    public async Task Shell_PublicPage320_TitleWraps()
    {
        var page = await OpenShellAsync(1440, 900);
        await page.WaitForFunctionAsync(
            "m => window.tmNotionDemo && typeof window.tmNotionDemo[m] === 'function'",
            "seedPublicSharePage",
            new PageWaitForFunctionOptions { Timeout = 60000 });
        await page.EvaluateAsync("m => window.tmNotionDemo[m]()", "seedPublicSharePage");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000,
        });

        await page.Locator(".tm-npsm-trigger").First.ClickAsync();
        await page.Locator(".tm-npsm").GetByTestId("notion-share-open").ClickAsync();
        var dialog = page.GetByTestId("notion-share-dialog");
        await dialog.GetByTestId("notion-share-create").ClickAsync();
        var publicUrl = await dialog.GetByTestId("notion-share-url").InputValueAsync();
        await page.Keyboard.PressAsync("Escape");

        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture', 'en');");
        var publicPage = await context.NewPageAsync();
        await publicPage.SetViewportSizeAsync(320, 700);
        await publicPage.GotoAsync(publicUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000,
        });
        await WaitForAppReadyAsync(publicPage);
        await publicPage.GetByTestId("notion-public-page").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });

        var title = publicPage.Locator(".tm-notion-header-title").First;
        await title.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
        // The title box itself must stay inside the viewport — an ancestor with overflow-x hidden
        // can clip a wider box without the document ever overflowing.
        var right = await title.EvaluateAsync<double>(
            "el => el.getBoundingClientRect().right");
        var viewport = await publicPage.EvaluateAsync<int>("() => document.documentElement.clientWidth");
        Assert.IsTrue(right <= viewport + 1,
            $"the public page H1 must wrap inside the viewport (right={right:0.#}, viewport={viewport})");
    }

    // ── Escape / focus return ───────────────────────────────────────────────────

    [TestMethod]
    [Description("Escape closes the drawer and returns focus to the toggle; same for the More overlay.")]
    public async Task Shell_Escape_ClosesDrawerAndOverlay_FocusReturnsToTrigger()
    {
        // Tablet band: the page tree is the shell's overlay drawer — opening it moves focus inside
        // (the overlay close button), so Escape owns the key and the focus scope hands it back.
        var page = await OpenShellAsync(1024, 768, touch: true);

        // Drawer: toggle opens it, Escape closes it, focus lands back on the toggle.
        var toggle = page.Locator(".tm-notion-sidebar-toggle").First;
        await toggle.ClickAsync();
        var overlay = page.GetByTestId("editor-shell-left-overlay");
        await overlay.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000,
        });
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual(0, await page.GetByTestId("editor-shell-left-overlay").CountAsync(),
            "Escape closes the drawer");
        var focusOnToggle = await page.EvaluateAsync<bool>(
            "() => document.activeElement?.classList.contains('tm-notion-sidebar-toggle') === true");
        Assert.IsTrue(focusOnToggle, "focus returns to the drawer trigger");

        // Overlay: the More menu opens, Escape closes it, focus lands back on More.
        var more = page.Locator(".tm-notion-topbar .tm-toolbar-more").First;
        await more.ClickAsync(new LocatorClickOptions { Force = true });
        var menu = page.Locator(".tm-toolbar-more__menu").First;
        await menu.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000,
        });
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual(0, await page.Locator(".tm-toolbar-more__menu:visible").CountAsync(),
            "Escape closes the More menu");
        var focusOnMore = await page.EvaluateAsync<bool>(
            "() => document.activeElement?.classList.contains('tm-toolbar-more') === true");
        Assert.IsTrue(focusOnMore, "focus returns to the More trigger");
    }
}
