using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Chat;

/// <summary>
/// E2E for the generic <c>TmTypedChat&lt;TItem&gt;</c> (FZE-15) on the /chat demo (WASM @ 7106),
/// section <c>chat-typed-section</c>: the <c>SupportTicketEvent</c> domain items render through
/// the host <c>ItemTemplate</c> + <c>TmChatBubble</c>, a send through the FORWARDED
/// <c>OnSend</c> appends a new item and the chat scrolls to the bottom on its own, and the
/// section screenshot lands in <c>__screenshots__/chat/</c> for UX review. The non-generic
/// <c>TmChat</c> demo sections must keep working — the other Chat specs cover them.
/// </summary>
[TestClass]
public class ChatTypedE2ETests : WasmTestBase
{
    private const string ChatPage = "/chat";
    private const string Section = "[data-testid='chat-typed-section']";

    private readonly List<string> _clientErrors = [];

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1100)
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

    private static ILocator Events(IPage page) =>
        page.Locator($"{Section} [data-testid='chat-typed-event']");

    private static ILocator Scroller(IPage page) => page.Locator($"{Section} .tm-chat__messages");

    private static ILocator ComposerInput(IPage page) => page.Locator($"{Section} .tm-chat__input");

    private static ILocator SendButton(IPage page) => page.Locator($"{Section} .tm-chat__send-btn");

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

    [TestMethod]
    [TestCategory("WASM")]
    public async Task TypedItems_RenderThroughItemTemplate()
    {
        var page = await OpenAsync();

        // The three seeded SupportTicketEvents render through the host template — TmChatBubble
        // markup, one per item, each carrying the domain texts.
        (await Events(page).CountAsync()).Should().Be(3);
        (await Item(page, "Printer offline again — can you check the spooler?").CountAsync()).Should().Be(1);
        (await Item(page, "Spooler restarted; the queue is draining now.").CountAsync()).Should().Be(1);
        (await Item(page, "Queue drained and the test page printed fine.").CountAsync()).Should().Be(1);

        // The template markup is TmChatBubble — NOT the built-in bubble row.
        (await page.Locator($"{Section} .tm-chat__bubble").CountAsync()).Should().Be(0);

        // The badge carries the domain state; variants differ per State.
        (await page.Locator($"{Section} [data-testid='chat-typed-event'].tm-chat-bubble--pending").CountAsync()).Should().Be(1);
        (await page.Locator($"{Section} [data-testid='chat-typed-event'].tm-chat-bubble--queued").CountAsync()).Should().Be(1);
        (await page.Locator($"{Section} [data-testid='chat-typed-event'].tm-chat-bubble--pill").CountAsync()).Should().Be(1);

        // The composer is the BUILT-IN one (no ComposerTemplate was passed).
        (await ComposerInput(page).CountAsync()).Should().Be(1);
        (await ComposerInput(page).GetAttributeAsync("placeholder"))
            .Should().Be("Reply to the ticket thread…", "Placeholder reached the chat through the splat");

        AssertNoClientErrors();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task TypedSend_ViaForwardedOnSend_AppendsItemAndScrollsToBottom()
    {
        var page = await OpenAsync();

        // Send through the BUILT-IN composer — the OnSend handler is a FORWARDED parameter
        // (the wrapper splats it onto the inner TmChat) and appends a new ticket event.
        await ComposerInput(page).FillAsync("Escalating: the printer is offline again.");
        await SendButton(page).ClickAsync();

        await page.WaitForFunctionAsync(
            @"sel => { const s = document.querySelector(sel);
                return !!s && s.querySelectorAll('[data-testid=""chat-typed-event""]').length === 4; }",
            Section,
            new PageWaitForFunctionOptions { Timeout = 30000, PollingInterval = 200 });

        (await Item(page, "Escalating: the printer is offline again.").CountAsync()).Should().Be(1,
            "the appended domain item renders through the same ItemTemplate");
        (await page.Locator($"{Section} [data-testid='chat-typed-count']").InnerTextAsync())
            .Should().Be("4", "the host-side counter reflects the appended item");

        // The chat scrolled itself to the bottom — the new item is the visible last row.
        var atBottom = await page.EvaluateAsync<int>(
            @"sel => { const s = document.querySelector(sel + ' .tm-chat__messages');
                return s.scrollHeight - s.scrollTop - s.clientHeight; }",
            Section);
        atBottom.Should().BeLessThan(40, "a send through the forwarded OnSend scrolls to the tail");

        AssertNoClientErrors();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task TypedSection_Screenshot()
    {
        var page = await OpenAsync();
        var section = page.Locator(Section);

        await SaveElementScreenshotAsync(section, "chat-typed-light");
        AssertNoClientErrors();
    }
}
