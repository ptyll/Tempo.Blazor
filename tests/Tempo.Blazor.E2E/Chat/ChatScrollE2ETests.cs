using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Chat;

/// <summary>
/// E2E for the TmChat scroll behavior (FZE-6) on the /chat demo (WASM @ 7106), section
/// <c>chat-scroll-section</c>: messages arriving while the user reads above the bottom are
/// counted behind the floating jump-to-new button (click → scroll + count reset), and scrolling
/// to the top edge auto-loads the older page, whose PREPEND preserves the reading position
/// instead of jumping. The real <c>chat.js</c> scroller runs in the browser — this is the
/// end-to-end contract of the snapshot/afterUpdate orchestration. Screenshots land in
/// <c>__screenshots__/chat/</c> for UX review.
/// </summary>
[TestClass]
public class ChatScrollE2ETests : WasmTestBase
{
    private const string ChatPage = "/chat";
    private const string Section = "[data-testid='chat-scroll-section']";

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

        // Wait for the chat's FIRST-RENDER scroll to settle: the WASM boot renders the section
        // chat several seconds after the app reports ready, and every scroll interaction below
        // would otherwise race that initial jump to the bottom.
        await page.WaitForFunctionAsync(
            @"sel => { const s = document.querySelector(sel);
                return !!s && s.scrollHeight - s.scrollTop - s.clientHeight < 40; }",
            $"{Section} .tm-chat__messages",
            new PageWaitForFunctionOptions { Timeout = 30000, PollingInterval = 200 });
        await page.WaitForTimeoutAsync(500);
        return page;
    }

    private static ILocator Scroller(IPage page) =>
        page.Locator($"{Section} .tm-chat__messages");

    private static ILocator JumpButton(IPage page) =>
        page.Locator($"{Section} [data-testid='chat-jump-to-new']");

    /// <summary>
    /// The id of the row the user is reading — the first message row whose bottom edge is
    /// below the viewport top (the same anchor the <c>chat.js</c> scroller keeps). After a
    /// prepend a sliver of a NEW row may legally peek above it, so the position assertion
    /// checks the anchor ROW's offset, not "the first visible id is unchanged".
    /// </summary>
    private const string FirstVisibleScript =
        "el => { const st = el.scrollTop;" +
        " for (const item of el.querySelectorAll('[data-message-id]')) {" +
        " const id = item.getAttribute('data-message-id');" +
        " if (!id) continue;" +
        " if (item.offsetTop + (item.offsetHeight || 0) > st) return id; } return null; }";

    /// <summary>The <c>offsetTop - scrollTop</c> of the row with the given id (its viewport offset).</summary>
    private const string RowOffsetScript =
        "(el, id) => { const st = el.scrollTop;" +
        " for (const item of el.querySelectorAll('[data-message-id]')) {" +
        " if (item.getAttribute('data-message-id') === id) return item.offsetTop - st; }" +
        " return null; }";

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Chat_Scroll_JumpButton_CountsUnreadAndScrollsToBottom()
    {
        var page = await OpenAsync();
        var scroller = Scroller(page);

        // The user scrolls up (NOT to the top edge — that would arm the older-page auto-load
        // and its position shift would race this test); the scroller reports away-from-bottom.
        await scroller.EvaluateAsync("el => { el.scrollTop = (el.scrollHeight - el.clientHeight) / 2; }");
        await page.WaitForTimeoutAsync(300);
        var readingPosition = await scroller.EvaluateAsync<int>("el => el.scrollTop");

        // An incoming message arrives — no scroll, a counted button instead.
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Inject incoming message" }).ClickAsync();
        await Assertions.Expect(JumpButton(page)).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Assertions.Expect(JumpButton(page)).ToContainTextAsync("1 new message",
            new LocatorAssertionsToContainTextOptions { Timeout = 5000 });

        var scrollTopAfterArrival = await scroller.EvaluateAsync<int>("el => el.scrollTop");
        Assert.AreEqual(readingPosition, scrollTopAfterArrival,
            "an away-from-bottom append must not move the reading position");

        // The click scrolls to the bottom (a smooth animation), resets the count and hides the
        // button — wait for the scroll to settle before judging it.
        await JumpButton(page).ClickAsync();
        await Assertions.Expect(JumpButton(page)).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await page.WaitForFunctionAsync(
            @"sel => { const s = document.querySelector(sel);
                return !!s && s.scrollHeight - s.scrollTop - s.clientHeight < 40; }",
            $"{Section} .tm-chat__messages",
            new PageWaitForFunctionOptions { Timeout = 10000, PollingInterval = 100 });

        await SaveScreenshotAsync(page, "chat-scroll-jump");
        AssertNoClientErrors();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Chat_Scroll_TopEdgeAutoLoad_PreservesTheReadingPosition()
    {
        var page = await OpenAsync();
        var scroller = Scroller(page);

        // Reach the top edge — the near-top report arms the automatic older-page load (no
        // button click). The position that must survive the prepend is the first visible row
        // and its offset below the viewport top — measured AFTER the scroll to the top (a
        // middle-position reference would be stale: the user LEFT the middle).
        await scroller.EvaluateAsync("el => { el.scrollTop = 0; }");
        await page.WaitForTimeoutAsync(300);
        var anchorId = await scroller.EvaluateAsync<string?>(FirstVisibleScript);
        Assert.IsNotNull(anchorId, "a top-of-list position must show a message row");
        var anchorOffsetBefore = await scroller.EvaluateAsync<int?>(RowOffsetScript, anchorId);

        // The prepend lands (8 older rows) and the row the user was reading keeps its exact
        // viewport position (a sliver of a new row may peek above it — that is fine).
        await Assertions.Expect(page.Locator($"{Section} [data-message-id^='scroll-old-']"))
            .ToHaveCountAsync(8, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        var anchorOffsetAfter = await scroller.EvaluateAsync<int?>(RowOffsetScript, anchorId);
        Assert.IsNotNull(anchorOffsetAfter, "the anchored row survives the prepend (the @key keeps it)");
        Assert.AreEqual(anchorOffsetBefore, anchorOffsetAfter,
            "the prepend must preserve the visual position: the row the user was reading keeps its offset");

        var scrollTop = await scroller.EvaluateAsync<int>("el => el.scrollTop");
        Assert.IsTrue(scrollTop > 100,
            "the position is preserved by shifting scrollTop, not by staying at the absolute top");

        await SaveScreenshotAsync(page, "chat-scroll-load-older");
        AssertNoClientErrors();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Chat_Scroll_JumpButton_TouchTargetKeyboardFocusAndThemeScreenshots()
    {
        var page = await OpenAsync();
        var scroller = Scroller(page);
        var chat = page.Locator($"{Section} .tm-chat");

        // The user scrolls up; an incoming message makes the floating button appear.
        await scroller.EvaluateAsync("el => { el.scrollTop = (el.scrollHeight - el.clientHeight) / 2; }");
        await page.WaitForTimeoutAsync(300);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Inject incoming message" }).ClickAsync();
        await Assertions.Expect(JumpButton(page)).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // WCAG 2.5.8 / touch: the floating target is at least 44px tall (min-height: var(--tm-touch-target)).
        var box = await JumpButton(page).BoundingBoxAsync();
        Assert.IsNotNull(box, "the visible button has a bounding box");
        Assert.IsTrue(box.Height >= 44,
            $"the jump button must meet the 44px touch target (was {box.Height:0.#}px)");

        // Keyboard activation: Enter on the button must never drop focus to the body (the
        // composer takes it — Detail == 0 is the keyboard branch).
        await JumpButton(page).FocusAsync();
        await JumpButton(page).PressAsync("Enter");
        await page.WaitForFunctionAsync(
            "() => document.activeElement !== null && document.activeElement !== document.body",
            null, new PageWaitForFunctionOptions { Timeout = 10000, PollingInterval = 100 });
        var activeAfterEnter = await page.EvaluateAsync<string>(
            "document.activeElement?.tagName?.toLowerCase() ?? 'none'");
        Assert.AreNotEqual("body", activeAfterEnter,
            "the keyboard click must move focus off the unmounting button (composer or a row)");

        await SaveScreenshotAsync(page, "chat-scroll-jump");
        await SaveElementScreenshotsAsync(page, chat, "chat-scroll-jump");
        AssertNoClientErrors();
    }

    /// <summary>
    /// Element screenshots of the whole chat with the jump button visible — light, then dark via
    /// <c>data-theme="dark"</c> (the demo's own theme toggle only flips the demo wrapper, the
    /// screenshot needs the whole document dark).
    /// </summary>
    private static async Task SaveElementScreenshotsAsync(IPage page, ILocator chat, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "chat");
        Directory.CreateDirectory(dir);
        await chat.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(dir, $"{fileName}-light.png") });

        await page.EvaluateAsync(@"() => {
            document.documentElement.setAttribute('data-theme', 'dark');
            document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', 'dark'));
        }");
        await page.WaitForTimeoutAsync(150);
        await chat.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(dir, $"{fileName}-dark.png") });

        // Restore the light theme so a later step in the same session is unaffected.
        await page.EvaluateAsync(@"() => {
            document.documentElement.removeAttribute('data-theme');
            document.querySelectorAll('[data-theme]').forEach(el => el.removeAttribute('data-theme'));
        }");
    }

    private void AssertNoClientErrors()
    {
        lock (_clientErrors)
        {
            Assert.IsTrue(_clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", _clientErrors));
        }
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "chat");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"{fileName}.png"), FullPage = true });
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
