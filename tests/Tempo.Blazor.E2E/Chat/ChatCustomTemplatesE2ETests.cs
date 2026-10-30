using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Chat;

/// <summary>
/// E2E for the TmChat custom-template phase (FZE-9) on the /chat demo (WASM @ 7106), section
/// <c>chat-custom-section</c>: the host renders the special message kinds through
/// <c>MessageTemplate</code> + <c>TmChatBubble</c> (AI draft, queued, discarded, tip; the locked
/// paid voice note lives in the Subscribe Gate section where the fan can unlock it),
/// ordinary messages fall back to <c>context.DefaultContent</c>, and the composer/scroll/live
/// region contracts hold on a fully template-driven chat with every built-in feature off.
/// Covers the carry-forwards: touch tap on Send keeps focus, the TmChatBubble alignment
/// contract (incoming left, outgoing right, system centered) and the exactly-one-live-region
/// rule over the WIDE selector. Screenshots land in <c>__screenshots__/chat/</c> for UX review.
/// </summary>
[TestClass]
public class ChatCustomTemplatesE2ETests : WasmTestBase
{
    private const string ChatPage = "/chat";
    private const string Section = "[data-testid='chat-custom-section']";
    private const string GateSection = "[data-testid='chat-subscribe-section']";
    private const string Phone = "[data-testid='chat-mobile-phone']";

    private readonly List<string> _clientErrors = [];

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1100, bool mobileEmulation = false)
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

        // Wait for the WASM boot to render the section chat AND its first-render scroll to
        // settle (every interaction below would otherwise race the initial jump to the bottom).
        await page.WaitForFunctionAsync(
            @"sel => { const s = document.querySelector(sel);
                return !!s && !!s.querySelector('.tm-chat__messages .tm-chat__item') &&
                    s.querySelector('.tm-chat__messages').scrollHeight -
                    s.querySelector('.tm-chat__messages').scrollTop -
                    s.querySelector('.tm-chat__messages').clientHeight < 40; }",
            Section,
            new PageWaitForFunctionOptions { Timeout = 30000, PollingInterval = 200 });
        await page.WaitForTimeoutAsync(500);
        return page;
    }

    private static ILocator Section_(IPage page) => page.Locator(Section);

    private static ILocator Scroller(IPage page) => page.Locator($"{Section} .tm-chat__messages");

    private static ILocator JumpButton(IPage page) =>
        page.Locator($"{Section} [data-testid='chat-jump-to-new']");

    private static ILocator Button(IPage page, string name) =>
        page.Locator(Section).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = name });

    private static ILocator Item(IPage page, string text) =>
        page.Locator($"{Section} .tm-chat__item", new PageLocatorOptions { HasText = text });

    // The demo shell's sticky top bar floats above the page content; hiding it during element
    // captures keeps it out of shots that include the viewport top.
    private const string HideStickyTopBar = ".sticky.top-0{visibility:hidden !important}";

    private static async Task SaveElementScreenshotAsync(ILocator locator, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "chat");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{fileName}.png");
        await locator.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = path,
            Timeout = 60_000,
            Style = HideStickyTopBar,
        });
        Assert.IsTrue(File.Exists(path), $"the screenshot was not written: {path}");
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

    private void AssertNoClientErrors()
    {
        lock (_clientErrors)
        {
            Assert.IsTrue(_clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", _clientErrors));
        }
    }

    // ── (a) every Kind renders through its template + the alignment contract ──

    [TestMethod]
    [TestCategory("WASM")]
    [TestCategory("Smoke")]
    public async Task CustomTemplates_RenderEveryKind_AndDefaultContent()
    {
        var page = await OpenAsync();
        var section = Section_(page);

        // Every host-owned kind renders its bubble inside the keyed row wrapper. The locked kind
        // moved to the Subscribe Gate section (it is the fan's unlock) — asserted there.
        await Assertions.Expect(section.Locator("[data-testid='chat-custom-draft']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await Assertions.Expect(section.Locator("[data-testid='chat-custom-queued']")).ToBeVisibleAsync();
        await Assertions.Expect(section.Locator("[data-testid='chat-custom-countdown']"))
            .ToContainTextAsync("Sends in", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });
        await Assertions.Expect(section.Locator("[data-testid='chat-custom-discarded']")).ToBeVisibleAsync();
        await Assertions.Expect(section.Locator("[data-testid='chat-custom-tip']")).ToBeVisibleAsync();
        await Assertions.Expect(section.Locator("[data-testid='chat-custom-locked']"))
            .ToContainTextAsync("$9.99", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });
        await Assertions.Expect(section.Locator("[data-testid='chat-custom-locked']"))
            .Not.ToContainTextAsync("Unlock", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });

        // Ordinary messages fall back to the built-in row (data-kind="" + default markup).
        await Assertions.Expect(section.Locator("[data-message-id='ct2'] .tm-chat__message"))
            .ToBeVisibleAsync();
        await Assertions.Expect(section.Locator("[data-kind='ai-draft']"))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });

        // The alignment contract of TmChatBubble: incoming LEFT, outgoing RIGHT, system + pill
        // CENTERED (measured against the showcase column that hosts the standalone bubbles).
        var showcase = await section.Locator("[data-testid='chat-custom-bubble-showcase']").BoundingBoxAsync();
        Assert.IsNotNull(showcase, "the bubble showcase column has a bounding box");
        var incoming = await section.Locator("[data-testid='chat-bubble-incoming']").BoundingBoxAsync();
        var outgoing = await section.Locator("[data-testid='chat-bubble-outgoing']").BoundingBoxAsync();
        var system = await section.Locator("[data-testid='chat-bubble-system']").BoundingBoxAsync();
        var pill = await section.Locator("[data-testid='chat-bubble-pill']").BoundingBoxAsync();
        Assert.IsNotNull(incoming);
        Assert.IsNotNull(outgoing);
        Assert.IsNotNull(system);
        Assert.IsNotNull(pill);

        Assert.IsTrue(incoming!.X - showcase.X < 5,
            $"the Incoming bubble must align LEFT (delta {incoming.X - showcase.X:0.#}px)");
        Assert.IsTrue(showcase.X + showcase.Width - (outgoing!.X + outgoing.Width) < 5,
            $"the Outgoing bubble must align RIGHT (delta {showcase.X + showcase.Width - (outgoing.X + outgoing.Width):0.#}px)");
        var showcaseCenter = showcase.X + showcase.Width / 2;
        Assert.IsTrue(Math.Abs(system!.X + system.Width / 2 - showcaseCenter) < 5,
            "the System bubble must be CENTERED");
        Assert.IsTrue(Math.Abs(pill!.X + pill.Width / 2 - showcaseCenter) < 5,
            "the Pill bubble must be CENTERED");

        // The light screenshot is captured by the dark-mode test (light first, then toggle) so
        // the theme pair comes from a single page lifecycle.
        AssertNoClientErrors();
    }

    // ── (b)+(c) Enter sends / Shift+Enter newline / IME guard / refocus ──

    [TestMethod]
    [TestCategory("WASM")]
    [TestCategory("Smoke")]
    public async Task CustomComposer_EnterSends_ShiftEnterNewline_ImeGuarded()
    {
        var page = await OpenAsync();
        var section = Section_(page);
        var input = page.Locator($"{Section} .tm-chat__input");

        // Enter sends: the message row appears and the composer is EMPTY and FOCUSED again.
        await input.ClickAsync();
        await input.FillAsync("Hello from the custom E2E");
        await input.PressAsync("Enter");
        await Assertions.Expect(Item(page, "Hello from the custom E2E"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Assertions.Expect(input)
            .ToHaveValueAsync(string.Empty, new LocatorAssertionsToHaveValueOptions { Timeout = 5000 });
        await Assertions.Expect(input)
            .ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 5000 });

        // Shift+Enter is a new line, not a submit.
        await input.ClickAsync();
        await input.PressSequentiallyAsync("line one");
        await input.PressAsync("Shift+Enter");
        await input.PressSequentiallyAsync("line two");
        Assert.IsTrue((await input.InputValueAsync()).Contains('\n'),
            "Shift+Enter must insert a new line into the composer");
        await Assertions.Expect(Item(page, "line one"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });

        // IME: a keydown inside a composition session (isComposing / keyCode 229) is IGNORED —
        // the guard returns before preventDefault, so the event is not prevented and the
        // composition text stays in the composer.
        await input.FillAsync("IME composition text");
        var composingPrevented = await input.EvaluateAsync<bool>(
            @"el => { const ev = new KeyboardEvent('keydown', {
                key: 'Enter', code: 'Enter', keyCode: 229, bubbles: true, cancelable: true, isComposing: true });
                el.dispatchEvent(ev);
                return ev.defaultPrevented; }");
        Assert.IsFalse(composingPrevented,
            "the IME guard must return early — a composition keydown is never prevented");

        await page.WaitForTimeoutAsync(500); // settle: nothing may have been submitted
        await Assertions.Expect(Item(page, "IME composition text"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 3000 });
        Assert.AreEqual("IME composition text", await input.InputValueAsync(),
            "after a settle the composition draft is still in the composer");

        // The CONTROL — the same synthetic Enter WITHOUT the composition flags — is handled: the
        // guard prevents the default and the message sends.
        var plainPrevented = await input.EvaluateAsync<bool>(
            @"el => { const ev = new KeyboardEvent('keydown', {
                key: 'Enter', code: 'Enter', bubbles: true, cancelable: true });
                el.dispatchEvent(ev);
                return ev.defaultPrevented; }");
        Assert.IsTrue(plainPrevented,
            "without the composition flags the guard handles Enter (preventDefault)");
        await Assertions.Expect(Item(page, "IME composition text"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Assertions.Expect(input)
            .ToHaveValueAsync(string.Empty, new LocatorAssertionsToHaveValueOptions { Timeout = 5000 });

        AssertNoClientErrors();
    }

    // ── (d) away-from-bottom append → jump button, position kept, click → end ──

    [TestMethod]
    [TestCategory("WASM")]
    public async Task CustomScroll_JumpButtonAppears_KeepsPosition_AndScrollsToEnd()
    {
        var page = await OpenAsync();
        var scroller = Scroller(page);

        await scroller.EvaluateAsync("el => { el.scrollTop = (el.scrollHeight - el.clientHeight) / 2; }");
        await page.WaitForTimeoutAsync(300);
        var readingPosition = await scroller.EvaluateAsync<int>("el => Math.round(el.scrollTop)");

        await Button(page, "Deliver incoming message").ClickAsync();
        await Assertions.Expect(JumpButton(page))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Assertions.Expect(JumpButton(page))
            .ToContainTextAsync("1 new message", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });

        var afterArrival = await scroller.EvaluateAsync<int>("el => Math.round(el.scrollTop)");
        Assert.AreEqual(readingPosition, afterArrival,
            "an away-from-bottom append must not move the reading position");

        await SaveElementScreenshotAsync(page.Locator($"{Section} .tm-chat").First, "chat-custom-jump-button");

        await JumpButton(page).ClickAsync();
        await Assertions.Expect(JumpButton(page))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await page.WaitForFunctionAsync(
            @"sel => { const s = document.querySelector(sel);
                return !!s && s.scrollHeight - s.scrollTop - s.clientHeight < 40; }",
            $"{Section} .tm-chat__messages",
            new PageWaitForFunctionOptions { Timeout = 10000, PollingInterval = 100 });
        AssertNoClientErrors();
    }

    // ── (e) load older keeps the visible anchor within ±2px of its position ──

    [TestMethod]
    [TestCategory("WASM")]
    public async Task CustomLoadOlder_KeepsTheAnchorWithinTwoPixels()
    {
        var page = await OpenAsync();
        var scroller = Scroller(page);

        // Read at a MIDDLE position (the top edge would arm the automatic older-page load and
        // race the button click); the anchor is the first row visible below the viewport top.
        await scroller.EvaluateAsync("el => { el.scrollTop = (el.scrollHeight - el.clientHeight) / 3; }");
        await page.WaitForTimeoutAsync(300);
        var anchorId = await scroller.EvaluateAsync<string?>(
            "el => { const st = el.scrollTop;" +
            " for (const item of el.querySelectorAll('[data-message-id]')) {" +
            " const id = item.getAttribute('data-message-id');" +
            " if (!id) continue;" +
            " if (item.offsetTop + (item.offsetHeight || 0) > st) return id; } return null; }");
        Assert.IsNotNull(anchorId, "a mid-list position must show a message row");

        // Stabilize the page scroll first: the anchor's boundingBox().y is viewport-relative, so
        // the button (already in view) must not trigger a further scroll when clicked. The demo's
        // own side button is named distinctly from the chat's in-list "Load older messages".
        var loadOlder = page.Locator(Section).GetByRole(AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "Simulate older page" });
        await loadOlder.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(200);
        var anchor = page.Locator($"{Section} [data-message-id='{anchorId}']");
        var yBefore = (await anchor.BoundingBoxAsync())!.Y;

        await loadOlder.ClickAsync();
        await Assertions.Expect(page.Locator($"{Section} [data-message-id^='ct-old-']"))
            .ToHaveCountAsync(8, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });

        var yAfter = (await anchor.BoundingBoxAsync())!.Y;
        Assert.IsTrue(Math.Abs(yAfter - yBefore) <= 2,
            $"the prepend must preserve the visible anchor position (y {yBefore:0.#} → {yAfter:0.#})");

        var scrollTop = await scroller.EvaluateAsync<int>("el => Math.round(el.scrollTop)");
        Assert.IsTrue(scrollTop > 0,
            "the position is preserved by shifting scrollTop, not by staying at the absolute top");
        AssertNoClientErrors();
    }

    // ── (f) EXACTLY ONE live region (wide selector) announcing the host text ──

    [TestMethod]
    [TestCategory("WASM")]
    public async Task CustomChat_HasExactlyOneLiveRegion_AndAnnouncesHostText()
    {
        var page = await OpenAsync();

        // The WIDE selector from the carry-forward: any second live region — even a hidden
        // role-based one — would break the exactly-one contract.
        var liveRegionCount = await page.EvaluateAsync<int>(
            @"sel => document.querySelector(sel).querySelectorAll(
                '[aria-live],[role=""status""],[role=""alert""],[role=""log""],[role=""marquee""],[role=""timer""]').length",
            Section);
        Assert.AreEqual(1, liveRegionCount,
            "the template-driven chat must keep EXACTLY ONE live region");

        var region = page.Locator($"{Section} [data-testid='chat-announcements']");
        await Assertions.Expect(region)
            .ToHaveAttributeAsync("aria-live", "polite", new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });

        // A delivered message reaches the region with the HOST's sentence (trailing throttle ≤1s).
        await Button(page, "Deliver incoming message").ClickAsync();
        await Assertions.Expect(region)
            .ToContainTextAsync("New message from Mia: Incoming message #1",
                new LocatorAssertionsToContainTextOptions { Timeout = 5000 });
        AssertNoClientErrors();
    }

    // ── (f2) Offline: the composer stays usable and sends become queued rows with a countdown ──

    [TestMethod]
    [TestCategory("WASM")]
    public async Task CustomOffline_ToggleQueuesSendsWithACountdown()
    {
        var page = await OpenAsync();
        var input = page.Locator($"{Section} .tm-chat__input");
        var queued = page.Locator($"{Section} [data-testid='chat-custom-queued']");
        var before = await queued.CountAsync();
        Assert.IsTrue(before >= 1, "the seeded conversation starts with a queued reply");

        // Flip the demo's Offline toggle: the banner says replies are queued and the composer
        // stays ENABLED (no Disabled binding) — sends become queued rows instead. The switch's
        // input is visually hidden behind the label — click the label.
        await page.Locator($"{Section} .chat-demo-conn__toggle .tm-toggle-label").ClickAsync();
        await Assertions.Expect(page.Locator($"{Section} [data-testid='chat-custom-banner']"))
            .ToContainTextAsync("replies are queued", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });
        await Assertions.Expect(input).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 5000 });

        await input.ClickAsync();
        await input.FillAsync("Queued while offline");
        await input.PressAsync("Enter");

        // The queued count goes up by exactly 1: the offline send became a queued row.
        await Assertions.Expect(queued)
            .ToHaveCountAsync(before + 1, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Assertions.Expect(Item(page, "Queued while offline"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Assertions.Expect(Item(page, "Queued while offline")
                .Locator("[data-testid='chat-custom-countdown']"))
            .ToContainTextAsync("Sends in", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });

        // Element screenshot of the whole chat — banner (offline state) + composer (usable).
        await SaveElementScreenshotAsync(page.Locator($"{Section} .tm-chat").First, "chat-custom-offline");
        AssertNoClientErrors();
    }

    // ── (f3) Approve/Stop: the row transitions plus focus + live-region announcements ──

    [TestMethod]
    [TestCategory("WASM")]
    public async Task CustomActions_ApproveAndStop_FocusAndAnnounce()
    {
        var page = await OpenAsync();
        var section = Section_(page);
        var input = page.Locator($"{Section} .tm-chat__input");
        var draft = section.Locator("[data-testid='chat-custom-draft']");
        await Assertions.Expect(draft)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        // Approve: the draft disappears, a plain outgoing row appears, the composer regains the
        // focus and the live region announces the host sentence.
        await section.Locator("[data-testid='chat-custom-approve']").ClickAsync();
        await Assertions.Expect(draft)
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Assertions.Expect(Item(page, "the studio is $30/h including the projector"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Assertions.Expect(input)
            .ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 5000 });
        var region = page.Locator($"{Section} [data-testid='chat-announcements']");
        await Assertions.Expect(region)
            .ToContainTextAsync("Draft approved", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });

        // Stop: the queued row becomes a discarded row (count +1) and disappears as queued.
        var discarded = section.Locator("[data-testid='chat-custom-discarded']");
        var discardedBefore = await discarded.CountAsync();
        await section.Locator("[data-testid='chat-custom-stop']").ClickAsync();
        await Assertions.Expect(discarded)
            .ToHaveCountAsync(discardedBefore + 1, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Assertions.Expect(section.Locator("[data-testid='chat-custom-queued']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Assertions.Expect(section.Locator("[data-testid='chat-custom-stop']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        AssertNoClientErrors();
    }

    // ── (g) 375×812 touch: 44px send, composer above the fake nav, tap keeps focus ──

    [TestMethod]
    [TestCategory("WASM")]
    public async Task CustomMobile_375x812_TouchTargets_ComposerAboveNav_TapKeepsFocus()
    {
        var page = await OpenAsync(375, 812, mobileEmulation: true);

        var coarse = await page.EvaluateAsync<bool>("matchMedia('(pointer: coarse)').matches");
        Assert.IsTrue(coarse, "the 375×812 mobile-emulated context must report pointer: coarse");

        // The custom section's send button reaches the 44px touch target on a coarse pointer.
        var sendButton = page.Locator($"{Section} .tm-chat__send-btn").First;
        var minWidth = await sendButton.EvaluateAsync<string>("el => getComputedStyle(el).minWidth");
        Assert.AreEqual("44px", minWidth, "the send button reaches --tm-touch-target on coarse pointers");
        var minHeight = await sendButton.EvaluateAsync<string>("el => getComputedStyle(el).minHeight");
        Assert.AreEqual("44px", minHeight, "the send button reaches --tm-touch-target on coarse pointers");

        // The host-owned bubble action buttons (Approve/Edit/Discard/Stop) reach 44px too.
        var actionsReachTarget = await page.EvaluateAsync<bool>(
            @"sel => { const buttons = document.querySelectorAll(sel + ' .chat-demo-bubble-action');
                return buttons.length > 0 &&
                    Array.from(buttons).every(b => parseFloat(getComputedStyle(b).minHeight) >= 44); }",
            Section);
        Assert.IsTrue(actionsReachTarget,
            "the bubble action buttons must reach the 44px touch target on coarse pointers");

        // The phone frame's composer rests ABOVE the simulated bottom navigation (56px offset):
        // bring the composer into view first (the measurement is viewport-relative and must not
        // be taken from a scroll PAST the chat), then compare it with the fake nav's top edge —
        // the engaged sticky position OR the natural position both satisfy the contract.
        var phoneComposer = page.Locator($"{Phone} .tm-chat__input-area");
        await phoneComposer.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(300);
        var composerAboveNav = await page.EvaluateAsync<bool>(
            @"() => {
                const nav = document.querySelector(""[data-testid='chat-mobile-phone'] .chat-demo-phone__nav"").getBoundingClientRect();
                const composer = document.querySelector(""[data-testid='chat-mobile-phone'] .tm-chat__input-area"").getBoundingClientRect();
                return composer.bottom <= nav.top + 1 && composer.top >= 0;
            }");
        Assert.IsTrue(composerAboveNav,
            "the sticky composer must stay visible above the simulated bottom navigation");

        // Carry-forward: a TOUCH TAP on Send keeps the focus in the textarea (and clears it).
        var phoneInput = page.Locator($"{Phone} .tm-chat__input");
        await phoneInput.ScrollIntoViewIfNeededAsync();
        await phoneInput.FillAsync("Tapped on a phone");
        await page.Locator($"{Phone} .tm-chat__send-btn").First.TapAsync();
        await Assertions.Expect(phoneInput)
            .ToHaveValueAsync(string.Empty, new LocatorAssertionsToHaveValueOptions { Timeout = 5000 });
        await Assertions.Expect(phoneInput)
            .ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 5000 });

        // Element screenshots: the section chat (banner + composer + bubbles) and the phone shot.
        await SaveElementScreenshotAsync(page.Locator($"{Section} .tm-chat").First, "chat-custom-mobile-375x812");
        await SaveElementScreenshotAsync(page.Locator(Phone), "chat-custom-mobile-phone-375x812");
        AssertNoClientErrors();
    }

    // ── (h) prefers-reduced-motion: the typing dots do not animate ──

    [TestMethod]
    [TestCategory("WASM")]
    public async Task CustomTyping_ReducedMotion_DotsDoNotAnimate()
    {
        var page = await OpenAsync();
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });

        await Button(page, "Simulate fan typing").ClickAsync();
        var indicator = page.Locator($"{Section} .tm-chat__typing-indicator");
        await Assertions.Expect(indicator)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Assertions.Expect(page.Locator($"{Section} .tm-chat__typing-text"))
            .ToContainTextAsync("Mia is typing…", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });

        // TypingAriaHidden=true: the demo announces nothing about typing through the indicator.
        await Assertions.Expect(indicator)
            .ToHaveAttributeAsync("aria-hidden", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });

        var animationName = await page.EvaluateAsync<string>(
            @"sel => { const dot = document.querySelector(sel + ' .tm-chat__typing-dots span');
                return dot ? getComputedStyle(dot).animationName : 'MISSING'; }",
            Section);
        Assert.AreEqual("none", animationName,
            "under prefers-reduced-motion the typing dots must not animate");
        AssertNoClientErrors();
    }

    // ── (i) dark mode of the template-driven section ──

    [TestMethod]
    [TestCategory("WASM")]
    public async Task CustomDarkMode_SectionRendersWithDarkTokens()
    {
        var page = await OpenAsync();
        var section = Section_(page);
        await Assertions.Expect(section.Locator("[data-testid='chat-custom-draft']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // Light capture FIRST, from the same page lifecycle the dark shot comes from.
        var draft = section.Locator("[data-testid='chat-custom-draft']");
        var lightBackground = await draft.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        await SaveElementScreenshotAsync(section, "chat-custom-light");

        // Use the DEMO's own theme toggle (the ThemeService path): the demo shell flips its
        // data-theme + dark class and the library tokens flip with it. Setting the attribute by
        // hand is NOT enough — the demo chrome (Tailwind dark: utilities) would stay light.
        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        await page.WaitForSelectorAsync(".demo-shell[data-theme='dark']",
            new PageWaitForSelectorOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(300);
        var theme = await page.EvaluateAsync<string>(
            "() => document.querySelector('.demo-shell')?.getAttribute('data-theme')");
        Assert.AreEqual("dark", theme);

        // The draft bubble's computed background must actually flip with the theme.
        var darkBackground = await draft.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.AreNotEqual(lightBackground, darkBackground,
            $"the draft bubble background must differ in dark mode (light: {lightBackground}, dark: {darkBackground})");

        await SaveElementScreenshotAsync(section, "chat-custom-dark");
        AssertNoClientErrors();
    }

    // ── Subscribe gate: the host composer gates, then behaves like the built-in one ──

    [TestMethod]
    [TestCategory("WASM")]
    public async Task SubscribeGate_GatesThenSendsThroughTheHostComposer()
    {
        var page = await OpenAsync();
        var gate = page.Locator(GateSection);
        await gate.ScrollIntoViewIfNeededAsync();
        await gate.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        var gateCard = gate.Locator("[data-testid='chat-gate-composer']");
        await Assertions.Expect(gateCard)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        // Dana's paid voice note renders through the gate chat's template: the price badge shows
        // once and the body guard keeps the transcript out of the DOM until it is unlocked.
        var lockedNote = gate.Locator("[data-testid='chat-gate-locked']");
        await Assertions.Expect(lockedNote)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await Assertions.Expect(lockedNote)
            .ToContainTextAsync("$9.99", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });
        await Assertions.Expect(page.GetByText("bring the contract"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });

        await SaveElementScreenshotAsync(gate, "chat-subscribe-gate");

        await gate.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Subscribe for $4.99/mo" })
            .ClickAsync();
        var gateInput = gate.Locator(".chat-demo-gate-composer__input");
        await Assertions.Expect(gateInput)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        // Enter submits through the attached pipeline (the same JS the built-in composer uses).
        await gateInput.FillAsync("Hey! Count me in for Friday.");
        await gateInput.PressAsync("Enter");
        await Assertions.Expect(gate.Locator(".tm-chat__item", new LocatorLocatorOptions { HasText = "Count me in for Friday." }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // The demo host answers a moment later through the same conversation.
        await Assertions.Expect(gate.Locator(".tm-chat__item", new LocatorLocatorOptions { HasText = "Thanks for subscribing!" }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // Unlock: the voice note falls back to a plain row, the focus moves onto it and the gate
        // chat's live region announces the host sentence.
        await gate.Locator("[data-testid='chat-gate-unlock']").ClickAsync();
        await Assertions.Expect(lockedNote)
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Assertions.Expect(gate.Locator(".tm-chat__item", new LocatorLocatorOptions { HasText = "bring the contract" }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Assertions.Expect(gate.Locator("[data-testid='chat-announcements']"))
            .ToContainTextAsync("Voice note unlocked.", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });

        await SaveElementScreenshotAsync(gate, "chat-subscribe-composer");
        AssertNoClientErrors();
    }

    // ── (j) FZE-12: the AUTO-ATTACHED composer (id=InputId, NO AttachAsync) ──

    private const string AutoAttachSection = "[data-testid='chat-autoattach-section']";
    private const string AutoAttachInput = "[data-testid='chat-autoattach-section'] .chat-demo-gate-composer__input";
    private const string AutoAttachCount = "[data-testid='chat-autoattach-count']";

    /// <summary>
    /// Opens /chat and settles the AUTO-ATTACH section's chat (its own gate — the default
    /// <see cref="OpenAsync"/> waits on the custom-templates section's scroll state).
    /// </summary>
    private async Task<IPage> OpenAutoAttachAsync(int width = 1440, int height = 1100)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
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

        var section = page.Locator(AutoAttachSection);
        await section.ScrollIntoViewIfNeededAsync();
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // The auto-attached composer is inert before the JS module lands: wait for the seeded
        // rows AND the settled first-render scroll before driving the composer.
        await page.WaitForFunctionAsync(
            @"sel => { const s = document.querySelector(sel);
                return !!s && !!s.querySelector('.tm-chat__messages .tm-chat__item') &&
                    s.querySelector('.tm-chat__messages').scrollHeight -
                    s.querySelector('.tm-chat__messages').scrollTop -
                    s.querySelector('.tm-chat__messages').clientHeight < 40; }",
            AutoAttachSection,
            new PageWaitForFunctionOptions { Timeout = 30000, PollingInterval = 200 });
        await page.WaitForTimeoutAsync(500);
        return page;
    }

    private static ILocator AutoAttachItem(IPage page, string text) =>
        page.Locator($"{AutoAttachSection} .tm-chat__item", new PageLocatorOptions { HasText = text });

    [TestMethod]
    [TestCategory("WASM")]
    public async Task AutoAttachComposer_EnterSends_ClearsAndRefocuses()
    {
        var page = await OpenAutoAttachAsync();
        var input = page.Locator(AutoAttachInput);
        var count = page.Locator(AutoAttachCount);
        var before = await count.TextContentAsync() ?? "0";

        await input.ClickAsync();
        await input.FillAsync("Hello from the auto-attached composer");
        await input.PressAsync("Enter");

        // The send went through the SHARED pipeline (the host counter increments) …
        await Assertions.Expect(page.Locator(AutoAttachCount))
            .ToHaveTextAsync((int.Parse(before) + 1).ToString(),
                new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });
        await Assertions.Expect(AutoAttachItem(page, "Hello from the auto-attached composer"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // …the composer CLEARED (the controlled input follows composer.Text) …
        await Assertions.Expect(input)
            .ToHaveValueAsync(string.Empty, new LocatorAssertionsToHaveValueOptions { Timeout = 5000 });

        // …and the FOCUS is back on the textarea (RefocusAfterSend via the auto handle).
        await Assertions.Expect(input)
            .ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 5000 });

        AssertNoClientErrors();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task AutoAttachComposer_ShiftEnterNewline_ImeGuarded()
    {
        var page = await OpenAutoAttachAsync();
        var input = page.Locator(AutoAttachInput);

        // Shift+Enter is a new line, not a submit.
        await input.ClickAsync();
        await input.PressSequentiallyAsync("line one");
        await input.PressAsync("Shift+Enter");
        await input.PressSequentiallyAsync("line two");
        Assert.IsTrue((await input.InputValueAsync()).Contains('\n'),
            "Shift+Enter must insert a new line into the auto-attached composer");
        await page.WaitForTimeoutAsync(400);
        await Assertions.Expect(AutoAttachItem(page, "line one"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 3000 });

        // IME: a keydown inside a composition session (isComposing / keyCode 229) is IGNORED —
        // the guard returns BEFORE preventDefault, so nothing is submitted.
        await input.FillAsync("IME composition text");
        var composingPrevented = await input.EvaluateAsync<bool>(
            @"el => { const ev = new KeyboardEvent('keydown', {
                key: 'Enter', code: 'Enter', keyCode: 229, bubbles: true, cancelable: true, isComposing: true });
                el.dispatchEvent(ev);
                return ev.defaultPrevented; }");
        Assert.IsFalse(composingPrevented,
            "the IME guard must return early — a composition keydown is never prevented");
        await page.WaitForTimeoutAsync(400);
        await Assertions.Expect(AutoAttachItem(page, "IME composition text"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 3000 });

        // The CONTROL — the same synthetic Enter WITHOUT the composition flags — submits: the
        // guard prevents the default and the message goes through the shared pipeline.
        var plainPrevented = await input.EvaluateAsync<bool>(
            @"el => { const ev = new KeyboardEvent('keydown', {
                key: 'Enter', code: 'Enter', bubbles: true, cancelable: true });
                el.dispatchEvent(ev);
                return ev.defaultPrevented; }");
        Assert.IsTrue(plainPrevented,
            "without the composition flags the auto-attached composer handles Enter (preventDefault)");
        await Assertions.Expect(AutoAttachItem(page, "IME composition text"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // UX screenshot of the whole auto-attach section (composer + host counter + chat).
        await SaveElementScreenshotAsync(page.Locator($"{AutoAttachSection} .tm-chat").First,
            "chat-autoattach-section");
        AssertNoClientErrors();
    }
}
