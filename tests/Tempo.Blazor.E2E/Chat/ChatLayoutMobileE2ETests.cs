using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Chat;

/// <summary>
/// E2E for the TmChat layout &amp; mobile phase (FZE-8) on the /chat demo (WASM), section
/// <c>chat-mobile-section</c>. The narrow-chat behaviors branch on the CHAT's own width
/// (<c>@container tm-chat (width &lt; 640px)</c>), so they are verifiable on a desktop window via
/// the demo's fixed-width panes:
/// <list type="bullet">
/// <item><c>chat-fill-pane</c> — <c>FillParent</c>: the chat is a flex child of a split row
/// (grows/shrinks, <c>tm-chat--fill</c>).</item>
/// <item><c>chat-thread-overlay-pane</c> — a ~340px chat: the open thread panel overlays the
/// FULL width instead of squeezing the composer.</item>
/// <item><c>chat-mobile-phone</c> — a 375px phone frame with a fake 56px bottom navigation: the
/// composer is sticky and rests 56px above the phone bottom while the page scrolls, and on a
/// coarse pointer the send button reaches the 44px touch target.</item>
/// </list>
/// Screenshots (375×812 and 1440×900, the DoD viewports) land in <c>__screenshots__/chat/</c>
/// as NEW files for the phase-9 UX review.
/// </summary>
[TestClass]
public class ChatLayoutMobileE2ETests : WasmTestBase
{
    private const string ChatPage = "/chat";
    private const string Section = "[data-testid='chat-mobile-section']";
    private const string FillPane = "[data-testid='chat-fill-pane']";
    private const string OverlayPane = "[data-testid='chat-thread-overlay-pane']";
    private const string Phone = "[data-testid='chat-mobile-phone']";

    private readonly List<string> _clientErrors = [];

    private async Task<IPage> OpenAsync(int width, int height, bool mobileEmulation = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            IsMobile = mobileEmulation,
            HasTouch = mobileEmulation,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        page.PageError += (_, e) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + e); };
        page.Console += (_, m) =>
        {
            if (m.Type == "error" && m.Text.Contains("Unhandled exception"))
                lock (_clientErrors) _clientErrors.Add("CONSOLE: " + m.Text);
        };
        await page.GotoAsync($"{BaseUrl}{ChatPage}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var section = page.Locator(Section);
        await section.ScrollIntoViewIfNeededAsync();
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(500);
        return page;
    }

    private static async Task AssertNoClientErrorsAsync(List<string> clientErrors)
    {
        lock (clientErrors)
        {
            Assert.IsTrue(clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", clientErrors));
        }
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "chat");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"{fileName}.png"), FullPage = false });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx"))) return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }

    // ── FillParent ───────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task FillParent_ChatGrowsWithTheSplitPane()
    {
        var page = await OpenAsync(1440, 900);
        var chat = page.Locator($"{FillPane} .tm-chat");
        await chat.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // The modifier class is on the root …
        var classAttribute = await chat.GetAttributeAsync("class");
        StringAssert.Contains(classAttribute, "tm-chat--fill", "FillParent must render the tm-chat--fill modifier");

        // … and the chat fills the fixed-height split row (minus the side panel's share of the gap).
        var split = await page.Locator(".chat-demo-split").BoundingBoxAsync();
        var side = await page.Locator($"{FillPane} .chat-demo-side").BoundingBoxAsync();
        var box = await chat.BoundingBoxAsync();
        Assert.IsNotNull(split);
        Assert.IsNotNull(side);
        Assert.IsNotNull(box);
        Assert.AreEqual(split!.Height, box!.Height, 2.0,
            "the chat must stretch to the split row's height as a flex child");
        // Exact flex math (the grid column width varies with the demo layout): the chat takes
        // everything beside the fixed side panel and the flex gap.
        Assert.AreEqual(split.Width - side!.Width - 16, box.Width, 4.0,
            "the chat must take the free width beside the fixed side panel");

        await SaveScreenshotAsync(page, "chat-fill-1440");
        await AssertNoClientErrorsAsync(_clientErrors);
    }

    // ── Narrow chat: full-width thread overlay ───────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task NarrowChat_ThreadPanelOverlaysFullWidth()
    {
        var page = await OpenAsync(1440, 900);
        var chat = page.Locator($"{OverlayPane} .tm-chat");
        await chat.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // Open the thread of the first message (the demo seeds a reply on "mob1").
        var badge = page.Locator($"{OverlayPane} [data-testid='chat-thread-open-mob1']");
        await badge.ScrollIntoViewIfNeededAsync();
        await badge.ClickAsync();
        var panel = page.Locator($"{OverlayPane} .tm-chat__thread-panel");
        await panel.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        var chatBox = await chat.BoundingBoxAsync();
        var panelBox = await panel.BoundingBoxAsync();
        Assert.IsNotNull(chatBox);
        Assert.IsNotNull(panelBox);
        Assert.AreEqual(chatBox!.Width, panelBox!.Width, 2.0,
            "on a narrow chat the panel overlays the whole width instead of squeezing the composer");
        Assert.AreEqual(chatBox!.X, panelBox!.X, 2.0, "the overlay starts at the chat's left edge");

        // The composer is NOT squeezed: it keeps its full width under the overlay.
        var inputArea = await page.Locator($"{OverlayPane} .tm-chat__input-area").BoundingBoxAsync();
        Assert.IsNotNull(inputArea);
        Assert.AreEqual(chatBox!.Width, inputArea!.Width, 2.0);

        await SaveScreenshotAsync(page, "chat-thread-overlay-narrow");
        await AssertNoClientErrorsAsync(_clientErrors);
    }

    // ── Sticky composer above the bottom navigation ──────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PhoneFrame_ComposerSticksAboveTheBottomNavigation()
    {
        var page = await OpenAsync(1440, 900);
        var phone = page.Locator(Phone);
        await phone.ScrollIntoViewIfNeededAsync();

        var composer = page.Locator($"{Phone} .tm-chat__input-area");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        var phoneBox = await phone.BoundingBoxAsync();
        Assert.IsNotNull(phoneBox);
        var navTop = phoneBox!.Y + phoneBox.Height - 56; // the fake nav is 56px tall

        // Scroll the phone page: the chat's box extends past the fold, so the sticky composer
        // stays pinned 56px above the phone bottom (the --tm-chat-composer-offset) at every
        // scroll position where the chat's own box is still below the fold.
        await page.EvaluateAsync(
            "sel => { document.querySelector(sel).querySelector('.chat-demo-phone__page').scrollTop = 300; }",
            Phone);
        await page.WaitForTimeoutAsync(300);
        var pinned = await composer.BoundingBoxAsync();
        Assert.IsNotNull(pinned);
        Assert.AreEqual(navTop, pinned!.Y + pinned.Height, 8.0,
            "the composer must stick 56px above the phone bottom while the page scrolls");

        await page.EvaluateAsync(
            "sel => { document.querySelector(sel).querySelector('.chat-demo-phone__page').scrollTop = 700; }",
            Phone);
        await page.WaitForTimeoutAsync(300);
        var pinnedDeeper = await composer.BoundingBoxAsync();
        Assert.IsNotNull(pinnedDeeper);
        Assert.AreEqual(navTop, pinnedDeeper!.Y + pinnedDeeper.Height, 8.0,
            "still pinned while the chat's box extends past the fold");

        // Scrolled past the chat's end, the composer RESTS at the chat's bottom edge — a sticky
        // element can never leave its containing block.
        await page.EvaluateAsync(
            "sel => { document.querySelector(sel).querySelector('.chat-demo-phone__page').scrollTop = 999999; }",
            Phone);
        await page.WaitForTimeoutAsync(300);
        var chatBox = await page.Locator($"{Phone} .tm-chat").First.BoundingBoxAsync();
        var resting = await composer.BoundingBoxAsync();
        Assert.IsNotNull(chatBox);
        Assert.IsNotNull(resting);
        Assert.AreEqual(chatBox!.Y + chatBox.Height, resting!.Y + resting.Height, 8.0,
            "once the chat's bottom is visible the composer rests at the chat's bottom edge");

        // The thread overlay's reply row sticks like the composer: open the mob1 thread, scroll
        // the phone page and the reply row pins to the SAME 56px offset the composer uses.
        var phoneBadge = page.Locator($"{Phone} [data-testid='chat-thread-open-mob1']");
        await phoneBadge.ScrollIntoViewIfNeededAsync();
        await phoneBadge.ClickAsync();
        var threadInputArea = page.Locator($"{Phone} .tm-chat__thread-input-area");
        await threadInputArea.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await page.EvaluateAsync(
            "sel => { document.querySelector(sel).querySelector('.chat-demo-phone__page').scrollTop = 300; }",
            Phone);
        await page.WaitForTimeoutAsync(300);
        var threadPinned = await threadInputArea.BoundingBoxAsync();
        Assert.IsNotNull(threadPinned);
        Assert.AreEqual(navTop, threadPinned!.Y + threadPinned.Height, 8.0,
            "the thread overlay's reply row sticks 56px above the phone bottom too");

        // On a wide chat (the a11y section card is ~800px on this viewport) the composer keeps
        // today's static position — sticky is the narrow-chat behavior only.
        var wideComposer = page.Locator("[data-testid='chat-a11y-section'] .tm-chat__input-area").First;
        var widePosition = await wideComposer.EvaluateAsync<string>("el => getComputedStyle(el).position");
        Assert.AreEqual("static", widePosition, "the desktop composer must not become sticky");

        await SaveScreenshotAsync(page, "chat-mobile-phone-1440");
        await AssertNoClientErrorsAsync(_clientErrors);
    }

    // ── 375×812 DoD viewport: coarse-pointer targets + sticky ────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task MobileViewport_TouchTargetsAndStickyComposer()
    {
        var page = await OpenAsync(375, 812, mobileEmulation: true);

        // The device emulation must actually report a coarse pointer, or the 44px assertion
        // below would say nothing.
        var coarse = await page.EvaluateAsync<bool>("matchMedia('(pointer: coarse)').matches");
        Assert.IsTrue(coarse, "the 375×812 mobile-emulated context must report pointer: coarse");

        var sendButton = page.Locator($"{Phone} .tm-chat__send-btn").First;
        var minWidth = await sendButton.EvaluateAsync<string>("el => getComputedStyle(el).minWidth");
        Assert.AreEqual("44px", minWidth, "the send button reaches the --tm-touch-target on coarse pointers");

        // The per-message actions live in the collaboration chat (the phone chat binds no action
        // handlers on purpose); the coarse-pointer rule is viewport-independent, so 375px is fine.
        var action = page.Locator("[data-testid='chat-collab-section'] .tm-chat__action").First;
        var actionMinHeight = await action.EvaluateAsync<string>("el => getComputedStyle(el).minHeight");
        Assert.AreEqual("44px", actionMinHeight, "the per-message actions reach the touch target too");

        // The action bar is IN THE FLOW on a coarse pointer: every action of the collaboration
        // chats must lie INSIDE its own chat's box (the old floating bar could hang outside).
        var actionsInsideChats = await page.EvaluateAsync<bool[]>(
            """
            () => [...document.querySelectorAll("[data-testid='chat-collab-section'] .tm-chat__action")]
                .map(a => {
                    const chat = a.closest('.tm-chat').getBoundingClientRect();
                    const r = a.getBoundingClientRect();
                    return r.top >= chat.top - 1 && r.bottom <= chat.bottom + 1
                        && r.left >= chat.left - 1 && r.right <= chat.right + 1;
                })
            """);
        Assert.IsTrue(actionsInsideChats.Length > 0, "the collaboration chats must render actions");
        Assert.IsTrue(actionsInsideChats.All(inside => inside),
            "every per-message action must lie inside its chat's box on a coarse pointer");

        // The thread close button is a 28px icon on fine pointers — it reaches the target too.
        var overlayBadge = page.Locator($"{OverlayPane} [data-testid='chat-thread-open-mob1']");
        await overlayBadge.ScrollIntoViewIfNeededAsync();
        await overlayBadge.ClickAsync();
        var threadClose = page.Locator($"{OverlayPane} .tm-chat__thread-close");
        await threadClose.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var closeMinHeight = await threadClose.EvaluateAsync<string>("el => getComputedStyle(el).minHeight");
        Assert.AreEqual("44px", closeMinHeight, "the thread close button reaches the touch target too");

        // And the sticky composer is live on the phone width as well.
        var composer = page.Locator($"{Phone} .tm-chat__input-area");
        var position = await composer.EvaluateAsync<string>("el => getComputedStyle(el).position");
        Assert.AreEqual("sticky", position);

        await SaveScreenshotAsync(page, "chat-mobile-375x812");
        await AssertNoClientErrorsAsync(_clientErrors);
    }

    // ── Narrow overlay: keyboard focus never lands on a covered element ──────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task NarrowOverlay_FocusFromTheThreadInput_StaysInThePanelOrLeavesTheChat()
    {
        var page = await OpenAsync(1440, 900);
        var badge = page.Locator($"{OverlayPane} [data-testid='chat-thread-open-mob1']");
        await badge.ScrollIntoViewIfNeededAsync();
        await badge.ClickAsync();
        var threadInput = page.Locator($"{OverlayPane} .tm-chat__thread-input");
        await threadInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await threadInput.FocusAsync();

        // Tab / Shift+Tab from the thread reply input: the covered body and composer are
        // visibility:hidden behind the full-width overlay, so focus must never land there — it
        // either stays inside the panel or leaves the chat entirely.
        foreach (var key in new[] { "Tab", "Shift+Tab", "Shift+Tab" })
        {
            await page.Keyboard.PressAsync(key);
            var focusLocation = await page.EvaluateAsync<string>(
                """
                () => {
                    const el = document.activeElement;
                    if (!el || el === document.body) return 'outside';
                    const chat = document.querySelector("[data-testid='chat-thread-overlay-pane'] .tm-chat");
                    if (!chat.contains(el)) return 'outside';
                    const panel = chat.querySelector('.tm-chat__thread-panel');
                    return panel.contains(el) ? 'panel' : 'chat-other';
                }
                """);
            Assert.IsTrue(focusLocation is "panel" or "outside",
                $"after {key} focus must stay inside the panel or leave the chat, got '{focusLocation}'");
        }
    }

    // ── 320×640 dark mode: no horizontal overflow, full-width overlay ────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Mobile320_DarkMode_NoHorizontalOverflow()
    {
        var page = await OpenAsync(320, 640, mobileEmulation: true);

        // Flip the demo's theme the same way the real toggle does (data-theme on the root and
        // every themed wrapper), then guard the flip actually landed.
        await page.EvaluateAsync(
            """
            () => {
                document.documentElement.setAttribute('data-theme', 'dark');
                document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', 'dark'));
            }
            """);
        var theme = await page.EvaluateAsync<string>("() => document.documentElement.getAttribute('data-theme')");
        Assert.AreEqual("dark", theme, "the dark-mode flip must have landed on the root");

        // The whole page fits the 320px scrollport: no horizontal overflow anywhere (the chat is
        // a size container — its intrinsic 360px fallback must never widen the page).
        var scrollWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        Assert.IsTrue(scrollWidth <= 320, $"documentElement.scrollWidth must be <= 320, got {scrollWidth}");

        // The full-width thread overlay still covers the whole (now ~288px) chat.
        var badge = page.Locator($"{OverlayPane} [data-testid='chat-thread-open-mob1']");
        await badge.ScrollIntoViewIfNeededAsync();
        await badge.ClickAsync();
        var chatBox = await page.Locator($"{OverlayPane} .tm-chat").BoundingBoxAsync();
        var panelBox = await page.Locator($"{OverlayPane} .tm-chat__thread-panel").BoundingBoxAsync();
        Assert.IsNotNull(chatBox);
        Assert.IsNotNull(panelBox);
        Assert.AreEqual(chatBox!.Width, panelBox!.Width, 2.0,
            "at 320px the overlay still covers the whole chat");

        await SaveScreenshotAsync(page, "chat-mobile-320-dark");
        await AssertNoClientErrorsAsync(_clientErrors);
    }
}
