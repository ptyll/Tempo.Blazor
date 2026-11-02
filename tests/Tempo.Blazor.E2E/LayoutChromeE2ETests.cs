using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F1 lane: the additive TmSidebar/TmTopBar capabilities on the REAL demo page (<c>/layout</c>).
/// Named sections (heading labels the group's list), <c>aria-current</c> resolved from the location,
/// the icon-only rail's aria-label + tooltip, the top bar's navigation disclosure button and its
/// account menu (APG menu, Escape restores focus), the command palette chord, and the 44px coarse
/// targets. Assertions read COMPUTED values and real geometry, never markup alone; the screenshots
/// land in <c>TestResults/layout-chrome</c> for the visual review.
/// </summary>
[TestClass]
public class LayoutChromeE2ETests : WasmTestBase
{
    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "layout-chrome");

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

    private async Task<IPage> OpenAsync(string route, int width, int height, bool touch, bool dark = false)
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
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{route}");
        await WaitForAppReadyAsync(page);
        await page.WaitForTimeoutAsync(400);

        if (dark)
        {
            await page.Locator("button[title*='dark' i]:visible").First.ClickAsync();
            await page.WaitForSelectorAsync("[data-theme='dark']", new PageWaitForSelectorOptions { Timeout = 15000 });
            await page.WaitForTimeoutAsync(400);
        }

        return page;
    }

    private async Task<string> ShootAsync(IPage page, ILocator subject, string name)
    {
        await subject.EvaluateAsync("el => { el.scrollIntoView({ block: 'center', behavior: 'instant' }); }");
        await page.WaitForTimeoutAsync(300);
        await page.Mouse.MoveAsync(0, 0);
        var path = Path.Combine(ShotDir, $"{name}.png");
        await subject.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
        return path;
    }

    private async Task<string> ShootViewportAsync(IPage page, ILocator subject, string name)
    {
        await subject.EvaluateAsync("el => { el.scrollIntoView({ block: 'center', behavior: 'instant' }); }");
        await page.WaitForTimeoutAsync(300);
        await page.Mouse.MoveAsync(0, 0);
        var path = Path.Combine(ShotDir, $"{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });
        return path;
    }

    // ── Sidebar: sections ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task Sidebar_Sections_Are_Named_By_Their_Headings()
    {
        var page = await OpenAsync("/layout", 1280, 900, touch: false);
        var rail = page.GetByTestId("sectioned-sidebar");
        await Assertions.Expect(rail).ToBeVisibleAsync();

        var headings = rail.Locator(".tm-sidebar-section-title");
        await Assertions.Expect(headings).ToHaveCountAsync(2);

        var report = await rail.EvaluateAsync<string[]>(
            """
            root => {
                const headings = [...root.querySelectorAll('.tm-sidebar-section-title')];
                return headings.map(h => {
                    const list = h.parentElement.querySelector('ul.tm-sidebar-nav-list');
                    const id = list?.getAttribute('aria-labelledby');
                    const target = id ? root.querySelector('#' + CSS.escape(id)) : null;
                    return [
                        h.tagName,
                        h.textContent.trim(),
                        id || '',
                        target ? target.textContent.trim() : '',
                        list ? String(list.querySelectorAll('.tm-sidebar-nav-item').length) : '0'
                    ].join('|');
                });
            }
            """);

        Assert.AreEqual(2, report.Length, "both sections render a heading and a list");
        foreach (var row in report)
        {
            var parts = row.Split('|');
            Assert.AreEqual("H2", parts[0], $"the group heading must be a real heading: {row}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(parts[1]), $"the heading has text: {row}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(parts[2]), $"the list is labelled: {row}");
            Assert.AreEqual(parts[1], parts[3], $"aria-labelledby resolves to the group's own heading: {row}");
            Assert.AreNotEqual("0", parts[4], $"the group renders its items: {row}");
        }

        // The landmark itself is named, and the two rails on this page are distinguishable.
        var label = await rail.Locator("nav").GetAttributeAsync("aria-label");
        StringAssert.Contains(label, "Sectioned", "the rail's nav landmark carries the host's name");

        // No two group lists share one labels target.
        var ids = await rail.Locator("ul.tm-sidebar-nav-list").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('aria-labelledby'))");
        CollectionAssert.AllItemsAreUnique(ids, "two lists named by one heading are one indistinguishable group");

        await ShootAsync(page, rail, "sidebar-sections-1280");
    }

    [TestMethod]
    public async Task Sidebar_AutoActive_Marks_The_Current_Page_Exactly_Once()
    {
        var page = await OpenAsync("/layout", 1280, 900, touch: false);
        var rail = page.GetByTestId("sectioned-sidebar");

        var current = rail.Locator("[aria-current='page']");
        await Assertions.Expect(current).ToHaveCountAsync(1);

        Assert.AreEqual("/layout", await current.GetAttributeAsync("href"),
            "the entry for the page being viewed is the current one");
        StringAssert.Contains(await current.TextContentAsync() ?? string.Empty, "Layout");

        // The basic rail (no AutoActive) marks nothing: the default behaviour is unchanged.
        var basic = page.GetByTestId("sidebar");
        await Assertions.Expect(basic.Locator("[aria-current='page']")).ToHaveCountAsync(0);
    }

    // ── Sidebar: collapsed rail ────────────────────────────────────────────────

    [TestMethod]
    public async Task Collapsed_Rail_Keeps_Every_Accessible_Name_And_Shows_Its_Tooltip()
    {
        var page = await OpenAsync("/layout", 1280, 900, touch: false);
        var rail = page.GetByTestId("sectioned-sidebar");

        await page.GetByTestId("sectioned-sidebar-toggle").ClickAsync();
        await Assertions.Expect(rail).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-sidebar-collapsed"));
        await page.WaitForTimeoutAsync(400);

        // Every link still has a name, and the tooltip carries the same text.
        var names = await rail.Locator("a.tm-sidebar-nav-link").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('aria-label') || '')");
        Assert.IsTrue(names.Length >= 3, $"the rail still renders its items, got {names.Length}");
        foreach (var name in names)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(name), "an icon-only link without a name is a nameless control");
        }

        Assert.IsTrue(names.Contains("Layout"), $"the labels come from the model, got: {string.Join(",", names)}");

        var tooltipTexts = await rail.Locator(".tm-tooltip__content").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.textContent.trim())");
        CollectionAssert.AreEqual(names, tooltipTexts,
            "each icon explains itself with the label it is named by");

        // The tooltip has to ESCAPE the 3.5rem rail: a clipped tooltip is an invisible one.
        var firstLink = rail.Locator("a.tm-sidebar-nav-link").First;
        await firstLink.HoverAsync();
        await page.WaitForTimeoutAsync(300);

        var geometry = await rail.EvaluateAsync<double[]>(
            """
            root => {
                const tip = root.querySelector('.tm-tooltip__content');
                const railBox = root.getBoundingClientRect();
                const tipBox = tip.getBoundingClientRect();
                const style = getComputedStyle(tip);
                return [railBox.right, tipBox.left, tipBox.width, tipBox.height, parseFloat(style.opacity)];
            }
            """);
        Assert.IsTrue(geometry[4] > 0.9, $"the hovered tooltip is painted, opacity was {geometry[4]}");
        Assert.IsTrue(geometry[2] > 0 && geometry[3] > 0, "the tooltip has a box");
        Assert.IsTrue(geometry[1] >= geometry[0] - 1,
            $"the tooltip sits to the right of the clipped rail (rail.right={geometry[0]}, tooltip.left={geometry[1]})");

        // The artifact has to SHOW the tooltip, so the pointer stays on the icon (the shared capture
        // helper parks the mouse at 0,0 to avoid hover flicker — which would hide it again) and the
        // shot is viewport-wide, because the tooltip paints outside the rail's box.
        await firstLink.HoverAsync();
        await page.WaitForTimeoutAsync(400);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, "sidebar-collapsed-tooltip-1280.png"),
            FullPage = false,
        });
    }

    [TestMethod]
    public async Task Collapsed_Rail_Keeps_The_Group_Names_For_Screen_Readers()
    {
        var page = await OpenAsync("/layout", 1280, 900, touch: false);
        var rail = page.GetByTestId("sectioned-sidebar");
        await page.GetByTestId("sectioned-sidebar-toggle").ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var report = await rail.EvaluateAsync<string[]>(
            """
            root => [...root.querySelectorAll('.tm-sidebar-section-title')].map(h => {
                const box = h.getBoundingClientRect();
                const style = getComputedStyle(h);
                return h.textContent.trim() + '|' + String(box.width) + '|' + style.position;
            })
            """);
        Assert.AreEqual(2, report.Length, "the headings are still in the DOM");
        foreach (var row in report)
        {
            var parts = row.Split('|');
            Assert.IsFalse(string.IsNullOrWhiteSpace(parts[0]), "the heading keeps its text");
            Assert.IsTrue(double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) <= 2,
                $"a collapsed rail does not paint a group heading: {row}");
        }
    }

    // ── Top bar ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task TopBar_Menu_Button_Reports_And_Toggles_Its_State()
    {
        var page = await OpenAsync("/layout", 1280, 900, touch: false);
        var button = page.GetByTestId("topbar-menu-button");
        await Assertions.Expect(button).ToBeVisibleAsync();

        Assert.AreEqual("false", await button.GetAttributeAsync("aria-expanded"));
        Assert.AreEqual("layout-topbar-menu-target", await button.GetAttributeAsync("aria-controls"),
            "the disclosure button names the region it opens");

        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "true");

        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "false");

        // A native button activates exactly once per Enter press.
        await button.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "true");
    }

    [TestMethod]
    public async Task TopBar_Account_Menu_Is_An_Apg_Menu_And_Escape_Restores_Focus()
    {
        var page = await OpenAsync("/layout", 1280, 900, touch: false);
        var trigger = page.GetByTestId("topbar-user-button");
        Assert.AreEqual("menu", await trigger.GetAttributeAsync("aria-haspopup"));
        Assert.AreEqual("false", await trigger.GetAttributeAsync("aria-expanded"));

        await trigger.ClickAsync();
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "true");

        var menu = page.GetByTestId("topbar-user-menu");
        Assert.AreEqual("menu", await menu.GetAttributeAsync("role"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(await menu.GetAttributeAsync("aria-label")),
            "a role=menu needs an accessible name");

        var items = menu.Locator("[role='menuitem']");
        await Assertions.Expect(items).ToHaveCountAsync(4);
        await Assertions.Expect(items.First).ToBeFocusedAsync(); // opening moves the focus into the menu
        var tabStops = await items.EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('tabindex') || '')");
        CollectionAssert.AreEqual(new[] { "-1", "-1", "-1", "-1" }, tabStops,
            "the entries are one roving focus stop, not four page tab stops");
        var labels = await items.EvaluateAllAsync<string[]>("els => els.map(e => e.textContent.trim())");
        CollectionAssert.AreEqual(
            new[] { "Your profile", "Account settings", "Delete account", "Sign out" }, labels,
            "host entries keep their order and the localized sign out closes the list");

        Assert.AreEqual("separator", await menu.Locator("[role='separator']").First.GetAttributeAsync("role"),
            "the destructive entry is separated from the navigation entries");

        await ShootViewportAsync(page, page.GetByTestId("topbar-demo"), "topbar-account-menu-1280");

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.GetByTestId("topbar-user-menu")).ToHaveCountAsync(0);
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "false");

        var focusTestId = await page.EvaluateAsync<string?>(
            "() => document.activeElement ? document.activeElement.getAttribute('data-testid') : null");
        Assert.AreEqual("topbar-user-button", focusTestId,
            "Escape hands focus back to the button that opened the menu");
    }

    [TestMethod]
    public async Task TopBar_Account_Menu_Keyboard_Follows_The_Apg_Menu_Button_Pattern()
    {
        var page = await OpenAsync("/layout", 1280, 900, touch: false);
        var trigger = page.GetByTestId("topbar-user-button");
        var menu = page.GetByTestId("topbar-user-menu");
        var items = menu.Locator("[role='menuitem']");

        // ArrowDown on the trigger opens the menu AND takes the focus with it.
        await trigger.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(menu).ToBeVisibleAsync();
        await Assertions.Expect(items.First).ToBeFocusedAsync();

        // ArrowDown / ArrowUp step with wraparound.
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(items.Nth(1)).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("End");
        await Assertions.Expect(items.Last).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(items.First).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(items.Last).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(items.First).ToBeFocusedAsync();

        // ArrowUp on the trigger opens the menu at its LAST entry.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(menu).ToHaveCountAsync(0);
        await page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(items.Last).ToBeFocusedAsync();

        await ShootViewportAsync(page, page.GetByTestId("topbar-demo"), "topbar-account-menu-keyboard-1280");

        // Tab closes the menu and never leaves the focus on <body>.
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.GetByTestId("topbar-user-menu")).ToHaveCountAsync(0);
        var active = await page.EvaluateAsync<string?>(
            "() => document.activeElement ? (document.activeElement.getAttribute('data-testid') || document.activeElement.tagName) : 'null'");
        Assert.AreNotEqual("BODY", active,
            "Tab removes the focused entry, so the trigger must own the focus before the browser moves on");
    }

    [TestMethod]
    public async Task TopBar_Account_Menu_Backdrop_Click_Returns_Focus_To_The_Trigger()
    {
        var page = await OpenAsync("/layout", 1280, 900, touch: false);
        var trigger = page.GetByTestId("topbar-user-button");
        await trigger.ClickAsync();
        await Assertions.Expect(page.GetByTestId("topbar-user-menu")).ToBeVisibleAsync();

        // The backdrop sits under the (transparent) menu overlay area: a DOM click is the same
        // handler the user's click runs, without depending on which pixel is not covered.
        await page.EvaluateAsync(
            """() => document.querySelector('[data-testid="topbar-user-menu-backdrop"]').click()""");

        await Assertions.Expect(page.GetByTestId("topbar-user-menu")).ToHaveCountAsync(0);
        await Assertions.Expect(trigger).ToBeFocusedAsync();
    }

    [TestMethod]
    public async Task TopBar_Account_Menu_Entry_Runs_The_Host_Command_And_Closes()
    {
        var page = await OpenAsync("/layout", 1280, 900, touch: false);
        await page.GetByTestId("topbar-user-button").ClickAsync();

        await page.GetByTestId("topbar-user-menu-item-delete").ClickAsync();

        await Assertions.Expect(page.GetByTestId("topbar-user-menu")).ToHaveCountAsync(0);
        try
        {
            await Assertions.Expect(page.GetByTestId("topbar-last-action")).ToContainTextAsync("Delete account");
        }
        catch (PlaywrightException)
        {
            var toast = await page.Locator(".tm-toast, .tm-toast-container").AllTextContentsAsync();
            Assert.Fail(
                "The host command did not report back. Paragraph: "
                + await page.GetByTestId("topbar-last-action").TextContentAsync()
                + " | toasts: " + string.Join(" / ", toast));
        }
    }

    [TestMethod]
    public async Task TopBar_Command_Palette_Trigger_Prints_And_Advertises_Its_Chord()
    {
        var page = await OpenAsync("/layout", 1280, 900, touch: false);
        var trigger = page.GetByTestId("topbar-command-palette");

        StringAssert.Contains(await trigger.GetAttributeAsync("aria-keyshortcuts") ?? string.Empty, "Control+K");
        Assert.AreEqual("Ctrl+K", (await trigger.Locator(".tm-topbar-search-hint").TextContentAsync())?.Trim());
        Assert.AreEqual("Open command palette", await trigger.GetAttributeAsync("aria-label"));
    }

    // ── Touch geometry and theme ───────────────────────────────────────────────

    [TestMethod]
    public async Task Coarse_Pointer_Targets_Are_At_Least_44_Pixels()
    {
        var page = await OpenAsync("/layout", 390, 844, touch: true);

        var sizes = await page.EvaluateAsync<double[]>(
            """
            () => {
                const ids = ['topbar-menu-button', 'topbar-user-button', 'topbar-command-palette'];
                const out = [];
                for (const id of ids) {
                    const el = document.querySelector("[data-testid='" + id + "']");
                    if (!el) { out.push(-1, -1); continue; }
                    const box = el.getBoundingClientRect();
                    out.push(box.width, box.height);
                }
                const toggle = document.querySelector("[data-testid='sectioned-sidebar-toggle']");
                out.push(toggle ? toggle.getBoundingClientRect().width : -1);
                out.push(toggle ? toggle.getBoundingClientRect().height : -1);
                return out;
            }
            """);

        for (var i = 0; i < sizes.Length; i++)
        {
            Assert.IsTrue(sizes[i] >= 43.5, $"coarse target #{i} is {sizes[i]}px, a finger needs 44");
        }

        await ShootAsync(page, page.GetByTestId("topbar-demo"), "topbar-mobile-390");
    }

    [TestMethod]
    public async Task Sidebar_And_TopBar_Follow_The_Dark_Theme()
    {
        var page = await OpenAsync("/layout", 1280, 900, touch: false);
        var rail = page.GetByTestId("sectioned-sidebar");

        var light = await rail.EvaluateAsync<string>(
            "el => getComputedStyle(el.querySelector('.tm-sidebar-nav-link')).color");
        await ShootAsync(page, rail, "sidebar-light-1280");

        await page.Locator("button[title*='dark' i]:visible").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-theme='dark']", new PageWaitForSelectorOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);

        var dark = await rail.EvaluateAsync<string>(
            "el => getComputedStyle(el.querySelector('.tm-sidebar-nav-link')).color");
        Assert.AreNotEqual(light, dark, "the rail reads semantic tokens, so a theme switch repaints it");

        await ShootAsync(page, rail, "sidebar-dark-1280");
        await ShootAsync(page, page.GetByTestId("topbar-demo"), "topbar-dark-1280");
    }
}
