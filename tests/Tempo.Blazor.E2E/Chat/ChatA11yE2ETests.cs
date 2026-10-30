using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Chat;

/// <summary>
/// E2E for the TmChat accessibility phase (FZE-7) on the /chat demo (WASM @ 7106), section
/// <c>chat-a11y-section</c>: the component keeps EXACTLY ONE polite live region in the real
/// browser DOM, the messages container is a labelled keyboard-scrollable <c>role=region</c>,
/// the host-supplied announcement texts reach that region (single message, summarized burst,
/// host action) and the per-message actions are revealed by keyboard focus
/// (<c>:focus-within</c>), not only by hover. Screenshots land in
/// <c>__screenshots__/chat/</c> for UX review.
/// </summary>
[TestClass]
public class ChatA11yE2ETests : WasmTestBase
{
    private const string ChatPage = "/chat";
    private const string Section = "[data-testid='chat-a11y-section']";

    private readonly List<string> _clientErrors = [];

    private async Task<IPage> OpenAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        page.PageError += (_, e) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + e); };
        page.Console += (_, m) =>
        {
            if (m.Type == "error" && m.Text.Contains("Unhandled exception"))
                lock (_clientErrors) _clientErrors.Add("CONSOLE: " + m.Text);
        };
        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}{ChatPage}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var section = page.Locator(Section);
        await section.ScrollIntoViewIfNeededAsync();
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // The WASM boot renders the section chat several seconds after the app reports ready.
        await page.WaitForFunctionAsync(
            @"sel => { const s = document.querySelector(sel);
                return !!s && !!s.querySelector('.tm-chat__messages .tm-chat__item'); }",
            Section,
            new PageWaitForFunctionOptions { Timeout = 30000, PollingInterval = 200 });
        await page.WaitForTimeoutAsync(500);
        return page;
    }

    private static ILocator Chat(IPage page) => page.Locator($"{Section} .tm-chat");

    private static ILocator Region(IPage page) =>
        page.Locator($"{Section} [data-testid='chat-announcements']");

    private static ILocator Button(IPage page, string text) =>
        page.Locator(Section).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = text });

    [TestMethod]
    [TestCategory("WASM")]
    public async Task ExactlyOneLiveRegion_AnnouncesHostTexts()
    {
        var page = await OpenAsync();

        // Exactly ONE aria-live element inside the chat root — and it is the sr-only region.
        var liveCount = await page.EvaluateAsync<int>(
            @"sel => document.querySelector(sel).querySelectorAll('[aria-live]').length",
            $"{Section} .tm-chat");
        Assert.AreEqual(1, liveCount, "the chat must keep exactly one live region in the browser");

        // The SAME count over the live-region ROLES: status/alert/log/marquee/timer all imply
        // aria-live — a hidden second live region must not slip in through a role alone.
        var roleLiveCount = await page.EvaluateAsync<int>(
            @"sel => document.querySelector(sel).querySelectorAll(
                '[aria-live],[role=""status""],[role=""alert""],[role=""log""],[role=""marquee""],[role=""timer""]').length",
            $"{Section} .tm-chat");
        Assert.AreEqual(1, roleLiveCount, "exactly one element carries a live region (attribute or role)");

        var region = Region(page);
        await Microsoft.Playwright.Assertions.Expect(region).ToHaveAttributeAsync("aria-live", "polite");
        await Microsoft.Playwright.Assertions.Expect(region).ToHaveAttributeAsync("aria-atomic", "true");
        Assert.AreEqual(string.Empty, (await region.TextContentAsync())?.Trim(),
            "nothing is announced before the host sends text");

        // The scrollable messages container is a labelled, keyboard-focusable region.
        var scroller = page.Locator($"{Section} .tm-chat__messages");
        await Microsoft.Playwright.Assertions.Expect(scroller).ToHaveAttributeAsync("role", "region");
        await Microsoft.Playwright.Assertions.Expect(scroller).ToHaveAttributeAsync("tabindex", "0");
        var ariaLabel = await scroller.GetAttributeAsync("aria-label");
        Assert.IsFalse(string.IsNullOrWhiteSpace(ariaLabel), "the messages region needs an accessible name");
        Assert.AreEqual("Accessibility demo messages", ariaLabel,
            "the demo passes a distinct label (a page with several chats must label each one)");

        // A single incoming message is announced with the host's own sentence.
        // (The button names are unique on /chat: the scroll section's unscoped page-level
        // GetByRole("Inject incoming message") must keep resolving to a single element.)
        await Button(page, "Deliver incoming message").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(region).ToHaveTextAsync(
            "New message from Bob: Incoming message #1", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });

        // A burst within the throttle window collapses into the host's summary.
        await Button(page, "Deliver a burst of 3").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(region).ToHaveTextAsync(
            "3 new messages arrived", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });

        // …and a host action goes through the same single region.
        await Button(page, "Approve a draft (host action)").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(region).ToHaveTextAsync(
            "Your draft was approved", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });

        AssertNoClientErrors();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task MessageActions_RevealOnKeyboardFocus_NotOnlyOnHover()
    {
        // The actions render only when their handlers are wired — the collaboration demo pane
        // (chat-pane-alice) has them all; the announcements pane is deliberately read-mostly.
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        page.PageError += (_, e) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + e); };
        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}{ChatPage}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        const string pane = "[data-testid='chat-pane-alice']";
        await page.Locator(pane).ScrollIntoViewIfNeededAsync();
        await page.WaitForFunctionAsync(
            @"sel => !!document.querySelector(sel + ' .tm-chat__item .tm-chat__actions')",
            pane,
            new PageWaitForFunctionOptions { Timeout = 30000, PollingInterval = 200 });

        // Hidden by default…
        var firstActions = page.Locator($"{pane} .tm-chat__item .tm-chat__actions").First;
        var hiddenOpacity = await firstActions.EvaluateAsync<string>("el => getComputedStyle(el).opacity");
        Assert.AreEqual("0", hiddenOpacity, "the actions are hidden until the row is hovered or focused");

        // …and revealed when the ITEM holds keyboard focus (focus-within, not hover).
        await page.Locator($"{pane} .tm-chat__item .tm-chat__action").First.FocusAsync();
        await Microsoft.Playwright.Assertions.Expect(firstActions)
            .ToHaveCSSAsync("opacity", "1", new LocatorAssertionsToHaveCSSOptions { Timeout = 5000 });
        await Microsoft.Playwright.Assertions.Expect(firstActions)
            .ToHaveCSSAsync("pointer-events", "auto", new LocatorAssertionsToHaveCSSOptions { Timeout = 5000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Announcements_Section_Screenshots()
    {
        var page = await OpenAsync();
        var chat = Chat(page);
        var scroller = page.Locator($"{Section} .tm-chat__messages");

        // The burst state (a visible announcement) — the region itself stays invisible, so the
        // screenshot documents the section layout the phase shipped.
        await Button(page, "Deliver a burst of 3").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(Region(page)).ToHaveTextAsync(
            "3 new messages arrived", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });

        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "chat");
        Directory.CreateDirectory(dir);
        await chat.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(dir, "chat-a11y-light.png") });

        // The container is a Tab stop: TAB into it and assert the visible keyboard focus
        // outline (inset so the scroller's clipping keeps it painted), then screenshot it.
        await TabIntoMessagesRegionAsync(page);
        await AssertFocusOutlineAsync(page);
        await chat.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(dir, "chat-a11y-focus-light.png") });

        await page.EvaluateAsync(@"() => {
            document.documentElement.setAttribute('data-theme', 'dark');
            document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', 'dark'));
        }");
        await page.WaitForTimeoutAsync(150);
        await chat.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(dir, "chat-a11y-dark.png") });

        // The outline survives the theme switch (the token resolves in dark mode too).
        await AssertFocusOutlineAsync(page);
        await chat.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(dir, "chat-a11y-focus-dark.png") });

        await page.EvaluateAsync(@"() => {
            document.documentElement.removeAttribute('data-theme');
            document.querySelectorAll('[data-theme]').forEach(el => el.removeAttribute('data-theme'));
        }");

        AssertNoClientErrors();
    }

    /// <summary>
    /// Walks the Tab order until the messages region holds the focus (the container is a Tab
    /// stop) — a real keyboard path, not a programmatic focus() that would skip :focus-visible.
    /// Shift+Tab walks BACKWARDS: the section's buttons sit after the chat in the DOM, so the
    /// region is a handful of steps away no matter how long the demo page is.
    /// </summary>
    private static async Task TabIntoMessagesRegionAsync(IPage page)
    {
        const string selector = $"{Section} .tm-chat__messages";
        for (var i = 0; i < 25; i++)
        {
            if (await page.EvaluateAsync<bool>(
                    @"sel => document.activeElement === document.querySelector(sel)", selector))
            {
                return;
            }

            await page.Keyboard.PressAsync("Shift+Tab");
        }

        Assert.Fail("Tab never reached the messages region; activeElement is: " +
            await page.EvaluateAsync<string>("() => { const el = document.activeElement; return el ? (el.tagName + '.' + (el.className || '')) : 'null'; }"));
    }

    private static async Task AssertFocusOutlineAsync(IPage page)
    {
        var scroller = page.Locator($"{Section} .tm-chat__messages");
        var outlineOffset = await scroller.EvaluateAsync<string>("el => getComputedStyle(el).outlineOffset");
        Assert.AreEqual("-2px", outlineOffset,
            "the inset outline stays visible although the scroller clips its own overflow");
        var outlineStyle = await scroller.EvaluateAsync<string>("el => getComputedStyle(el).outlineStyle");
        Assert.AreEqual("solid", outlineStyle, "the keyboard focus indicator must be visible");
    }

    private void AssertNoClientErrors()
    {
        lock (_clientErrors)
        {
            Assert.IsTrue(_clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", _clientErrors));
        }
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx"))) return directory;
            directory = directory.Parent!;
        }

        throw new InvalidOperationException("repo root not found");
    }
}
